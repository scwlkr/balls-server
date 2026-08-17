using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using BallsServer.Core.Preflight;
using BallsServer.Presentation;
using Microsoft.Win32;

namespace BallsServer.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IHostDashboardPresentation _presentation;

    public MainWindow(IHostDashboardPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        _presentation = presentation;
        _presentation.PropertyChanged += Presentation_PropertyChanged;
        SummaryAreas = new ObservableCollection<DashboardAreaViewModel>(CreateSummaryAreas());
        AdministratorInformation = AdministratorInformationViewModel.Pending();

        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DashboardAreaViewModel> SummaryAreas { get; }

    public AdministratorInformationViewModel AdministratorInformation { get; private set; }

    public string TargetPath
    {
        get => _presentation.SelectedFolder;
        set => _presentation.SelectFolder(value);
    }

    public bool CanRefresh => _presentation.CanRefresh;

    public bool CanCancel => _presentation.CanCancel;

    public bool CanEditTarget => _presentation.CanEditFolder;

    public string? FolderValidationMessage => _presentation.FolderValidationMessage;

    public string RunStatusText => _presentation.RunStatusText;

    public string RunDetailText => _presentation.RunState switch
    {
        DashboardRunState.Running when _presentation.Progress is { } progress =>
            DescribeRunningSnapshot($"Check {progress.Position} of {progress.Total}: {progress.CheckTitle}."),
        DashboardRunState.Running => DescribeRunningSnapshot("Starting the read-only Host Files preflight."),
        DashboardRunState.Canceled => DescribeInterruptedSnapshot("Canceled"),
        DashboardRunState.Failed => DescribeInterruptedSnapshot("The Host Files preflight could not complete"),
        _ => DescribeSnapshot(),
    };

    private async void Window_Loaded(object sender, RoutedEventArgs e) =>
        await _presentation.LaunchAsync();

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose an existing folder to evaluate",
            Multiselect = false,
        };

        if (_presentation.FolderValidationMessage is null)
        {
            dialog.InitialDirectory = _presentation.SelectedFolder;
        }

        if (dialog.ShowDialog(this) is true)
        {
            _presentation.SelectFolder(dialog.FolderName);
            TargetPathTextBox.CaretIndex = TargetPath.Length;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await _presentation.RefreshAsync();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _presentation.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        _presentation.Close();
        _presentation.PropertyChanged -= Presentation_PropertyChanged;
        base.OnClosed(e);
    }

    private void Presentation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IHostDashboardPresentation.SummaryAreas) or
            nameof(IHostDashboardPresentation.LastCompletedSnapshot))
        {
            UpdateSummaryAreas();
        }

        if (e.PropertyName == nameof(IHostDashboardPresentation.LastCompletedSnapshot) &&
            _presentation.LastCompletedSnapshot is { } snapshot)
        {
            AdministratorInformation = AdministratorInformationViewModel.FromInformation(
                snapshot.AdministratorInformation);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private string DescribeSnapshot()
    {
        if (_presentation.LastCompletedSnapshot is not { } snapshot)
        {
            return FolderValidationMessage ??
                "Not checked yet. The first read-only diagnostic starts when the window opens.";
        }

        var observation =
            $"Observed {snapshot.TargetPath} from {snapshot.StartedAt.ToLocalTime():g} to {snapshot.CompletedAt.ToLocalTime():g}.";

        return _presentation.SnapshotNeedsRefresh
            ? $"{observation} The selected folder is different and needs Refresh."
            : observation;
    }

    private string DescribeInterruptedSnapshot(string interruption)
    {
        if (_presentation.LastCompletedSnapshot is not { } snapshot)
        {
            return $"{interruption}. No completed observation is available; status remains Not checked.";
        }

        var description =
            $"{interruption}. Showing the completed observation for {snapshot.TargetPath} at {snapshot.CompletedAt.ToLocalTime():g}.";

        return _presentation.SnapshotNeedsRefresh
            ? $"{description} The selected folder is different and needs Refresh."
            : description;
    }

    private string DescribeRunningSnapshot(string progress)
    {
        if (_presentation.LastCompletedSnapshot is not { } snapshot)
        {
            return $"{progress} No completed observation is available yet.";
        }

        return $"{progress} Still showing the completed observation for {snapshot.TargetPath} " +
            $"from {snapshot.StartedAt.ToLocalTime():g} to {snapshot.CompletedAt.ToLocalTime():g}.";
    }

    private IEnumerable<DashboardAreaViewModel> CreateSummaryAreas() =>
        _presentation.SummaryAreas.Select((area, index) => new DashboardAreaViewModel(
            area,
            position: index,
            _presentation.SetDetailsExpanded));

    private void UpdateSummaryAreas()
    {
        var updatedAreas = _presentation.SummaryAreas;
        if (updatedAreas.Count != SummaryAreas.Count ||
            updatedAreas.Where((area, index) => area.Id != SummaryAreas[index].Id).Any())
        {
            SummaryAreas.Clear();
            foreach (var area in CreateSummaryAreas())
            {
                SummaryAreas.Add(area);
            }

            return;
        }

        for (var index = 0; index < updatedAreas.Count; index++)
        {
            SummaryAreas[index].Update(updatedAreas[index]);
        }
    }
}

