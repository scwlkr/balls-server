using System.Runtime.InteropServices;

namespace BallsServer.Core.Preflight;

public sealed record AdministratorObservation(bool IsAdministrator, bool IsElevated);

public sealed record WindowsVersionObservation(
    bool IsWindows,
    int Major,
    int Minor,
    int Build,
    int Revision,
    string ProductName,
    string EditionId,
    string DisplayVersion,
    Architecture Architecture);

public sealed record StorageObservation(
    string VolumeRoot,
    DriveType DriveType,
    string FileSystem,
    long AvailableFreeBytes,
    long TotalBytes);

public enum NetworkCategory
{
    Unknown,
    Public,
    Private,
    DomainAuthenticated,
}

public sealed record NetworkConnectionProfile(string InterfaceAlias, NetworkCategory Category);

public sealed record NetworkProfileObservation(IReadOnlyList<NetworkConnectionProfile> Profiles);

public enum FirewallProfileKind
{
    Unknown,
    Domain,
    Private,
    Public,
}

public enum FirewallDefaultAction
{
    Unknown,
    NotConfigured,
    Allow,
    Block,
}

public sealed record FirewallProfileObservation(
    FirewallProfileKind Profile,
    bool Enabled,
    FirewallDefaultAction DefaultInboundAction,
    FirewallDefaultAction DefaultOutboundAction);

public sealed record FirewallObservation(IReadOnlyList<FirewallProfileObservation> Profiles);

public enum WindowsServiceState
{
    Unknown,
    NotInstalled,
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused,
}

public sealed record TailscaleObservation(
    bool IsInstalled,
    WindowsServiceState ServiceState,
    string BackendState,
    bool IsOnline,
    int AddressCount);

public enum SmbDialect
{
    Unknown = 0,
    NoLimit = 1,
    Smb202 = 202,
    Smb210 = 210,
    Smb300 = 300,
    Smb302 = 302,
    Smb311 = 311,
}

public sealed record SmbDialectRange(SmbDialect? Minimum, SmbDialect? Maximum);

public sealed record SmbObservation(
    WindowsServiceState ServerServiceState,
    bool? IsSmb1Enabled,
    bool? IsSmb2Enabled,
    bool? EncryptData,
    SmbDialectRange DialectRange);

public sealed record FolderPermissionObservation(
    bool Exists,
    bool CanReadAndTraverse,
    bool CanModify);
