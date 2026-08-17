using System.ComponentModel;
using System.Runtime.InteropServices;
using BallsServer.Core.Preflight;
using Microsoft.Win32.SafeHandles;

namespace BallsServer.Windows;

internal interface IFolderAccessEvaluator
{
    FolderPermissionObservation Evaluate(string targetPath);
}

internal sealed class WindowsFolderPermissionProbe : IFolderPermissionProbe
{
    private readonly IFolderAccessEvaluator _evaluator;

    internal WindowsFolderPermissionProbe()
        : this(new NativeFolderAccessEvaluator())
    {
    }

    internal WindowsFolderPermissionProbe(IFolderAccessEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public ValueTask<ProbeResult<FolderPermissionObservation>> ObserveAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return ValueTask.FromResult(ProbeResult.Observed(_evaluator.Evaluate(targetPath)));
        }
        catch (Exception exception) when (ProbeErrors.IsExpected(exception) || exception is ArgumentException)
        {
            return ValueTask.FromResult(ProbeErrors.Unavailable<FolderPermissionObservation>(
                "folder_acl_query_failed",
                "Windows did not report the effective permissions for the selected folder."));
        }
    }
}

internal sealed class NativeFolderAccessEvaluator : IFolderAccessEvaluator
{
    private const uint InvalidFileAttributes = 0xFFFFFFFF;
    private const uint FileAttributeDirectory = 0x00000010;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint GroupSecurityInformation = 0x00000002;
    private const uint DaclSecurityInformation = 0x00000004;
    private const int SeFileObject = 1;

    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const int SecurityImpersonation = 2;
    private const int ErrorInsufficientBuffer = 122;

    private const uint FileGenericRead = 0x00120089;
    private const uint FileGenericWrite = 0x00120116;
    private const uint FileGenericExecute = 0x001200A0;
    private const uint FileAllAccess = 0x001F01FF;
    private const uint FileTraverse = 0x00000020;
    private const uint Delete = 0x00010000;

    public FolderPermissionObservation Evaluate(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var fullPath = Path.GetFullPath(targetPath);
        var attributes = FolderSecurityNativeMethods.GetFileAttributes(fullPath);
        if (attributes == InvalidFileAttributes)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ErrorFileNotFound or ErrorPathNotFound)
            {
                return new FolderPermissionObservation(
                    Exists: false,
                    CanReadAndTraverse: false,
                    CanModify: false);
            }

            throw new Win32Exception(error);
        }

        if ((attributes & FileAttributeDirectory) == 0)
        {
            return new FolderPermissionObservation(
                Exists: false,
                CanReadAndTraverse: false,
                CanModify: false);
        }

        var result = FolderSecurityNativeMethods.GetNamedSecurityInfo(
            fullPath,
            SeFileObject,
            OwnerSecurityInformation | GroupSecurityInformation | DaclSecurityInformation,
            out _,
            out _,
            out _,
            out _,
            out var securityDescriptor);
        if (result != 0)
        {
            throw new Win32Exception(checked((int)result));
        }

        try
        {
            using var processToken = OpenCurrentProcessToken();
            if (!FolderSecurityNativeMethods.DuplicateToken(
                    processToken,
                    SecurityImpersonation,
                    out var impersonationToken))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            using (impersonationToken)
            {
                var mapping = new GenericMapping
                {
                    GenericRead = FileGenericRead,
                    GenericWrite = FileGenericWrite,
                    GenericExecute = FileGenericExecute,
                    GenericAll = FileAllAccess,
                };

                var canReadAndTraverse = HasAccess(
                    securityDescriptor,
                    impersonationToken,
                    FileGenericRead | FileTraverse,
                    mapping);
                var canModify = HasAccess(
                    securityDescriptor,
                    impersonationToken,
                    FileGenericRead | FileGenericWrite | FileGenericExecute | Delete,
                    mapping);

                return new FolderPermissionObservation(
                    Exists: true,
                    canReadAndTraverse,
                    canModify);
            }
        }
        finally
        {
            _ = FolderSecurityNativeMethods.LocalFree(securityDescriptor);
        }
    }

    private static SafeAccessTokenHandle OpenCurrentProcessToken()
    {
        if (!FolderSecurityNativeMethods.OpenProcessToken(
                FolderSecurityNativeMethods.GetCurrentProcess(),
                TokenQuery | TokenDuplicate,
                out var token))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return token;
    }

    private static bool HasAccess(
        IntPtr securityDescriptor,
        SafeAccessTokenHandle token,
        uint desiredAccess,
        GenericMapping mapping)
    {
        uint privilegeSetLength = 1024;
        var privilegeSet = Marshal.AllocHGlobal(checked((int)privilegeSetLength));
        try
        {
            if (!FolderSecurityNativeMethods.AccessCheck(
                    securityDescriptor,
                    token,
                    desiredAccess,
                    ref mapping,
                    privilegeSet,
                    ref privilegeSetLength,
                    out _,
                    out var accessStatus))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != ErrorInsufficientBuffer)
                {
                    throw new Win32Exception(error);
                }

                Marshal.FreeHGlobal(privilegeSet);
                privilegeSet = Marshal.AllocHGlobal(checked((int)privilegeSetLength));
                if (!FolderSecurityNativeMethods.AccessCheck(
                        securityDescriptor,
                        token,
                        desiredAccess,
                        ref mapping,
                        privilegeSet,
                        ref privilegeSetLength,
                        out _,
                        out accessStatus))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }

            return accessStatus;
        }
        finally
        {
            Marshal.FreeHGlobal(privilegeSet);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct GenericMapping
{
    internal uint GenericRead;
    internal uint GenericWrite;
    internal uint GenericExecute;
    internal uint GenericAll;
}

internal static class FolderSecurityNativeMethods
{
    [DllImport("kernel32.dll", EntryPoint = "GetFileAttributesW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint GetFileAttributes(string fileName);

    [DllImport("advapi32.dll", EntryPoint = "GetNamedSecurityInfoW",
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint GetNamedSecurityInfo(
        string objectName,
        int objectType,
        uint securityInfo,
        out IntPtr owner,
        out IntPtr group,
        out IntPtr dacl,
        out IntPtr sacl,
        out IntPtr securityDescriptor);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateToken(
        SafeAccessTokenHandle existingToken,
        int impersonationLevel,
        out SafeAccessTokenHandle duplicateToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AccessCheck(
        IntPtr securityDescriptor,
        SafeAccessTokenHandle clientToken,
        uint desiredAccess,
        ref GenericMapping genericMapping,
        IntPtr privilegeSet,
        ref uint privilegeSetLength,
        out uint grantedAccess,
        [MarshalAs(UnmanagedType.Bool)] out bool accessStatus);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);
}
