using BallsServer.Core.Sharing;
using BallsServer.Presentation;

namespace BallsServer.Presentation.Tests;

public sealed class FirstSharePresentationTests
{
    [Fact]
    public void PreviewHostSetupDescribesSelectedFolderAndExplicitAccessPath()
    {
        var presentation = new FirstSharePresentation(
            new AcceptingFolderValidator(),
            TimeProvider.System);

        presentation.ShowHostFiles();
        presentation.SelectHostFolder(@"C:\Shared");
        presentation.SelectHostAccessPath(AccessPathKind.Tailscale);
        presentation.PreviewHostSetup();

        Assert.Equal(FirstSharePage.HostFiles, presentation.ActivePage);
        Assert.Equal(@"C:\Shared", presentation.HostPreview?.ManagedFolder);
        Assert.Equal(AccessPathKind.Tailscale, presentation.HostPreview?.AccessPath);
        Assert.Equal(
            [
                "Share the selected folder as Balls.",
                "Create one limited client credential.",
                "Allow SMB only through the selected Tailscale path.",
                "Record every product-owned change for Stop Sharing.",
            ],
            presentation.HostPreview?.Changes);
        Assert.True(presentation.CanApplyHostSetup);
    }

    [Fact]
    public void PreviewConnectionUsesExactEndpointWithoutRenderingPassword()
    {
        var now = new DateTimeOffset(2026, 8, 17, 19, 0, 0, TimeSpan.Zero);
        var grant = new SetupCodeGrant(
            SetupCodeCodec.CurrentVersion,
            AccessPathKind.Tailscale,
            "owner-pc.example.ts.net",
            "Balls",
            @"OWNER-PC\BallsClient-7H4K2M",
            "correct-horse-battery-staple-47",
            now.AddMinutes(10));
        var presentation = new FirstSharePresentation(
            new AcceptingFolderValidator(),
            new FixedTimeProvider(now));

        presentation.ShowConnectToFiles();
        presentation.SetSetupCode(SetupCodeCodec.Encode(grant));
        presentation.PreviewConnection();

        Assert.Equal(FirstSharePage.ConnectToFiles, presentation.ActivePage);
        Assert.Equal(@"\\owner-pc.example.ts.net\Balls", presentation.ConnectionPreview?.Endpoint);
        Assert.Equal(AccessPathKind.Tailscale, presentation.ConnectionPreview?.AccessPath);
        Assert.Equal(@"OWNER-PC\BallsClient-7H4K2M", presentation.ConnectionPreview?.CredentialLabel);
        Assert.DoesNotContain(grant.Password, presentation.ConnectionPreview?.ToString(), StringComparison.Ordinal);
        Assert.True(presentation.CanApplyConnection);
    }

    [Fact]
    public async Task ApplyHostSetupPublishesOneTimeCodeFromCoordinator()
    {
        var result = HostSetupResult.Completed("BALLS1.synthetic-code");
        var coordinator = new RecordingHostSetupCoordinator(result);
        var presentation = new FirstSharePresentation(
            new AcceptingFolderValidator(),
            TimeProvider.System,
            coordinator);

        presentation.ShowHostFiles();
        presentation.SelectHostFolder(@"C:\Shared");
        presentation.PreviewHostSetup();
        await presentation.ApplyHostSetupAsync();

        Assert.Equal(@"C:\Shared", coordinator.Request?.ManagedFolder);
        Assert.Equal(AccessPathKind.Local, coordinator.Request?.AccessPath);
        Assert.Equal(HostSetupState.Completed, presentation.HostSetupState);
        Assert.Equal("BALLS1.synthetic-code", presentation.GeneratedSetupCode);
        Assert.DoesNotContain("synthetic-code", presentation.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectUsesTheExactDecodedEndpointAndSelectedDrive()
    {
        var now = new DateTimeOffset(2026, 8, 17, 19, 0, 0, TimeSpan.Zero);
        var service = new RecordingClientConnectionService();
        var presentation = new FirstSharePresentation(
            new AcceptingFolderValidator(),
            new FixedTimeProvider(now),
            clientConnectionService: service);
        var grant = new SetupCodeGrant(
            SetupCodeCodec.CurrentVersion,
            AccessPathKind.Tailscale,
            "owner-pc.example.ts.net",
            "Balls",
            @"OWNER-PC\BallsClient-7H4K2M",
            "correct-horse-battery-staple-47",
            now.AddMinutes(10));

        presentation.ShowConnectToFiles();
        presentation.SetSetupCode(SetupCodeCodec.Encode(grant));
        presentation.PreviewConnection();
        presentation.SelectDriveLetter('P');
        await presentation.ApplyConnectionAsync();

        Assert.Equal(grant, service.Request?.Grant);
        Assert.Equal('P', service.Request?.DriveLetter);
        Assert.True(service.Request?.SaveCredential);
        Assert.Equal(ClientConnectionState.Connected, presentation.ClientConnectionState);
        Assert.DoesNotContain(grant.Password, service.Request?.ToString(), StringComparison.Ordinal);
    }

    private sealed class AcceptingFolderValidator : IFolderValidator
    {
        public FolderValidation Validate(string path) => FolderValidation.Valid(path);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class RecordingHostSetupCoordinator(HostSetupResult result) : IHostSetupCoordinator
    {
        public HostSetupPreview? Request { get; private set; }

        public Task<HostSetupResult> ApplyAsync(
            HostSetupPreview request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingClientConnectionService : IClientConnectionService
    {
        public ClientConnectionRequest? Request { get; private set; }

        public IReadOnlyList<char> GetAvailableDriveLetters() => ['P', 'Q'];

        public Task<ClientConnectionResult> ConnectAsync(
            ClientConnectionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(ClientConnectionResult.Connected(
                request.DriveLetter,
                $@"\\{request.Grant.HostName}\{request.Grant.ShareName}"));
        }

        public Task<ClientConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ClientConnectionResult.Disconnected());
    }
}
