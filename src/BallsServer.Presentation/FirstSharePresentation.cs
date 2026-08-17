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

public enum ClientConnectionState
{
    Idle,
    Connecting,
    Disconnecting,
    Connected,
    Disconnected,
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
    private readonly IClientConnectionService? _clientConnectionService;
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
    private char _selectedDriveLetter;
    private bool _saveCredential = true;
    private ClientConnectionState _clientConnectionState;
    private string? _clientConnectionMessage;

    public FirstSharePresentation(
        IFolderValidator folderValidator,
        TimeProvider timeProvider,
        IHostSetupCoordinator? hostSetupCoordinator = null,
        IClientConnectionService? clientConnectionService = null)
    {
        ArgumentNullException.ThrowIfNull(folderValidator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _folderValidator = folderValidator;
        _timeProvider = timeProvider;
        _hostSetupCoordinator = hostSetupCoordinator;
        _clientConnectionService = clientConnectionService;
        AvailableDriveLetters = clientConnectionService?.GetAvailableDriveLetters() ?? ['Z'];
        _selectedDriveLetter = AvailableDriveLetters.Count > 0 ? AvailableDriveLetters[0] : default;
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

    public bool CanApplyConnection =>
        _connectionGrant is not null &&
        ConnectionPreview is not null &&
        _selectedDriveLetter != default &&
        _clientConnectionState is not ClientConnectionState.Connecting and not ClientConnectionState.Disconnecting;

    public IReadOnlyList<char> AvailableDriveLetters { get; }

    public char SelectedDriveLetter => _selectedDriveLetter;

    public bool SaveCredential => _saveCredential;

    public ClientConnectionState ClientConnectionState => _clientConnectionState;

    public string? ClientConnectionMessage => _clientConnectionMessage;

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

    public void SelectDriveLetter(char driveLetter)
    {
        driveLetter = char.ToUpperInvariant(driveLetter);
        if (!AvailableDriveLetters.Contains(driveLetter))
        {
            throw new ArgumentOutOfRangeException(nameof(driveLetter));
        }

        _selectedDriveLetter = driveLetter;
        OnPropertyChanged(nameof(SelectedDriveLetter));
    }

    public void SetSaveCredential(bool saveCredential)
    {
        _saveCredential = saveCredential;
        OnPropertyChanged(nameof(SaveCredential));
    }

    public async Task ApplyConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionGrant is null || ConnectionPreview is null ||
            _clientConnectionState == ClientConnectionState.Connecting)
        {
            return;
        }

        if (!_saveCredential)
        {
            SetClientConnectionResult(ClientConnectionResult.Refused(
                "Consent to save the limited credential is required for a reconnecting drive."));
            return;
        }

        if (_clientConnectionService is null || _selectedDriveLetter == default)
        {
            SetClientConnectionResult(ClientConnectionResult.Refused(
                "No supported drive letter is available in this build."));
            return;
        }

        _clientConnectionState = ClientConnectionState.Connecting;
        _clientConnectionMessage = "Connecting to Balls Server…";
        NotifyClientConnectionChanged();
        try
        {
            SetClientConnectionResult(await _clientConnectionService.ConnectAsync(
                new ClientConnectionRequest(_connectionGrant, _selectedDriveLetter, _saveCredential),
                cancellationToken).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetClientConnectionResult(ClientConnectionResult.Canceled());
        }
        catch (Exception)
        {
            SetClientConnectionResult(ClientConnectionResult.Failed());
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_clientConnectionService is null ||
            _clientConnectionState is ClientConnectionState.Connecting or ClientConnectionState.Disconnecting)
        {
            return;
        }

        _clientConnectionState = ClientConnectionState.Disconnecting;
        _clientConnectionMessage = "Disconnecting Balls Server…";
        NotifyClientConnectionChanged();
        try
        {
            SetClientConnectionResult(await _clientConnectionService
                .DisconnectAsync(cancellationToken)
                .ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetClientConnectionResult(ClientConnectionResult.Canceled());
        }
        catch (Exception)
        {
            SetClientConnectionResult(ClientConnectionResult.Failed());
        }
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
        _clientConnectionState = ClientConnectionState.Idle;
        _clientConnectionMessage = null;
        OnPropertyChanged(nameof(ConnectionPreview));
        OnPropertyChanged(nameof(ConnectionValidationMessage));
        OnPropertyChanged(nameof(CanApplyConnection));
        NotifyClientConnectionChanged();
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

    private void SetClientConnectionResult(ClientConnectionResult result)
    {
        _clientConnectionState = result.Status switch
        {
            ClientConnectionResultStatus.Connected => ClientConnectionState.Connected,
            ClientConnectionResultStatus.Disconnected => ClientConnectionState.Disconnected,
            ClientConnectionResultStatus.Canceled => ClientConnectionState.Canceled,
            ClientConnectionResultStatus.Refused => ClientConnectionState.Refused,
            ClientConnectionResultStatus.Failed => ClientConnectionState.Failed,
            _ => ClientConnectionState.Failed,
        };
        _clientConnectionMessage = result.PublicMessage;
        NotifyClientConnectionChanged();
    }

    private void NotifyClientConnectionChanged()
    {
        OnPropertyChanged(nameof(ClientConnectionState));
        OnPropertyChanged(nameof(ClientConnectionMessage));
        OnPropertyChanged(nameof(CanApplyConnection));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
