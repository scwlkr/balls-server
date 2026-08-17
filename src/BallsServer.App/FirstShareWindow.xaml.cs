using System.ComponentModel;
using System.Windows;
using BallsServer.Core.Sharing;
using BallsServer.Presentation;
using Microsoft.Win32;

namespace BallsServer.App;

public partial class FirstShareWindow : Window, INotifyPropertyChanged
{
    private readonly FirstSharePresentation _presentation;

    public FirstShareWindow(FirstSharePresentation presentation, FirstSharePage page)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (page is not FirstSharePage.HostFiles and not FirstSharePage.ConnectToFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        _presentation = presentation;
        _presentation.PropertyChanged += Presentation_PropertyChanged;
        if (page == FirstSharePage.HostFiles)
        {
            _presentation.ShowHostFiles();
        }
        else
        {
            _presentation.ShowConnectToFiles();
        }

        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PageTitle => _presentation.ActivePage == FirstSharePage.HostFiles
        ? "Host files"
        : "Connect to files";

    public string PageSubtitle => _presentation.ActivePage == FirstSharePage.HostFiles
        ? "Turn one folder on this PC into a private Windows shared drive."
        : "Map the host folder into File Explorer on this PC.";

    public Visibility HostPanelVisibility => _presentation.ActivePage == FirstSharePage.HostFiles
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ConnectPanelVisibility => _presentation.ActivePage == FirstSharePage.ConnectToFiles
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string HostFolder
    {
        get => _presentation.HostFolder;
        set => _presentation.SelectHostFolder(value);
    }

    public bool IsLocalAccessPath
    {
        get => _presentation.HostAccessPath == AccessPathKind.Local;
        set
        {
            if (value)
            {
                _presentation.SelectHostAccessPath(AccessPathKind.Local);
            }
        }
    }

    public bool IsTailscaleAccessPath
    {
        get => _presentation.HostAccessPath == AccessPathKind.Tailscale;
        set
        {
            if (value)
            {
                _presentation.SelectHostAccessPath(AccessPathKind.Tailscale);
            }
        }
    }

    public string? HostValidationMessage => _presentation.HostValidationMessage;

    public Visibility HostPreviewVisibility => _presentation.HostPreview is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public IReadOnlyList<string> HostChanges =>
        _presentation.HostPreview?.Changes ?? Array.Empty<string>();

    public bool CanApplyHostSetup =>
        _presentation.CanApplyHostSetup && _presentation.HostSetupState != HostSetupState.Applying;

    public string HostApplyButtonText => _presentation.HostSetupState == HostSetupState.Applying
        ? "Connecting to Balls Server…"
        : "Apply setup";

    public string HostSetupMessage => _presentation.HostSetupMessage ?? string.Empty;

    public Visibility HostSetupStatusVisibility => _presentation.HostSetupState == HostSetupState.Idle
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string GeneratedSetupCode => _presentation.GeneratedSetupCode ?? string.Empty;

    public Visibility GeneratedSetupCodeVisibility => _presentation.GeneratedSetupCode is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string SetupCode
    {
        get => _presentation.SetupCode;
        set => _presentation.SetSetupCode(value);
    }

    public string? ConnectionValidationMessage => _presentation.ConnectionValidationMessage;

    public Visibility ConnectionPreviewVisibility => _presentation.ConnectionPreview is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string ConnectionEndpoint => _presentation.ConnectionPreview?.Endpoint ?? string.Empty;

    public string ConnectionCredentialLabel =>
        _presentation.ConnectionPreview?.CredentialLabel ?? string.Empty;

    public IReadOnlyList<string> ConnectionChanges =>
        _presentation.ConnectionPreview?.Changes ?? Array.Empty<string>();

    private void BrowseHostFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the folder to host",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) is true)
        {
            _presentation.SelectHostFolder(dialog.FolderName);
        }
    }

    private void PreviewHostSetupButton_Click(object sender, RoutedEventArgs e) =>
        _presentation.PreviewHostSetup();

    private async void ApplyHostSetupButton_Click(object sender, RoutedEventArgs e) =>
        await _presentation.ApplyHostSetupAsync();

    private void CopySetupCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_presentation.GeneratedSetupCode is { Length: > 0 } setupCode)
        {
            Clipboard.SetText(setupCode);
        }
    }

    private void PreviewConnectionButton_Click(object sender, RoutedEventArgs e) =>
        _presentation.PreviewConnection();

    protected override void OnClosed(EventArgs e)
    {
        _presentation.PropertyChanged -= Presentation_PropertyChanged;
        base.OnClosed(e);
    }

    private void Presentation_PropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
