using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class StoragePreflightCheckTests
{
    [Theory]
    [InlineData(DriveType.Unknown, "NTFS", 100, PreflightCheckStatus.Unknown, "storage_drive_type_unknown")]
    [InlineData(DriveType.Removable, "ReFS", 99, PreflightCheckStatus.ActionRequired, "fixed_local_disk_required")]
    [InlineData(DriveType.Fixed, "ReFS", 99, PreflightCheckStatus.ActionRequired, "ntfs_required")]
    [InlineData(DriveType.Fixed, "NTFS", 99, PreflightCheckStatus.ActionRequired, "insufficient_free_space")]
    [InlineData(DriveType.Fixed, "NTFS", 100, PreflightCheckStatus.Ready, "storage_supported")]
    [InlineData(DriveType.Fixed, "ntfs", 101, PreflightCheckStatus.Ready, "storage_supported")]
    public async Task CheckAsyncRequiresFixedNtfsStorageAtTheInclusiveFreeSpaceThreshold(
        DriveType driveType,
        string fileSystem,
        long freeBytes,
        PreflightCheckStatus expectedStatus,
        string expectedReasonCode)
    {
        var probe = new StubStorageProbe(ProbeResult.Observed(new StorageObservation(
            @"C:\",
            driveType,
            fileSystem,
            freeBytes,
            1_000)));
        var check = new StoragePreflightCheck(probe);

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
        Assert.Equal(TestData.Context.TargetPath, probe.ObservedPath);
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknown()
    {
        var check = new StoragePreflightCheck(new StubStorageProbe(
            ProbeResult.Unavailable<StorageObservation>("volume_missing", "No volume.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(PreflightCheckStatus.Unknown, result.Status);
        Assert.Equal("volume_missing", result.ReasonCode);
    }
}
