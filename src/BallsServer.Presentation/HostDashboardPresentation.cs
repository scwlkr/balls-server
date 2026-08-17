using System.Collections.ObjectModel;
using System.ComponentModel;
using BallsServer.Core.Preflight;

namespace BallsServer.Presentation;

public enum DashboardRunState
{
    NotChecked,
    Running,
    Completed,
    Canceled,
    Failed,
}

public enum DashboardAreaId
{
    Computer,
    ManagedFolder,
    LocalAccess,
    TailscaleAccess,
    HostingState,
}

public enum DashboardStatusKind
{
    NotChecked,
    Ready,
    Warning,
    ActionRequired,
    Unknown,
    ReadyWithWarnings,
    NotReady,
    Indeterminate,
    NotConfigured,
}

public sealed record DashboardStatusPresentation(
    DashboardStatusKind Kind,
    string Text,
    string Cue);

internal static class DashboardAccessibility
{
    public static string Describe(
        string title,
        DashboardStatusPresentation status,
        string summary) =>
        $"{title}: {status.Text}. {summary}";
}

public sealed record DashboardPrerequisitePresentation(
    PreflightCheckId Id,
    string Title,
    DashboardStatusPresentation Status,
    string Summary,
    string ReasonCode,
    IReadOnlyList<PreflightEvidence> Evidence)
{
    public string AccessibleStatus => DashboardAccessibility.Describe(Title, Status, Summary);
}

public sealed record DashboardAreaPresentation(
    DashboardAreaId Id,
    string Title,
    DashboardStatusPresentation Status,
    string Summary,
    string? EvaluatedFolderPath,
    DateTimeOffset? ObservedFrom,
    DateTimeOffset? ObservedAt,
    IReadOnlyList<DashboardPrerequisitePresentation> PrerequisiteResults,
    string DetailsSummary,
    bool IsDetailsExpanded)
{
    public string AccessibleStatus => DashboardAccessibility.Describe(Title, Status, Summary);

    public string DetailsToggleText => IsDetailsExpanded ? "Hide details" : "Show details";

    public string DetailsToggleAccessibleName =>
        IsDetailsExpanded ? $"Hide {Title} details" : $"Show {Title} details";
}

public sealed record FolderValidation(bool IsValid, string? ValidatedPath, string? ErrorMessage)
{
    public static FolderValidation Valid(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FolderValidation(true, path, null);
    }

    public static FolderValidation Invalid(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new FolderValidation(false, null, message);
    }
}

public interface IFolderValidator
{
    FolderValidation Validate(string path);
}

public sealed class SystemFolderValidator : IFolderValidator
{
    public FolderValidation Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FolderValidation.Invalid("Enter an existing folder to check.");
        }

        var trimmedPath = path.Trim();

        try
        {
            return Directory.Exists(trimmedPath)
                ? FolderValidation.Valid(Path.GetFullPath(trimmedPath))
                : FolderValidation.Invalid("That folder does not exist. Choose an existing folder.");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FolderValidation.Invalid("That folder path is not valid. Choose an existing folder.");
        }
    }
}

public interface IHostDashboardPresentation : INotifyPropertyChanged
{
    string SelectedFolder { get; }

    DashboardRunState RunState { get; }

    string RunStatusText { get; }

    PreflightProgress? Progress { get; }

    string SnapshotStatusText { get; }

    PreflightReport? LastCompletedSnapshot { get; }

    IReadOnlyList<DashboardAreaPresentation> SummaryAreas { get; }

    bool SnapshotNeedsRefresh { get; }

    string? FolderValidationMessage { get; }

    bool CanRefresh { get; }

    bool CanCancel { get; }

    bool CanEditFolder { get; }

    Task LaunchAsync();

    Task RefreshAsync();

    void SelectFolder(string path);

    void SetDetailsExpanded(DashboardAreaId areaId, bool isExpanded);

    void Cancel();

    void Close();
}

