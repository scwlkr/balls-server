using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using BallsServer.Core.Sharing;
using BallsServer.Presentation;
using Microsoft.Win32.SafeHandles;

namespace BallsServer.App;

public sealed class ElevatedHostSetupCoordinator : IHostSetupCoordinator
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(3);

    public async Task<HostSetupResult> ApplyAsync(
        HostSetupPreview request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteAsync(
            HostSetupOperation.Apply,
            request.ManagedFolder,
            request.AccessPath,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<HostSetupResult> StopSharingAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(HostSetupOperation.StopSharing, null, null, cancellationToken);

    private static async Task<HostSetupResult> ExecuteAsync(
        HostSetupOperation operationKind,
        string? managedFolder,
        AccessPathKind? accessPath,
        CancellationToken cancellationToken)
    {

        var helperPath = Path.Combine(AppContext.BaseDirectory, "BallsServer.Helper.exe");
        if (!File.Exists(helperPath))
        {
            return HostSetupResult.Refused("The protected setup helper is missing. Update Balls Server and try again.");
        }

        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            return HostSetupResult.Refused("Windows could not identify the account that started Balls Server.");
        }

        var pipeName = $"BallsServer.Helper.v1.{RandomHex(16)}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: HelperPipeFrame.MaximumPayloadBytes,
            outBufferSize: HelperPipeFrame.MaximumPayloadBytes);
        var now = TimeProvider.System.GetUtcNow();
        var protocolRequest = new HostSetupRequest(
            HostSetupProtocol.CurrentVersion,
            RandomHex(16),
            RandomHex(32),
            sid,
            now,
            now.AddMinutes(3),
            operationKind,
            managedFolder,
            accessPath);

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(OperationTimeout);
        Process? helper = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };
            startInfo.ArgumentList.Add("--pipe");
            startInfo.ArgumentList.Add(pipeName);
            helper = Process.Start(startInfo);
            if (helper is null)
            {
                return HostSetupResult.Failed();
            }

            using var connection = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
            connection.CancelAfter(ConnectionTimeout);
            await pipe.WaitForConnectionAsync(connection.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcessId) ||
                clientProcessId != (uint)helper.Id)
            {
                return HostSetupResult.Refused("The protected setup helper identity could not be verified.");
            }

            await HelperPipeFrame.WriteAsync(
                pipe,
                HostSetupProtocol.EncodeRequest(protocolRequest),
                operation.Token).ConfigureAwait(false);
            var responseJson = await HelperPipeFrame.ReadAsync(pipe, operation.Token).ConfigureAwait(false);
            var response = HostSetupProtocol.DecodeResponse(responseJson);
            if (response.OperationId != protocolRequest.OperationId ||
                response.Nonce != protocolRequest.Nonce ||
                operationKind == HostSetupOperation.Apply &&
                response.Result.Status == HostSetupResultStatus.Stopped ||
                operationKind == HostSetupOperation.StopSharing &&
                response.Result.Status != HostSetupResultStatus.Stopped)
            {
                return HostSetupResult.Refused("The protected setup response did not match this request.");
            }

            return response.Result;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return HostSetupResult.Canceled();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or Win32Exception or FormatException or OperationCanceledException)
        {
            return HostSetupResult.Failed();
        }
        finally
        {
            helper?.Dispose();
        }
    }

    private static string RandomHex(int byteCount) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}
