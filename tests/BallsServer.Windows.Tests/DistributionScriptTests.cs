using System.Diagnostics;
using System.Text;

namespace BallsServer.Windows.Tests;

public sealed class DistributionScriptTests
{
    [Fact]
    public void RetiredInstallerAndHistoricalPublisherAreParseable()
    {
        var repository = FindRepositoryRoot();
        var installerPath = Path.Combine(repository, "install.ps1");
        var publisherPath = Path.Combine(repository, "scripts", "Publish-Portable.ps1");

        Assert.True(File.Exists(installerPath));
        Assert.True(File.Exists(publisherPath));
        AssertPowerShellParses(installerPath);
        AssertPowerShellParses(publisherPath);

        var installer = File.ReadAllText(installerPath);
        Assert.Contains("retired and unsupported", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://github.com/scwlkr/balls", installer, StringComparison.Ordinal);
        Assert.Contains("throw", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-WebRequest", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-RestMethod", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-Expression", installer, StringComparison.OrdinalIgnoreCase);

        var publisher = File.ReadAllText(publisherPath);
        Assert.Contains("BallsServer.App.csproj", publisher, StringComparison.Ordinal);
        Assert.Contains("BallsServer.Helper.csproj", publisher, StringComparison.Ordinal);
        Assert.Contains("--self-contained", publisher, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", publisher, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BallsServer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void AssertPowerShellParses(string scriptPath)
    {
        var escapedPath = scriptPath.Replace("'", "''", StringComparison.Ordinal);
        var parserScript =
            "$tokens=$null;$errors=$null;" +
            "[Management.Automation.Language.Parser]::ParseFile('" + escapedPath +
            "',[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count -ne 0){exit 1}";
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(parserScript)));

        using var parser = Process.Start(startInfo);
        Assert.NotNull(parser);
        Assert.True(parser.WaitForExit(15_000));
        Assert.Equal(0, parser.ExitCode);
    }
}
