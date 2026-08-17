using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class FolderPermissionProbeTests
{
    [Fact]
    public async Task ProbeReturnsTheTypedAclEvaluation()
    {
        const string targetPath = @"C:\Host";
        var observation = new FolderPermissionObservation(true, true, false);
        var evaluator = new StubFolderAccessEvaluator(_ => observation);
        var probe = new WindowsFolderPermissionProbe(evaluator);

        var result = await probe.ObserveAsync(targetPath, CancellationToken.None);

        Assert.True(result.HasValue);
        Assert.Equal(observation, result.Value);
        Assert.Equal(targetPath, evaluator.TargetPath);
    }

    [Fact]
    public async Task ProbeConvertsExpectedAclFailureToSanitizedUnavailableResult()
    {
        var evaluator = new StubFolderAccessEvaluator(
            _ => throw new UnauthorizedAccessException("secret account or ACL detail"));
        var probe = new WindowsFolderPermissionProbe(evaluator);

        var result = await probe.ObserveAsync(@"C:\Host", CancellationToken.None);

        Assert.False(result.HasValue);
        Assert.Equal("folder_acl_query_failed", result.ErrorCode);
        Assert.Equal(
            "Windows did not report the effective permissions for the selected folder.",
            result.ErrorMessage);
        Assert.DoesNotContain("secret", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbePropagatesCallerCancellationWithoutInvokingEvaluator()
    {
        var evaluator = new StubFolderAccessEvaluator(
            _ => throw new InvalidOperationException("Evaluator should not run."));
        var probe = new WindowsFolderPermissionProbe(evaluator);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.ObserveAsync(@"C:\Host", cancellation.Token).AsTask());

        Assert.Null(evaluator.TargetPath);
    }
}
