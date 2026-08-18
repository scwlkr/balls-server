using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BallsServer.Core.Sharing;
using BallsServer.Presentation;
using BallsServer.Windows;

namespace BallsServer.App.Tests;

public sealed class FirstShareWindowSmokeTests
{
    [Fact]
    public async Task HelperRunsTheProductionOwnershipPolicyForWindowsPowerShell()
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "BallsServer.Helper.dll");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("--ownership-policy");

        using var process = new Process { StartInfo = startInfo };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync("{}".AsMemory(), timeout.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("\"status\":\"Unknown\"", await outputTask, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelperRunsStableFolderIdentityForWindowsPowerShell()
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "BallsServer.Helper.dll");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("--folder-identity");

        using var process = new Process { StartInfo = startInfo };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync(Path.GetTempPath().AsMemory(), timeout.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
        Assert.Matches("^[0-9a-f]{8}:[0-9a-f]{16}$", (await outputTask).Trim());
    }

    [Fact]
    public void HostAndConnectFlowsRenderAndRespondWithoutShowingAWindow()
    {
        RunOnSta(() =>
        {
            var application = new App();
            application.InitializeComponent();

            var now = new DateTimeOffset(2026, 8, 17, 19, 0, 0, TimeSpan.Zero);
            var presentation = new FirstSharePresentation(
                new AcceptingFolderValidator(),
                new FixedTimeProvider(now),
                new CompletingHostSetupCoordinator(),
                new CompletingClientConnectionService());

            var hostWindow = new FirstShareWindow(presentation, FirstSharePage.HostFiles);
            RenderOffscreen(hostWindow);

            AssertAccessibleControl(hostWindow, "Folder to host", typeof(TextBox));
            AssertAccessibleControl(hostWindow, "Private local network", typeof(RadioButton));
            var hostPreviewButton = AssertAccessibleControl(
                hostWindow,
                "Preview Host Files setup",
                typeof(Button));
            AssertLogoRendered(hostWindow);

            presentation.SelectHostFolder(@"C:\Shared");
            hostPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RenderOffscreen(hostWindow);

            Assert.Contains(
                Descendants<TextBlock>(hostWindow),
                text => text.Text == "What Balls Server will change" &&
                    text.Visibility == Visibility.Visible);
            var applyButton = AssertAccessibleControl(
                hostWindow,
                "Apply Host Files setup",
                typeof(Button));
            applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RenderOffscreen(hostWindow);
            AssertAccessibleControl(
                hostWindow,
                "Generated Balls Server setup code",
                typeof(TextBox));
            hostWindow.Close();

            var grant = new SetupCodeGrant(
                SetupCodeCodec.CurrentVersion,
                AccessPathKind.Tailscale,
                "owner-pc.example.ts.net",
                "Balls",
                @"OWNER-PC\BallsClient-7H4K2M",
                "synthetic-test-password-47",
                now.AddMinutes(10));
            var connectWindow = new FirstShareWindow(presentation, FirstSharePage.ConnectToFiles);
            RenderOffscreen(connectWindow);

            AssertAccessibleControl(connectWindow, "Balls Server setup code", typeof(TextBox));
            var connectPreviewButton = AssertAccessibleControl(
                connectWindow,
                "Preview connection",
                typeof(Button));
            presentation.SetSetupCode(SetupCodeCodec.Encode(grant));
            connectPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RenderOffscreen(connectWindow);

            Assert.Contains(
                Descendants<TextBlock>(connectWindow),
                text => text.Text == @"\\owner-pc.example.ts.net\Balls" &&
                    text.Visibility == Visibility.Visible);
            Assert.DoesNotContain(
                Descendants<TextBlock>(connectWindow),
                text => text.Text.Contains(grant.Password, StringComparison.Ordinal));
            AssertAccessibleControl(connectWindow, "Mapped drive letter", typeof(ComboBox));
            AssertAccessibleControl(connectWindow, "Save limited credential", typeof(CheckBox));
            var connectButton = AssertAccessibleControl(
                connectWindow,
                "Connect mapped drive",
                typeof(Button));
            connectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RenderOffscreen(connectWindow);
            Assert.Contains(
                Descendants<TextBlock>(connectWindow),
                text => text.Text == "Balls Server is connected as drive P:." &&
                    text.Visibility == Visibility.Visible);
            connectWindow.Close();

            var helperRequest = new HostSetupRequest(
                HostSetupProtocol.CurrentVersion,
                "0123456789abcdef0123456789abcdef",
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "S-1-5-21-100-200-300-1001",
                now,
                now.AddMinutes(2),
                HostSetupOperation.Apply,
                @"C:\Shared",
                AccessPathKind.Local);
            var approvalWindow = new BallsServer.Helper.HostSetupApprovalWindow(
                helperRequest,
                new HostSetupMutationPreview(new string('a', 64), 4));
            RenderOffscreen(approvalWindow);

            Assert.Contains("revision 4", approvalWindow.PlanReference, StringComparison.Ordinal);
            AssertAccessibleControl(approvalWindow, "Approved managed folder", typeof(TextBlock));
            AssertAccessibleControl(approvalWindow, "Cancel host setup", typeof(Button));
            AssertAccessibleControl(approvalWindow, "Approve host setup", typeof(Button));
            AssertLogoRendered(approvalWindow);
            approvalWindow.Close();
        });
    }

    private static FrameworkElement AssertAccessibleControl(
        DependencyObject root,
        string accessibleName,
        Type expectedType)
    {
        var match = Descendants<FrameworkElement>(TraversalRoot(root))
            .Single(element => AutomationProperties.GetName(element) == accessibleName);

        Assert.IsType(expectedType, match);
        Assert.True(match.IsEnabled);
        return match;
    }

    private static void AssertLogoRendered(DependencyObject root)
    {
        var logo = Descendants<Image>(TraversalRoot(root)).Single();

        Assert.NotNull(logo.Source);
        Assert.True(logo.ActualWidth > 0);
        Assert.True(logo.ActualHeight > 0);
    }

    private static void RenderOffscreen(Window window)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)window.Width,
            (int)window.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(content);

        Assert.Equal((int)window.Width, bitmap.PixelWidth);
        Assert.Equal((int)window.Height, bitmap.PixelHeight);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        root = TraversalRoot(root);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static DependencyObject TraversalRoot(DependencyObject root) => root is Window window
        ? Assert.IsAssignableFrom<DependencyObject>(window.Content)
        : root;

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class AcceptingFolderValidator : IFolderValidator
    {
        public FolderValidation Validate(string path) => FolderValidation.Valid(path);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class CompletingHostSetupCoordinator : IHostSetupCoordinator
    {
        public Task<HostSetupResult> ApplyAsync(
            HostSetupPreview request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HostSetupResult.Completed("BALLS1.synthetic-code"));

        public Task<HostSetupResult> StopSharingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HostSetupResult.Stopped());
    }

    private sealed class CompletingClientConnectionService : IClientConnectionService
    {
        public IReadOnlyList<char> GetAvailableDriveLetters() => ['P'];

        public Task<ClientConnectionResult> ConnectAsync(
            ClientConnectionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ClientConnectionResult.Connected(
                request.DriveLetter,
                $@"\\{request.Grant.HostName}\{request.Grant.ShareName}"));

        public Task<ClientConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ClientConnectionResult.Disconnected());
    }
}
