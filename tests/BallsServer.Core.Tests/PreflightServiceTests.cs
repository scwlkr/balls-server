using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

public sealed class PreflightServiceTests
{
    [Fact]
    public async Task RunAsyncSortsChecksIntoCatalogOrder()
    {
        var checks = CreateChecks().Reverse();
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(HostPreflightCatalog.OrderedCheckIds, report.Checks.Select(result => result.Id));
    }

    [Fact]
    public async Task RunAsyncReportsEachCheckInCatalogOrder()
    {
        var service = new PreflightService(CreateChecks(), TestData.Policy);
        var updates = new List<PreflightProgress>();

        await service.RunAsync(
            new PreflightRequest(TestData.Context.TargetPath),
            progress: new RecordingProgress<PreflightProgress>(updates.Add));

        Assert.Equal(HostPreflightCatalog.OrderedCheckIds, updates.Select(update => update.CheckId));
        Assert.Equal(Enumerable.Range(1, HostPreflightCatalog.OrderedCheckIds.Count), updates.Select(update => update.Position));
        Assert.All(updates, update => Assert.Equal(HostPreflightCatalog.OrderedCheckIds.Count, update.Total));
    }

    [Fact]
    public async Task RunAsyncPublishesTheStructuredReadinessAreas()
    {
        var service = new PreflightService(CreateChecks(), TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(HostPreflightCatalog.OrderedCheckIds, report.Checks.Select(result => result.Id));
        Assert.Equal(
            HostPreflightCatalog.OrderedCheckIds.Where(id => id != PreflightCheckId.Administrator),
            report.Prerequisites.Select(result => result.Id));
        AssertAggregate(
            report.Computer,
            PreflightAggregateId.Computer,
            PreflightCheckId.WindowsVersion,
            PreflightCheckId.Firewall,
            PreflightCheckId.Smb);
        AssertAggregate(
            report.ManagedFolder,
            PreflightAggregateId.ManagedFolder,
            PreflightCheckId.Storage,
            PreflightCheckId.FolderPermissions);
        AssertAggregate(
            report.LocalAccess,
            PreflightAggregateId.LocalAccess,
            PreflightCheckId.WindowsVersion,
            PreflightCheckId.Firewall,
            PreflightCheckId.Smb,
            PreflightCheckId.Storage,
            PreflightCheckId.FolderPermissions,
            PreflightCheckId.NetworkProfile);
        AssertAggregate(
            report.TailscaleAccess,
            PreflightAggregateId.TailscaleAccess,
            PreflightCheckId.WindowsVersion,
            PreflightCheckId.Firewall,
            PreflightCheckId.Smb,
            PreflightCheckId.Storage,
            PreflightCheckId.FolderPermissions,
            PreflightCheckId.Tailscale);
        Assert.Equal(TestData.Context.TargetPath, report.ManagedFolder.EvaluatedFolderPath);
        Assert.Equal(TestData.Context.TargetPath, report.LocalAccess.EvaluatedFolderPath);
        Assert.Equal(TestData.Context.TargetPath, report.TailscaleAccess.EvaluatedFolderPath);
        Assert.Equal(HostingState.NotConfigured, report.HostingState.State);
        Assert.Equal("Hosting state", report.HostingState.Title);
        Assert.Contains("not configured", report.HostingState.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not create a managed share", report.HostingState.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not verify a client connection", report.HostingState.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PreflightCheckStatus.Ready, AdministratorInformationAvailability.Available)]
    [InlineData(PreflightCheckStatus.Warning, AdministratorInformationAvailability.Available)]
    [InlineData(PreflightCheckStatus.ActionRequired, AdministratorInformationAvailability.Available)]
    [InlineData(PreflightCheckStatus.Unknown, AdministratorInformationAvailability.Unavailable)]
    public async Task RunAsyncTreatsAdministratorStateAsFutureSetupInformationOnly(
        PreflightCheckStatus administratorStatus,
        AdministratorInformationAvailability expectedAvailability)
    {
        var checks = CreateChecks();
        checks[0] = ReturningCheck(PreflightCheckId.Administrator, 10, administratorStatus);
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(expectedAvailability, report.AdministratorInformation.Availability);
        Assert.Equal($"Administrator_{administratorStatus}", report.AdministratorInformation.ReasonCode);
        Assert.All(
            new[] { report.Computer, report.ManagedFolder, report.LocalAccess, report.TailscaleAccess },
            aggregate => Assert.Equal(PreflightOverallStatus.Ready, aggregate.Status));
        Assert.DoesNotContain(
            report.Computer.Prerequisites
                .Concat(report.ManagedFolder.Prerequisites)
                .Concat(report.LocalAccess.Prerequisites)
                .Concat(report.TailscaleAccess.Prerequisites),
            result => result.Id == PreflightCheckId.Administrator);
    }

    [Theory]
    [InlineData(PreflightCheckStatus.Ready, PreflightCheckStatus.Ready, PreflightOverallStatus.Ready, PreflightOverallStatus.Ready)]
    [InlineData(PreflightCheckStatus.Ready, PreflightCheckStatus.Warning, PreflightOverallStatus.Ready, PreflightOverallStatus.ReadyWithWarnings)]
    [InlineData(PreflightCheckStatus.Ready, PreflightCheckStatus.ActionRequired, PreflightOverallStatus.Ready, PreflightOverallStatus.NotReady)]
    [InlineData(PreflightCheckStatus.Ready, PreflightCheckStatus.Unknown, PreflightOverallStatus.Ready, PreflightOverallStatus.Indeterminate)]
    [InlineData(PreflightCheckStatus.Warning, PreflightCheckStatus.Ready, PreflightOverallStatus.ReadyWithWarnings, PreflightOverallStatus.Ready)]
    [InlineData(PreflightCheckStatus.Warning, PreflightCheckStatus.Warning, PreflightOverallStatus.ReadyWithWarnings, PreflightOverallStatus.ReadyWithWarnings)]
    [InlineData(PreflightCheckStatus.Warning, PreflightCheckStatus.ActionRequired, PreflightOverallStatus.ReadyWithWarnings, PreflightOverallStatus.NotReady)]
    [InlineData(PreflightCheckStatus.Warning, PreflightCheckStatus.Unknown, PreflightOverallStatus.ReadyWithWarnings, PreflightOverallStatus.Indeterminate)]
    [InlineData(PreflightCheckStatus.ActionRequired, PreflightCheckStatus.Ready, PreflightOverallStatus.NotReady, PreflightOverallStatus.Ready)]
    [InlineData(PreflightCheckStatus.ActionRequired, PreflightCheckStatus.Warning, PreflightOverallStatus.NotReady, PreflightOverallStatus.ReadyWithWarnings)]
    [InlineData(PreflightCheckStatus.ActionRequired, PreflightCheckStatus.ActionRequired, PreflightOverallStatus.NotReady, PreflightOverallStatus.NotReady)]
    [InlineData(PreflightCheckStatus.ActionRequired, PreflightCheckStatus.Unknown, PreflightOverallStatus.NotReady, PreflightOverallStatus.Indeterminate)]
    [InlineData(PreflightCheckStatus.Unknown, PreflightCheckStatus.Ready, PreflightOverallStatus.Indeterminate, PreflightOverallStatus.Ready)]
    [InlineData(PreflightCheckStatus.Unknown, PreflightCheckStatus.Warning, PreflightOverallStatus.Indeterminate, PreflightOverallStatus.ReadyWithWarnings)]
    [InlineData(PreflightCheckStatus.Unknown, PreflightCheckStatus.ActionRequired, PreflightOverallStatus.Indeterminate, PreflightOverallStatus.NotReady)]
    [InlineData(PreflightCheckStatus.Unknown, PreflightCheckStatus.Unknown, PreflightOverallStatus.Indeterminate, PreflightOverallStatus.Indeterminate)]
    public async Task RunAsyncReducesLocalAndTailscaleAccessIndependently(
        PreflightCheckStatus localStatus,
        PreflightCheckStatus tailscaleStatus,
        PreflightOverallStatus expectedLocalStatus,
        PreflightOverallStatus expectedTailscaleStatus)
    {
        var checks = CreateChecks();
        checks[3] = ReturningCheck(PreflightCheckId.NetworkProfile, 40, localStatus);
        checks[5] = ReturningCheck(PreflightCheckId.Tailscale, 60, tailscaleStatus);
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(PreflightOverallStatus.Ready, report.Computer.Status);
        Assert.Equal(PreflightOverallStatus.Ready, report.ManagedFolder.Status);
        Assert.Equal(expectedLocalStatus, report.LocalAccess.Status);
        Assert.Equal(expectedTailscaleStatus, report.TailscaleAccess.Status);
    }

    [Fact]
    public async Task RunAsyncAppliesFailClosedPrecedenceWithinEveryReadinessArea()
    {
        var checks = CreateChecks();
        checks[1] = ReturningCheck(PreflightCheckId.WindowsVersion, 20, PreflightCheckStatus.Unknown);
        checks[2] = ReturningCheck(PreflightCheckId.Storage, 30, PreflightCheckStatus.Unknown);
        checks[4] = ReturningCheck(PreflightCheckId.Firewall, 50, PreflightCheckStatus.Warning);
        checks[6] = ReturningCheck(PreflightCheckId.Smb, 70, PreflightCheckStatus.ActionRequired);
        checks[7] = ReturningCheck(PreflightCheckId.FolderPermissions, 80, PreflightCheckStatus.Warning);
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(PreflightOverallStatus.NotReady, report.Computer.Status);
        Assert.Equal(PreflightOverallStatus.Indeterminate, report.ManagedFolder.Status);
        Assert.Equal(PreflightOverallStatus.NotReady, report.LocalAccess.Status);
        Assert.Equal(PreflightOverallStatus.NotReady, report.TailscaleAccess.Status);
    }

    [Theory]
    [InlineData(PreflightCheckId.WindowsVersion, PreflightCheckStatus.ActionRequired, PreflightOverallStatus.NotReady, PreflightOverallStatus.Ready, PreflightOverallStatus.NotReady)]
    [InlineData(PreflightCheckId.Storage, PreflightCheckStatus.Unknown, PreflightOverallStatus.Ready, PreflightOverallStatus.Indeterminate, PreflightOverallStatus.Indeterminate)]
    [InlineData(PreflightCheckId.Firewall, PreflightCheckStatus.Warning, PreflightOverallStatus.ReadyWithWarnings, PreflightOverallStatus.Ready, PreflightOverallStatus.ReadyWithWarnings)]
    [InlineData(PreflightCheckId.FolderPermissions, PreflightCheckStatus.ActionRequired, PreflightOverallStatus.Ready, PreflightOverallStatus.NotReady, PreflightOverallStatus.NotReady)]
    public async Task RunAsyncPropagatesSharedPrerequisitesToBothAccessPaths(
        PreflightCheckId sharedCheckId,
        PreflightCheckStatus sharedCheckStatus,
        PreflightOverallStatus expectedComputerStatus,
        PreflightOverallStatus expectedFolderStatus,
        PreflightOverallStatus expectedPathStatus)
    {
        var checks = CreateChecks();
        var index = Array.FindIndex(checks, check => check.Id == sharedCheckId);
        checks[index] = ReturningCheck(sharedCheckId, checks[index].Order, sharedCheckStatus);
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(expectedComputerStatus, report.Computer.Status);
        Assert.Equal(expectedFolderStatus, report.ManagedFolder.Status);
        Assert.Equal(expectedPathStatus, report.LocalAccess.Status);
        Assert.Equal(expectedPathStatus, report.TailscaleAccess.Status);
    }

    [Fact]
    public void ConstructorRejectsASetThatDoesNotMatchTheCatalog()
    {
        var checks = CreateChecks();
        checks[^1] = ReturningCheck(PreflightCheckId.Firewall, 80);

        var exception = Assert.Throws<ArgumentException>(() => new PreflightService(checks, TestData.Policy));

        Assert.Equal("checks", exception.ParamName);
    }

    [Theory]
    [InlineData(PreflightCheckStatus.Ready, PreflightOverallStatus.Ready, true)]
    [InlineData(PreflightCheckStatus.Warning, PreflightOverallStatus.ReadyWithWarnings, true)]
    [InlineData(PreflightCheckStatus.Unknown, PreflightOverallStatus.Indeterminate, false)]
    [InlineData(PreflightCheckStatus.ActionRequired, PreflightOverallStatus.NotReady, false)]
    public async Task RunAsyncReducesIndividualPrerequisiteStatuses(
        PreflightCheckStatus checkStatus,
        PreflightOverallStatus expectedOverallStatus,
        bool expectedIsReady)
    {
        var checks = CreateChecks();
        checks[4] = ReturningCheck(PreflightCheckId.Firewall, 50, checkStatus);
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(expectedOverallStatus, report.Computer.Status);
        Assert.Equal(expectedIsReady, report.Computer.IsReady);
        Assert.Equal(expectedOverallStatus, report.LocalAccess.Status);
        Assert.Equal(expectedOverallStatus, report.TailscaleAccess.Status);
    }

    [Fact]
    public async Task RunAsyncActionRequiredDominatesUnknownAndWarning()
    {
        var checks = CreateChecks();
        checks[1] = ReturningCheck(PreflightCheckId.WindowsVersion, 20, PreflightCheckStatus.Unknown);
        checks[4] = ReturningCheck(PreflightCheckId.Firewall, 50, PreflightCheckStatus.Warning);
        checks[6] = ReturningCheck(PreflightCheckId.Smb, 70, PreflightCheckStatus.ActionRequired);
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal(PreflightOverallStatus.NotReady, report.Computer.Status);
        Assert.Equal(PreflightOverallStatus.NotReady, report.LocalAccess.Status);
        Assert.Equal(PreflightOverallStatus.NotReady, report.TailscaleAccess.Status);
    }

    [Fact]
    public async Task RunAsyncIsolatesAnExceptionAndContinuesWithLaterChecks()
    {
        var checks = CreateChecks();
        checks[3] = new StubPreflightCheck(
            PreflightCheckId.NetworkProfile,
            40,
            static (_, _) => throw new InvalidOperationException("sensitive environmental detail"));
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        var failed = report.Checks[3];
        Assert.Equal(PreflightCheckStatus.Unknown, failed.Status);
        Assert.Equal("check_failed", failed.ReasonCode);
        Assert.DoesNotContain("sensitive", failed.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, checks[4].CallCount);
        Assert.Equal(PreflightOverallStatus.Ready, report.Computer.Status);
        Assert.Equal(PreflightOverallStatus.Ready, report.ManagedFolder.Status);
        Assert.Equal(PreflightOverallStatus.Indeterminate, report.LocalAccess.Status);
        Assert.Equal(PreflightOverallStatus.Ready, report.TailscaleAccess.Status);
    }

    [Fact]
    public async Task RunAsyncTreatsAnUnrelatedCancellationExceptionAsAnIsolatedFailure()
    {
        var checks = CreateChecks();
        checks[2] = new StubPreflightCheck(
            PreflightCheckId.Storage,
            30,
            static (_, _) => throw new OperationCanceledException("not requested by the caller"));
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        Assert.Equal("check_failed", report.Checks[2].ReasonCode);
        Assert.Equal(1, checks[3].CallCount);
    }

    [Fact]
    public async Task RunAsyncReplacesAResultWhoseIdDoesNotMatchItsCheck()
    {
        var checks = CreateChecks();
        checks[4] = new StubPreflightCheck(
            PreflightCheckId.Firewall,
            50,
            static (_, _) => ValueTask.FromResult(TestData.CheckResult(PreflightCheckId.Smb)));
        var service = new PreflightService(checks, TestData.Policy);

        var report = await service.RunAsync(new PreflightRequest(TestData.Context.TargetPath));

        var result = report.Checks[4];
        Assert.Equal(PreflightCheckId.Firewall, result.Id);
        Assert.Equal("Firewall title", result.Title);
        Assert.Equal(PreflightCheckStatus.Unknown, result.Status);
        Assert.Equal("invalid_check_result", result.ReasonCode);
        Assert.Equal(PreflightOverallStatus.Indeterminate, report.Computer.Status);
        Assert.Equal(PreflightOverallStatus.Indeterminate, report.LocalAccess.Status);
        Assert.Equal(PreflightOverallStatus.Indeterminate, report.TailscaleAccess.Status);
    }

    [Fact]
    public async Task RunAsyncWithPreCanceledTokenDoesNotRunAnyCheck()
    {
        var checks = CreateChecks();
        var service = new PreflightService(checks, TestData.Policy);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync(new PreflightRequest(TestData.Context.TargetPath), cancellation.Token));

        Assert.All(checks, check => Assert.Equal(0, check.CallCount));
    }

