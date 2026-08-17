using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using BallsServer.Core.Preflight;

namespace BallsServer.Windows;

internal readonly record struct TailscaleStatus(
    string BackendState,
    bool IsOnline,
    int AddressCount);

internal interface ITailscaleStatusSource
{
    ValueTask<TailscaleStatus> QueryAsync(CancellationToken cancellationToken);
}

internal sealed class TailscaleStatusSource : ITailscaleStatusSource
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);
    private const int MaximumOutputCharacters = 1024 * 1024;

    public async ValueTask<TailscaleStatus> QueryAsync(CancellationToken cancellationToken)
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Tailscale",
            "tailscale.exe");
        if (!File.Exists(executable))
        {
            throw new ReadOnlyProbeException("The installed Tailscale command is unavailable.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("status");
        startInfo.ArgumentList.Add("--json");

        var output = await BoundedReadOnlyProcessRunner.RunAsync(
            startInfo,
            QueryTimeout,
            MaximumOutputCharacters,
            cancellationToken).ConfigureAwait(false);
        return TailscaleStatusJsonParser.Parse(output);
    }
}

internal static class TailscaleStatusJsonParser
{
    internal static TailscaleStatus Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ReadOnlyProbeException("Tailscale returned an invalid status object.");
        }

        var backendState = JsonHelpers.RequiredString(root, "BackendState");
        var self = root.TryGetProperty("Self", out var selfElement) &&
            selfElement.ValueKind == JsonValueKind.Object
            ? selfElement
            : default;

        var addresses = ReadAddresses(root, "TailscaleIPs");
        if (addresses.Count == 0 && self.ValueKind == JsonValueKind.Object)
        {
            addresses = ReadAddresses(self, "TailscaleIPs");
        }

        bool? reportedOnline = null;
        if (self.ValueKind == JsonValueKind.Object && self.TryGetProperty("Online", out var online))
        {
            reportedOnline = online.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => throw new ReadOnlyProbeException("Tailscale returned an invalid online state."),
            };
        }

        var isOnline = reportedOnline ??
            (string.Equals(backendState, "Running", StringComparison.OrdinalIgnoreCase) && addresses.Count > 0);
        return new TailscaleStatus(backendState, isOnline, addresses.Count);
    }

    private static HashSet<IPAddress> ReadAddresses(JsonElement element, string propertyName)
    {
        var addresses = new HashSet<IPAddress>();
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return addresses;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new ReadOnlyProbeException("Tailscale returned an invalid address collection.");
        }

        foreach (var value in property.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String ||
                !IPAddress.TryParse(value.GetString(), out var address))
            {
                throw new ReadOnlyProbeException("Tailscale returned an invalid assigned address.");
            }

            addresses.Add(address);
        }

        return addresses;
    }
}

internal sealed class WindowsTailscaleProbe(
    IWindowsServiceStatusSource serviceStatus,
    ITailscaleStatusSource statusSource) : ITailscaleProbe
{
    public async ValueTask<ProbeResult<TailscaleObservation>> ObserveAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var service = serviceStatus.Query("Tailscale");
            if (!service.IsInstalled || service.State != WindowsServiceState.Running)
            {
                return ProbeResult.Observed(new TailscaleObservation(
                    service.IsInstalled,
                    service.State,
                    "Unavailable",
                    IsOnline: false,
                    AddressCount: 0));
            }

            var status = await statusSource.QueryAsync(cancellationToken).ConfigureAwait(false);
            return ProbeResult.Observed(new TailscaleObservation(
                IsInstalled: true,
                service.State,
                status.BackendState,
                status.IsOnline,
                status.AddressCount));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            return ProbeErrors.Unavailable<TailscaleObservation>(
                "tailscale_query_failed",
                "Windows and Tailscale did not report a usable connection status.");
        }
    }
}
