using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class SmbPreflightCheckTests
{
    [Fact]
    public async Task CheckAsyncAcceptsACompleteSmb3DialectRange()
    {
        var observation = new SmbObservation(
            WindowsServiceState.Running,
            IsSmb1Enabled: false,
            IsSmb2Enabled: true,
            EncryptData: false,
            new SmbDialectRange(SmbDialect.Smb300, SmbDialect.Smb311));
        var check = new SmbPreflightCheck(new StubSmbProbe(ProbeResult.Observed(observation)));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(PreflightCheckStatus.Ready, result.Status);
        Assert.Equal("smb3_policy_satisfied", result.ReasonCode);
        Assert.Contains(result.Evidence, item =>
            item.Label == "Minimum SMB 2/3 dialect" && item.Value == "SMB 3.0");
        Assert.Contains(result.Evidence, item =>
            item.Label == "Maximum SMB 2/3 dialect" && item.Value == "SMB 3.1.1");
    }

    [Theory]
    [InlineData(WindowsServiceState.Unknown, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.Unknown, "smb_server_state_unknown")]
    [InlineData(WindowsServiceState.NotInstalled, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.Stopped, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.StartPending, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.StopPending, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.ContinuePending, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.PausePending, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.Paused, false, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.Stopped, true, false, null, null, PreflightCheckStatus.ActionRequired, "smb_server_not_running")]
    [InlineData(WindowsServiceState.Running, false, false, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb2_disabled")]
    [InlineData(WindowsServiceState.Running, null, false, null, null, PreflightCheckStatus.ActionRequired, "smb2_disabled")]
    [InlineData(WindowsServiceState.Running, true, false, SmbDialect.NoLimit, SmbDialect.Smb202, PreflightCheckStatus.ActionRequired, "smb2_disabled")]
    [InlineData(WindowsServiceState.Running, true, true, null, null, PreflightCheckStatus.ActionRequired, "smb1_enabled")]
    [InlineData(WindowsServiceState.Unknown, true, true, null, null, PreflightCheckStatus.ActionRequired, "smb1_enabled")]
    [InlineData(WindowsServiceState.Running, true, true, SmbDialect.NoLimit, SmbDialect.Smb202, PreflightCheckStatus.ActionRequired, "smb1_enabled")]
    [InlineData(WindowsServiceState.Running, null, true, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.Unknown, "smb1_state_unknown")]
    [InlineData(WindowsServiceState.Running, false, null, SmbDialect.Smb300, SmbDialect.Smb311, PreflightCheckStatus.Unknown, "smb2_state_unknown")]
    [InlineData(WindowsServiceState.Running, false, true, null, SmbDialect.Smb311, PreflightCheckStatus.Unknown, "smb_minimum_dialect_unknown")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Unknown, SmbDialect.Smb311, PreflightCheckStatus.Unknown, "smb_minimum_dialect_unknown")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb300, null, PreflightCheckStatus.Unknown, "smb_maximum_dialect_unknown")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb300, SmbDialect.Unknown, PreflightCheckStatus.Unknown, "smb_maximum_dialect_unknown")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.NoLimit, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_minimum_below_3")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb202, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_minimum_below_3")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb210, SmbDialect.Smb311, PreflightCheckStatus.ActionRequired, "smb_minimum_below_3")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb210, SmbDialect.Smb202, PreflightCheckStatus.ActionRequired, "smb_minimum_below_3")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb300, SmbDialect.Smb202, PreflightCheckStatus.ActionRequired, "smb_maximum_below_3")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb300, SmbDialect.Smb210, PreflightCheckStatus.ActionRequired, "smb_maximum_below_3")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb311, SmbDialect.Smb300, PreflightCheckStatus.ActionRequired, "smb_dialect_range_contradictory")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb300, SmbDialect.NoLimit, PreflightCheckStatus.Ready, "smb3_policy_satisfied")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb300, SmbDialect.Smb300, PreflightCheckStatus.Ready, "smb3_policy_satisfied")]
    [InlineData(WindowsServiceState.Running, false, true, SmbDialect.Smb302, SmbDialect.Smb311, PreflightCheckStatus.Ready, "smb3_policy_satisfied")]
    public async Task CheckAsyncAppliesFailClosedSmbPolicy(
        WindowsServiceState serverState,
        bool? isSmb1Enabled,
        bool? isSmb2Enabled,
        SmbDialect? minimumDialect,
        SmbDialect? maximumDialect,
        PreflightCheckStatus expectedStatus,
        string expectedReasonCode)
    {
        var observation = new SmbObservation(
            serverState,
            isSmb1Enabled,
            isSmb2Enabled,
            EncryptData: false,
            new SmbDialectRange(minimumDialect, maximumDialect));
        var check = new SmbPreflightCheck(new StubSmbProbe(ProbeResult.Observed(observation)));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }

    [Fact]
    public async Task CheckAsyncWhenProbeIsUnavailableReturnsUnknown()
    {
        var check = new SmbPreflightCheck(new StubSmbProbe(
            ProbeResult.Unavailable<SmbObservation>("smb_unavailable", "No SMB data.")));

        var result = await check.CheckAsync(TestData.Context, CancellationToken.None);

        Assert.Equal(PreflightCheckStatus.Unknown, result.Status);
        Assert.Equal("smb_unavailable", result.ReasonCode);
    }
}
