using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class FirewallJsonTests
{
    [Fact]
    public void ParseAcceptsASingleObject()
    {
        const string json = """
            {"Profile":"Private","Enabled":true,"DefaultInboundAction":"Block","DefaultOutboundAction":"Allow"}
            """;

        var observation = FirewallJsonParser.Parse(json);

        var profile = Assert.Single(observation.Profiles);
        Assert.Equal(FirewallProfileKind.Private, profile.Profile);
        Assert.True(profile.Enabled);
        Assert.Equal(FirewallDefaultAction.Block, profile.DefaultInboundAction);
        Assert.Equal(FirewallDefaultAction.Allow, profile.DefaultOutboundAction);
    }

    [Fact]
    public void ParseAcceptsAnArrayAndMapsUnrecognizedValuesToUnknown()
    {
        const string json = """
            [
              {"Profile":"Domain","Enabled":true,"DefaultInboundAction":"Block","DefaultOutboundAction":"Allow"},
              {"Profile":"Future","Enabled":false,"DefaultInboundAction":"Future","DefaultOutboundAction":"NotConfigured"}
            ]
            """;

        var observation = FirewallJsonParser.Parse(json);

        Assert.Equal(2, observation.Profiles.Count);
        Assert.Equal(FirewallProfileKind.Domain, observation.Profiles[0].Profile);
        Assert.Equal(FirewallProfileKind.Unknown, observation.Profiles[1].Profile);
        Assert.Equal(FirewallDefaultAction.Unknown, observation.Profiles[1].DefaultInboundAction);
        Assert.Equal(FirewallDefaultAction.NotConfigured, observation.Profiles[1].DefaultOutboundAction);
    }

    [Fact]
    public async Task ProbeConvertsMalformedJsonToUnavailableWithoutLeakingInput()
    {
        const string sensitiveJson = "{\"ApiKey\":\"do-not-leak\",\"broken\":\"";
        var source = new StubPowerShellJsonSource(sensitiveJson);
        var probe = new WindowsFirewallProbe(source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.False(result.HasValue);
        Assert.Equal("firewall_query_failed", result.ErrorCode);
        Assert.Equal("Windows did not report the effective firewall profiles.", result.ErrorMessage);
        Assert.DoesNotContain("do-not-leak", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal([PowerShellQuery.FirewallProfiles], source.Queries);
    }
}
