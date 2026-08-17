using System.Globalization;

namespace BallsServer.Core.Preflight;

public sealed class NetworkProfilePreflightCheck(INetworkProfileProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.NetworkProfile;

    public string Title => "Network profile";

    public int Order => 40;

    public async ValueTask<PreflightCheckResult> CheckAsync(
        PreflightContext context,
        CancellationToken cancellationToken)
    {
        var probeResult = await probe.ObserveAsync(cancellationToken).ConfigureAwait(false);
        if (!probeResult.HasValue)
        {
            return PreflightCheckHelpers.ProbeUnavailable(probeResult, Id, Title);
        }

        var profiles = probeResult.Value!.Profiles;
        if (profiles.Count == 0)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "no_connected_network",
                "No connected Windows network profile was found.");
        }

        var trustedCount = profiles.Count(static profile =>
            profile.Category is NetworkCategory.Private or NetworkCategory.DomainAuthenticated);
        var publicCount = profiles.Count(static profile => profile.Category == NetworkCategory.Public);
        var unknownCount = profiles.Count(static profile => profile.Category == NetworkCategory.Unknown);
        var evidence = new[]
        {
            new PreflightEvidence("Connected profiles", profiles.Count.ToString(CultureInfo.InvariantCulture)),
            new PreflightEvidence("Private or domain", trustedCount.ToString(CultureInfo.InvariantCulture)),
            new PreflightEvidence("Public", publicCount.ToString(CultureInfo.InvariantCulture)),
            new PreflightEvidence("Unknown", unknownCount.ToString(CultureInfo.InvariantCulture)),
        };

        if (trustedCount == 0 && publicCount > 0 && unknownCount == 0)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "public_network_only",
                "The PC is connected only through a Public Windows network profile.",
                evidence);
        }

        if (trustedCount == 0)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "network_category_unknown",
                "Windows did not identify a trusted connected network profile.",
                evidence);
        }

        if (publicCount > 0 || unknownCount > 0)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Warning,
                "mixed_network_profiles",
                "A trusted profile is available, but another connected profile is Public or unknown.",
                evidence);
        }

        return PreflightCheckResult.Create(
            Id,
            Title,
            PreflightCheckStatus.Ready,
            "trusted_network_profile",
            "Connected networks use Private or domain-authenticated profiles.",
            evidence);
    }
}
