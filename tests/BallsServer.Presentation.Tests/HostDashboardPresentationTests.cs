using BallsServer.Core.Preflight;
using BallsServer.Presentation;

namespace BallsServer.Presentation.Tests;

public sealed class HostDashboardPresentationTests
{
    private const string DocumentsFolder = @"C:\Users\Owner\Documents";
    private const string OtherFolder = @"D:\Shared";

    [Fact]
    public void InitialStateUsesTheCurrentProfilesDocumentsFolder()
    {
        var dashboard = new HostDashboardPresentation(
            new NeverRunPreflightService(),
            new StubFolderValidator(_ => FolderValidation.Valid(DocumentsFolder)),
            DocumentsFolder);

        Assert.Equal(DocumentsFolder, dashboard.SelectedFolder);
        Assert.Equal(DashboardRunState.NotChecked, dashboard.RunState);
        Assert.Equal("Not checked", dashboard.SnapshotStatusText);
        Assert.Null(dashboard.LastCompletedSnapshot);
        Assert.True(dashboard.CanRefresh);
        Assert.False(dashboard.CanCancel);
        Assert.True(dashboard.CanEditFolder);
        Assert.Collection(
            dashboard.SummaryAreas,
            area => Assert.Equal(DashboardAreaId.Computer, area.Id),
            area => Assert.Equal(DashboardAreaId.ManagedFolder, area.Id),
            area => Assert.Equal(DashboardAreaId.LocalAccess, area.Id),
            area => Assert.Equal(DashboardAreaId.TailscaleAccess, area.Id),
            area => Assert.Equal(DashboardAreaId.HostingState, area.Id));
        Assert.All(
            dashboard.SummaryAreas.Take(4),
            area => Assert.Equal(DashboardStatusKind.NotChecked, area.Status.Kind));
        var hosting = dashboard.SummaryAreas[^1];
        Assert.Equal(DashboardStatusKind.NotConfigured, hosting.Status.Kind);
        Assert.Contains("not configured", hosting.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchShowsOrderedProgressWithoutPublishingAPartialSnapshot()
    {
        var completions = HostPreflightCatalog.OrderedCheckIds
            .Select(_ => new TaskCompletionSource<PreflightCheckResult>(
                TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var checks = HostPreflightCatalog.OrderedCheckIds
            .Select((id, index) => new DelegatePreflightCheck(
                id,
                (index + 1) * 10,
                (_, _) => new ValueTask<PreflightCheckResult>(completions[index].Task)))
            .ToArray();
        var dashboard = CreateDashboard(new PreflightService(checks, PreflightPolicy.HostDefault));

        var launch = dashboard.LaunchAsync();

        Assert.Equal(DashboardRunState.Running, dashboard.RunState);
        Assert.False(dashboard.CanRefresh);
        Assert.True(dashboard.CanCancel);
        Assert.False(dashboard.CanEditFolder);

        for (var index = 0; index < completions.Length; index++)
        {
            var expectedId = HostPreflightCatalog.OrderedCheckIds[index];
            await WaitUntilAsync(() => dashboard.Progress?.Position == index + 1);
            Assert.Equal(expectedId, dashboard.Progress?.CheckId);
            Assert.Equal(completions.Length, dashboard.Progress?.Total);
            Assert.Null(dashboard.LastCompletedSnapshot);
            completions[index].SetResult(CheckResult(expectedId));
        }

        await launch;

        Assert.Equal(DashboardRunState.Completed, dashboard.RunState);
        Assert.Equal("Checked", dashboard.SnapshotStatusText);
        Assert.Equal(DocumentsFolder, dashboard.LastCompletedSnapshot?.TargetPath);
        Assert.Equal(8, dashboard.LastCompletedSnapshot?.Checks.Count);
    }

    [Fact]
    public async Task ChangingTheFolderMarksTheCompletedSnapshotAsNeedingRefresh()
    {
        var dashboard = new HostDashboardPresentation(
            new PreflightService(CreateChecks(), PreflightPolicy.HostDefault),
            new StubFolderValidator(path => FolderValidation.Valid(path)),
            DocumentsFolder);
        await dashboard.LaunchAsync();

        dashboard.SelectFolder(OtherFolder);

        Assert.Equal(OtherFolder, dashboard.SelectedFolder);
        Assert.True(dashboard.SnapshotNeedsRefresh);
        Assert.Equal("Needs Refresh", dashboard.SnapshotStatusText);

        await dashboard.RefreshAsync();

        Assert.Equal(OtherFolder, dashboard.LastCompletedSnapshot?.TargetPath);
        Assert.False(dashboard.SnapshotNeedsRefresh);
        Assert.Equal("Checked", dashboard.SnapshotStatusText);
    }

    [Fact]
    public async Task InvalidFoldersAreExplainedBeforeOrchestrationStarts()
    {
        var dashboard = new HostDashboardPresentation(
            new NeverRunPreflightService(),
            new SystemFolderValidator(),
            Path.GetTempPath());

        dashboard.SelectFolder("  ");
        await dashboard.RefreshAsync();

        Assert.Equal("Enter an existing folder to check.", dashboard.FolderValidationMessage);
        Assert.False(dashboard.CanRefresh);
        Assert.Equal(DashboardRunState.NotChecked, dashboard.RunState);

        dashboard.SelectFolder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        await dashboard.RefreshAsync();

        Assert.Equal("That folder does not exist. Choose an existing folder.", dashboard.FolderValidationMessage);
        Assert.False(dashboard.CanRefresh);
        Assert.Null(dashboard.LastCompletedSnapshot);
    }

    [Fact]
    public async Task RefreshRevalidatesAFolderThatNoLongerExistsBeforeOrchestrationStarts()
    {
        var validations = new Queue<FolderValidation>(
        [
            FolderValidation.Valid(DocumentsFolder),
            FolderValidation.Invalid("That folder does not exist. Choose an existing folder."),
        ]);
        var dashboard = new HostDashboardPresentation(
            new NeverRunPreflightService(),
            new StubFolderValidator(_ => validations.Dequeue()),
            DocumentsFolder);

        await dashboard.RefreshAsync();

        Assert.Equal("That folder does not exist. Choose an existing folder.", dashboard.FolderValidationMessage);
        Assert.Equal(DashboardRunState.NotChecked, dashboard.RunState);
        Assert.False(dashboard.CanRefresh);
        Assert.Null(dashboard.LastCompletedSnapshot);
    }

    [Fact]
    public async Task CancelingTheInitialRunKeepsNotCheckedAndSuppressesLateCompletion()
    {
        var lateResult = new TaskCompletionSource<PreflightCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = CreateChecks();
        checks[^1] = new DelegatePreflightCheck(
            PreflightCheckId.FolderPermissions,
            80,
            (_, _) => new ValueTask<PreflightCheckResult>(lateResult.Task));
        var dashboard = CreateDashboard(new PreflightService(checks, PreflightPolicy.HostDefault));
        var launch = dashboard.LaunchAsync();

        dashboard.Cancel();

        Assert.Equal(DashboardRunState.Canceled, dashboard.RunState);
        Assert.Equal("Canceled", dashboard.RunStatusText);
        Assert.Equal("Not checked", dashboard.SnapshotStatusText);
        Assert.Null(dashboard.LastCompletedSnapshot);
        Assert.False(dashboard.CanRefresh);
        Assert.False(dashboard.CanCancel);

        lateResult.SetResult(CheckResult(PreflightCheckId.FolderPermissions));
        await launch;

        Assert.Equal(DashboardRunState.Canceled, dashboard.RunState);
        Assert.Null(dashboard.LastCompletedSnapshot);
        Assert.True(dashboard.CanRefresh);
    }

    [Fact]
    public async Task AnActiveRunRejectsAnotherRefreshAndFolderEdits()
    {
        var lateResult = new TaskCompletionSource<PreflightCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = CreateChecks();
        checks[^1] = new DelegatePreflightCheck(
            PreflightCheckId.FolderPermissions,
            80,
            (_, _) => new ValueTask<PreflightCheckResult>(lateResult.Task));
        var dashboard = CreateDashboard(new PreflightService(checks, PreflightPolicy.HostDefault));
        var launch = dashboard.LaunchAsync();

        dashboard.SelectFolder(OtherFolder);
        await dashboard.RefreshAsync();

        Assert.Equal(DocumentsFolder, dashboard.SelectedFolder);
        Assert.Equal(8, dashboard.Progress?.Position);
        Assert.Null(dashboard.LastCompletedSnapshot);

        lateResult.SetResult(CheckResult(PreflightCheckId.FolderPermissions));
        await launch;

        Assert.Equal(DocumentsFolder, dashboard.LastCompletedSnapshot?.TargetPath);
    }

    [Fact]
    public async Task CancelingARefreshPreservesThePriorSnapshot()
    {
        var lateResult = new TaskCompletionSource<PreflightCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRun = 0;
        var checks = CreateChecks();
        checks[^1] = new DelegatePreflightCheck(
            PreflightCheckId.FolderPermissions,
            80,
            (_, _) => Interlocked.Increment(ref secondRun) == 1
                ? ValueTask.FromResult(CheckResult(PreflightCheckId.FolderPermissions))
                : new ValueTask<PreflightCheckResult>(lateResult.Task));
        var dashboard = new HostDashboardPresentation(
            new PreflightService(checks, PreflightPolicy.HostDefault),
            new StubFolderValidator(path => FolderValidation.Valid(path)),
            DocumentsFolder);
        await dashboard.LaunchAsync();
        var priorSnapshot = dashboard.LastCompletedSnapshot;
        dashboard.SelectFolder(OtherFolder);
        var refresh = dashboard.RefreshAsync();

        Assert.Same(priorSnapshot, dashboard.LastCompletedSnapshot);
        Assert.Equal(DocumentsFolder, dashboard.LastCompletedSnapshot?.TargetPath);
        Assert.Equal(DashboardRunState.Running, dashboard.RunState);
        dashboard.Cancel();

        Assert.Same(priorSnapshot, dashboard.LastCompletedSnapshot);
        Assert.True(dashboard.SnapshotNeedsRefresh);
        Assert.Equal("Needs Refresh", dashboard.SnapshotStatusText);

        lateResult.SetResult(CheckResult(PreflightCheckId.FolderPermissions));
        await refresh;

        Assert.Same(priorSnapshot, dashboard.LastCompletedSnapshot);
    }

    [Fact]
    public async Task UnexpectedOrchestrationFailurePreservesThePriorSnapshot()
    {
        var time = new ThrowAfterTimeProvider(
            new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 13, 12, 0, 8, TimeSpan.Zero));
        var dashboard = CreateDashboard(
            new PreflightService(CreateChecks(), PreflightPolicy.HostDefault, time));
        await dashboard.LaunchAsync();
        var priorSnapshot = dashboard.LastCompletedSnapshot;

        await dashboard.RefreshAsync();

        Assert.Equal(DashboardRunState.Failed, dashboard.RunState);
        Assert.Equal("Could not check", dashboard.RunStatusText);
        Assert.Same(priorSnapshot, dashboard.LastCompletedSnapshot);
        Assert.Equal("Checked", dashboard.SnapshotStatusText);
    }

    [Fact]
    public async Task ClosingCancelsActiveWorkAndPreventsAnotherRun()
    {
        var lateResult = new TaskCompletionSource<PreflightCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = CreateChecks();
        checks[^1] = new DelegatePreflightCheck(
            PreflightCheckId.FolderPermissions,
            80,
            (_, _) => new ValueTask<PreflightCheckResult>(lateResult.Task));
        var dashboard = CreateDashboard(new PreflightService(checks, PreflightPolicy.HostDefault));
        var launch = dashboard.LaunchAsync();

        dashboard.Close();
        lateResult.SetResult(CheckResult(PreflightCheckId.FolderPermissions));
        await launch;
        await dashboard.RefreshAsync();

        Assert.Equal(DashboardRunState.Canceled, dashboard.RunState);
        Assert.Null(dashboard.LastCompletedSnapshot);
        Assert.False(dashboard.CanRefresh);
        Assert.False(dashboard.CanEditFolder);
    }

    [Fact]
    public async Task CompletionPublishesTheRequestedFolderAndObservationTimesTogether()
    {
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddSeconds(8);
        var dashboard = CreateDashboard(new PreflightService(
            CreateChecks(),
            PreflightPolicy.HostDefault,
            new SequenceTimeProvider(startedAt, completedAt)));

        await dashboard.LaunchAsync();

        var snapshot = Assert.IsType<PreflightReport>(dashboard.LastCompletedSnapshot);
        Assert.Equal(DocumentsFolder, snapshot.TargetPath);
        Assert.Equal(startedAt, snapshot.StartedAt);
        Assert.Equal(completedAt, snapshot.CompletedAt);
    }

    [Fact]
    public async Task CompletionPublishesIndependentReadinessAndFutureSetupInformation()
    {
        var checks = CreateChecks();
        checks[0] = new DelegatePreflightCheck(
            PreflightCheckId.Administrator,
            10,
            (_, _) => ValueTask.FromResult(CheckResult(
                PreflightCheckId.Administrator,
                PreflightCheckStatus.ActionRequired)));
        checks[5] = new DelegatePreflightCheck(
            PreflightCheckId.Tailscale,
            60,
            (_, _) => ValueTask.FromResult(CheckResult(
                PreflightCheckId.Tailscale,
                PreflightCheckStatus.ActionRequired)));
        var dashboard = CreateDashboard(new PreflightService(checks, PreflightPolicy.HostDefault));

        await dashboard.LaunchAsync();

        var snapshot = Assert.IsType<PreflightReport>(dashboard.LastCompletedSnapshot);
        Assert.Equal(PreflightOverallStatus.Ready, snapshot.LocalAccess.Status);
        Assert.Equal(PreflightOverallStatus.NotReady, snapshot.TailscaleAccess.Status);
        Assert.Equal(
            AdministratorInformationAvailability.Available,
            snapshot.AdministratorInformation.Availability);
        Assert.All(
            new[] { snapshot.Computer, snapshot.ManagedFolder, snapshot.LocalAccess, snapshot.TailscaleAccess },
            aggregate => Assert.False(string.IsNullOrWhiteSpace(aggregate.Summary)));
        Assert.DoesNotContain(
            snapshot.Prerequisites,
            result => result.Id == PreflightCheckId.Administrator);
    }

    [Fact]
    public async Task CompletionPublishesAccessibleSummaryAreasWithIndividualDetails()
    {
        var startedAt = new DateTimeOffset(2026, 8, 14, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddSeconds(8);
        var checks = CreateChecks();
        checks[6] = new DelegatePreflightCheck(
            PreflightCheckId.Smb,
            70,
            (_, _) => ValueTask.FromResult(PreflightCheckResult.Create(
                PreflightCheckId.Smb,
                "SMB file sharing",
                PreflightCheckStatus.Warning,
                "smb_advisory",
                "SMB meets policy with an advisory warning.",
                new PreflightEvidence("Protocol", "SMB 3.0 or newer"))));
        var dashboard = CreateDashboard(new PreflightService(
            checks,
            PreflightPolicy.HostDefault,
            new SequenceTimeProvider(startedAt, completedAt)));

        await dashboard.LaunchAsync();

        Assert.Collection(
            dashboard.SummaryAreas,
            computer =>
            {
                Assert.Equal(DashboardAreaId.Computer, computer.Id);
                Assert.Equal(DashboardStatusKind.ReadyWithWarnings, computer.Status.Kind);
                Assert.Equal("Ready with warnings", computer.Status.Text);
                Assert.False(string.IsNullOrWhiteSpace(computer.Status.Cue));
                Assert.Equal(3, computer.PrerequisiteResults.Count);
                var smb = Assert.Single(computer.PrerequisiteResults, result => result.Id == PreflightCheckId.Smb);
                Assert.Equal("smb_advisory", smb.ReasonCode);
                Assert.Equal("SMB 3.0 or newer", Assert.Single(smb.Evidence).Value);
                Assert.Contains("Computer: Ready with warnings", computer.AccessibleStatus, StringComparison.Ordinal);
            },
            managedFolder => Assert.Equal(2, managedFolder.PrerequisiteResults.Count),
            localAccess => Assert.Equal(6, localAccess.PrerequisiteResults.Count),
            tailscaleAccess => Assert.Equal(6, tailscaleAccess.PrerequisiteResults.Count),
            hosting =>
            {
                Assert.Equal(DashboardAreaId.HostingState, hosting.Id);
                Assert.Equal(DashboardStatusKind.NotConfigured, hosting.Status.Kind);
                Assert.Equal("Not configured", hosting.Status.Text);
                Assert.False(string.IsNullOrWhiteSpace(hosting.Status.Cue));
                Assert.Empty(hosting.PrerequisiteResults);
                Assert.Contains("does not inspect or adopt existing Windows shares", hosting.DetailsSummary, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Hosting state: Not configured", hosting.AccessibleStatus, StringComparison.Ordinal);
            });
        Assert.All(dashboard.SummaryAreas, area =>
        {
            Assert.Equal(startedAt, area.ObservedFrom);
            Assert.Equal(completedAt, area.ObservedAt);
            Assert.False(area.IsDetailsExpanded);
            Assert.StartsWith("Show", area.DetailsToggleText, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(area.DetailsToggleAccessibleName));
        });
    }

    [Fact]
    public async Task ExpandingAndCollapsingDetailsChangesPresentationOnly()
    {
        var service = new CountingPreflightService(
            new PreflightService(CreateChecks(), PreflightPolicy.HostDefault));
        var dashboard = CreateDashboard(service);
        await dashboard.LaunchAsync();
        var snapshot = dashboard.LastCompletedSnapshot;

        dashboard.SetDetailsExpanded(DashboardAreaId.Computer, isExpanded: true);

        var expanded = Assert.Single(
            dashboard.SummaryAreas,
            area => area.Id == DashboardAreaId.Computer);
        Assert.True(expanded.IsDetailsExpanded);
        Assert.Equal("Hide details", expanded.DetailsToggleText);
        Assert.Equal("Hide Computer details", expanded.DetailsToggleAccessibleName);
        Assert.Same(snapshot, dashboard.LastCompletedSnapshot);
        Assert.Equal(1, service.RunCount);

        dashboard.SetDetailsExpanded(DashboardAreaId.Computer, isExpanded: false);

        var collapsed = Assert.Single(
            dashboard.SummaryAreas,
            area => area.Id == DashboardAreaId.Computer);
        Assert.False(collapsed.IsDetailsExpanded);
        Assert.Equal("Show details", collapsed.DetailsToggleText);
        Assert.Equal("Show Computer details", collapsed.DetailsToggleAccessibleName);
        Assert.Same(snapshot, dashboard.LastCompletedSnapshot);
        Assert.Equal(1, service.RunCount);
    }

    [Fact]
    public async Task DetailsUseCanonicalTextAndNonColorCuesForDistinctStatuses()
    {
        var checks = CreateChecks();
        checks[1] = new DelegatePreflightCheck(
            PreflightCheckId.WindowsVersion,
            20,
            (_, _) => ValueTask.FromResult(CheckResult(
                PreflightCheckId.WindowsVersion,
                PreflightCheckStatus.Warning)));
        checks[4] = new DelegatePreflightCheck(
            PreflightCheckId.Firewall,
            50,
            (_, _) => ValueTask.FromResult(CheckResult(
                PreflightCheckId.Firewall,
                PreflightCheckStatus.ActionRequired)));
        checks[6] = new DelegatePreflightCheck(
            PreflightCheckId.Smb,
            70,
            (_, _) => ValueTask.FromResult(CheckResult(
                PreflightCheckId.Smb,
                PreflightCheckStatus.Unknown)));
        var dashboard = CreateDashboard(new PreflightService(checks, PreflightPolicy.HostDefault));

        await dashboard.LaunchAsync();

        var computer = Assert.Single(
            dashboard.SummaryAreas,
            area => area.Id == DashboardAreaId.Computer);
        Assert.Equal((DashboardStatusKind.NotReady, "Not ready", "⊘"),
            (computer.Status.Kind, computer.Status.Text, computer.Status.Cue));
        Assert.Collection(
            computer.PrerequisiteResults,
            warning => Assert.Equal(
                (DashboardStatusKind.Warning, "Warning", "!"),
                (warning.Status.Kind, warning.Status.Text, warning.Status.Cue)),
            actionRequired => Assert.Equal(
                (DashboardStatusKind.ActionRequired, "Action required", "×"),
                (actionRequired.Status.Kind, actionRequired.Status.Text, actionRequired.Status.Cue)),
            unknown => Assert.Equal(
                (DashboardStatusKind.Unknown, "Unknown", "?"),
                (unknown.Status.Kind, unknown.Status.Text, unknown.Status.Cue)));
    }

    private static HostDashboardPresentation CreateDashboard(IPreflightService service) =>
        new(
            service,
            new StubFolderValidator(_ => FolderValidation.Valid(DocumentsFolder)),
            DocumentsFolder);

    private static DelegatePreflightCheck[] CreateChecks() =>
        HostPreflightCatalog.OrderedCheckIds
            .Select((id, index) => new DelegatePreflightCheck(
                id,
                (index + 1) * 10,
                (_, _) => ValueTask.FromResult(CheckResult(id))))
            .ToArray();

    private static PreflightCheckResult CheckResult(
        PreflightCheckId id,
        PreflightCheckStatus status = PreflightCheckStatus.Ready) =>
        PreflightCheckResult.Create(
            id,
            id == PreflightCheckId.Administrator ? "Administrator" : id.ToString(),
            status,
            status.ToString(),
            $"{status}.");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected presentation state was not observed in time.");
    }

    private sealed class NeverRunPreflightService : IPreflightService
    {
        public Task<PreflightReport> RunAsync(
            PreflightRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The initial state must not start orchestration.");

        public Task<PreflightReport> RunAsync(
            PreflightRequest request,
            IProgress<PreflightProgress> progress,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The initial state must not start orchestration.");
    }

    private sealed class CountingPreflightService(IPreflightService inner) : IPreflightService
    {
        public int RunCount { get; private set; }

        public Task<PreflightReport> RunAsync(
            PreflightRequest request,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            return inner.RunAsync(request, cancellationToken);
        }

        public Task<PreflightReport> RunAsync(
            PreflightRequest request,
            IProgress<PreflightProgress> progress,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            return inner.RunAsync(request, progress, cancellationToken);
        }
    }

    private sealed class StubFolderValidator(Func<string, FolderValidation> validate) : IFolderValidator
    {
        public FolderValidation Validate(string path) => validate(path);
    }

    private sealed class DelegatePreflightCheck(
        PreflightCheckId id,
        int order,
        Func<PreflightContext, CancellationToken, ValueTask<PreflightCheckResult>> run) : IPreflightCheck
    {
        public PreflightCheckId Id => id;

        public string Title => id == PreflightCheckId.Administrator ? "Administrator" : id.ToString();

        public int Order => order;

        public ValueTask<PreflightCheckResult> CheckAsync(
            PreflightContext context,
            CancellationToken cancellationToken) =>
            run(context, cancellationToken);
    }

    private class SequenceTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _values = new(values);

        public override DateTimeOffset GetUtcNow() => _values.Dequeue();
    }

    private sealed class ThrowAfterTimeProvider(params DateTimeOffset[] values) : SequenceTimeProvider(values)
    {
    }
}
