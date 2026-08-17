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

public enum HostSetupState
{
    Idle,
    Applying,
    Completed,
    Canceled,
    Refused,
    Failed,
}

public interface IHostSetupCoordinator
{
    Task<HostSetupResult> ApplyAsync(
        HostSetupPreview request,
        CancellationToken cancellationToken = default);
}

public sealed class FirstSharePresentation : INotifyPropertyChanged
{
    private readonly IFolderValidator _folderValidator;
    private readonly TimeProvider _timeProvider;
    private readonly IHostSetupCoordinator? _hostSetupCoordinator;
    private FirstSharePage _activePage;
    private string _hostFolder = string.Empty;
    private AccessPathKind _hostAccessPath = AccessPathKind.Local;
    private HostSetupPreview? _hostPreview;
    private string? _hostValidationMessage;
    private string _setupCode = string.Empty;
    private SetupCodeGrant? _connectionGrant;
    private ConnectionSetupPreview? _connectionPreview;
    private string? _connectionValidationMessage;
    private HostSetupState _hostSetupState;
    private string? _hostSetupMessage;
    private string? _generatedSetupCode;

    public FirstSharePresentation(
        IFolderValidator folderValidator,
        TimeProvider timeProvider,
        IHostSetupCoordinator? hostSetupCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(folderValidator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _folderValidator = folderValidator;
        _timeProvider = timeProvider;
        _hostSetupCoordinator = hostSetupCoordinator;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FirstSharePage ActivePage => _activePage;

    public string HostFolder => _hostFolder;

    public AccessPathKind HostAccessPath => _hostAccessPath;

    public HostSetupPreview? HostPreview => _hostPreview;

    public string? HostValidationMessage => _hostValidationMessage;

    public bool CanApplyHostSetup => HostPreview is not null;

    public HostSetupState HostSetupState => _hostSetupState;

    public string? HostSetupMessage => _hostSetupMessage;

    public string? GeneratedSetupCode => _generatedSetupCode;

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

    public async Task ApplyHostSetupAsync(CancellationToken cancellationToken = default)
    {
        if (HostPreview is null || _hostSetupState == HostSetupState.Applying)
        {
            return;
        }

        if (_hostSetupCoordinator is null)
        {
            SetHostSetupResult(HostSetupResult.Refused("Host setup is unavailable in this build."));
            return;
        }

        _hostSetupState = HostSetupState.Applying;
        _hostSetupMessage = "Waiting for Windows approval and the elevated setup preview…";
        _generatedSetupCode = null;
        NotifyHostSetupChanged();

        try
        {
            SetHostSetupResult(await _hostSetupCoordinator
                .ApplyAsync(HostPreview, cancellationToken)
                .ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetHostSetupResult(HostSetupResult.Canceled());
        }
        catch (Exception)
        {
            SetHostSetupResult(HostSetupResult.Failed());
        }
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
        _hostSetupState = HostSetupState.Idle;
        _hostSetupMessage = null;
        _generatedSetupCode = null;
        OnPropertyChanged(nameof(HostPreview));
        OnPropertyChanged(nameof(HostValidationMessage));
        OnPropertyChanged(nameof(CanApplyHostSetup));
        NotifyHostSetupChanged();
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

    private void SetHostSetupResult(HostSetupResult result)
    {
        _hostSetupState = result.Status switch
        {
            HostSetupResultStatus.Completed => HostSetupState.Completed,
            HostSetupResultStatus.Canceled => HostSetupState.Canceled,
            HostSetupResultStatus.Refused => HostSetupState.Refused,
            HostSetupResultStatus.Failed => HostSetupState.Failed,
            _ => HostSetupState.Failed,
        };
        _hostSetupMessage = result.PublicMessage;
        _generatedSetupCode = result.Succeeded ? result.SetupCode : null;
        NotifyHostSetupChanged();
    }

    private void NotifyHostSetupChanged()
    {
        OnPropertyChanged(nameof(HostSetupState));
        OnPropertyChanged(nameof(HostSetupMessage));
        OnPropertyChanged(nameof(GeneratedSetupCode));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
