using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BallsServer.Core.Sharing;

namespace BallsServer.Windows;

public sealed record HostSetupMutationRequest(
    string ManagedFolder,
    AccessPathKind AccessPath,
    string UserName,
    string Password)
{
    public HostSetupOperation Operation { get; init; } = HostSetupOperation.Apply;

    public static HostSetupMutationRequest StopSharing() => new(
        string.Empty,
        AccessPathKind.Local,
        string.Empty,
        string.Empty)
    {
        Operation = HostSetupOperation.StopSharing,
    };

    public override string ToString() =>
        $"HostSetupMutationRequest {{ Operation = {Operation}, ManagedFolder = [REDACTED], AccessPath = {AccessPath}, " +
        $"UserName = {UserName}, Password = [REDACTED] }}";
}

public sealed partial record HostSetupPowerShellCommand(
    ProcessStartInfo StartInfo,
    string StandardInput)
{
    public const string ScriptFileName = "HostSetup.ps1";

    public static HostSetupPowerShellCommand Create(HostSetupMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Operation) ||
            request.Operation == HostSetupOperation.Apply &&
            (string.IsNullOrWhiteSpace(request.ManagedFolder) ||
             !Path.IsPathFullyQualified(request.ManagedFolder) ||
             !Enum.IsDefined(request.AccessPath) ||
             string.IsNullOrWhiteSpace(request.UserName) ||
             !UserNamePattern().IsMatch(request.UserName) ||
             string.IsNullOrEmpty(request.Password) ||
             request.Password.Length is < 20 or > 128) ||
            request.Operation == HostSetupOperation.StopSharing &&
            (request.ManagedFolder.Length > 0 || request.UserName.Length > 0 || request.Password.Length > 0))
        {
            throw new ArgumentException("The host mutation request is invalid.", nameof(request));
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, ScriptFileName);
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException("The fixed host setup script is missing.");
        }

        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment.Remove("PSModulePath");
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        var input = JsonSerializer.Serialize(new MutationEnvelope(
            request.Operation.ToString(),
            request.ManagedFolder,
            request.AccessPath.ToString(),
            request.UserName,
            request.Password));
        return new HostSetupPowerShellCommand(startInfo, input);
    }

    public override string ToString() =>
        $"HostSetupPowerShellCommand {{ FileName = {StartInfo.FileName}, StandardInput = [REDACTED] }}";

    [GeneratedRegex("^BallsClient-[A-Z0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();

    private sealed record MutationEnvelope(
        string Operation,
        string ManagedFolder,
        string AccessPath,
        string UserName,
        string Password);
}
