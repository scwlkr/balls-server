using BallsServer.Core.Preflight;

namespace BallsServer.Windows;

public static class WindowsPreflightFactory
{
    public static IPreflightService CreateHostService()
    {
        var services = new NativeWindowsServiceStatusSource();
        var powershell = new StaticPowerShellJsonSource();

        var probes = new HostPreflightProbes(
            new WindowsAdministratorProbe(),
            new WindowsVersionProbe(),
            new WindowsStorageProbe(),
            new WindowsNetworkProfileProbe(powershell),
            new WindowsFirewallProbe(powershell),
            new WindowsTailscaleProbe(services, new TailscaleStatusSource()),
            new WindowsSmbProbe(services, powershell),
            new WindowsFolderPermissionProbe());

        return HostPreflightCatalog.CreateService(probes);
    }
}
