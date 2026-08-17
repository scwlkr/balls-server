using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using BallsServer.Core.Preflight;
using Microsoft.Win32;

namespace BallsServer.Windows;

internal sealed class WindowsAdministratorProbe : IAdministratorProbe
{
    public ValueTask<ProbeResult<AdministratorObservation>> ObserveAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var isMember = identity.Groups?.Contains(administrators) == true;
            var elevation = new TokenElevation();
            var elevationSize = Marshal.SizeOf<TokenElevation>();

            if (!LocalSystemNativeMethods.GetTokenInformation(
                    identity.AccessToken,
                    TokenInformationClass.TokenElevation,
                    ref elevation,
                    elevationSize,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return ValueTask.FromResult(ProbeResult.Observed(
                new AdministratorObservation(isMember, elevation.TokenIsElevated != 0)));
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception))
        {
            return ValueTask.FromResult(ProbeErrors.Unavailable<AdministratorObservation>(
                "administrator_query_failed",
                "Windows did not report the current account's administrator status."));
        }
    }
}

internal sealed class WindowsVersionProbe : IWindowsVersionProbe
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public ValueTask<ProbeResult<WindowsVersionObservation>> ObserveAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var version = new OsVersionInfo
            {
                Size = Marshal.SizeOf<OsVersionInfo>(),
            };

            var status = LocalSystemNativeMethods.RtlGetVersion(ref version);
            if (status != 0)
            {
                throw new ReadOnlyProbeException("Windows did not report its true operating system version.");
            }

            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var currentVersion = localMachine.OpenSubKey(CurrentVersionKey, writable: false);
            if (currentVersion is null)
            {
                throw new ReadOnlyProbeException("Windows edition information is unavailable.");
            }

            var productName = ReadRegistryString(currentVersion, "ProductName");
            var editionId = ReadRegistryString(currentVersion, "EditionID");
            var displayVersion = ReadRegistryString(currentVersion, "DisplayVersion");
            var revision = ReadRegistryInteger(currentVersion, "UBR");

            return ValueTask.FromResult(ProbeResult.Observed(new WindowsVersionObservation(
                IsWindows: true,
                Major: checked((int)version.MajorVersion),
                Minor: checked((int)version.MinorVersion),
                Build: checked((int)version.BuildNumber),
                Revision: revision,
                ProductName: productName,
                EditionId: editionId,
                DisplayVersion: displayVersion,
                Architecture: RuntimeInformation.OSArchitecture)));
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception) || exception is OverflowException)
        {
            return ValueTask.FromResult(ProbeErrors.Unavailable<WindowsVersionObservation>(
                "windows_version_query_failed",
                "Windows did not report its edition and true operating system version."));
        }
    }

    private static string ReadRegistryString(RegistryKey key, string valueName)
    {
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ReadOnlyProbeException("A required Windows edition value is unavailable.");
        }

        return value.Trim();
    }

    private static int ReadRegistryInteger(RegistryKey key, string valueName)
    {
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value switch
        {
            int integer => integer,
            long integer when integer is >= int.MinValue and <= int.MaxValue => (int)integer,
            _ => throw new ReadOnlyProbeException("A required Windows version value is unavailable."),
        };
    }
}

internal sealed class WindowsStorageProbe : IStorageProbe
{
    public ValueTask<ProbeResult<StorageObservation>> ObserveAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var fullPath = Path.GetFullPath(targetPath);
            if (IsUncPath(fullPath))
            {
                return ValueTask.FromResult(ProbeResult.Observed(new StorageObservation(
                    Path.GetPathRoot(fullPath) ?? fullPath,
                    DriveType.Network,
                    "Unknown",
                    AvailableFreeBytes: 0,
                    TotalBytes: 0)));
            }

