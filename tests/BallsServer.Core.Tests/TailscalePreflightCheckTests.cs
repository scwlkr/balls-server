using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class TailscalePreflightCheckTests
{
    [Theory]
    [InlineData(false, WindowsServiceState.Running, "Running", true, 1, PreflightCheckStatus.ActionRequired, "tailscale_not_installed")]
    [InlineData(true, WindowsServiceState.NotInstalled, "Running", true, 1, PreflightCheckStatus.ActionRequired, "tailscale_not_installed")]
    [InlineData(true, WindowsServiceState.Unknown, "Running", true, 1, PreflightCheckStatus.Unknown, "tailscale_service_state_unknown")]
    [InlineData(true, WindowsServiceState.Stopped, "Running", true, 1, PreflightCheckStatus.ActionRequired, "tailscale_service_not_running")]
    [InlineData(true, WindowsServiceState.Running, "NeedsLogin", true, 1, PreflightCheckStatus.ActionRequired, "tailscale_not_connected")]
    [InlineData(true, WindowsServiceState.Running, "running", false, 1, PreflightCheckStatus.ActionRequired, "tailscale_offline")]
    [InlineData(true, WindowsServiceState.Running, "Running", true, 0, PreflightCheckStatus.ActionRequired, "tailscale_offline")]
    [InlineData(true, WindowsServiceState.Running, "running", true, 1, PreflightCheckStatus.Ready, "tailscale_connected")]
    public async Task CheckAsyncAppliesInstallationServiceBackendAndOnlinePolicyInOrder(
        bool isInstalled,
        WindowsServiceState serviceState,
        string backendState,
        bool isOnline,
        int addressCount,
        PreflightCheckStatus expectedStatus,
        string expectedReasonCode)
    {
        var observation = new TailscaleObservation(
            isInstalled,
            serviceState,
            backendState,
            isOnline,
            addressCount);
        var check = new TailscalePreflightCheck(new StubTailscaleProbe(ProbeResult.Observed(observation)));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknown()
    {
        var check = new TailscalePreflightCheck(new StubTailscaleProbe(
            ProbeResult.Unavailable<TailscaleObservation>("tailscale_unavailable", "No Tailscale data.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(PreflightCheckStatus.Unknown, result.Status);
        Assert.Equal("tailscale_unavailable", result.ReasonCode);
    }
}
