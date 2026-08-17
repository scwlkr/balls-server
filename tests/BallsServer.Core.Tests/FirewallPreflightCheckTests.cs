using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class FirewallPreflightCheckTests
{
    [Fact]
    public async Task CheckAsyncWithNoProfilesReturnsUnknown()
    {
        var result = await CheckAsync();

        AssertResult(result, PreflightCheckStatus.Unknown, "firewall_profiles_missing");
    }

    [Fact]
    public async Task CheckAsyncWhenAnyProfileIsDisabledRequiresActionFirst()
    {
        var result = await CheckAsync(
            Profile(FirewallProfileKind.Domain, enabled: true, FirewallDefaultAction.Block),
            Profile(FirewallProfileKind.Private, enabled: false, FirewallDefaultAction.Allow),
            Profile(FirewallProfileKind.Public, enabled: true, FirewallDefaultAction.Block));

        AssertResult(result, PreflightCheckStatus.ActionRequired, "firewall_disabled");
    }

    [Fact]
    public async Task CheckAsyncWhenInboundIsAllowedByDefaultRequiresAction()
    {
        var result = await CheckAsync(CompleteProfiles(inboundAction: FirewallDefaultAction.Allow));

        AssertResult(result, PreflightCheckStatus.ActionRequired, "firewall_inbound_allowed_by_default");
    }

    [Theory]
    [InlineData(FirewallDefaultAction.Unknown)]
    [InlineData(FirewallDefaultAction.NotConfigured)]
    public async Task CheckAsyncWhenInboundDefaultIsNotKnownFailsClosed(
        FirewallDefaultAction inboundAction)
    {
        var result = await CheckAsync(CompleteProfiles(inboundAction));

        AssertResult(result, PreflightCheckStatus.Unknown, "firewall_default_unknown");
    }

    [Fact]
    public async Task CheckAsyncWhenOutboundIsBlockedReturnsWarning()
    {
        var result = await CheckAsync(CompleteProfiles(outboundAction: FirewallDefaultAction.Block));

        AssertResult(result, PreflightCheckStatus.Warning, "firewall_outbound_restricted");
    }

    [Fact]
    public async Task CheckAsyncWhenEnabledAndInboundIsBlockedIsReady()
    {
        var result = await CheckAsync(CompleteProfiles());

        AssertResult(result, PreflightCheckStatus.Ready, "firewall_enabled");
    }

    [Fact]
    public async Task CheckAsyncWhenARequiredProfileIsMissingFailsClosed()
    {
        var result = await CheckAsync(
            Profile(FirewallProfileKind.Domain, enabled: true, FirewallDefaultAction.Block),
            Profile(FirewallProfileKind.Private, enabled: true, FirewallDefaultAction.Block));

        AssertResult(result, PreflightCheckStatus.Unknown, "firewall_profiles_incomplete");
    }

    [Fact]
    public async Task CheckAsyncWhenAProfileIsDuplicatedFailsClosed()
    {
        var result = await CheckAsync(
            Profile(FirewallProfileKind.Domain, enabled: true, FirewallDefaultAction.Block),
            Profile(FirewallProfileKind.Private, enabled: true, FirewallDefaultAction.Block),
            Profile(FirewallProfileKind.Private, enabled: true, FirewallDefaultAction.Block));

        AssertResult(result, PreflightCheckStatus.Unknown, "firewall_profiles_incomplete");
    }

    [Fact]
    public async Task CheckAsyncWhenAProfileKindIsUnknownFailsClosed()
    {
        var result = await CheckAsync(
            Profile(FirewallProfileKind.Domain, enabled: true, FirewallDefaultAction.Block),
            Profile(FirewallProfileKind.Private, enabled: true, FirewallDefaultAction.Block),
            Profile(FirewallProfileKind.Unknown, enabled: true, FirewallDefaultAction.Block));

        AssertResult(result, PreflightCheckStatus.Unknown, "firewall_profiles_incomplete");
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknown()
    {
        var check = new FirewallPreflightCheck(new StubFirewallProbe(
            ProbeResult.Unavailable<FirewallObservation>("firewall_unavailable", "No firewall data.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        AssertResult(result, PreflightCheckStatus.Unknown, "firewall_unavailable");
    }

    private static FirewallProfileObservation Profile(
        FirewallProfileKind kind,
        bool enabled,
        FirewallDefaultAction inboundAction,
        FirewallDefaultAction outboundAction = FirewallDefaultAction.Allow) =>
        new(kind, enabled, inboundAction, outboundAction);

    private static FirewallProfileObservation[] CompleteProfiles(
        FirewallDefaultAction inboundAction = FirewallDefaultAction.Block,
        FirewallDefaultAction outboundAction = FirewallDefaultAction.Allow) =>
    [
        Profile(FirewallProfileKind.Domain, enabled: true, inboundAction, outboundAction),
        Profile(FirewallProfileKind.Private, enabled: true, inboundAction, outboundAction),
        Profile(FirewallProfileKind.Public, enabled: true, inboundAction, outboundAction),
    ];

    private static async Task<PreflightCheckResult> CheckAsync(
        params FirewallProfileObservation[] profiles)
    {
        var check = new FirewallPreflightCheck(new StubFirewallProbe(
            ProbeResult.Observed(new FirewallObservation(profiles))));

        return await check.CheckAsync(TestData.Context, CancellationToken.None);
    }

    private static void AssertResult(
        PreflightCheckResult result,
        PreflightCheckStatus expectedStatus,
        string expectedReasonCode)
    {
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }
}
