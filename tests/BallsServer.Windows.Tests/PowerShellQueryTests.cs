using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class PowerShellQueryTests
{
    [Fact]
    public void QueryAllowListContainsOnlyTheThreeReadOnlyDiagnostics()
    {
        PowerShellQuery[] expected =
        [
            PowerShellQuery.ConnectedNetworkProfiles,
            PowerShellQuery.FirewallProfiles,
            PowerShellQuery.SmbServerConfiguration,
        ];

        Assert.Equal(expected, Enum.GetValues<PowerShellQuery>());
    }

    [Fact]
    public async Task QueryAsyncRefusesAValueOutsideTheAllowListBeforeStartingAProcess()
    {
        var source = new StaticPowerShellJsonSource();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => source.QueryAsync((PowerShellQuery)int.MaxValue, CancellationToken.None).AsTask());

        Assert.Equal("query", exception.ParamName);
    }

    [Fact]
    public void SmbQuerySelectsOnlyTheApprovedReadOnlyConfigurationFields()
    {
        var script = StaticPowerShellJsonSource.GetScript(PowerShellQuery.SmbServerConfiguration);

        Assert.Contains("Get-SmbServerConfiguration -ErrorAction Stop", script, StringComparison.Ordinal);
        Assert.Contains("EnableSMB1Protocol = $configuration.EnableSMB1Protocol", script, StringComparison.Ordinal);
        Assert.Contains("EnableSMB2Protocol = $configuration.EnableSMB2Protocol", script, StringComparison.Ordinal);
        Assert.Contains("Smb2DialectMin = [string]$configuration.Smb2DialectMin", script, StringComparison.Ordinal);
        Assert.Contains("Smb2DialectMax = [string]$configuration.Smb2DialectMax", script, StringComparison.Ordinal);
        Assert.Contains("EncryptData = $configuration.EncryptData", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-", script, StringComparison.OrdinalIgnoreCase);
    }
}
