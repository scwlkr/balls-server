using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class HostPreflightCatalogTests
{
    [Fact]
    public void OrderedCheckIdsContainTheEightChecksInProductOrder()
    {
        PreflightCheckId[] expected =
        [
            PreflightCheckId.Administrator,
            PreflightCheckId.WindowsVersion,
            PreflightCheckId.Storage,
            PreflightCheckId.NetworkProfile,
            PreflightCheckId.Firewall,
            PreflightCheckId.Tailscale,
            PreflightCheckId.Smb,
            PreflightCheckId.FolderPermissions,
        ];

        Assert.Equal(expected, HostPreflightCatalog.OrderedCheckIds);
    }

    [Fact]
    public async Task CreateServiceRunsTheExactCatalogInOrder()
    {
        var service = HostPreflightCatalog.CreateService(TestData.HealthyProbes(), TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(HostPreflightCatalog.OrderedCheckIds, report.Checks.Select(result => result.Id));
        Assert.All(report.Checks, result => Assert.Equal(PreflightCheckStatus.Ready, result.Status));
        Assert.All(
            new[] { report.Computer, report.ManagedFolder, report.LocalAccess, report.TailscaleAccess },
            aggregate => Assert.Equal(PreflightOverallStatus.Ready, aggregate.Status));
        Assert.Equal(HostingState.NotConfigured, report.HostingState.State);
    }
}
