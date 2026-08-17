using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BallsServer.Core.Preflight;
using Microsoft.Win32.SafeHandles;

namespace BallsServer.Windows;

internal sealed class ReadOnlyProbeException : Exception
{
    internal ReadOnlyProbeException(string message)
        : base(message)
    {
    }

    internal ReadOnlyProbeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class ProbeErrors
{
    internal static ProbeResult<T> Unavailable<T>(string code, string message)
        where T : notnull => ProbeResult.Unavailable<T>(code, message);

    internal static bool IsExpected(Exception exception) => exception is
        ReadOnlyProbeException or
        Win32Exception or
        IOException or
        UnauthorizedAccessException or
        InvalidOperationException or
        System.Security.SecurityException or
        System.Text.Json.JsonException;
}

internal static class BoundedReadOnlyProcessRunner
{
    internal static async ValueTask<string> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan queryTimeout,
        int maximumOutputCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumOutputCharacters, 0);
        cancellationToken.ThrowIfCancellationRequested();

        if (startInfo.UseShellExecute ||
            !startInfo.RedirectStandardOutput ||
            !startInfo.RedirectStandardError)
        {
            throw new ArgumentException(
                "A read-only diagnostic process must use redirected standard streams without a shell.",
                nameof(startInfo));
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new ReadOnlyProbeException("The read-only query could not be started.");
            }
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            throw new ReadOnlyProbeException("The read-only query could not be started.", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(queryTimeout);

        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await standardOutput.ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new ReadOnlyProbeException("The read-only process did not complete its query.");
            }

            if (output.Length == 0 || output.Length > maximumOutputCharacters)
            {
                throw new ReadOnlyProbeException("The read-only process returned an invalid response.");
            }

            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new ReadOnlyProbeException("The read-only process query timed out.");
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            TryTerminate(process);
            throw new ReadOnlyProbeException("The read-only process query failed.", exception);
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and cleanup.
        }
        catch (Win32Exception)
        {
            // Best-effort cleanup of the diagnostic child process.
        }
    }
}

internal enum PowerShellQuery
{
    ConnectedNetworkProfiles,
    FirewallProfiles,
    SmbServerConfiguration,
}

internal interface IPowerShellJsonSource
{
    ValueTask<string> QueryAsync(PowerShellQuery query, CancellationToken cancellationToken);
}

internal sealed class StaticPowerShellJsonSource : IPowerShellJsonSource
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);
    private const int MaximumOutputCharacters = 256 * 1024;

    public async ValueTask<string> QueryAsync(
        PowerShellQuery query,
        CancellationToken cancellationToken)
    {
        var script = GetScript(query);

        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(executable))
        {
            throw new ReadOnlyProbeException("The Windows query host is unavailable.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        return await BoundedReadOnlyProcessRunner.RunAsync(
            startInfo,
            QueryTimeout,
            MaximumOutputCharacters,
            cancellationToken).ConfigureAwait(false);
    }

    internal static string GetScript(PowerShellQuery query) => query switch
    {
        PowerShellQuery.ConnectedNetworkProfiles =>
            "$profiles = @(Get-NetConnectionProfile -ErrorAction Stop | " +
            "Where-Object { $_.IPv4Connectivity -ne 'Disconnected' -or $_.IPv6Connectivity -ne 'Disconnected' } | " +
            "ForEach-Object { [PSCustomObject]@{ InterfaceAlias = [string]$_.InterfaceAlias; " +
            "NetworkCategory = [string]$_.NetworkCategory } }); " +
            "ConvertTo-Json -InputObject $profiles -Compress -Depth 3",
        PowerShellQuery.FirewallProfiles =>
            "$profiles = @(Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop | " +
            "ForEach-Object { [PSCustomObject]@{ Profile = [string]$_.Name; Enabled = [bool]$_.Enabled; " +
            "DefaultInboundAction = [string]$_.DefaultInboundAction; " +
            "DefaultOutboundAction = [string]$_.DefaultOutboundAction } }); " +
            "ConvertTo-Json -InputObject $profiles -Compress -Depth 3",
        PowerShellQuery.SmbServerConfiguration =>
            "$configuration = Get-SmbServerConfiguration -ErrorAction Stop; " +
            "[PSCustomObject]@{ EnableSMB1Protocol = $configuration.EnableSMB1Protocol; " +
            "EnableSMB2Protocol = $configuration.EnableSMB2Protocol; " +
            "Smb2DialectMin = [string]$configuration.Smb2DialectMin; " +
            "Smb2DialectMax = [string]$configuration.Smb2DialectMax; " +
            "EncryptData = $configuration.EncryptData } | ConvertTo-Json -Compress -Depth 2",
        _ => throw new ArgumentOutOfRangeException(nameof(query)),
    };
}

internal readonly record struct WindowsServiceStatus(bool IsInstalled, WindowsServiceState State);

internal interface IWindowsServiceStatusSource
{
    WindowsServiceStatus Query(string serviceName);
}

internal sealed class NativeWindowsServiceStatusSource : IWindowsServiceStatusSource
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ScStatusProcessInfo = 0;

    public WindowsServiceStatus Query(string serviceName)
    {
        if (serviceName is not ("Tailscale" or "LanmanServer"))
        {
            throw new ArgumentOutOfRangeException(nameof(serviceName));
        }

        using var manager = ServiceNativeMethods.OpenSCManager(null, null, ScManagerConnect);
        if (manager.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        using var service = ServiceNativeMethods.OpenService(manager, serviceName, ServiceQueryStatus);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorServiceDoesNotExist)
            {
                return new WindowsServiceStatus(false, WindowsServiceState.NotInstalled);
            }

            throw new Win32Exception(error);
        }

        var status = new ServiceStatusProcess();
        if (!ServiceNativeMethods.QueryServiceStatusEx(
                service,
                ScStatusProcessInfo,
                ref status,
                Marshal.SizeOf<ServiceStatusProcess>(),
                out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new WindowsServiceStatus(true, MapState(status.CurrentState));
    }

    internal static WindowsServiceState MapState(uint state) => state switch
    {
        1 => WindowsServiceState.Stopped,
        2 => WindowsServiceState.StartPending,
        3 => WindowsServiceState.StopPending,
        4 => WindowsServiceState.Running,
        5 => WindowsServiceState.ContinuePending,
        6 => WindowsServiceState.PausePending,
        7 => WindowsServiceState.Paused,
        _ => WindowsServiceState.Unknown,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct ServiceStatusProcess
{
    internal uint ServiceType;
    internal uint CurrentState;
    internal uint ControlsAccepted;
    internal uint Win32ExitCode;
    internal uint ServiceSpecificExitCode;
    internal uint CheckPoint;
    internal uint WaitHint;
    internal uint ProcessId;
    internal uint ServiceFlags;
}

internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeServiceHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => ServiceNativeMethods.CloseServiceHandle(handle);
}

internal static class ServiceNativeMethods
{
    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern SafeServiceHandle OpenSCManager(
        string? machineName,
        string? databaseName,
        uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern SafeServiceHandle OpenService(
        SafeServiceHandle serviceControlManager,
        string serviceName,
        uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceStatusEx(
        SafeServiceHandle service,
        int infoLevel,
        ref ServiceStatusProcess serviceStatus,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr serviceHandle);
}
