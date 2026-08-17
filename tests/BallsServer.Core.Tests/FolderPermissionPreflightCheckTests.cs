using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class FolderPermissionPreflightCheckTests
{
    [Theory]
    [InlineData(false, true, true, PreflightCheckStatus.ActionRequired, "folder_missing")]
    [InlineData(true, false, true, PreflightCheckStatus.ActionRequired, "folder_access_insufficient")]
    [InlineData(true, true, false, PreflightCheckStatus.ActionRequired, "folder_access_insufficient")]
    [InlineData(true, true, true, PreflightCheckStatus.Ready, "folder_access_ready")]
    public async Task CheckAsyncRequiresAnExistingFolderWithReadTraverseAndModifyRights(
        bool exists,
        bool canReadAndTraverse,
        bool canModify,
        PreflightCheckStatus expectedStatus,
        string expectedReasonCode)
    {
        var probe = new StubFolderPermissionProbe(ProbeResult.Observed(
            new FolderPermissionObservation(exists, canReadAndTraverse, canModify)));
        var check = new FolderPermissionPreflightCheck(probe);

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
        Assert.Equal(TestData.Context.TargetPath, probe.ObservedPath);
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknown()
    {
        var check = new FolderPermissionPreflightCheck(new StubFolderPermissionProbe(
            ProbeResult.Unavailable<FolderPermissionObservation>("folder_unavailable", "No folder data.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(PreflightCheckStatus.Unknown, result.Status);
        Assert.Equal("folder_unavailable", result.ReasonCode);
    }
}
