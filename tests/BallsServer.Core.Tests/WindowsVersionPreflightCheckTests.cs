using System.Runtime.InteropServices;
using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class WindowsVersionPreflightCheckTests
{
    [Theory]
    [InlineData(false, "Professional", 26100, PreflightCheckStatus.ActionRequired, "windows_required")]
    [InlineData(true, "Core", 26100, PreflightCheckStatus.ActionRequired, "windows_pro_required")]
    [InlineData(true, "Professional", 26099, PreflightCheckStatus.ActionRequired, "windows_build_too_old")]
    [InlineData(true, "Professional", 26100, PreflightCheckStatus.Ready, "windows_supported")]
    [InlineData(true, "professional", 26101, PreflightCheckStatus.Ready, "windows_supported")]
    public async Task CheckAsyncAppliesPlatformEditionAndInclusiveBuildThresholds(
        bool isWindows,
        string editionId,
        int build,
        PreflightCheckStatus expectedStatus,
        string expectedReasonCode)
    {
        var observation = new WindowsVersionObservation(
            isWindows,
            10,
            0,
            build,
            123,
            "Windows",
            editionId,
            "24H2",
            Architecture.X64);
        var check = new WindowsVersionPreflightCheck(new StubWindowsVersionProbe(
            ProbeResult.Observed(observation)));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknown()
    {
        var check = new WindowsVersionPreflightCheck(new StubWindowsVersionProbe(
            ProbeResult.Unavailable<WindowsVersionObservation>("version_unavailable", "No version.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(PreflightCheckStatus.Unknown, result.Status);
        Assert.Equal("version_unavailable", result.ReasonCode);
    }
}
