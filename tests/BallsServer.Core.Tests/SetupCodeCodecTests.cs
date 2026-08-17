using BallsServer.Core.Sharing;

namespace BallsServer.Core.Tests;

public sealed class SetupCodeCodecTests
{
    [Fact]
    public void DecodePreservesExplicitEndpointAndLimitedCredential()
    {
        var expiresAt = new DateTimeOffset(2026, 8, 17, 20, 0, 0, TimeSpan.Zero);
        var grant = new SetupCodeGrant(
            SetupCodeCodec.CurrentVersion,
            AccessPathKind.Tailscale,
            "owner-pc.example.ts.net",
            "Balls",
            "BallsClient-7H4K2M",
            "correct-horse-battery-staple-47",
            expiresAt);

        var encoded = SetupCodeCodec.Encode(grant);
        var decoded = SetupCodeCodec.Decode(encoded, expiresAt.AddMinutes(-5));

        Assert.Equal(grant, decoded);
    }

    [Fact]
    public void DecodeRejectsPublicIpEndpoint()
    {
        var now = new DateTimeOffset(2026, 8, 17, 19, 0, 0, TimeSpan.Zero);
        var encoded = SetupCodeCodec.Encode(new SetupCodeGrant(
            SetupCodeCodec.CurrentVersion,
            AccessPathKind.Local,
            "8.8.8.8",
            "Balls",
            "BallsClient-7H4K2M",
            "correct-horse-battery-staple-47",
            now.AddMinutes(10)));

        var exception = Assert.Throws<FormatException>(() => SetupCodeCodec.Decode(encoded, now));

        Assert.Equal("That setup code contains a public or unsupported host.", exception.Message);
    }
}
