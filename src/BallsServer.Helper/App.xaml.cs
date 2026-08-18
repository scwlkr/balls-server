using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows;
using BallsServer.Core.Preflight;
using BallsServer.Core.Sharing;
using BallsServer.Windows;

namespace BallsServer.Helper;

public partial class App : Application
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(3);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!TryReadPipeName(e.Args, out var pipeName))
        {
            Shutdown(2);
            return;
        }

        using var operation = new CancellationTokenSource(OperationTimeout);
        try
        {
            await RunAsync(pipeName, operation.Token);
            Shutdown();
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or FormatException or UnauthorizedAccessException)
        {
            Shutdown(1);
        }
    }

    private static async Task RunAsync(string pipeName, CancellationToken cancellationToken)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connection.CancelAfter(ConnectionTimeout);
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(connection.Token);

        var requestJson = await HelperPipeFrame.ReadAsync(pipe, cancellationToken);
        var request = HostSetupProtocol.DecodeRequest(requestJson, TimeProvider.System.GetUtcNow());
        var currentSid = WindowsIdentity.GetCurrent().User?.Value;
        HostSetupResult result;

        if (!string.Equals(currentSid, request.InitiatingUserSid, StringComparison.Ordinal))
        {
            result = HostSetupResult.Refused(
                "Windows approval must use the same account that started Balls Server.");
        }
        else
        {
            result = await ReviewAndApplyAsync(request, cancellationToken);
        }

        var response = new HostSetupResponse(
            HostSetupProtocol.CurrentVersion,
            request.OperationId,
            request.Nonce,
            result);
        await HelperPipeFrame.WriteAsync(
            pipe,
            HostSetupProtocol.EncodeResponse(response),
            cancellationToken);
    }

    private static async Task<HostSetupResult> ReviewAndApplyAsync(
        HostSetupRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Operation == HostSetupOperation.Apply)
        {
            var report = await WindowsPreflightFactory.CreateHostService()
                .RunAsync(new PreflightRequest(request.ManagedFolder!), cancellationToken);
            var selectedPath = request.AccessPath == AccessPathKind.Local
                ? report.LocalAccess
                : report.TailscaleAccess;
            if (!report.Computer.IsReady || !report.ManagedFolder.IsReady || !selectedPath.IsReady)
            {
                return HostSetupResult.Refused(
                    "Host setup was refused because the selected folder or private access path is not ready.");
            }
        }

        var approval = new HostSetupApprovalWindow(request);
        if (approval.ShowDialog() is not true)
        {
            return HostSetupResult.Canceled();
        }

        var userName = request.Operation == HostSetupOperation.Apply
            ? HostCredentialGenerator.CreateUserName()
            : string.Empty;
        var password = request.Operation == HostSetupOperation.Apply
            ? HostCredentialGenerator.CreatePassword()
            : string.Empty;
        try
        {
            var output = await new PowerShellHostSetupMutator().ApplyAsync(
                request.Operation == HostSetupOperation.Apply
                    ? new HostSetupMutationRequest(
                        request.ManagedFolder!,
                        request.AccessPath!.Value,
                        userName,
                        password)
                    : HostSetupMutationRequest.StopSharing(),
                cancellationToken);
            if (output.SharingStopped)
            {
                return HostSetupResult.Stopped();
            }
            if (output.AlreadyConfigured)
            {
                return HostSetupResult.Refused(
                    "Host setup is already configured and was verified without changing it. Stop Sharing before creating a new setup code.");
            }
            var grant = new SetupCodeGrant(
                SetupCodeCodec.CurrentVersion,
                request.AccessPath!.Value,
                output.HostName,
                output.ShareName,
                output.UserName,
                password,
                TimeProvider.System.GetUtcNow().AddHours(24));
            return HostSetupResult.Completed(SetupCodeCodec.Encode(grant));
        }
        catch (HostSetupMutationException)
        {
            return HostSetupResult.Failed();
        }
    }

    private static bool TryReadPipeName(string[] arguments, out string pipeName)
    {
        pipeName = string.Empty;
        if (arguments.Length != 2 || arguments[0] != "--pipe")
        {
            return false;
        }

        pipeName = arguments[1];
        return Regex.IsMatch(
            pipeName,
            "^BallsServer\\.Helper\\.v1\\.[0-9a-f]{32}$",
            RegexOptions.CultureInvariant);
    }
}
