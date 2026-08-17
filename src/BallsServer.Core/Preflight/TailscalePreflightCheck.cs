using System.Globalization;

namespace BallsServer.Core.Preflight;

public sealed class TailscalePreflightCheck(ITailscaleProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.Tailscale;

    public string Title => "Tailscale";

    public int Order => 60;

    public async ValueTask<PreflightCheckResult> CheckAsync(
        PreflightContext context,
        CancellationToken cancellationToken)
    {
        var probeResult = await probe.ObserveAsync(cancellationToken).ConfigureAwait(false);
        if (!probeResult.HasValue)
        {
            return PreflightCheckHelpers.ProbeUnavailable(probeResult, Id, Title);
        }

        var observation = probeResult.Value!;
        var evidence = new[]
        {
            new PreflightEvidence("Installed", PreflightCheckHelpers.YesNo(observation.IsInstalled)),
            new PreflightEvidence("Service", observation.ServiceState.ToString()),
            new PreflightEvidence("Backend", observation.BackendState),
            new PreflightEvidence("Online", PreflightCheckHelpers.YesNo(observation.IsOnline)),
            new PreflightEvidence("Assigned addresses", observation.AddressCount.ToString(CultureInfo.InvariantCulture)),
        };

        if (!observation.IsInstalled || observation.ServiceState == WindowsServiceState.NotInstalled)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "tailscale_not_installed",
                "Tailscale is not installed. This diagnostic will not install it.",
                evidence);
        }

        if (observation.ServiceState == WindowsServiceState.Unknown)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "tailscale_service_state_unknown",
                "Windows did not report a recognized Tailscale service state.",
                evidence);
        }

        if (observation.ServiceState != WindowsServiceState.Running)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "tailscale_service_not_running",
                "The Tailscale service is not running.",
                evidence);
        }

        if (!string.Equals(observation.BackendState, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "tailscale_not_connected",
                "Tailscale is installed but is not signed in and connected.",
                evidence);
        }

        if (!observation.IsOnline || observation.AddressCount == 0)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "tailscale_offline",
                "Tailscale is not currently online with an assigned address.",
                evidence);
        }

        return PreflightCheckResult.Create(
            Id,
            Title,
            PreflightCheckStatus.Ready,
            "tailscale_connected",
            "Tailscale is running and connected.",
            evidence);
    }
}
