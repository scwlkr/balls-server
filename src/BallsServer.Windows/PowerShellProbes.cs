using System.Text.Json;
using BallsServer.Core.Preflight;

namespace BallsServer.Windows;

internal sealed class WindowsNetworkProfileProbe(IPowerShellJsonSource source) : INetworkProfileProbe
{
    public async ValueTask<ProbeResult<NetworkProfileObservation>> ObserveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await source.QueryAsync(
                PowerShellQuery.ConnectedNetworkProfiles,
                cancellationToken).ConfigureAwait(false);
            return ProbeResult.Observed(NetworkProfileJsonParser.Parse(json));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            return ProbeErrors.Unavailable<NetworkProfileObservation>(
                "network_profile_query_failed",
                "Windows did not report the connected network profiles.");
        }
    }
}

internal static class NetworkProfileJsonParser
{
    internal static NetworkProfileObservation Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var profiles = new List<NetworkConnectionProfile>();

        foreach (var element in JsonHelpers.EnumerateObjectOrArray(document.RootElement))
        {
            var alias = JsonHelpers.RequiredString(element, "InterfaceAlias");
            var categoryText = JsonHelpers.RequiredString(element, "NetworkCategory");
            var category = categoryText switch
            {
                "Public" => NetworkCategory.Public,
                "Private" => NetworkCategory.Private,
                "DomainAuthenticated" => NetworkCategory.DomainAuthenticated,
                _ => NetworkCategory.Unknown,
            };
            profiles.Add(new NetworkConnectionProfile(alias, category));
        }

        return new NetworkProfileObservation(profiles.AsReadOnly());
    }
}

internal sealed class WindowsFirewallProbe(IPowerShellJsonSource source) : IFirewallProbe
{
    public async ValueTask<ProbeResult<FirewallObservation>> ObserveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await source.QueryAsync(
                PowerShellQuery.FirewallProfiles,
                cancellationToken).ConfigureAwait(false);
            return ProbeResult.Observed(FirewallJsonParser.Parse(json));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            return ProbeErrors.Unavailable<FirewallObservation>(
                "firewall_query_failed",
                "Windows did not report the effective firewall profiles.");
        }
    }
}

internal static class FirewallJsonParser
{
    internal static FirewallObservation Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var profiles = new List<FirewallProfileObservation>();

        foreach (var element in JsonHelpers.EnumerateObjectOrArray(document.RootElement))
        {
            var profile = JsonHelpers.RequiredString(element, "Profile") switch
            {
                "Domain" => FirewallProfileKind.Domain,
                "Private" => FirewallProfileKind.Private,
                "Public" => FirewallProfileKind.Public,
                _ => FirewallProfileKind.Unknown,
            };
            var enabled = JsonHelpers.RequiredBoolean(element, "Enabled");
            var inbound = ParseAction(JsonHelpers.RequiredString(element, "DefaultInboundAction"));
            var outbound = ParseAction(JsonHelpers.RequiredString(element, "DefaultOutboundAction"));
            profiles.Add(new FirewallProfileObservation(profile, enabled, inbound, outbound));
        }

        return new FirewallObservation(profiles.AsReadOnly());
    }

    private static FirewallDefaultAction ParseAction(string value) => value switch
    {
        "Allow" => FirewallDefaultAction.Allow,
        "Block" => FirewallDefaultAction.Block,
        "NotConfigured" => FirewallDefaultAction.NotConfigured,
        _ => FirewallDefaultAction.Unknown,
    };
}

internal sealed class WindowsSmbProbe(
    IWindowsServiceStatusSource serviceStatus,
    IPowerShellJsonSource source) : ISmbProbe
{
    public async ValueTask<ProbeResult<SmbObservation>> ObserveAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WindowsServiceStatus service;
        try
        {
            service = serviceStatus.Query("LanmanServer");
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            return SmbUnavailable();
        }

        if (!service.IsInstalled)
        {
            return ProbeResult.Observed(WithoutConfiguration(service.State));
        }

        try
        {
            var json = await source.QueryAsync(
                PowerShellQuery.SmbServerConfiguration,
                cancellationToken).ConfigureAwait(false);
            var configuration = SmbConfigurationJsonParser.Parse(json);

            return ProbeResult.Observed(new SmbObservation(
                service.State,
                configuration.IsSmb1Enabled,
                configuration.IsSmb2Enabled,
                configuration.EncryptData,
                configuration.DialectRange));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            return service.State == WindowsServiceState.Running
                ? SmbUnavailable()
                : ProbeResult.Observed(WithoutConfiguration(service.State));
        }
    }

    private static SmbObservation WithoutConfiguration(WindowsServiceState serviceState) => new(
        serviceState,
        IsSmb1Enabled: null,
        IsSmb2Enabled: null,
        EncryptData: null,
        DialectRange: new SmbDialectRange(null, null));

    private static ProbeResult<SmbObservation> SmbUnavailable() =>
        ProbeErrors.Unavailable<SmbObservation>(
            "smb_query_failed",
            "Windows did not report the SMB server state and configuration.");
}

internal readonly record struct SmbConfiguration(
    bool? IsSmb1Enabled,
    bool? IsSmb2Enabled,
    SmbDialectRange DialectRange,
    bool? EncryptData);

internal static class SmbConfigurationJsonParser
{
    internal static SmbConfiguration Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = JsonHelpers.RequireSingleObject(document.RootElement);
        return new SmbConfiguration(
            JsonHelpers.OptionalBoolean(root, "EnableSMB1Protocol"),
            JsonHelpers.OptionalBoolean(root, "EnableSMB2Protocol"),
            new SmbDialectRange(
                ParseDialect(root, "Smb2DialectMin"),
                ParseDialect(root, "Smb2DialectMax")),
            JsonHelpers.OptionalBoolean(root, "EncryptData"));
    }

    private static SmbDialect? ParseDialect(JsonElement element, string propertyName)
    {
        var value = JsonHelpers.OptionalString(element, propertyName);
        return value switch
        {
            null => null,
            "None" => SmbDialect.NoLimit,
            "SMB202" => SmbDialect.Smb202,
            "SMB210" => SmbDialect.Smb210,
            "SMB300" => SmbDialect.Smb300,
            "SMB302" => SmbDialect.Smb302,
            "SMB311" => SmbDialect.Smb311,
            _ => SmbDialect.Unknown,
        };
    }
}

internal static class JsonHelpers
{
    internal static IEnumerable<JsonElement> EnumerateObjectOrArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new ReadOnlyProbeException("The query returned an invalid JSON item.");
                }

                yield return item;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Null)
        {
            throw new ReadOnlyProbeException("The query returned invalid JSON data.");
        }
    }

    internal static JsonElement RequireSingleObject(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1)
        {
            var item = root[0];
            if (item.ValueKind == JsonValueKind.Object)
            {
                return item;
            }
        }

        throw new ReadOnlyProbeException("The query returned an invalid JSON object.");
    }

    internal static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new ReadOnlyProbeException("The query omitted a required string value.");
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ReadOnlyProbeException("The query returned an empty required string value.");
        }

        return value;
    }

    internal static bool RequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ReadOnlyProbeException("The query omitted a required Boolean value.");
        }

        return property.GetBoolean();
    }

    internal static bool? OptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ReadOnlyProbeException("The query returned an invalid Boolean value."),
        };
    }

    internal static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new ReadOnlyProbeException("The query returned an invalid string value.");
        }

        return property.GetString();
    }
}
