using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BallsServer.Core.Sharing;

namespace BallsServer.Windows;

internal interface IWindowsClientPlatform
{
    IReadOnlyList<char> GetAvailableDriveLetters();

    bool CredentialExists(string target);

    bool IsDriveLetterAvailable(char driveLetter);

    void SaveCredential(string target, string userName, string password);

    void MapDrive(char driveLetter, string unc, string userName, string password);

    void VerifyRoundTrip(char driveLetter, string operationId);

    string? GetMappedUnc(char driveLetter);

    void UnmapDrive(char driveLetter, string expectedUnc);

    void DeleteCredential(string target);
}

internal interface IClientConnectionStateStore
{
    ClientConnectionStateRecord? Load();

    void Save(ClientConnectionStateRecord record);

    void Delete();
}

internal sealed record ClientConnectionStateRecord(
    int Version,
    string HostName,
    string Unc,
    string UserName,
    char DriveLetter)
{
    public override string ToString() =>
        $"ClientConnectionStateRecord {{ Version = {Version}, HostName = [REDACTED], " +
        $"Unc = [REDACTED], UserName = [REDACTED], DriveLetter = {DriveLetter} }}";
}

public sealed class WindowsClientConnectionService : IClientConnectionService
{
    private const int CurrentStateVersion = 1;
    private readonly IWindowsClientPlatform _platform;
    private readonly IClientConnectionStateStore _stateStore;

    public WindowsClientConnectionService()
        : this(new WindowsClientPlatform(), new JsonClientConnectionStateStore())
    {
    }

    internal WindowsClientConnectionService(
        IWindowsClientPlatform platform,
        IClientConnectionStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(stateStore);
        _platform = platform;
        _stateStore = stateStore;
    }

    public IReadOnlyList<char> GetAvailableDriveLetters() => _platform.GetAvailableDriveLetters();

    public Task<ClientConnectionResult> ConnectAsync(
        ClientConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var driveLetter = char.ToUpperInvariant(request.DriveLetter);
        var unc = $@"\\{request.Grant.HostName}\{request.Grant.ShareName}";
        if (!request.SaveCredential ||
            driveLetter is < 'D' or > 'Z' ||
            request.Grant.ShareName != "Balls" ||
            _stateStore.Load() is not null)
        {
            return Task.FromResult(ClientConnectionResult.Refused(
                "The requested connection conflicts with existing client state."));
        }

        var credentialSaved = false;
        var mapped = false;
        try
        {
            if (_platform.CredentialExists(request.Grant.HostName) ||
                !_platform.IsDriveLetterAvailable(driveLetter))
            {
                return Task.FromResult(ClientConnectionResult.Refused(
                    "The selected credential target or drive letter is already in use."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            _platform.SaveCredential(
                request.Grant.HostName,
                request.Grant.UserName,
                request.Grant.Password);
            credentialSaved = true;
            cancellationToken.ThrowIfCancellationRequested();
            _platform.MapDrive(
                driveLetter,
                unc,
                request.Grant.UserName,
                request.Grant.Password);
            mapped = true;
            cancellationToken.ThrowIfCancellationRequested();
            _platform.VerifyRoundTrip(driveLetter, RandomHex(16));
            cancellationToken.ThrowIfCancellationRequested();
            _stateStore.Save(new ClientConnectionStateRecord(
                CurrentStateVersion,
                request.Grant.HostName,
                unc,
                request.Grant.UserName,
                driveLetter));
            return Task.FromResult(ClientConnectionResult.Connected(driveLetter, unc));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RollBack(driveLetter, unc, request.Grant.HostName, mapped, credentialSaved);
            throw;
        }
        catch (Exception exception) when (
            exception is ClientPlatformException or IOException or UnauthorizedAccessException)
        {
            RollBack(driveLetter, unc, request.Grant.HostName, mapped, credentialSaved);
            return Task.FromResult(ClientConnectionResult.Failed());
        }
    }

    public Task<ClientConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = _stateStore.Load();
        if (state is null)
        {
            return Task.FromResult(ClientConnectionResult.Disconnected());
        }

        try
        {
            var mappedUnc = _platform.GetMappedUnc(state.DriveLetter);
            if (mappedUnc is not null && !string.Equals(mappedUnc, state.Unc, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ClientConnectionResult.Refused(
                    "The recorded drive letter now belongs to another connection and was preserved."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (mappedUnc is not null)
            {
                _platform.UnmapDrive(state.DriveLetter, state.Unc);
            }

            _platform.DeleteCredential(state.HostName);
            _stateStore.Delete();
            return Task.FromResult(ClientConnectionResult.Disconnected());
        }
        catch (Exception exception) when (
            exception is ClientPlatformException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ClientConnectionResult.Failed());
        }
    }

    private static string RandomHex(int byteCount) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

    private void RollBack(
        char driveLetter,
        string unc,
        string credentialTarget,
        bool mapped,
        bool credentialSaved)
    {
        if (mapped)
        {
            try
            {
                _platform.UnmapDrive(driveLetter, unc);
            }
            catch (ClientPlatformException)
            {
            }
        }

        if (credentialSaved)
        {
            try
            {
                _platform.DeleteCredential(credentialTarget);
            }
            catch (ClientPlatformException)
            {
            }
        }
    }
}

internal sealed class JsonClientConnectionStateStore : IClientConnectionStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };
    private readonly string _statePath;

    public JsonClientConnectionStateStore()
    {
        _statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Balls Server",
            "client-state.json");
    }

    public ClientConnectionStateRecord? Load()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        var record = JsonSerializer.Deserialize<ClientConnectionStateRecord>(
            File.ReadAllText(_statePath, Encoding.UTF8),
            JsonOptions) ?? throw new IOException("The client state is incomplete.");
        if (record.Version != 1 ||
            record.DriveLetter is < 'D' or > 'Z' ||
            string.IsNullOrWhiteSpace(record.HostName) ||
            record.Unc != $@"\\{record.HostName}\Balls")
        {
            throw new IOException("The client state is invalid.");
        }

        return record;
    }

