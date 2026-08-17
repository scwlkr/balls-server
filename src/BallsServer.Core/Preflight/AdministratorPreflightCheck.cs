namespace BallsServer.Core.Preflight;

public sealed class AdministratorPreflightCheck(IAdministratorProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.Administrator;

    public string Title => "Administrator access";

    public int Order => 10;

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
            new PreflightEvidence("Administrator account", PreflightCheckHelpers.YesNo(observation.IsAdministrator)),
            new PreflightEvidence("Process elevated", PreflightCheckHelpers.YesNo(observation.IsElevated)),
        };

        if (!observation.IsAdministrator)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "administrator_membership_required",
                "A different administrator may need to approve future Host Files setup. This dashboard stays unelevated.",
                evidence);
        }

        return observation.IsElevated
            ? PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Ready,
                "administrator_elevated",
                "This process is elevated, although Host Files readiness does not require elevation. Future setup approval is separate.",
                evidence)
            : PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "administrator_elevation_required",
                "This account can approve future Host Files setup when asked. This dashboard stays unelevated.",
                evidence);
    }
}
