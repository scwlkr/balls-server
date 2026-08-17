namespace BallsServer.Core.Preflight;

public sealed class WindowsVersionPreflightCheck(IWindowsVersionProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.WindowsVersion;

    public string Title => "Windows edition and version";

    public int Order => 20;

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
            new PreflightEvidence("Product", observation.ProductName),
            new PreflightEvidence("Edition", observation.EditionId),
            new PreflightEvidence("Display version", observation.DisplayVersion),
            new PreflightEvidence("Build", $"{observation.Build}.{observation.Revision}"),
            new PreflightEvidence("Architecture", observation.Architecture.ToString()),
        };

        if (!observation.IsWindows)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "windows_required",
                "Balls Server v0.1 supports Windows 11 only.",
                evidence);
        }

        if (!context.Policy.SupportsEdition(observation.EditionId))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "windows_pro_required",
                "Windows 11 Pro is required for the supported hosting configuration.",
                evidence);
        }

        if (observation.Build < context.Policy.MinimumWindowsBuild)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "windows_build_too_old",
                $"Windows 11 24H2 or later is required (build {context.Policy.MinimumWindowsBuild} or newer).",
                evidence);
        }

        return PreflightCheckResult.Create(
            Id,
            Title,
            PreflightCheckStatus.Ready,
            "windows_supported",
            "This Windows edition and version are supported.",
            evidence);
    }
}