public sealed class DashboardAreaViewModel : INotifyPropertyChanged
{
    private readonly Action<DashboardAreaId, bool> _setDetailsExpanded;
    private bool _isDetailsExpanded;

    public DashboardAreaViewModel(
        DashboardAreaPresentation area,
        int position,
        Action<DashboardAreaId, bool> setDetailsExpanded)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(setDetailsExpanded);
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        Id = area.Id;
        TabIndex = position + 4;
        GridRow = position / 2;
        GridColumn = position == 4 ? 0 : position % 2;
        GridColumnSpan = position == 4 ? 2 : 1;
        _setDetailsExpanded = setDetailsExpanded;
        Update(area);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardAreaId Id { get; }

    public string Title { get; private set; } = string.Empty;

    public string StatusText { get; private set; } = string.Empty;

    public string StatusCue { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public string? EvaluatedFolderContext { get; private set; }

    public string ObservationContext { get; private set; } = string.Empty;

    public IReadOnlyList<CheckResultViewModel> PrerequisiteResults { get; private set; } =
        Array.Empty<CheckResultViewModel>();

    public string DetailsSummary { get; private set; } = string.Empty;

    public string DetailsToggleAccessibleName =>
        _isDetailsExpanded ? $"Hide {Title} details" : $"Show {Title} details";

    public int TabIndex { get; }

    public int GridRow { get; }

    public int GridColumn { get; }

    public int GridColumnSpan { get; }

    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        set
        {
            if (_isDetailsExpanded == value)
            {
                return;
            }

            _isDetailsExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDetailsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsToggleAccessibleName)));
            _setDetailsExpanded(Id, value);
        }
    }

    public Brush StatusBackground { get; private set; } = Brushes.Gainsboro;

    public Brush StatusForeground { get; private set; } = Brushes.Black;

    public Visibility EvaluatedFolderContextVisibility => string.IsNullOrWhiteSpace(EvaluatedFolderContext)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string AccessibleStatus { get; private set; } = string.Empty;

    public void Update(DashboardAreaPresentation area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.Id != Id)
        {
            throw new ArgumentException("The dashboard area identity cannot change.", nameof(area));
        }

        Title = area.Title;
        var appearance = StatusAppearance.For(area.Status);
        StatusText = appearance.Text;
        StatusCue = appearance.Cue;
        Summary = area.Summary;
        EvaluatedFolderContext = area.EvaluatedFolderPath is null
            ? null
            : $"Evaluated managed folder: {area.EvaluatedFolderPath}";
        ObservationContext = area.ObservedFrom is { } observedFrom && area.ObservedAt is { } observedAt
            ? $"Observed from {observedFrom.ToLocalTime():g} to {observedAt.ToLocalTime():g}."
            : "No completed observation is available yet.";
        PrerequisiteResults = area.PrerequisiteResults
            .Select(CheckResultViewModel.FromResult)
            .ToArray();
        DetailsSummary = area.DetailsSummary;
        _isDetailsExpanded = area.IsDetailsExpanded;
        AccessibleStatus = area.AccessibleStatus;
        StatusBackground = appearance.Background;
        StatusForeground = appearance.Foreground;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

public sealed class AdministratorInformationViewModel
{
    private AdministratorInformationViewModel(
        StatusAppearance appearance,
        string summary)
    {
        StatusText = appearance.Text;
        StatusCue = appearance.Cue;
        Summary = summary;
        StatusBackground = appearance.Background;
        StatusForeground = appearance.Foreground;
    }

    public string StatusText { get; }

    public string StatusCue { get; }

    public string Summary { get; }

    public Brush StatusBackground { get; }

    public Brush StatusForeground { get; }

