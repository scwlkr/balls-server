namespace BallsServer.Core.Preflight;

public sealed class FolderPermissionPreflightCheck(IFolderPermissionProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.FolderPermissions;

    public string Title => "Folder permissions";

    public int Order => 80;

    public async ValueTask<PreflightCheckResult> CheckAsync(
        PreflightContext context,
        CancellationToken cancellationToken)
    {
        var probeResult = await probe.ObserveAsync(context.TargetPath, cancellationToken).ConfigureAwait(false);
        if (!probeResult.HasValue)
        {
            return PreflightCheckHelpers.ProbeUnavailable(probeResult, Id, Title);
        }

        var observation = probeResult.Value!;
        var evidence = new[]
        {
            new PreflightEvidence("Folder exists", PreflightCheckHelpers.YesNo(observation.Exists)),
            new PreflightEvidence("Read and traverse", PreflightCheckHelpers.YesNo(observation.CanReadAndTraverse)),
            new PreflightEvidence("Modify", PreflightCheckHelpers.YesNo(observation.CanModify)),
        };

        if (!observation.Exists)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "folder_missing",
                "Choose an existing folder. This diagnostic will not create one.",
                evidence);
        }

        if (!observation.CanReadAndTraverse || !observation.CanModify)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "folder_access_insufficient",
                "The current security token does not have read, traverse, and modify access to this folder.",
                evidence);
        }

        return PreflightCheckResult.Create(
            Id,
            Title,
            PreflightCheckStatus.Ready,
            "folder_access_ready",
            "The current security token can read and modify the selected folder.",
            evidence);
    }
}