            var existingPath = FindNearestExistingAncestor(fullPath);
            var volumeRoot = FindVolumeRoot(existingPath);
            var driveType = MapDriveType(LocalSystemNativeMethods.GetDriveType(volumeRoot));
            if (driveType == DriveType.Network)
            {
                return ValueTask.FromResult(ProbeResult.Observed(new StorageObservation(
                    volumeRoot,
                    driveType,
                    "Unknown",
                    AvailableFreeBytes: 0,
                    TotalBytes: 0)));
            }

            var fileSystemName = new char[256];
            if (!LocalSystemNativeMethods.GetVolumeInformation(
                    volumeRoot,
                    IntPtr.Zero,
                    0,
                    out _,
                    out _,
                    out _,
                    fileSystemName,
                    checked((uint)fileSystemName.Length)))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (!LocalSystemNativeMethods.GetDiskFreeSpaceEx(
                    volumeRoot,
                    out var availableFreeBytes,
                    out var totalBytes,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return ValueTask.FromResult(ProbeResult.Observed(new StorageObservation(
                volumeRoot,
                driveType,
                ReadNullTerminatedString(fileSystemName),
                checked((long)availableFreeBytes),
                checked((long)totalBytes))));
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception) || exception is ArgumentException)
        {
            return ValueTask.FromResult(ProbeErrors.Unavailable<StorageObservation>(
                "storage_query_failed",
                "Windows did not report storage information for the selected path."));
        }
    }

    internal static string FindNearestExistingAncestor(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var candidate = Path.GetFullPath(targetPath);
        while (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            var parent = Directory.GetParent(candidate);
            if (parent is null)
            {
                throw new DirectoryNotFoundException();
            }

            candidate = parent.FullName;
        }

        return candidate;
    }

    internal static string FindVolumeRoot(string existingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingPath);

        var buffer = new char[32_768];
        if (!LocalSystemNativeMethods.GetVolumePathName(
                existingPath,
                buffer,
                checked((uint)buffer.Length)))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var volumeRoot = ReadNullTerminatedString(buffer);
        if (volumeRoot.Length == 0)
        {
            throw new ReadOnlyProbeException("Windows did not report the selected path's volume root.");
        }

        return volumeRoot;
    }

    private static string ReadNullTerminatedString(char[] buffer)
    {
        var terminator = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
    }

    internal static DriveType MapDriveType(uint nativeDriveType) => nativeDriveType switch
    {
        1 => DriveType.NoRootDirectory,
        2 => DriveType.Removable,
        3 => DriveType.Fixed,
        4 => DriveType.Network,
        5 => DriveType.CDRom,
        6 => DriveType.Ram,
        _ => DriveType.Unknown,
    };

    internal static bool IsUncPath(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
        (path.StartsWith(@"\\", StringComparison.Ordinal) &&
         !path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
         !path.StartsWith(@"\\.\", StringComparison.Ordinal));
}

[StructLayout(LayoutKind.Sequential)]
internal struct TokenElevation
{
    internal int TokenIsElevated;
}

internal enum TokenInformationClass
{
    TokenElevation = 20,
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct OsVersionInfo
{
    internal int Size;
    internal uint MajorVersion;
    internal uint MinorVersion;
    internal uint BuildNumber;
    internal uint PlatformId;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string ServicePack;
}

internal static class LocalSystemNativeMethods
{
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        Microsoft.Win32.SafeHandles.SafeAccessTokenHandle tokenHandle,
        TokenInformationClass tokenInformationClass,
        ref TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll")]
    internal static extern int RtlGetVersion(ref OsVersionInfo versionInformation);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", CharSet = CharSet.Unicode,
        ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumePathName(
        string fileName,
        [Out] char[] volumePathName,
        uint bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetDriveTypeW", CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    internal static extern uint GetDriveType(string rootPathName);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", CharSet = CharSet.Unicode,
        ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeInformation(
        string rootPathName,
        IntPtr volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        [Out] char[] fileSystemNameBuffer,
        uint fileSystemNameSize);

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode,
        ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetDiskFreeSpaceEx(
        string directoryName,
        out ulong freeBytesAvailable,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);
}