    public static AdministratorInformationViewModel Pending() => new(
        StatusAppearance.Pending,
        "Administrator information is shown only for future Host Files setup and never affects readiness.");

    public static AdministratorInformationViewModel FromInformation(AdministratorInformation information)
    {
        ArgumentNullException.ThrowIfNull(information);

        return information.Availability == AdministratorInformationAvailability.Available
            ? new AdministratorInformationViewModel(
                StatusAppearance.Information,
                information.Summary)
            : new AdministratorInformationViewModel(
                StatusAppearance.InformationUnavailable,
                information.Summary);
    }
}

public sealed class CheckResultViewModel
{
    private CheckResultViewModel(
        string title,
        StatusAppearance appearance,
        string summary,
        string? reasonCode,
        IReadOnlyList<PreflightEvidence> evidence,
        string accessibleStatus)
    {
        Title = title;
        StatusText = appearance.Text;
        StatusCue = appearance.Cue;
        Summary = summary;
        ReasonCode = reasonCode;
        Evidence = evidence;
        AccessibleStatus = accessibleStatus;
        StatusBackground = appearance.Background;
        StatusForeground = appearance.Foreground;
    }

    public string Title { get; }

    public string StatusText { get; }

    public string StatusCue { get; }

    public string Summary { get; }

    public string? ReasonCode { get; }

    public IReadOnlyList<PreflightEvidence> Evidence { get; }

    public string AccessibleStatus { get; }

    public Brush StatusBackground { get; }

    public Brush StatusForeground { get; }

    public Visibility ReasonVisibility => string.IsNullOrWhiteSpace(ReasonCode)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility EvidenceVisibility => Evidence.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public static CheckResultViewModel Pending(string title) => new(
        title,
        StatusAppearance.Pending,
        "Waiting for a completed Host Files preflight observation.",
        null,
        Array.Empty<PreflightEvidence>(),
        $"{title}: Not checked. Waiting for a completed Host Files preflight observation.");

    public static CheckResultViewModel FromResult(DashboardPrerequisitePresentation result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var appearance = StatusAppearance.For(result.Status);

        return new CheckResultViewModel(
            result.Title,
            appearance,
            result.Summary,
            result.ReasonCode,
            result.Evidence,
            result.AccessibleStatus);
    }
}

internal sealed record StatusAppearance(
    string Text,
    string Cue,
    Brush Background,
    Brush Foreground)
{
    public static StatusAppearance Pending { get; } = new(
        "Not checked",
        "–",
        Brushes.Gainsboro,
        Brushes.Black);

    public static StatusAppearance Information { get; } = new(
        "Future setup information",
        "i",
        ColorBrush("#FFDDEAF7"),
        ColorBrush("#FF174A75"));

    public static StatusAppearance InformationUnavailable { get; } = new(
        "Information unavailable",
        "?",
        ColorBrush("#FFE6E8EC"),
        ColorBrush("#FF3D4755"));

    public static StatusAppearance Unknown { get; } = new(
        "Unknown",
        "?",
        ColorBrush("#FFE6E8EC"),
        ColorBrush("#FF3D4755"));

    public static StatusAppearance For(DashboardStatusPresentation status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var colors = status.Kind switch
        {
            DashboardStatusKind.Ready => (ColorBrush("#FFDDF3E4"), ColorBrush("#FF185C30")),
            DashboardStatusKind.Warning => (ColorBrush("#FFFFEEC4"), ColorBrush("#FF694A00")),
            DashboardStatusKind.ActionRequired => (ColorBrush("#FFFFDDDA"), ColorBrush("#FF8A1C16")),
            DashboardStatusKind.Unknown => (ColorBrush("#FFE6E8EC"), ColorBrush("#FF3D4755")),
            DashboardStatusKind.ReadyWithWarnings => (ColorBrush("#FFFFE7B0"), ColorBrush("#FF654100")),
            DashboardStatusKind.NotReady => (ColorBrush("#FFFFD4D0"), ColorBrush("#FF7D1410")),
            DashboardStatusKind.Indeterminate => (ColorBrush("#FFE1E5EB"), ColorBrush("#FF35404E")),
            DashboardStatusKind.NotConfigured => (ColorBrush("#FFE9E4DA"), ColorBrush("#FF51483A")),
            _ => (Brushes.Gainsboro, Brushes.Black),
        };

        return new StatusAppearance(status.Text, status.Cue, colors.Item1, colors.Item2);
    }

    private static SolidColorBrush ColorBrush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));
}
