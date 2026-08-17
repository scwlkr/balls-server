using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class WindowsPreflightSmokeTests
{
    [Fact]
    public async Task ProductionHostDiagnosticCompletesWithTheFullCatalog()
    {
        var service = WindowsPreflightFactory.CreateHostService();
        var targetPath = AppContext.BaseDirectory;

        var report = await service.RunAsync(new PreflightRequest(targetPath));

        Assert.Equal(Path.GetFullPath(targetPath), report.TargetPath);
        Assert.Equal(HostPreflightCatalog.OrderedCheckIds, report.Checks.Select(result => result.Id));
        Assert.Equal(8, report.Checks.Count);
        Assert.Equal(
            HostPreflightCatalog.OrderedCheckIds.Where(id => id != PreflightCheckId.Administrator),
            report.Prerequisites.Select(result => result.Id));
        Assert.Equal(PreflightAggregateId.Computer, report.Computer.Id);
        Assert.Equal(PreflightAggregateId.ManagedFolder, report.ManagedFolder.Id);
        Assert.Equal(PreflightAggregateId.LocalAccess, report.LocalAccess.Id);
        Assert.Equal(PreflightAggregateId.TailscaleAccess, report.TailscaleAccess.Id);
        Assert.Equal(HostingState.NotConfigured, report.HostingState.State);
        Assert.Equal(Path.GetFullPath(targetPath), report.ManagedFolder.EvaluatedFolderPath);
        Assert.Equal(Path.GetFullPath(targetPath), report.LocalAccess.EvaluatedFolderPath);
        Assert.Equal(Path.GetFullPath(targetPath), report.TailscaleAccess.EvaluatedFolderPath);
        Assert.All(
            new[] { report.Computer, report.ManagedFolder, report.LocalAccess, report.TailscaleAccess },
            aggregate =>
            {
                Assert.NotEmpty(aggregate.Prerequisites);
                Assert.False(string.IsNullOrWhiteSpace(aggregate.Summary));
            });
        Assert.False(string.IsNullOrWhiteSpace(report.HostingState.Summary));

        var smb = Assert.Single(report.Checks, result => result.Id == PreflightCheckId.Smb);
        Assert.NotEqual(PreflightCheckStatus.Warning, smb.Status);
        if (smb.ReasonCode == "smb_query_failed")
        {
            Assert.Empty(smb.Evidence);
        }
        else
        {
            Assert.Equal(
                [
                    "Server service",
                    "SMB 2/3 enabled",
                    "SMB 1 enabled",
                    "Minimum SMB 2/3 dialect",
                    "Maximum SMB 2/3 dialect",
                    "Encryption required",
                ],
                smb.Evidence.Select(item => item.Label));
        }
    }
}
