using BallsServer.Core.Sharing;

namespace BallsServer.Core.Tests;

public sealed class HostSetupProtocolTests
{
    [Fact]
    public void OperationDiscriminatorUsesANewProtocolVersion()
    {
        Assert.Equal(2, HostSetupProtocol.CurrentVersion);
    }

    [Fact]
    public void RequestRoundTripsWithoutExposingPrivateFieldsInDiagnostics()
    {
        var now = new DateTimeOffset(2026, 8, 17, 20, 0, 0, TimeSpan.Zero);
        var request = new HostSetupRequest(
            HostSetupProtocol.CurrentVersion,
            "0123456789abcdef0123456789abcdef",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "S-1-5-21-100-200-300-1001",
            now,
            now.AddMinutes(2),
            HostSetupOperation.Apply,
            @"C:\Private\Shared",
            AccessPathKind.Tailscale);

        var decoded = HostSetupProtocol.DecodeRequest(
            HostSetupProtocol.EncodeRequest(request),
            now.AddSeconds(1));

        Assert.Equal(request, decoded);
        Assert.DoesNotContain(request.ManagedFolder!, decoded.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(request.InitiatingUserSid, decoded.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RequestRejectsExpiredOrExtendedAuthority()
    {
        var now = new DateTimeOffset(2026, 8, 17, 20, 0, 0, TimeSpan.Zero);
        var expired = Request(now.AddMinutes(-3), now.AddSeconds(-1));
        var extended = Request(now, now.AddMinutes(4));

        Assert.Throws<FormatException>(() =>
            HostSetupProtocol.DecodeRequest(HostSetupProtocol.EncodeRequest(expired), now));
        Assert.Throws<FormatException>(() =>
            HostSetupProtocol.DecodeRequest(HostSetupProtocol.EncodeRequest(extended), now));
    }

    [Fact]
    public void RequestRequiresAnExplicitOperationAndAllowsOnlyPathlessStopSharing()
    {
        var now = new DateTimeOffset(2026, 8, 18, 20, 0, 0, TimeSpan.Zero);
        var applyWithoutOperation =
            "{\"Version\":2,\"OperationId\":\"0123456789abcdef0123456789abcdef\"," +
            "\"Nonce\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"," +
            "\"InitiatingUserSid\":\"S-1-5-21-100-200-300-1001\"," +
            "\"IssuedAt\":\"2026-08-18T20:00:00+00:00\",\"ExpiresAt\":\"2026-08-18T20:02:00+00:00\"," +
            "\"ManagedFolder\":\"C:\\\\Shared\",\"AccessPath\":0}";
        var stopSharing =
            "{\"Version\":2,\"OperationId\":\"0123456789abcdef0123456789abcdef\"," +
            "\"Nonce\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"," +
            "\"InitiatingUserSid\":\"S-1-5-21-100-200-300-1001\"," +
            "\"IssuedAt\":\"2026-08-18T20:00:00+00:00\",\"ExpiresAt\":\"2026-08-18T20:02:00+00:00\"," +
            "\"Operation\":1,\"ManagedFolder\":null,\"AccessPath\":null}";

        Assert.Throws<FormatException>(() => HostSetupProtocol.DecodeRequest(applyWithoutOperation, now));
        var decoded = HostSetupProtocol.DecodeRequest(stopSharing, now);

        Assert.Contains("Operation = StopSharing", decoded.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", decoded.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseRoundTripsCompletedSetupCodeAndRejectsUnknownFields()
    {
        var response = new HostSetupResponse(
            HostSetupProtocol.CurrentVersion,
            "0123456789abcdef0123456789abcdef",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            HostSetupResult.Completed("BALLS1.synthetic-secret"));

        var encoded = HostSetupProtocol.EncodeResponse(response);
        var decoded = HostSetupProtocol.DecodeResponse(encoded);

        Assert.Equal(response, decoded);
        Assert.DoesNotContain("synthetic-secret", response.ToString(), StringComparison.Ordinal);
        Assert.Throws<FormatException>(() =>
            HostSetupProtocol.DecodeResponse(encoded.TrimEnd('}') + ",\"extra\":true}"));
    }

    [Fact]
    public void ResponseRoundTripsStopSharingWithoutASetupSecret()
    {
        const string encoded =
            "{\"Version\":2,\"OperationId\":\"0123456789abcdef0123456789abcdef\"," +
            "\"Nonce\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"," +
            "\"Status\":4,\"SetupCode\":null}";

        var decoded = HostSetupProtocol.DecodeResponse(encoded);

        Assert.Contains("Status = Stopped", decoded.Result.ToString(), StringComparison.Ordinal);
        Assert.Null(decoded.Result.SetupCode);
    }

    [Fact]
    public async Task PipeFrameRoundTripsOneBoundedUtf8Message()
    {
        await using var stream = new MemoryStream();

        await HelperPipeFrame.WriteAsync(stream, "hello Balls Server");
        stream.Position = 0;

        Assert.Equal("hello Balls Server", await HelperPipeFrame.ReadAsync(stream));
        await Assert.ThrowsAsync<FormatException>(() =>
            HelperPipeFrame.WriteAsync(stream, new string('x', HelperPipeFrame.MaximumPayloadBytes + 1)));
    }

    private static HostSetupRequest Request(DateTimeOffset issuedAt, DateTimeOffset expiresAt) => new(
        HostSetupProtocol.CurrentVersion,
        "0123456789abcdef0123456789abcdef",
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        "S-1-5-21-100-200-300-1001",
        issuedAt,
        expiresAt,
        HostSetupOperation.Apply,
        @"C:\Shared",
        AccessPathKind.Local);
}
