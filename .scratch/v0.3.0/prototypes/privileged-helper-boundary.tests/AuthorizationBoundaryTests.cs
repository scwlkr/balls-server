using System.Text;
using BallsServer.SecurityPrototype;

namespace BallsServer.SecurityPrototype.Tests;

public sealed class AuthorizationBoundaryTests
{
    private const string UserSid = "S-1-5-21-1000-1001-1002-1003";
    private const string Nonce = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Strict_codec_accepts_one_bounded_typed_request()
    {
        HelperRequest request = TestScenario.Request();

        byte[] encoded = StrictProtocolCodec.EncodeRequest(request);
        ProtocolDecodeResult decoded = StrictProtocolCodec.DecodeRequest(encoded);

        Assert.True(decoded.IsAccepted);
        Assert.Equal("balls-helper/1", decoded.Request!.ProtocolVersion);
        Assert.Equal(16_384, StrictProtocolCodec.MaximumRequestBytes);
    }

    [Fact]
    public void Strict_codec_rejects_unknown_fields()
    {
        byte[] encoded = Encoding.UTF8.GetBytes(TestScenario.RequestJson().Replace("}", ",\"approval\":true}"));

        ProtocolDecodeResult decoded = StrictProtocolCodec.DecodeRequest(encoded);

        Assert.Equal(RefusalCode.UnknownField, decoded.RefusalCode);
    }

    [Fact]
    public void Strict_codec_rejects_oversized_messages_before_parsing()
    {
        byte[] encoded = new byte[16_385];

        ProtocolDecodeResult decoded = StrictProtocolCodec.DecodeRequest(encoded);

        Assert.Equal(RefusalCode.MessageTooLarge, decoded.RefusalCode);
    }

    [Fact]
    public void Strict_terminal_codec_binds_operation_and_nonce()
    {
        byte[] encoded = StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, TerminalResult.Completed());

        TerminalDecodeResult decoded = StrictProtocolCodec.DecodeTerminal(encoded, "operation-7", Nonce);

