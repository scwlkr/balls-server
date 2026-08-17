using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class AdministratorPreflightCheckTests
{
    [Theory]
    [InlineData(false, false, PreflightCheckStatus.ActionRequired, "administrator_membership_required")]
    [InlineData(false, true, PreflightCheckStatus.ActionRequired, "administrator_membership_required")]
    [InlineData(true, false, PreflightCheckStatus.ActionRequired, "administrator_elevation_required")]
    [InlineData(true, true, PreflightCheckStatus.Ready, "administrator_elevated")]
    public async Task CheckAsyncAppliesMembershipAndElevationPolicy(
        bool isAdministrator,
        bool isElevated,
        PreflightCheckStatus expectedStatus,
        string expectedReasonCode)
    {
        var check = new AdministratorPreflightCheck(new StubAdministratorProbe(
            ProbeResult.Observed(new AdministratorObservation(isAdministrator, isElevated))));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknownDiagnosticData()
    {
        var check = new AdministratorPreflightCheck(new StubAdministratorProbe(
            ProbeResult.Unavailable<AdministratorObservation>("access_denied", "Access was denied.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(PreflightCheckStatus.Unknown, result.Status);
        Assert.Equal("access_denied", result.ReasonCode);
        Assert.Equal("Access was denied.", result.Summary);
    }
}
