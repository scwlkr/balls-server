using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BallsServer.Core.Sharing;

namespace BallsServer.Windows;

public sealed partial record HostSetupMutationPreview(string PlanDigest, long Revision)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static HostSetupMutationPreview Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            var envelope = JsonSerializer.Deserialize<PreviewEnvelope>(json, JsonOptions) ??
                throw new FormatException("Host setup preview returned an incomplete result.");
            if (envelope.Status != "PreviewReady" || envelope.Revision < 0 ||
                !DigestPattern().IsMatch(envelope.PlanDigest))
            {
                throw new FormatException("Host setup preview returned an invalid result.");
            }

            return new HostSetupMutationPreview(envelope.PlanDigest, envelope.Revision);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new FormatException("Host setup preview returned a malformed result.", exception);
        }
    }

    public override string ToString() =>
        $"MutationPreview {{ PlanDigest = {PlanDigest}, Revision = {Revision} }}";

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();

    private sealed record PreviewEnvelope
    {
        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("planDigest")]
        public required string PlanDigest { get; init; }

        [JsonPropertyName("revision")]
        public required long Revision { get; init; }
    }
}

public sealed partial record HostSetupMutationOutput(
    string HostName,
    string ShareName,
    string UserName,
    bool AlreadyConfigured = false,
    bool SharingStopped = false)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static HostSetupMutationOutput Parse(string json, AccessPathKind accessPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var envelope = JsonSerializer.Deserialize<OutputEnvelope>(json, JsonOptions) ??
                throw new FormatException("Host setup returned an incomplete result.");
            if (envelope.ShareName != "Balls" ||
                string.IsNullOrWhiteSpace(envelope.UserName) ||
                !UserNamePattern().IsMatch(envelope.UserName) ||
                string.IsNullOrWhiteSpace(envelope.HostName) ||
                !IsAllowedEndpoint(envelope.HostName, accessPath))
            {
                throw new FormatException("Host setup returned an invalid result.");
            }

            return new HostSetupMutationOutput(
                envelope.HostName,
                envelope.ShareName,
                envelope.UserName,
                envelope.AlreadyConfigured);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new FormatException("Host setup returned a malformed result.", exception);
        }
    }

    public static HostSetupMutationOutput ParseStopSharing(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var envelope = JsonSerializer.Deserialize<StopOutputEnvelope>(json, JsonOptions) ??
                throw new FormatException("Stop Sharing returned an incomplete result.");
            if (envelope.Status != "Stopped" || envelope.ShareName != "Balls")
            {
                throw new FormatException("Stop Sharing returned an invalid result.");
            }

            return new HostSetupMutationOutput(string.Empty, envelope.ShareName, string.Empty, false, true);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new FormatException("Stop Sharing returned a malformed result.", exception);
        }
    }

    public override string ToString() =>
        $"HostSetupMutationOutput {{ HostName = [REDACTED], ShareName = {ShareName}, " +
        $"UserName = [REDACTED], AlreadyConfigured = {AlreadyConfigured}, " +
        $"SharingStopped = {SharingStopped} }}";

    private static bool IsAllowedEndpoint(string hostName, AccessPathKind accessPath)
    {
        if (!IPAddress.TryParse(hostName, out var address))
        {
            return accessPath switch
            {
                AccessPathKind.Local => HostNamePattern().IsMatch(hostName),
                AccessPathKind.Tailscale => hostName.EndsWith(
                    ".ts.net",
                    StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return accessPath switch
        {
            AccessPathKind.Local =>
                bytes[0] == 10 ||
                bytes[0] == 192 && bytes[1] == 168 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31,
            AccessPathKind.Tailscale => false,
            _ => false,
        };
    }

    [GeneratedRegex(
        "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,13}[A-Za-z0-9])?\\\\BallsClient-[A-Z0-9]{6}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,13}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostNamePattern();

    private sealed record OutputEnvelope
    {
        [JsonPropertyName("hostName")]
        public required string HostName { get; init; }

        [JsonPropertyName("shareName")]
        public required string ShareName { get; init; }

        [JsonPropertyName("userName")]
        public required string UserName { get; init; }

        [JsonPropertyName("alreadyConfigured")]
        public bool AlreadyConfigured { get; init; }
    }

    private sealed record StopOutputEnvelope
    {
        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("shareName")]
        public required string ShareName { get; init; }
    }
}

public interface IHostSetupMutator
{
    Task<HostSetupMutationPreview> PreviewAsync(
        HostSetupMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<HostSetupMutationOutput> ApplyAsync(
        HostSetupMutationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PowerShellHostSetupMutator : IHostSetupMutator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private const int MaximumOutputCharacters = 16 * 1024;

    public async Task<HostSetupMutationPreview> PreviewAsync(
        HostSetupMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(
            HostSetupPowerShellCommand.CreatePreview(request),
            cancellationToken).ConfigureAwait(false);
        try
        {
            return HostSetupMutationPreview.Parse(output.Trim());
        }
        catch (FormatException)
        {
            throw new HostSetupMutationException();
        }
    }

    public async Task<HostSetupMutationOutput> ApplyAsync(
        HostSetupMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(
            HostSetupPowerShellCommand.Create(request),
            cancellationToken).ConfigureAwait(false);
        try
        {
            return request.Operation == HostSetupOperation.StopSharing
                ? HostSetupMutationOutput.ParseStopSharing(output.Trim())
                : HostSetupMutationOutput.Parse(output.Trim(), request.AccessPath);
        }
        catch (FormatException)
        {
            throw new HostSetupMutationException();
        }
    }

    private static async Task<string> RunAsync(
        HostSetupPowerShellCommand command,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = command.StartInfo };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            if (!process.Start())
            {
                throw new HostSetupMutationException();
            }

            var outputTask = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.StandardInput.WriteAsync(command.StandardInput.AsMemory(), timeout.Token)
                .ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new HostSetupMutationException();
            }

            return output;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new HostSetupMutationException();
        }
        catch (HostSetupMutationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or FormatException)
        {
            TryKill(process);
            throw new HostSetupMutationException();
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        var output = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToString();
            }

            if (output.Length + read > MaximumOutputCharacters)
            {
                throw new HostSetupMutationException();
            }

            output.Append(buffer, 0, read);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class HostSetupMutationException : Exception
{
    public HostSetupMutationException()
        : base("The protected host setup operation did not complete.")
    {
    }
}
