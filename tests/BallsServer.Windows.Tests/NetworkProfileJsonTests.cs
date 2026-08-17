using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class NetworkProfileJsonTests
{
    [Fact]
    public void ParseAcceptsASingleObject()
    {
        const string json = """
            {"InterfaceAlias":"Ethernet","NetworkCategory":"Private"}
            """;

        var observation = NetworkProfileJsonParser.Parse(json);

        var profile = Assert.Single(observation.Profiles);
        Assert.Equal("Ethernet", profile.InterfaceAlias);
        Assert.Equal(NetworkCategory.Private, profile.Category);
    }

    [Fact]
    public void ParseAcceptsAnArrayAndMapsUnrecognizedCategoriesToUnknown()
    {
        const string json = """
            [
              {"InterfaceAlias":"Wi-Fi","NetworkCategory":"Public"},
              {"InterfaceAlias":"Tunnel","NetworkCategory":"FutureCategory"}
            ]
            """;

        var observation = NetworkProfileJsonParser.Parse(json);

        Assert.Collection(
            observation.Profiles,
            profile => Assert.Equal(NetworkCategory.Public, profile.Category),
            profile => Assert.Equal(NetworkCategory.Unknown, profile.Category));
    }

    [Fact]
    public async Task ProbeConvertsMalformedJsonToUnavailableWithoutLeakingInput()
    {
        const string sensitiveJson = "{\"Password\":\"do-not-leak\",\"broken\":\"";
        var source = new StubPowerShellJsonSource(sensitiveJson);
        var probe = new WindowsNetworkProfileProbe(source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.False(result.HasValue);
        Assert.Equal("network_profile_query_failed", result.ErrorCode);
        Assert.Equal("Windows did not report the connected network profiles.", result.ErrorMessage);
        Assert.DoesNotContain("do-not-leak", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal([PowerShellQuery.ConnectedNetworkProfiles], source.Queries);
    }
}
