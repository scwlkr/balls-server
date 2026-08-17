using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BallsServer.Core.Sharing;

namespace BallsServer.Windows.Tests;

public sealed class HostSetupPowerShellCommandTests
{
    [Fact]
    public void CommandKeepsFolderAndCredentialOutOfProcessArguments()
    {
        var request = new HostSetupMutationRequest(
            @"C:\Private\Shared",
            AccessPathKind.Tailscale,
            "BallsClient-7H4K2M",
            "synthetic-password-47");

        var command = HostSetupPowerShellCommand.Create(request);
        var arguments = string.Join(' ', command.StartInfo.ArgumentList);

        Assert.True(command.StartInfo.RedirectStandardInput);
        Assert.True(command.StartInfo.RedirectStandardOutput);
        Assert.True(command.StartInfo.RedirectStandardError);
        Assert.DoesNotContain(request.ManagedFolder, arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(request.Password, arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(request.UserName, arguments, StringComparison.Ordinal);
        using var input = JsonDocument.Parse(command.StandardInput);
        Assert.Equal(request.ManagedFolder, input.RootElement.GetProperty("ManagedFolder").GetString());
        Assert.Equal(request.Password, input.RootElement.GetProperty("Password").GetString());
        Assert.DoesNotContain(request.ManagedFolder, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(request.Password, command.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FixedScriptUsesNarrowShareFirewallAndRollbackOperations()
    {
        var command = HostSetupPowerShellCommand.Create(new HostSetupMutationRequest(
            @"C:\Shared",
            AccessPathKind.Local,
            "BallsClient-7H4K2M",
            "synthetic-password-47"));
        var script = File.ReadAllText(command.StartInfo.ArgumentList.Last());

        Assert.Contains("New-LocalUser", script, StringComparison.Ordinal);
        Assert.Contains("New-SmbShare", script, StringComparison.Ordinal);
        Assert.Contains("New-NetFirewallRule", script, StringComparison.Ordinal);
        Assert.Contains("LocalSubnet", script, StringComparison.Ordinal);
        Assert.Contains("Remove-SmbShare", script, StringComparison.Ordinal);
        Assert.Contains("Remove-LocalUser", script, StringComparison.Ordinal);
        Assert.Contains("RemoveAccessRuleSpecific", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-SmbServerConfiguration", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Enable-WindowsOptionalFeature", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputParserAcceptsOnlyTheBoundedPublicResult()
    {
        var output = HostSetupMutationOutput.Parse(
            "{\"hostName\":\"owner-pc.example.ts.net\",\"shareName\":\"Balls\",\"userName\":\"OWNER-PC\\\\BallsClient-7H4K2M\"}",
            AccessPathKind.Tailscale);

        Assert.Equal("owner-pc.example.ts.net", output.HostName);
        Assert.Throws<FormatException>(() => HostSetupMutationOutput.Parse(
            "{\"hostName\":\"public.example.com\",\"shareName\":\"Balls\",\"userName\":\"OWNER-PC\\\\BallsClient-7H4K2M\"}",
            AccessPathKind.Tailscale));
        Assert.Throws<FormatException>(() => HostSetupMutationOutput.Parse(
            "{\"hostName\":\"owner-pc.example.ts.net\",\"shareName\":\"Balls\",\"userName\":\"OWNER-PC\\\\BallsClient-7H4K2M\",\"extra\":true}",
            AccessPathKind.Tailscale));
    }

    [Fact]
    public void FixedMutationScriptPassesThePowerShellParserWithoutExecuting()
    {
        var command = HostSetupPowerShellCommand.Create(new HostSetupMutationRequest(
            @"C:\Shared",
            AccessPathKind.Local,
            "BallsClient-7H4K2M",
            "synthetic-password-47"));
        var mutationScriptPath = command.StartInfo.ArgumentList.Last().Replace("'", "''", StringComparison.Ordinal);
        var parserScript =
            "$tokens=$null;$errors=$null;" +
            "[Management.Automation.Language.Parser]::ParseFile('" +
            mutationScriptPath +
            "',[ref]$tokens,[ref]$errors)|Out-Null;" +
            "if($errors.Count -ne 0){exit 1}";
        var parserStartInfo = new ProcessStartInfo
        {
            FileName = command.StartInfo.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        parserStartInfo.ArgumentList.Add("-NoProfile");
        parserStartInfo.ArgumentList.Add("-NonInteractive");
        parserStartInfo.ArgumentList.Add("-EncodedCommand");
        parserStartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(parserScript)));

        using var parser = Process.Start(parserStartInfo);
        Assert.NotNull(parser);
        Assert.True(parser.WaitForExit(15_000));
        Assert.Equal(0, parser.ExitCode);
    }
}
