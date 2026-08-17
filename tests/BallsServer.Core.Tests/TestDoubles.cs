using System.Runtime.InteropServices;
using BallsServer.Core.Preflight;

namespace BallsServer.Core.Tests;

internal static class TestData
{
    public static PreflightPolicy Policy { get; } = new(
        minimumWindowsBuild: 26100,
        supportedEditionIds: ["Professional"],
        requiredFileSystem: "NTFS",
        minimumFreeBytes: 100);

    public static PreflightContext Context { get; } = new(@"C:\Host", Policy);

    public static PreflightCheckResult CheckResult(
        PreflightCheckId id,
        PreflightCheckStatus status = PreflightCheckStatus.Ready) =>
        PreflightCheckResult.Create(
            id,
            $"{id} title",
            status,
            $"{id}_{status}",
            $"{id} returned {status}.");

    public static HostPreflightProbes HealthyProbes() => new(
        new StubAdministratorProbe(ProbeResult.Observed(new AdministratorObservation(true, true))),
        new StubWindowsVersionProbe(ProbeResult.Observed(new WindowsVersionObservation(
            true,
            10,
            0,
            26100,
            1,
            "Windows 11 Pro",
            "Professional",
            "24H2",
            Architecture.X64))),
        new StubStorageProbe(ProbeResult.Observed(new StorageObservation(
            @"C:\",
            DriveType.Fixed,
            "NTFS",
            100,
            1_000))),
        new StubNetworkProfileProbe(ProbeResult.Observed(new NetworkProfileObservation(
            [new NetworkConnectionProfile("Ethernet", NetworkCategory.Private)]))),
        new StubFirewallProbe(ProbeResult.Observed(new FirewallObservation(
            [
                new FirewallProfileObservation(
                    FirewallProfileKind.Domain,
                    true,
                    FirewallDefaultAction.Block,
                    FirewallDefaultAction.Allow),
                new FirewallProfileObservation(
                    FirewallProfileKind.Private,
                    true,
                    FirewallDefaultAction.Block,
                    FirewallDefaultAction.Allow),
                new FirewallProfileObservation(
                    FirewallProfileKind.Public,
                    true,
                    FirewallDefaultAction.Block,
                    FirewallDefaultAction.Allow),
            ]))),
        new StubTailscaleProbe(ProbeResult.Observed(new TailscaleObservation(
            true,
            WindowsServiceState.Running,
            "Running",
            true,
            1))),
        new StubSmbProbe(ProbeResult.Observed(new SmbObservation(
            WindowsServiceState.Running,
            false,
            true,
            false,
            new SmbDialectRange(SmbDialect.Smb300, SmbDialect.Smb311)))),
        new StubFolderPermissionProbe(ProbeResult.Observed(new FolderPermissionObservation(true, true, true))));
}

internal sealed class StubPreflightCheck(
    PreflightCheckId id,
    int order,
    Func<PreflightContext, CancellationToken, ValueTask<PreflightCheckResult>> execute) : IPreflightCheck
{
    public PreflightCheckId Id { get; } = id;

    public string Title { get; } = $"{id} title";

    public int Order { get; } = order;

    public int CallCount { get; private set; }

    public ValueTask<PreflightCheckResult> CheckAsync(
        PreflightContext context,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return execute(context, cancellationToken);
    }
}

internal sealed class StubAdministratorProbe(ProbeResult<AdministratorObservation> result) : IAdministratorProbe
{
    public ValueTask<ProbeResult<AdministratorObservation>> ObserveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(result);
}

internal sealed class StubWindowsVersionProbe(ProbeResult<WindowsVersionObservation> result) : IWindowsVersionProbe
{
    public ValueTask<ProbeResult<WindowsVersionObservation>> ObserveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(result);
}

internal sealed class StubStorageProbe(ProbeResult<StorageObservation> result) : IStorageProbe
{
    public string? ObservedPath { get; private set; }

    public ValueTask<ProbeResult<StorageObservation>> ObserveAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        ObservedPath = targetPath;
        return ValueTask.FromResult(result);
    }
}

internal sealed class StubNetworkProfileProbe(ProbeResult<NetworkProfileObservation> result) : INetworkProfileProbe
{
    public ValueTask<ProbeResult<NetworkProfileObservation>> ObserveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(result);
}

internal sealed class StubFirewallProbe(ProbeResult<FirewallObservation> result) : IFirewallProbe
{
    public ValueTask<ProbeResult<FirewallObservation>> ObserveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(result);
}

internal sealed class StubTailscaleProbe(ProbeResult<TailscaleObservation> result) : ITailscaleProbe
{
    public ValueTask<ProbeResult<TailscaleObservation>> ObserveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(result);
}

internal sealed class StubSmbProbe(ProbeResult<SmbObservation> result) : ISmbProbe
{
    public ValueTask<ProbeResult<SmbObservation>> ObserveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(result);
}

internal sealed class StubFolderPermissionProbe(ProbeResult<FolderPermissionObservation> result) : IFolderPermissionProbe
{
    public string? ObservedPath { get; private set; }

    public ValueTask<ProbeResult<FolderPermissionObservation>> ObserveAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        ObservedPath = targetPath;
        return ValueTask.FromResult(result);
    }
}