    public void Save(ClientConnectionStateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var directory = Path.GetDirectoryName(_statePath) ??
            throw new IOException("The client state location is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"client-state.{Guid.NewGuid():N}.pending");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(record, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _statePath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Delete()
    {
        if (File.Exists(_statePath))
        {
            File.Delete(_statePath);
        }
    }
}

internal sealed class WindowsClientPlatform : IWindowsClientPlatform
{
    private const int NoError = 0;
    private const int ErrorNotConnected = 2250;
    private const int ErrorNotFound = 1168;
    private const int ResourceTypeDisk = 1;
    private const int ConnectUpdateProfile = 1;
    private const int CredentialTypeDomainPassword = 2;
    private const int CredentialPersistLocalMachine = 2;

    public IReadOnlyList<char> GetAvailableDriveLetters()
    {
        var used = DriveInfo.GetDrives()
            .Select(static drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();
        return Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(static value => (char)value)
            .Where(letter => !used.Contains(letter))
            .ToArray();
    }

    public bool CredentialExists(string target)
    {
        if (CredRead(target, CredentialTypeDomainPassword, 0, out var credential))
        {
            CredFree(credential);
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new ClientPlatformException();
    }

    public bool IsDriveLetterAvailable(char driveLetter) =>
        GetMappedUnc(driveLetter) is null &&
        !DriveInfo.GetDrives().Any(drive =>
            char.ToUpperInvariant(drive.Name[0]) == char.ToUpperInvariant(driveLetter));

    public void SaveCredential(string target, string userName, string password)
    {
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocHGlobal(passwordBytes.Length);
        try
        {
            Marshal.Copy(passwordBytes, 0, blob, passwordBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeDomainPassword,
                TargetName = target,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = userName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new ClientPlatformException();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            for (var index = 0; index < passwordBytes.Length; index++)
            {
                Marshal.WriteByte(blob, index, 0);
            }

            Marshal.FreeHGlobal(blob);
        }
    }

    public void MapDrive(char driveLetter, string unc, string userName, string password)
    {
        var resource = new NetResource
        {
            ResourceType = ResourceTypeDisk,
            LocalName = $"{char.ToUpperInvariant(driveLetter)}:",
            RemoteName = unc,
        };
        var result = WNetAddConnection2(ref resource, password, userName, ConnectUpdateProfile);
        if (result != NoError)
        {
            throw new ClientPlatformException();
        }
    }

    public void VerifyRoundTrip(char driveLetter, string operationId)
    {
        var root = $"{char.ToUpperInvariant(driveLetter)}:\\";
        var original = Path.Combine(root, $".ballsserver-verify-{operationId}.tmp");
        var renamed = Path.Combine(root, $".ballsserver-verify-{operationId}.renamed.tmp");
        var marker = $"Balls Server verification {operationId}";
        try
        {
            using (var stream = new FileStream(
                original,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(marker);
            }

            if (File.ReadAllText(original, Encoding.UTF8) != marker)
            {
                throw new ClientPlatformException();
            }

            File.Move(original, renamed);
            if (File.ReadAllText(renamed, Encoding.UTF8) != marker)
            {
                throw new ClientPlatformException();
            }

            File.Delete(renamed);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new ClientPlatformException();
        }
        finally
        {
            TryDeleteExact(original);
            TryDeleteExact(renamed);
        }
    }

    public string? GetMappedUnc(char driveLetter)
    {
        var localName = $"{char.ToUpperInvariant(driveLetter)}:";
        var capacity = 512;
        var remoteName = new char[capacity];
        var result = WNetGetConnection(localName, remoteName, ref capacity);
        if (result == NoError)
        {
            var terminator = Array.IndexOf(remoteName, '\0');
            return new string(remoteName, 0, terminator >= 0 ? terminator : capacity);
        }

        return result == ErrorNotConnected
            ? null
            : throw new ClientPlatformException();
    }

    public void UnmapDrive(char driveLetter, string expectedUnc)
    {
        var observed = GetMappedUnc(driveLetter);
        if (observed is null)
        {
            return;
        }

        if (!string.Equals(observed, expectedUnc, StringComparison.OrdinalIgnoreCase))
        {
            throw new ClientPlatformException();
        }

        var result = WNetCancelConnection2(
            $"{char.ToUpperInvariant(driveLetter)}:",
            ConnectUpdateProfile,
            force: false);
        if (result is not NoError and not ErrorNotConnected)
        {
            throw new ClientPlatformException();
        }
    }

    public void DeleteCredential(string target)
    {
        if (CredDelete(target, CredentialTypeDomainPassword, 0))
        {
            return;
        }

        if (Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw new ClientPlatformException();
        }
    }

    private static void TryDeleteExact(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NetResource netResource,
        string password,
        string userName,
        int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(
        string localName,
        [Out] char[] remoteName,
        ref int length);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        int type,
        int flags,
        out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int ResourceType;
        public int DisplayType;
        public int Usage;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? LocalName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? RemoteName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Provider;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }
}

public sealed class ClientPlatformException : Exception
{
    public ClientPlatformException()
        : base("The Windows client connection operation did not complete.")
    {
    }
}
