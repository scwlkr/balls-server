namespace BallsServer.Core.Preflight;

public sealed class StoragePreflightCheck(IStorageProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.Storage;

    public string Title => "Storage location";

    public int Order => 30;

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
            new PreflightEvidence("Volume", observation.VolumeRoot),
            new PreflightEvidence("Drive type", observation.DriveType.ToString()),
            new PreflightEvidence("File system", observation.FileSystem),
            new PreflightEvidence("Free space", PreflightCheckHelpers.FormatBytes(observation.AvailableFreeBytes)),
            new PreflightEvidence("Total space", PreflightCheckHelpers.FormatBytes(observation.TotalBytes)),
        };

        if (observation.DriveType == DriveType.Unknown)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "storage_drive_type_unknown",
                "Windows could not determine the selected folder's drive type.",
                evidence);
        }

        if (observation.DriveType != DriveType.Fixed)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "fixed_local_disk_required",
                "Choose a folder on a fixed local disk.",
                evidence);
        }

        if (!string.Equals(
                observation.FileSystem,
                context.Policy.RequiredFileSystem,
                StringComparison.OrdinalIgnoreCase))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "ntfs_required",
                $"The hosting folder must be on a {context.Policy.RequiredFileSystem} volume.",
                evidence);
        }

        if (observation.AvailableFreeBytes < context.Policy.MinimumFreeBytes)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "insufficient_free_space",
                $"At least {PreflightCheckHelpers.FormatBytes(context.Policy.MinimumFreeBytes)} of free space is required.",
                evidence);
        }

        return PreflightCheckResult.Create(
            Id,
            Title,
            PreflightCheckStatus.Ready,
            "storage_supported",
            "The selected folder is on a supported local volume.",
            evidence);
    }
}