        Assert.True(decoded.IsAccepted);
        Assert.Equal(TerminalStatus.Completed, decoded.Result!.Status);
    }

    [Fact]
    public void Strict_terminal_codec_rejects_unknown_fields()
    {
        byte[] encoded = StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, TerminalResult.Completed());
        string json = Encoding.UTF8.GetString(encoded).Replace("}", ",\"debug\":\"private\"}");

        TerminalDecodeResult decoded = StrictProtocolCodec.DecodeTerminal(Encoding.UTF8.GetBytes(json), "operation-7", Nonce);

        Assert.Equal(RefusalCode.UnknownField, decoded.RefusalCode);
    }

    [Theory]
    [InlineData("operation-8", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("operation-7", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void Strict_terminal_codec_rejects_wrong_response_binding(string operationId, string nonce)
    {
        byte[] encoded = StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, TerminalResult.Completed());

        TerminalDecodeResult decoded = StrictProtocolCodec.DecodeTerminal(encoded, operationId, nonce);

        Assert.Equal(RefusalCode.BindingMismatch, decoded.RefusalCode);
    }

    [Fact]
    public void Terminal_codec_refuses_private_diagnostic_content()
    {
        TerminalResult unsafeResult = new(TerminalStatus.Refused, RefusalCode.UntrustedImage, 0, 0, @"C:\Users\owner\helper.exe S-1-5-21-9 password=hunter2");

        Assert.Throws<ArgumentException>(() => StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, unsafeResult));
    }

    [Theory]
    [InlineData("operationId", "different-operation", RefusalCode.BindingMismatch)]
    [InlineData("nonce", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", RefusalCode.BindingMismatch)]
    [InlineData("expectedRevision", "8", RefusalCode.StaleRevision)]
    public void Request_binding_rejects_wrong_operation_nonce_or_revision(string field, string value, RefusalCode expected)
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            EncodedRequest = Encoding.UTF8.GetBytes(TestScenario.MutateField(field, value))
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(expected, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Fact]
    public void Remote_clients_are_refused()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            Helper = TestScenario.Helper() with { IsRemote = true }
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.RemotePeer, result.Code);
    }

    [Fact]
    public void Pipe_squatters_are_refused_by_expected_process_instance()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            Dashboard = TestScenario.Dashboard() with { ProcessId = 999 }
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.UnexpectedProcess, result.Code);
    }

    [Theory]
    [InlineData("user", RefusalCode.WrongUser)]
    [InlineData("session", RefusalCode.WrongSession)]
    [InlineData("integrity", RefusalCode.WrongIntegrity)]
    [InlineData("process", RefusalCode.UnexpectedProcess)]
    [InlineData("image", RefusalCode.UntrustedImage)]
    [InlineData("hash", RefusalCode.UntrustedHash)]
    [InlineData("signer", RefusalCode.UntrustedSigner)]
    public void Mutual_peer_authentication_fails_closed_for_wrong_evidence(string defect, RefusalCode expected)
    {
        PeerEvidence helper = defect switch
        {
            "user" => TestScenario.Helper() with { UserSid = "S-1-5-21-9" },
            "session" => TestScenario.Helper() with { SessionId = 4 },
            "integrity" => TestScenario.Helper() with { Integrity = IntegrityLevel.Medium, IsElevated = false },
            "process" => TestScenario.Helper() with { StartedAtTicks = 301 },
            "image" => TestScenario.Helper() with { ImagePath = @"C:\build\helper.exe" },
            "hash" => TestScenario.Helper() with { Sha256 = new string('0', 64) },
            "signer" => TestScenario.Helper() with { SignerThumbprint = "UNTRUSTED" },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        AuthorizationScenario scenario = TestScenario.Valid() with { Helper = helper };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(expected, result.Code);
    }

    [Fact]
    public void Dead_peer_race_is_refused_after_identity_checks()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            Helper = TestScenario.Helper() with { IsAliveAfterVerification = false }
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.DeadPeer, result.Code);
    }

    [Fact]
    public void Replayed_nonce_is_refused()
    {
        AuthorizationBoundary boundary = new();
        AuthorizationScenario scenario = TestScenario.Valid();

        TerminalResult first = boundary.Execute(scenario);
        TerminalResult replay = boundary.Execute(scenario);

        Assert.Equal(TerminalStatus.Completed, first.Status);
        Assert.Equal(RefusalCode.Replay, replay.Code);
        Assert.Equal(0, replay.AuthorizedOperationCount);
    }

    [Fact]
    public void Changed_authoritative_plan_is_refused_without_apply()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            AuthoritativePlanDigest = new string('b', 64)
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.PlanChanged, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Theory]
    [InlineData(true, 1_050L, RefusalCode.Cancelled, TerminalStatus.Canceled, true)]
    [InlineData(false, 1_181L, RefusalCode.Crashed, TerminalStatus.Unknown, false)]
    public void Cancellation_and_request_timeout_stop_before_authorization(
        bool cancelled,
        long now,
        RefusalCode expected,
        TerminalStatus expectedStatus,
        bool expectedDelivered)
    {
        HelperPhaseTimeline timeline = TestScenario.Timeline();
        if (!cancelled)
        {
            timeline = timeline with
            {
                TerminalWriteStartedAt = now,
                TerminalWriteCompletedAt = now + 5
            };
        }

        AuthorizationScenario scenario = TestScenario.Valid() with { IsCancelled = cancelled, Timeline = timeline };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(expected, result.Code);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedDelivered, result.WasDelivered);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Fact]
    public void Helper_owned_consent_expires_after_two_monotonic_minutes()
    {
        ConsentLease lease = TestScenario.ConsentLease(createdAt: 2_000);

        ConsentConsumeResult result = lease.TryConsume(TestScenario.ConsentBinding(), monotonicNow: 2_121);

        Assert.Equal(RefusalCode.ConsentExpired, result.Code);
        Assert.False(result.IsAuthorized);
    }

    [Fact]
    public void Second_apply_on_same_helper_consent_is_refused()
    {
        ConsentLease lease = TestScenario.ConsentLease(createdAt: 2_000);

        ConsentConsumeResult first = lease.TryConsume(TestScenario.ConsentBinding(), monotonicNow: 2_010);
        ConsentConsumeResult second = lease.TryConsume(TestScenario.ConsentBinding(), monotonicNow: 2_011);

        Assert.True(first.IsAuthorized);
        Assert.Equal(RefusalCode.ConsentConsumed, second.Code);
    }

    [Fact]
    public void Uac_and_dashboard_only_approval_do_not_authorize()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with { HelperApplyDecision = false, DashboardClaimedApproval = true };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.HelperConsentRequired, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Theory]
    [InlineData(CrashPoint.BeforeJournal, TerminalStatus.Unknown, false)]
    [InlineData(CrashPoint.AfterJournal, TerminalStatus.RepairNeeded, true)]
    public void Crash_points_return_typed_dashboard_outcomes(CrashPoint crashPoint, TerminalStatus expected, bool expectedDelivered)
    {
        AuthorizationScenario scenario = TestScenario.Valid() with { CrashPoint = crashPoint };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(expected, result.Status);
        Assert.Equal(expectedDelivered, result.IsTerminal);
        Assert.DoesNotContain(@"C:\", result.PublicMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Terminal_channel_writes_exactly_one_response()
    {
        TerminalResponseGate gate = new();
        TerminalResult result = TerminalResult.Completed();

        Assert.True(gate.TryWrite(result));
        Assert.False(gate.TryWrite(result));
        Assert.Equal(1, gate.ResponseCount);
    }

    [Fact]
    public void Setup_code_secret_can_be_taken_exactly_once()
    {
        OneShotSecretResponse response = new(Encoding.UTF8.GetBytes("not-a-real-credential"));

        Assert.NotNull(response.TryTake());
        Assert.Null(response.TryTake());
        Assert.Equal(1, response.TakeCount);
    }

    [Fact]
    public void Production_trust_requires_protected_path_and_trusted_signer()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            Helper = TestScenario.Helper() with { IsAdministratorProtectedPath = false, IsAuthenticodeTrusted = false }
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.UntrustedImage, result.Code);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Unsigned_development_requires_every_disposable_vm_guard(bool marker, bool snapshot, bool uniqueNamespace)
    {
        DevelopmentGuard guard = new(marker, snapshot, uniqueNamespace ? "BallsServer.Test.4ea9" : "BallsServer");

        bool accepted = DevelopmentTrustPolicy.AcceptUnsigned(TestScenario.Helper() with
        {
            IsAdministratorProtectedPath = false,
            IsAuthenticodeTrusted = false,
            SignerThumbprint = null
        }, guard);

        Assert.False(accepted);
    }

    [Fact]
    public void Disposable_vm_guard_allows_unsigned_prototype_only_in_isolated_mode()
    {
        DevelopmentGuard guard = new(true, true, "BallsServer.Test.4ea9");

        bool accepted = DevelopmentTrustPolicy.AcceptUnsigned(TestScenario.Helper() with
        {
            IsAdministratorProtectedPath = false,
            IsAuthenticodeTrusted = false,
            SignerThumbprint = null
        }, guard);

        Assert.True(accepted);
    }

    [Fact]
    public void One_locally_authorized_request_completes_once_without_system_mutation()
    {
        AuthorizationBoundary boundary = new();

        TerminalResult result = boundary.Execute(TestScenario.Valid());

        Assert.Equal(TerminalStatus.Completed, result.Status);
        Assert.Equal(1, result.AuthorizedOperationCount);
        Assert.Equal(0, result.SystemMutationCount);
    }
}

