using System.Diagnostics;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class BoundedReadOnlyProcessRunnerTests
{
    [Fact]
    public async Task RunAsyncRejectsShellOrUnredirectedExecutionBeforeStartingAProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "this-process-must-never-start.exe",
            UseShellExecute = true,
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => BoundedReadOnlyProcessRunner.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(1),
                100,
                CancellationToken.None).AsTask());

        Assert.Equal("startInfo", exception.ParamName);
    }

    [Fact]
    public async Task RunAsyncHonorsPreCancellationBeforeStartingAProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "this-process-must-never-start.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BoundedReadOnlyProcessRunner.RunAsync(
                startInfo,
                TimeSpan.FromSeconds(1),
                100,
                cancellation.Token).AsTask());
    }
}
