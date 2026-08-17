using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class NetworkProfilePreflightCheckTests
{
    [Fact]
    public async Task CheckAsyncWithNoConnectedProfilesRequiresAction()
    {
        var result = await CheckAsync();

        AssertResult(result, PreflightCheckStatus.ActionRequired, "no_connected_network");
    }

    [Fact]
    public async Task CheckAsyncWithOnlyPublicProfilesRequiresAction()
    {
        var result = await CheckAsync(NetworkCategory.Public, NetworkCategory.Public);

        AssertResult(result, PreflightCheckStatus.ActionRequired, "public_network_only");
    }

    [Fact]
    public async Task CheckAsyncWithOnlyUnknownProfilesFailsClosed()
    {
        var result = await CheckAsync(NetworkCategory.Unknown);

        AssertResult(result, PreflightCheckStatus.Unknown, "network_category_unknown");
    }

    [Fact]
    public async Task CheckAsyncWithPublicAndUnknownButNoTrustedProfileFailsClosed()
    {
        var result = await CheckAsync(NetworkCategory.Public, NetworkCategory.Unknown);

        AssertResult(result, PreflightCheckStatus.Unknown, "network_category_unknown");
    }

    [Theory]
    [InlineData(NetworkCategory.Public)]
    [InlineData(NetworkCategory.Unknown)]
    public async Task CheckAsyncWithTrustedAndUntrustedProfilesReturnsWarning(
        NetworkCategory untrustedCategory)
    {
        var result = await CheckAsync(NetworkCategory.Private, untrustedCategory);

        AssertResult(result, PreflightCheckStatus.Warning, "mixed_network_profiles");
    }

    [Theory]
    [InlineData(NetworkCategory.Private)]
    [InlineData(NetworkCategory.DomainAuthenticated)]
    public async Task CheckAsyncWithOnlyTrustedProfilesIsReady(NetworkCategory trustedCategory)
    {
        var result = await CheckAsync(trustedCategory, NetworkCategory.Private);

        AssertResult(result, PreflightCheckStatus.Ready, "trusted_network_profile");
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknown()
    {
        var check = new NetworkProfilePreflightCheck(new StubNetworkProfileProbe(
            ProbeResult.Unavailable<NetworkProfileObservation>("network_unavailable", "No network data.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        AssertResult(result, PreflightCheckStatus.Unknown, "network_unavailable");
    }

    private static async Task<PreflightCheckResult> CheckAsync(params NetworkCategory[] categories)
    {
        var profiles = categories
            .Select((category, index) => new NetworkConnectionProfile($"Adapter {index}", category))
            .ToArray();
        var check = new NetworkProfilePreflightCheck(new StubNetworkProfileProbe(
            ProbeResult.Observed(new NetworkProfileObservation(profiles))));

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