internal static class TestScenario
{
    public static HelperRequest Request() => new(
        "balls-helper/1",
        "authorize",
        "OP-02",
        "operation-7",
        AuthorizationBoundaryTestsAccessor.Nonce,
        7,
        AuthorizationBoundaryTestsAccessor.Digest,
        "S-1-5-21-1000-1001-1002-1003",
        3,
        1_185);

    public static string RequestJson() => "{\"protocolVersion\":\"balls-helper/1\",\"messageType\":\"authorize\",\"operation\":\"OP-02\",\"operationId\":\"operation-7\",\"nonce\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"expectedRevision\":7,\"provisionalPlanDigest\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"initiatingUserSid\":\"S-1-5-21-1000-1001-1002-1003\",\"sessionId\":3,\"deadlineMonotonic\":1185}";

    public static string MutateField(string field, string value) => field switch
    {
        "operationId" => RequestJson().Replace("\"operationId\":\"operation-7\"", $"\"operationId\":\"{value}\""),
        "nonce" => RequestJson().Replace($"\"nonce\":\"{AuthorizationBoundaryTestsAccessor.Nonce}\"", $"\"nonce\":\"{value}\""),
        "expectedRevision" => RequestJson().Replace("\"expectedRevision\":7", $"\"expectedRevision\":{value}"),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    public static PeerEvidence Dashboard() => new(
        PeerRole.Dashboard, 100, 200, "S-1-5-21-1000-1001-1002-1003", 3,
        IntegrityLevel.Medium, false, @"C:\Program Files\Balls Server\BallsServer.exe", "0.4.0.0",
        new string('d', 64), "AA11", true, true, false);

    public static PeerEvidence Helper() => new(
        PeerRole.Helper, 101, 300, "S-1-5-21-1000-1001-1002-1003", 3,
        IntegrityLevel.High, true, @"C:\Program Files\Balls Server\BallsServer.Helper.exe", "0.4.0.0",
        new string('e', 64), "AA11", true, true, false);

    public static AuthorizationScenario Valid() => new(
        StrictProtocolCodec.EncodeRequest(Request()),
        Dashboard(),
        Helper(),
        new ProcessInstance(100, 200),
        new ProcessInstance(101, 300),
        "S-1-5-21-1000-1001-1002-1003",
        3,
        7,
        AuthorizationBoundaryTestsAccessor.Digest,
        false,
        true,
        false,
        CrashPoint.None)
    {
        PipeInstanceId = "pipe-instance-7",
        Timeline = Timeline(),
        DisplayedConsentBinding = ConsentBinding(),
        ApplyConsentBinding = ConsentBinding()
    };

    public static HelperPhaseTimeline Timeline() => new(
        PipeCreatedAt: 1_000,
        HelperLaunchedAt: 1_005,
        DashboardEvidenceStartedAt: 1_005,
        DashboardEvidenceCompletedAt: 1_010,
        HelperEvidenceStartedAt: 1_010,
        HelperEvidenceCompletedAt: 1_015,
        MutualAuthenticationCompletedAt: 1_015,
        RequestLengthReadStartedAt: 1_015,
        RequestLengthReadCompletedAt: 1_020,
        RequestBodyReadStartedAt: 1_020,
        RequestBodyReadCompletedAt: 1_025,
        ReobservationStartedAt: 1_025,
        ReobservationCompletedAt: 1_050,
        PlanDisplayedAt: 1_050,
        ApplyAt: 1_060,
        TerminalWriteStartedAt: 1_060,
        TerminalWriteCompletedAt: 1_065);

    public static ConsentBinding ConsentBinding() => new(
        new ProcessInstance(101, 300),
        "S-1-5-21-1000-1001-1002-1003",
        3,
        "OP-02",
        "operation-7",
        AuthorizationBoundaryTestsAccessor.Nonce,
        7,
        AuthorizationBoundaryTestsAccessor.Digest,
        "pipe-instance-7");

    public static ConsentLease ConsentLease(long createdAt) => new(ConsentBinding(), createdAt);
}

internal static class AuthorizationBoundaryTestsAccessor
{
    public const string Nonce = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
}