public sealed class HostDashboardPresentation : IHostDashboardPresentation
{
    private const string HostingDetailsSummary =
        "Hosting state is separate from prerequisite results and access-path readiness. Balls Server does not inspect or adopt existing Windows shares.";
    private static readonly ReadOnlyCollection<(DashboardAreaId Id, string Title)> DashboardAreas =
        Array.AsReadOnly<(DashboardAreaId, string)>(
        [
            (DashboardAreaId.Computer, "Computer"),
            (DashboardAreaId.ManagedFolder, "Managed folder"),
            (DashboardAreaId.LocalAccess, "Local access"),
            (DashboardAreaId.TailscaleAccess, "Tailscale access"),
            (DashboardAreaId.HostingState, "Hosting state"),
        ]);
    private readonly IPreflightService _preflightService;
    private readonly IFolderValidator _folderValidator;
    private FolderValidation _folderValidation;
    private string _selectedFolder;
    private bool _hasLaunched;
    private bool _isRunning;
    private bool _cancelRequested;
    private bool _closed;
    private long _nextRunId;
    private long _acceptedRunId;
    private CancellationTokenSource? _runCancellation;
    private DashboardRunState _runState = DashboardRunState.NotChecked;
    private PreflightProgress? _progress;
    private PreflightReport? _lastCompletedSnapshot;
    private IReadOnlyList<DashboardAreaPresentation> _summaryAreas;
    private readonly HashSet<DashboardAreaId> _expandedAreaIds = [];

    public HostDashboardPresentation(
        IPreflightService preflightService,
        IFolderValidator folderValidator,
        string documentsFolder)
    {
        ArgumentNullException.ThrowIfNull(preflightService);
        ArgumentNullException.ThrowIfNull(folderValidator);
        ArgumentNullException.ThrowIfNull(documentsFolder);

        _preflightService = preflightService;
        _folderValidator = folderValidator;
        _selectedFolder = documentsFolder;
        _folderValidation = folderValidator.Validate(documentsFolder);
        _summaryAreas = CreatePendingSummaryAreas(_expandedAreaIds);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SelectedFolder => _selectedFolder;

    public DashboardRunState RunState => _runState;

    public string RunStatusText => RunState switch
    {
        DashboardRunState.NotChecked => "Not checked",
        DashboardRunState.Running => "Checking…",
        DashboardRunState.Completed => "Check complete",
        DashboardRunState.Canceled => "Canceled",
        DashboardRunState.Failed => "Could not check",
        _ => "Not checked",
    };

    public PreflightProgress? Progress => _progress;

    public string SnapshotStatusText => LastCompletedSnapshot is null
        ? "Not checked"
        : SnapshotNeedsRefresh ? "Needs Refresh" : "Checked";

    public PreflightReport? LastCompletedSnapshot => _lastCompletedSnapshot;

    public IReadOnlyList<DashboardAreaPresentation> SummaryAreas => _summaryAreas;

    public bool SnapshotNeedsRefresh => LastCompletedSnapshot is not null &&
        !string.Equals(
            _folderValidation.ValidatedPath ?? SelectedFolder.Trim(),
            LastCompletedSnapshot.TargetPath,
            StringComparison.OrdinalIgnoreCase);

    public string? FolderValidationMessage => _folderValidation.ErrorMessage;

    public bool CanRefresh => !_closed && !_isRunning && _folderValidation.IsValid;

    public bool CanCancel => _isRunning && !_cancelRequested;

    public bool CanEditFolder => !_closed && !_isRunning;

    public Task LaunchAsync()
    {
        if (_closed || _hasLaunched)
        {
            return Task.CompletedTask;
        }

        _hasLaunched = true;
        return RunAsync();
    }

    public Task RefreshAsync() => CanRefresh ? RunAsync() : Task.CompletedTask;

    public void SelectFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!CanEditFolder || string.Equals(_selectedFolder, path, StringComparison.Ordinal))
        {
            return;
        }

