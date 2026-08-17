namespace BallsServer.Core.Preflight;

public sealed class FirewallPreflightCheck(IFirewallProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.Firewall;

    public string Title => "Windows Firewall";

    public int Order => 50;

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
            return PreflightCheckResult.Unknown(
                Id,
                Title,
                "firewall_profiles_missing",
                "Windows did not return any firewall profiles.");
        }

        var evidence = profiles
            .Select(profile => new PreflightEvidence(
                profile.Profile.ToString(),
                $"{(profile.Enabled ? "Enabled" : "Disabled")}; inbound {profile.DefaultInboundAction}"))
            .ToArray();

        if (profiles.Any(static profile => !profile.Enabled))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "firewall_disabled",
                "Windows Firewall must be enabled on every profile before hosting files.",
                evidence);
        }

        if (profiles.Any(static profile => profile.DefaultInboundAction == FirewallDefaultAction.Allow))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "firewall_inbound_allowed_by_default",
                "A firewall profile allows inbound traffic by default.",
                evidence);
        }

        var requiredProfiles = new[]
        {
            FirewallProfileKind.Domain,
            FirewallProfileKind.Private,
            FirewallProfileKind.Public,
        };

        if (profiles.Count != requiredProfiles.Length ||
            profiles.Select(static profile => profile.Profile).Distinct().Count() != requiredProfiles.Length ||
            requiredProfiles.Any(required => profiles.All(profile => profile.Profile != required)))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "firewall_profiles_incomplete",
                "Windows did not report one distinct Domain, Private, and Public firewall profile.",
                evidence);
        }

        if (profiles.Any(static profile =>
                profile.DefaultInboundAction is FirewallDefaultAction.Unknown or FirewallDefaultAction.NotConfigured))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "firewall_default_unknown",
                "Windows did not report an effective default inbound action for every firewall profile.",
                evidence);
        }

        if (profiles.Any(static profile => profile.DefaultOutboundAction == FirewallDefaultAction.Block))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Warning,
                "firewall_outbound_restricted",
                "Inbound defaults are safe, but at least one profile blocks outbound traffic by default.",
                evidence);
        }

        return PreflightCheckResult.Create(
            Id,
            Title,
            PreflightCheckStatus.Ready,
            "firewall_enabled",
            "Windows Firewall is enabled and blocks unsolicited inbound traffic by default.",
            evidence);
    }
}
