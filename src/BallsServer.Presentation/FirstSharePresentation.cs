using System.ComponentModel;
using BallsServer.Core.Sharing;

namespace BallsServer.Presentation;

public enum FirstSharePage
{
    Dashboard,
    HostFiles,
    ConnectToFiles,
}

public sealed record HostSetupPreview(
    string ManagedFolder,
    AccessPathKind AccessPath,
    IReadOnlyList<string> Changes);

public sealed record ConnectionSetupPreview(
    string Endpoint,
    AccessPathKind AccessPath,
    string CredentialLabel,
    IReadOnlyList<string> Changes);

public sealed class FirstSharePresentation : INotifyPropertyChanged
{
    private readonly IFolderValidator _folderValidator;
    private readonly TimeProvider _timeProvider;
    private FirstSharePage _activePage;
    private string _hostFolder = string.Empty;
    private AccessPathKind _hostAccessPath = AccessPathKind.Local;
    private HostSetupPreview? _hostPreview;
    private string? _hostValidationMessage;
    private string _setupCode = string.Empty;
    private SetupCodeGrant? _connectionGrant;
    private ConnectionSetupPreview? _connectionPreview;
    private string? _connectionValidationMessage;

    public FirstSharePresentation(IFolderValidator folderValidator, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(folderValidator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _folderValidator = folderValidator;
        _timeProvider = timeProvider;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FirstSharePage ActivePage => _activePage;

    public string HostFolder => _hostFolder;

    public AccessPathKind HostAccessPath => _hostAccessPath;

    public HostSetupPreview? HostPreview => _hostPreview;

    public string? HostValidationMessage => _hostValidationMessage;

    public bool CanApplyHostSetup => HostPreview is not null;

    public string SetupCode => _setupCode;

    public ConnectionSetupPreview? ConnectionPreview => _connectionPreview;

    public string? ConnectionValidationMessage => _connectionValidationMessage;

    public bool CanApplyConnection => _connectionGrant is not null && ConnectionPreview is not null;

    public void ShowHostFiles()
    {
        _activePage = FirstSharePage.HostFiles;
        OnPropertyChanged(nameof(ActivePage));
    }

    public void ShowConnectToFiles()
    {
        _activePage = FirstSharePage.ConnectToFiles;
        OnPropertyChanged(nameof(ActivePage));
    }

    public void SelectHostFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _hostFolder = path;
        ClearHostPreview();
        OnPropertyChanged(nameof(HostFolder));
    }

    public void SelectHostAccessPath(AccessPathKind accessPath)
    {
        if (!Enum.IsDefined(accessPath))
        {
            throw new ArgumentOutOfRangeException(nameof(accessPath));
        }

        _hostAccessPath = accessPath;
        ClearHostPreview();
        OnPropertyChanged(nameof(HostAccessPath));
    }

    public void PreviewHostSetup()
    {
        var validation = _folderValidator.Validate(HostFolder);
        _hostValidationMessage = validation.ErrorMessage;
        _hostPreview = validation.IsValid
            ? new HostSetupPreview(
                validation.ValidatedPath!,
                HostAccessPath,
                CreateHostChanges(HostAccessPath))
            : null;

        OnPropertyChanged(nameof(HostValidationMessage));
        OnPropertyChanged(nameof(HostPreview));
        OnPropertyChanged(nameof(CanApplyHostSetup));
    }

    public void SetSetupCode(string setupCode)
    {
        ArgumentNullException.ThrowIfNull(setupCode);

        _setupCode = setupCode;
        ClearConnectionPreview();
        OnPropertyChanged(nameof(SetupCode));
    }

    public void PreviewConnection()
    {
        try
        {
            _connectionGrant = SetupCodeCodec.Decode(SetupCode, _timeProvider.GetUtcNow());
            _connectionPreview = new ConnectionSetupPreview(
                $@"\\{_connectionGrant.HostName}\{_connectionGrant.ShareName}",
                _connectionGrant.AccessPath,
                _connectionGrant.UserName,
                [
                    "Save the limited credential in Windows Credential Manager after consent.",
                    "Map an available persistent drive in File Explorer.",
                    "Verify access with one isolated temporary file.",
                    "Record the mapping and credential for Disconnect.",
                ]);
            _connectionValidationMessage = null;
        }
        catch (FormatException exception)
        {
            _connectionGrant = null;
            _connectionPreview = null;
            _connectionValidationMessage = exception.Message;
        }

        OnPropertyChanged(nameof(ConnectionPreview));
        OnPropertyChanged(nameof(ConnectionValidationMessage));
        OnPropertyChanged(nameof(CanApplyConnection));
    }

    private void ClearHostPreview()
    {
        _hostPreview = null;
        _hostValidationMessage = null;
        OnPropertyChanged(nameof(HostPreview));
        OnPropertyChanged(nameof(HostValidationMessage));
        OnPropertyChanged(nameof(CanApplyHostSetup));
    }

    private void ClearConnectionPreview()
    {
        _connectionGrant = null;
        _connectionPreview = null;
        _connectionValidationMessage = null;
        OnPropertyChanged(nameof(ConnectionPreview));
        OnPropertyChanged(nameof(ConnectionValidationMessage));
        OnPropertyChanged(nameof(CanApplyConnection));
    }

    private static IReadOnlyList<string> CreateHostChanges(AccessPathKind accessPath) =>
        [
            "Share the selected folder as Balls.",
            "Create one limited client credential.",
            $"Allow SMB only through the selected {DescribeAccessPath(accessPath)} path.",
            "Record every product-owned change for Stop Sharing.",
        ];

    private static string DescribeAccessPath(AccessPathKind accessPath) => accessPath switch
    {
        AccessPathKind.Local => "local-network",
        AccessPathKind.Tailscale => "Tailscale",
        _ => throw new ArgumentOutOfRangeException(nameof(accessPath)),
    };

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
