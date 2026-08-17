using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BallsServer.SecurityPrototype;

public sealed record PipeFeasibilityResult(
    bool ServerCreatedBeforeClientLaunch,
    bool ServerObservedClientProcessId,
    bool ClientObservedServerProcessId,
    int RequestCount,
    int ResponseCount,
    bool CleanupCompleted);

public static partial class EphemeralNamedPipeProbe
{
    public static async Task<PipeFeasibilityResult> RunAsync(TimeSpan timeout)
    {
        string pipeName = $"BallsServer.Test.HelperBoundary.{Guid.NewGuid():N}";
        using CancellationTokenSource timeoutSource = new(timeout);
        using NamedPipeServerStream server = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        bool createdBeforeLaunch = !server.IsConnected;
        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        string assemblyPath = typeof(EphemeralNamedPipeProbe).Assembly.Location;
        ProcessStartInfo startInfo = new(dotnetHost)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("pipe-client");
        startInfo.ArgumentList.Add(pipeName);

        using Process child = Process.Start(startInfo) ?? throw new InvalidOperationException("The isolated pipe client could not start.");
        bool cleanupCompleted = false;
        try
        {
            await server.WaitForConnectionAsync(timeoutSource.Token).ConfigureAwait(false);
            using StreamReader reader = new(server, leaveOpen: true);
            using StreamWriter writer = new(server, leaveOpen: true) { AutoFlush = true };

            string? request = await reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
            string[] fields = request?.Split('|') ?? [];
            if (fields.Length != 2 ||
                !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int claimedClientPid) ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int observedServerPid))
            {
                throw new InvalidDataException("The bounded pipe feasibility request was malformed.");
            }

            if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out uint observedClientPid))
            {
                throw new InvalidOperationException("Windows did not provide the connected client process ID.");
            }

            bool serverObservedClient = observedClientPid == (uint)child.Id && claimedClientPid == child.Id;
            bool clientObservedServer = observedServerPid == Environment.ProcessId;
            await writer.WriteLineAsync("terminal|accepted").ConfigureAwait(false);
            await child.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            string standardError = await child.StandardError.ReadToEndAsync(timeoutSource.Token).ConfigureAwait(false);
            if (child.ExitCode != 0)
            {
                throw new InvalidOperationException($"The isolated pipe client failed with exit code {child.ExitCode}: {standardError}");
            }

            cleanupCompleted = child.HasExited;
            return new(createdBeforeLaunch, serverObservedClient, clientObservedServer, 1, 1, cleanupCompleted);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                cleanupCompleted = true;
            }
        }
    }

    internal static async Task<int> RunClientAsync(string pipeName)
    {
        using NamedPipeClientStream client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);

        if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out uint serverPid))
        {
            return 3;
        }

        using StreamReader reader = new(client, leaveOpen: true);
        using StreamWriter writer = new(client, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync($"{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}|{serverPid.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        string? response = await reader.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
        return response == "terminal|accepted" ? 0 : 4;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
