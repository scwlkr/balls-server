using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class SmbConfigurationJsonTests
{
    [Theory]
    [InlineData("{\"EnableSMB1Protocol\":false,\"EnableSMB2Protocol\":true,\"Smb2DialectMin\":\"SMB300\",\"Smb2DialectMax\":\"SMB311\",\"EncryptData\":true}")]
    [InlineData("[{\"Smb2DialectMax\":\"SMB311\",\"EncryptData\":true,\"EnableSMB2Protocol\":true,\"Smb2DialectMin\":\"SMB300\",\"EnableSMB1Protocol\":false}]")]
    public void ParseAcceptsAnObjectOrSingletonArray(string json)
    {
        var configuration = SmbConfigurationJsonParser.Parse(json);

        Assert.False(configuration.IsSmb1Enabled);
        Assert.True(configuration.IsSmb2Enabled);
        Assert.Equal(SmbDialect.Smb300, configuration.DialectRange.Minimum);
        Assert.Equal(SmbDialect.Smb311, configuration.DialectRange.Maximum);
        Assert.True(configuration.EncryptData);
    }

    [Fact]
    public void ParsePreservesMissingAndNullValuesAsUnknown()
    {
        const string json = """{"EnableSMB1Protocol":null,"Smb2DialectMin":null,"Smb2DialectMax":null}""";

        var configuration = SmbConfigurationJsonParser.Parse(json);

        Assert.Null(configuration.IsSmb1Enabled);
        Assert.Null(configuration.IsSmb2Enabled);
        Assert.Null(configuration.DialectRange.Minimum);
        Assert.Null(configuration.DialectRange.Maximum);
        Assert.Null(configuration.EncryptData);
    }

    [Theory]
    [InlineData("None", SmbDialect.NoLimit)]
    [InlineData("SMB202", SmbDialect.Smb202)]
    [InlineData("SMB210", SmbDialect.Smb210)]
    [InlineData("SMB300", SmbDialect.Smb300)]
    [InlineData("SMB302", SmbDialect.Smb302)]
    [InlineData("SMB311", SmbDialect.Smb311)]
    [InlineData("SMB400", SmbDialect.Unknown)]
    public void ParseMapsRecognizedAndForwardUnknownDialects(string value, SmbDialect expected)
    {
        var json = $$"""{"Smb2DialectMin":"{{value}}","Smb2DialectMax":"{{value}}"}""";

        var configuration = SmbConfigurationJsonParser.Parse(json);

        Assert.Equal(expected, configuration.DialectRange.Minimum);
        Assert.Equal(expected, configuration.DialectRange.Maximum);
    }

    [Theory]
    [InlineData("0", SmbDialect.NoLimit)]
    [InlineData("514", SmbDialect.Smb202)]
    [InlineData("528", SmbDialect.Smb210)]
    [InlineData("768", SmbDialect.Smb300)]
    [InlineData("770", SmbDialect.Smb302)]
    [InlineData("785", SmbDialect.Smb311)]
    [InlineData("65535", SmbDialect.NoLimit)]
    [InlineData("65536", SmbDialect.NoLimit)]
    public void ParseMapsNumericCimDialectValues(string value, SmbDialect expected)
    {
        var json = $$"""{"Smb2DialectMin":"{{value}}","Smb2DialectMax":"{{value}}"}""";

        var configuration = SmbConfigurationJsonParser.Parse(json);

        Assert.Equal(expected, configuration.DialectRange.Minimum);
        Assert.Equal(expected, configuration.DialectRange.Maximum);
    }

    [Fact]
    public async Task ProbeReturnsTypedDialectRange()
    {
        const string json = """
            {"EnableSMB1Protocol":false,"EnableSMB2Protocol":true,"Smb2DialectMin":"SMB300","Smb2DialectMax":"None","EncryptData":false}
            """;
        var source = new StubPowerShellJsonSource(json);
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Running));
        var probe = new WindowsSmbProbe(services, source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.True(result.HasValue);
        Assert.Equal(SmbDialect.Smb300, result.Value!.DialectRange.Minimum);
        Assert.Equal(SmbDialect.NoLimit, result.Value.DialectRange.Maximum);
    }

    [Fact]
    public async Task ProbeConvertsMalformedDialectValueToUnavailable()
    {
        const string json = """
            {"EnableSMB1Protocol":false,"EnableSMB2Protocol":true,"Smb2DialectMin":300,"Smb2DialectMax":"SMB311"}
            """;
        var source = new StubPowerShellJsonSource(json);
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Running));
        var probe = new WindowsSmbProbe(services, source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.False(result.HasValue);
        Assert.Equal("smb_query_failed", result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbeConvertsBoundedQueryFailureToUnavailable(bool accessDenied)
    {
        Exception failure = accessDenied
            ? new UnauthorizedAccessException("private details")
            : new ReadOnlyProbeException("The read-only process query timed out.");
        var source = new ThrowingPowerShellJsonSource(failure);
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Running));
        var probe = new WindowsSmbProbe(services, source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.False(result.HasValue);
        Assert.Equal("smb_query_failed", result.ErrorCode);
        Assert.DoesNotContain("private details", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbePropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new StubPowerShellJsonSource("unexpected query");
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Running));
        var probe = new WindowsSmbProbe(services, source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.ObserveAsync(cancellation.Token).AsTask());

        Assert.Empty(services.ServiceNames);
        Assert.Empty(source.Queries);
    }

    [Fact]
    public async Task ProbeConvertsMalformedJsonToUnavailableWithoutLeakingInput()
    {
        const string sensitiveJson = "{\"Credential\":\"do-not-leak\",\"broken\":\"";
        var source = new StubPowerShellJsonSource(sensitiveJson);
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Running));
        var probe = new WindowsSmbProbe(services, source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.False(result.HasValue);
        Assert.Equal("smb_query_failed", result.ErrorCode);
        Assert.Equal("Windows did not report the SMB server state and configuration.", result.ErrorMessage);
        Assert.DoesNotContain("do-not-leak", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal(["LanmanServer"], services.ServiceNames);
        Assert.Equal([PowerShellQuery.SmbServerConfiguration], source.Queries);
    }

    [Fact]
    public async Task ProbeDoesNotQueryConfigurationWhenServerServiceIsNotInstalled()
    {
        var source = new StubPowerShellJsonSource("unexpected query");
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(false, WindowsServiceState.NotInstalled));
        var probe = new WindowsSmbProbe(services, source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.True(result.HasValue);
        Assert.Equal(WindowsServiceState.NotInstalled, result.Value!.ServerServiceState);
        Assert.Null(result.Value.IsSmb1Enabled);
        Assert.Null(result.Value.IsSmb2Enabled);
        Assert.Null(result.Value.DialectRange.Minimum);
        Assert.Null(result.Value.DialectRange.Maximum);
        Assert.Null(result.Value.EncryptData);
        Assert.Empty(source.Queries);
        Assert.Equal(["LanmanServer"], services.ServiceNames);
    }

    [Fact]
    public async Task ProbeObservesConfigurationWhenInstalledServerServiceIsStopped()
    {
        const string json = """
            {"EnableSMB1Protocol":false,"EnableSMB2Protocol":true,"Smb2DialectMin":"SMB300","Smb2DialectMax":"SMB311","EncryptData":false}
            """;
        var source = new StubPowerShellJsonSource(json);
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Stopped));
        var probe = new WindowsSmbProbe(services, source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.True(result.HasValue);
        Assert.Equal(WindowsServiceState.Stopped, result.Value!.ServerServiceState);
        Assert.False(result.Value.IsSmb1Enabled);
        Assert.True(result.Value.IsSmb2Enabled);
        Assert.Equal(SmbDialect.Smb300, result.Value.DialectRange.Minimum);
        Assert.Equal(SmbDialect.Smb311, result.Value.DialectRange.Maximum);
        Assert.Equal([PowerShellQuery.SmbServerConfiguration], source.Queries);
    }

    [Fact]
    public async Task ProbePreservesKnownStoppedServiceWhenConfigurationIsUnavailable()
    {
        var source = new StubPowerShellJsonSource("malformed configuration");
        var services = new StubWindowsServiceStatusSource(
            new WindowsServiceStatus(true, WindowsServiceState.Stopped));
        var probe = new WindowsSmbProbe(services, source);

        var result = await probe.ObserveAsync(CancellationToken.None);

        Assert.True(result.HasValue);
        Assert.Equal(WindowsServiceState.Stopped, result.Value!.ServerServiceState);
        Assert.Null(result.Value.IsSmb1Enabled);
        Assert.Null(result.Value.IsSmb2Enabled);
        Assert.Null(result.Value.DialectRange.Minimum);
        Assert.Null(result.Value.DialectRange.Maximum);
    }
}