        _selectedFolder = path;
        _folderValidation = _folderValidator.Validate(path);
        OnPropertyChanged(nameof(SelectedFolder));
        OnPropertyChanged(nameof(FolderValidationMessage));
        OnPropertyChanged(nameof(SnapshotNeedsRefresh));
        OnPropertyChanged(nameof(SnapshotStatusText));
        OnPropertyChanged(nameof(CanRefresh));
    }

    public void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }

        _cancelRequested = true;
        _acceptedRunId = ++_nextRunId;
        _runCancellation?.Cancel();
        SetRunState(DashboardRunState.Canceled);
        _progress = null;
        OnPropertyChanged(nameof(Progress));
        NotifyActionStateChanged();
    }

    public void SetDetailsExpanded(DashboardAreaId areaId, bool isExpanded)
    {
        if (!Enum.IsDefined(areaId) || _expandedAreaIds.Contains(areaId) == isExpanded)
        {
            return;
        }

        if (isExpanded)
        {
            _expandedAreaIds.Add(areaId);
        }
        else
        {
            _expandedAreaIds.Remove(areaId);
        }

        _summaryAreas = LastCompletedSnapshot is { } snapshot
            ? CreateSummaryAreas(snapshot, _expandedAreaIds)
            : CreatePendingSummaryAreas(_expandedAreaIds);
        OnPropertyChanged(nameof(SummaryAreas));
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        Cancel();
        _closed = true;
        NotifyActionStateChanged();
    }

    private async Task RunAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _folderValidation = _folderValidator.Validate(SelectedFolder);
        OnPropertyChanged(nameof(FolderValidationMessage));
        OnPropertyChanged(nameof(CanRefresh));

        if (!_folderValidation.IsValid)
        {
            return;
        }

        var requestedFolder = _folderValidation.ValidatedPath!;
        var runId = ++_nextRunId;
        _acceptedRunId = runId;
        _cancelRequested = false;
        _isRunning = true;
        _progress = null;
        SetRunState(DashboardRunState.Running);
        OnPropertyChanged(nameof(Progress));
        NotifyActionStateChanged();

        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;

        try
        {
            var progress = new CallbackProgress<PreflightProgress>(value =>
            {
                if (!IsAccepted(runId))
                {
                    return;
                }

                _progress = value;
                OnPropertyChanged(nameof(Progress));
            });
            var report = await _preflightService.RunAsync(
                new PreflightRequest(requestedFolder),
                progress,
                cancellation.Token);

            if (!IsAccepted(runId))
            {
                return;
            }

            _lastCompletedSnapshot = report;
            _summaryAreas = CreateSummaryAreas(report, _expandedAreaIds);
            SetRunState(DashboardRunState.Completed);
            OnPropertyChanged(nameof(LastCompletedSnapshot));
            OnPropertyChanged(nameof(SummaryAreas));
            OnPropertyChanged(nameof(SnapshotNeedsRefresh));
            OnPropertyChanged(nameof(SnapshotStatusText));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsAccepted(runId))
            {
                SetRunState(DashboardRunState.Canceled);
            }
        }
        catch (Exception)
        {
            if (IsAccepted(runId))
            {
                SetRunState(DashboardRunState.Failed);
            }
        }
        finally
        {
            if (ReferenceEquals(_runCancellation, cancellation))
            {
                _runCancellation = null;
            }

            _isRunning = false;
            NotifyActionStateChanged();
        }
    }

    private void SetRunState(DashboardRunState state)
    {
        _runState = state;
        OnPropertyChanged(nameof(RunState));
        OnPropertyChanged(nameof(RunStatusText));
    }

    private void NotifyActionStateChanged()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanEditFolder));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool IsAccepted(long runId) => runId == _acceptedRunId;

    private static ReadOnlyCollection<DashboardAreaPresentation> CreatePendingSummaryAreas(
        HashSet<DashboardAreaId> expandedAreaIds) =>
        Array.AsReadOnly(DashboardAreas
            .Select(area => CreatePendingArea(area.Id, area.Title, expandedAreaIds))
            .ToArray());

    private static DashboardAreaPresentation CreatePendingArea(
        DashboardAreaId id,
        string title,
        HashSet<DashboardAreaId> expandedAreaIds)
    {
        var isHostingState = id == DashboardAreaId.HostingState;
        var hostingState = HostingStateResult.NotConfigured;

        return new DashboardAreaPresentation(
            id,
            title,
            Status(isHostingState ? DashboardStatusKind.NotConfigured : DashboardStatusKind.NotChecked),
            isHostingState
                ? hostingState.Summary
                : "Waiting for a completed Host Files preflight observation.",
            EvaluatedFolderPath: null,
            ObservedFrom: null,
            ObservedAt: null,
            PrerequisiteResults: Array.Empty<DashboardPrerequisitePresentation>(),
            isHostingState
                ? HostingDetailsSummary
                : "Individual prerequisite results and safe evidence appear after the Host Files preflight completes.",
            expandedAreaIds.Contains(id));
    }

    private static ReadOnlyCollection<DashboardAreaPresentation> CreateSummaryAreas(
        PreflightReport report,
        HashSet<DashboardAreaId> expandedAreaIds) =>
        Array.AsReadOnly(DashboardAreas
            .Select(area => CreateCompletedArea(area.Id, report, expandedAreaIds))
            .ToArray());

    private static DashboardAreaPresentation CreateCompletedArea(
        DashboardAreaId id,
        PreflightReport report,
        HashSet<DashboardAreaId> expandedAreaIds)
    {
        if (id == DashboardAreaId.HostingState)
        {
            return new DashboardAreaPresentation(
                DashboardAreaId.HostingState,
                report.HostingState.Title,
                Status(report.HostingState.State),
                report.HostingState.Summary,
                EvaluatedFolderPath: null,
                report.StartedAt,
                report.CompletedAt,
                PrerequisiteResults: Array.Empty<DashboardPrerequisitePresentation>(),
                HostingDetailsSummary,
                expandedAreaIds.Contains(DashboardAreaId.HostingState));
        }

        var aggregate = id switch
        {
            DashboardAreaId.Computer => report.Computer,
            DashboardAreaId.ManagedFolder => report.ManagedFolder,
            DashboardAreaId.LocalAccess => report.LocalAccess,
            DashboardAreaId.TailscaleAccess => report.TailscaleAccess,
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };

        return CreateAggregateArea(id, aggregate, report, expandedAreaIds);
    }

    private static DashboardAreaPresentation CreateAggregateArea(
        DashboardAreaId id,
        PreflightAggregateResult aggregate,
        PreflightReport report,
        HashSet<DashboardAreaId> expandedAreaIds) =>
        new(
            id,
            aggregate.Title,
            Status(aggregate.Status),
            aggregate.Summary,
            aggregate.EvaluatedFolderPath,
            report.StartedAt,
            report.CompletedAt,
            Array.AsReadOnly(aggregate.Prerequisites.Select(CreatePrerequisite).ToArray()),
            "Individual prerequisite results for this area, with plain-language reasons, reason codes, and safe evidence.",
            expandedAreaIds.Contains(id));

    private static DashboardPrerequisitePresentation CreatePrerequisite(PreflightCheckResult result) =>
        new(
            result.Id,
            result.Title,
            Status(result.Status),
            result.Summary,
            result.ReasonCode,
            result.Evidence);

    private static DashboardStatusPresentation Status(PreflightCheckStatus status) => status switch
    {
        PreflightCheckStatus.Ready => Status(DashboardStatusKind.Ready),
        PreflightCheckStatus.Warning => Status(DashboardStatusKind.Warning),
        PreflightCheckStatus.ActionRequired => Status(DashboardStatusKind.ActionRequired),
        PreflightCheckStatus.Unknown => Status(DashboardStatusKind.Unknown),
        _ => Status(DashboardStatusKind.Unknown),
    };

    private static DashboardStatusPresentation Status(PreflightOverallStatus status) => status switch
    {
        PreflightOverallStatus.Ready => Status(DashboardStatusKind.Ready),
        PreflightOverallStatus.ReadyWithWarnings => Status(DashboardStatusKind.ReadyWithWarnings),
        PreflightOverallStatus.NotReady => Status(DashboardStatusKind.NotReady),
        PreflightOverallStatus.Indeterminate => Status(DashboardStatusKind.Indeterminate),
        _ => Status(DashboardStatusKind.Unknown),
    };

    private static DashboardStatusPresentation Status(HostingState state) => state switch
    {
        HostingState.NotConfigured => Status(DashboardStatusKind.NotConfigured),
        _ => Status(DashboardStatusKind.NotConfigured),
    };

    private static DashboardStatusPresentation Status(DashboardStatusKind kind) => kind switch
    {
        DashboardStatusKind.NotChecked => new(kind, "Not checked", "–"),
        DashboardStatusKind.Ready => new(kind, "Ready", "✓"),
        DashboardStatusKind.Warning => new(kind, "Warning", "!"),
        DashboardStatusKind.ActionRequired => new(kind, "Action required", "×"),
        DashboardStatusKind.Unknown => new(kind, "Unknown", "?"),
        DashboardStatusKind.ReadyWithWarnings => new(kind, "Ready with warnings", "△"),
        DashboardStatusKind.NotReady => new(kind, "Not ready", "⊘"),
        DashboardStatusKind.Indeterminate => new(kind, "Indeterminate", "◇"),
        DashboardStatusKind.NotConfigured => new(kind, "Not configured", "○"),
        _ => new(DashboardStatusKind.Unknown, "Unknown", "?"),
    };

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        private readonly SynchronizationContext? _synchronizationContext;

        public CallbackProgress(Action<T> callback)
        {
            _callback = callback;
            _synchronizationContext = SynchronizationContext.Current;
        }

        public void Report(T value)
        {
            if (_synchronizationContext is null ||
                ReferenceEquals(_synchronizationContext, SynchronizationContext.Current))
            {
                _callback(value);
                return;
            }

            _synchronizationContext.Post(_ => _callback(value), null);
        }
    }
}
