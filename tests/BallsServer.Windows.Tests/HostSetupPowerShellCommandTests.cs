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
        Assert.Equal("Apply", input.RootElement.GetProperty("Operation").GetString());
        Assert.Equal(request.ManagedFolder, input.RootElement.GetProperty("ManagedFolder").GetString());
        Assert.Equal(request.Password, input.RootElement.GetProperty("Password").GetString());
        Assert.DoesNotContain(request.ManagedFolder, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(request.Password, command.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardInputRoundTripsAsJsonThroughWindowsPowerShell()
    {
        var command = HostSetupPowerShellCommand.Create(new HostSetupMutationRequest(
            @"C:\Shared",
            AccessPathKind.Local,
            "BallsClient-7H4K2M",
            "synthetic-password-47"));
        var startInfo = new ProcessStartInfo
        {
            FileName = command.StartInfo.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = command.StartInfo.StandardInputEncoding,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "$payload=[Console]::In.ReadToEnd()|ConvertFrom-Json;" +
            "[Console]::Out.Write([string]$payload.AccessPath)");

        using var process = new Process { StartInfo = startInfo };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync(command.StandardInput.AsMemory(), timeout.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("Local", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task CommandUsesTheNativeWindowsPowerShellModulePath()
    {
        var command = HostSetupPowerShellCommand.Create(new HostSetupMutationRequest(
            @"C:\Shared",
            AccessPathKind.Local,
            "BallsClient-7H4K2M",
            "synthetic-password-47"));
        var startInfo = command.StartInfo;
        startInfo.ArgumentList.Clear();
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "$ErrorActionPreference='Stop';" +
            "ConvertTo-SecureString 'synthetic-password' -AsPlainText -Force|Out-Null;" +
            "[Console]::Out.Write('ready')");

        using var process = new Process { StartInfo = startInfo };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("ready", output);
        Assert.Empty(error);
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

        var repeated = HostSetupMutationOutput.Parse(
            "{\"hostName\":\"owner-pc.example.ts.net\",\"shareName\":\"Balls\"," +
            "\"userName\":\"OWNER-PC\\\\BallsClient-7H4K2M\",\"alreadyConfigured\":true}",
            AccessPathKind.Tailscale);
        Assert.Contains("AlreadyConfigured = True", repeated.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OutputParserAcceptsOnlyTheBoundedStopSharingResult()
    {
        var parser = typeof(HostSetupMutationOutput).GetMethod(
            "ParseStopSharing",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.NotNull(parser);
        var output = parser.Invoke(null, ["{\"status\":\"Stopped\",\"shareName\":\"Balls\"}"]);

        Assert.Contains("SharingStopped = True", output?.ToString(), StringComparison.Ordinal);
        Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            parser.Invoke(null, ["{\"status\":\"Stopped\",\"shareName\":\"Balls\",\"extra\":true}"]));
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

    [Fact]
    public async Task FixedScriptPlansJournaledInitialSetupBeforeAnyResourceMutation()
    {
        var plan = await RunPlanAsync(
            "{\"operation\":\"Apply\",\"ledgerStatus\":\"Absent\"," +
            "\"resources\":[\"Missing\",\"Missing\",\"Missing\",\"Missing\",\"Missing\"]}");

        Assert.Equal(
            [
                "InitializeOwnershipJournal",
                "CreateGroup",
                "CreateAccount",
                "AddMembership",
                "AddFolderAce",
                "CreateShare",
                "CreateFirewallRule",
                "VerifyEffectiveAccess",
                "CommitOwnership",
            ],
            plan.RootElement.GetProperty("primitives").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
        Assert.Equal("Ready", plan.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task FixedScriptRefusesAmbiguityAndVerifiesRepeatedConformantSetupWithoutCreation()
    {
        using var repeated = await RunPlanAsync(
            "{\"operation\":\"Apply\",\"ledgerStatus\":\"Committed\"," +
            "\"resources\":[\"OwnedConformant\",\"OwnedConformant\",\"OwnedConformant\"," +
            "\"OwnedConformant\",\"OwnedConformant\"]}");
        using var conflict = await RunPlanAsync(
            "{\"operation\":\"Apply\",\"ledgerStatus\":\"Absent\"," +
            "\"resources\":[\"UnmanagedConflict\",\"Missing\",\"Missing\",\"Missing\",\"Missing\"]}");
        using var ambiguous = await RunPlanAsync(
            "{\"operation\":\"Apply\",\"ledgerStatus\":\"Committed\"," +
            "\"resources\":[\"OwnedConformant\",\"Ambiguous\",\"OwnedConformant\"," +
            "\"OwnedConformant\",\"OwnedConformant\"]}");

        Assert.Equal("NoChanges", repeated.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            ["VerifyEffectiveAccess"],
            repeated.RootElement.GetProperty("primitives").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
        Assert.Equal("Refused", conflict.RootElement.GetProperty("status").GetString());
        Assert.Equal("Refused", ambiguous.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task FixedScriptReconcilesInterruptedSetupWithoutRetryingAppliedPrimitives()
    {
        using var plan = await RunPlanAsync(
            "{\"operation\":\"Apply\",\"ledgerStatus\":\"Applying\"," +
            "\"resources\":[\"OwnedConformant\",\"OwnedConformant\",\"Missing\",\"Missing\",\"Missing\"]," +
            "\"appliedPrimitives\":[\"CreateGroup\",\"CreateAccount\"]}");

        Assert.Equal("Recovering", plan.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            ["RemoveAccount", "RemoveGroup", "MarkCanceled"],
            plan.RootElement.GetProperty("primitives").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
        Assert.DoesNotContain(
            plan.RootElement.GetProperty("primitives").EnumerateArray(),
            item => item.GetString()?.StartsWith("Create", StringComparison.Ordinal) is true);
    }

    [Fact]
    public async Task FixedScriptRollsBackEveryAppliedPrimitiveInReverseOrder()
    {
        using var plan = await RunPlanAsync(
            "{\"operation\":\"Apply\",\"ledgerStatus\":\"Applying\"," +
            "\"resources\":[\"OwnedConformant\",\"OwnedConformant\",\"OwnedConformant\"," +
            "\"OwnedConformant\",\"OwnedConformant\"]," +
            "\"appliedPrimitives\":[\"CreateGroup\",\"CreateAccount\",\"AddMembership\"," +
            "\"AddFolderAce\",\"CreateShare\",\"CreateFirewallRule\"]}");

        Assert.Equal("Recovering", plan.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            [
                "RemoveFirewallRule",
                "RemoveShare",
                "RemoveFolderAce",
                "RemoveMembership",
                "RemoveAccount",
                "RemoveGroup",
                "MarkCanceled",
            ],
            plan.RootElement.GetProperty("primitives").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task FixedScriptStopsOnlyProvenOwnedConfigurationAndNeverDeletesTheFolder()
    {
        using var plan = await RunPlanAsync(
            "{\"operation\":\"StopSharing\",\"ledgerStatus\":\"Committed\"," +
            "\"resources\":[\"OwnedConformant\",\"OwnedConformant\",\"OwnedConformant\"," +
            "\"OwnedConformant\",\"OwnedConformant\"]}");

        Assert.Equal("Ready", plan.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            [
                "DisableAccount",
                "RemoveMembership",
                "RemoveShare",
                "RemoveFirewallRule",
                "RemoveFolderAce",
                "RemoveAccount",
                "RemoveGroup",
                "MarkHostRemoved",
            ],
            plan.RootElement.GetProperty("primitives").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
        Assert.DoesNotContain(
            plan.RootElement.GetProperty("primitives").EnumerateArray(),
            item => item.GetString()?.Contains("Folder", StringComparison.Ordinal) is true &&
                item.GetString() != "RemoveFolderAce");
    }

    private static async Task<JsonDocument> RunPlanAsync(string input)
    {
        var command = HostSetupPowerShellCommand.Create(new HostSetupMutationRequest(
            @"C:\Shared",
            AccessPathKind.Local,
            "BallsClient-7H4K2M",
            "synthetic-password-47"));
        var startInfo = new ProcessStartInfo
        {
            FileName = command.StartInfo.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
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
        startInfo.ArgumentList.Add(command.StartInfo.ArgumentList.Last());
        startInfo.ArgumentList.Add("-PlanOnly");

        using var process = new Process { StartInfo = startInfo };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;

        Assert.True(process.ExitCode == 0, error);
        return JsonDocument.Parse(output);
    }
}