    [Fact]
    public async Task RunAsyncPropagatesCancellationFromACheckAndStops()
    {
        using var cancellation = new CancellationTokenSource();
        var checks = CreateChecks();
        checks[2] = new StubPreflightCheck(
            PreflightCheckId.Storage,
            30,
            (_, token) =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<PreflightCheckResult>(token);
            });
        var service = new PreflightService(checks, TestData.Policy);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync(new PreflightRequest(TestData.Context.TargetPath), cancellation.Token));

        Assert.Equal(1, checks[2].CallCount);
        Assert.Equal(0, checks[3].CallCount);
    }

    private static StubPreflightCheck[] CreateChecks() =>
        HostPreflightCatalog.OrderedCheckIds
            .Select((id, index) => ReturningCheck(id, (index + 1) * 10))
            .ToArray();

    private static StubPreflightCheck ReturningCheck(
        PreflightCheckId id,
        int order,
        PreflightCheckStatus status = PreflightCheckStatus.Ready) =>
        new(
            id,
            order,
            (_, _) => ValueTask.FromResult(TestData.CheckResult(id, status)));

    private static void AssertAggregate(
        PreflightAggregateResult aggregate,
        PreflightAggregateId expectedId,
        params PreflightCheckId[] expectedChecks)
    {
        Assert.Equal(expectedId, aggregate.Id);
        Assert.Equal(expectedChecks, aggregate.Prerequisites.Select(result => result.Id));
    }

    private sealed class RecordingProgress<T>(Action<T> record) : IProgress<T>
    {
        public void Report(T value) => record(value);
    }
}
