using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class TailscaleStatusJsonTests
{
    [Fact]
    public void ParseExtractsOnlyLocalStateAndIgnoresPeersAndUsers()
    {
        const string json = """
            {
              "BackendState":"Running",
              "TailscaleIPs":["100.64.0.1","fd7a:115c:a1e0::1","100.64.0.1"],
              "Self":{"Online":true,"TailscaleIPs":["100.64.0.99"]},
              "Peer":{"peer-key":{"Online":false,"TailscaleIPs":["100.64.0.2"]}},
              "User":{"123":{"LoginName":"owner@example.com"}}
            }
            """;

        var status = TailscaleStatusJsonParser.Parse(json);

        Assert.Equal("Running", status.BackendState);
        Assert.True(status.IsOnline);
        Assert.Equal(2, status.AddressCount);
    }

    [Fact]
    public void ParseFallsBackToSelfAddressesWhenRootAddressesAreAbsent()
    {
        const string json = """
            {"BackendState":"Running","Self":{"Online":true,"TailscaleIPs":["100.64.0.3"]}}
            """;

        var status = TailscaleStatusJsonParser.Parse(json);

        Assert.True(status.IsOnline);
        Assert.Equal(1, status.AddressCount);
    }

    [Fact]
    public void ParseInfersOnlineFromRunningBackendAndLocalAddressWhenSelfOmitsIt()
    {
        const string json = """
            {"BackendState":"Running","TailscaleIPs":["100.64.0.3"],"Self":{}}
            """;

        var status = TailscaleStatusJsonParser.Parse(json);

        Assert.True(status.IsOnline);
        Assert.Equal(1, status.AddressCount);
    }

    [Fact]
    public void ParseHonorsExplicitOfflineStateEvenWithAnAddress()
    {
        const string json = """
            {"BackendState":"Running","TailscaleIPs":["100.64.0.3"],"Self":{"Online":false}}
            """;

        var status = TailscaleStatusJsonParser.Parse(json);

        Assert.False(status.IsOnline);
        Assert.Equal(1, status.AddressCount);
    }

    [Fact]
    public async Task ProbeUsesOnlyServiceStateWhenTailscaleIsNotRunning()
    {
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Stopped));
        var statusSource = new StubTailscaleStatusSource(new TailscaleStatus("Running", true, 1));
        var probe = new WindowsTailscaleProbe(services, statusSource);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.True(result.HasValue);
        Assert.Equal(WindowsServiceState.Stopped, result.Value!.ServiceState);
        Assert.Equal("Unavailable", result.Value.BackendState);
        Assert.Equal(0, statusSource.CallCount);
        Assert.Equal(["Tailscale"], services.ServiceNames);
    }

    [Fact]
    public async Task ProbeProjectsRunningServiceAndParsedLocalStatus()
    {
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Running));
        var statusSource = new StubTailscaleStatusSource(new TailscaleStatus("Running", true, 2));
        var probe = new WindowsTailscaleProbe(services, statusSource);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.True(result.HasValue);
        Assert.True(result.Value!.IsInstalled);
        Assert.Equal(WindowsServiceState.Running, result.Value.ServiceState);
        Assert.Equal("Running", result.Value.BackendState);
        Assert.True(result.Value.IsOnline);
        Assert.Equal(2, result.Value.AddressCount);
        Assert.Equal(1, statusSource.CallCount);
    }
}
