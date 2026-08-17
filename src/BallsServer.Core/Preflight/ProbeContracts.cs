namespace BallsServer.Core.Preflight;

public interface IAdministratorProbe
{
    ValueTask<ProbeResult<AdministratorObservation>> ObserveAsync(CancellationToken cancellationToken);
}

public interface IWindowsVersionProbe
{
    ValueTask<ProbeResult<WindowsVersionObservation>> ObserveAsync(CancellationToken cancellationToken);
}

public interface IStorageProbe
{
    ValueTask<ProbeResult<StorageObservation>> ObserveAsync(string targetPath, CancellationToken cancellationToken);
}

public interface INetworkProfileProbe
{
    ValueTask<ProbeResult<NetworkProfileObservation>> ObserveAsync(CancellationToken cancellationToken);
}

public interface IFirewallProbe
{
    ValueTask<ProbeResult<FirewallObservation>> ObserveAsync(CancellationToken cancellationToken);
}

public interface ITailscaleProbe
{
    ValueTask<ProbeResult<TailscaleObservation>> ObserveAsync(CancellationToken cancellationToken);
}

public interface ISmbProbe
{
    ValueTask<ProbeResult<SmbObservation>> ObserveAsync(CancellationToken cancellationToken);
}

public interface IFolderPermissionProbe
{
    ValueTask<ProbeResult<FolderPermissionObservation>> ObserveAsync(
        string targetPath,
        CancellationToken cancellationToken);
}

public sealed record HostPreflightProbes(
    IAdministratorProbe Administrator,
    IWindowsVersionProbe WindowsVersion,
    IStorageProbe Storage,
    INetworkProfileProbe NetworkProfile,
    IFirewallProbe Firewall,
    ITailscaleProbe Tailscale,
    ISmbProbe Smb,
    IFolderPermissionProbe FolderPermissions);
