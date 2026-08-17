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
            "BallsClient-7H4K2M",
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
        Assert.Equal("BallsClient-7H4K2M", presentation.ConnectionPreview?.CredentialLabel);
        Assert.DoesNotContain(grant.Password, presentation.ConnectionPreview?.ToString(), StringComparison.Ordinal);
        Assert.True(presentation.CanApplyConnection);
    }

    private sealed class AcceptingFolderValidator : IFolderValidator
    {
        public FolderValidation Validate(string path) => FolderValidation.Valid(path);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
