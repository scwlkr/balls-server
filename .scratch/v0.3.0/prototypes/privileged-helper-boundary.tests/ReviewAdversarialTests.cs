using System.Collections.Concurrent;
using System.Text;
using BallsServer.SecurityPrototype;

namespace BallsServer.SecurityPrototype.Tests;

public sealed class ReviewAdversarialTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [MemberData(nameof(MalformedRequests))]
    public void Malformed_request_shapes_return_typed_refusal_without_throwing(string json)
    {
        ProtocolDecodeResult? result = null;

        Exception? exception = Record.Exception(() => result = StrictProtocolCodec.DecodeRequest(Encoding.UTF8.GetBytes(json)));

        Assert.Null(exception);
        Assert.Equal(RefusalCode.MalformedMessage, result!.RefusalCode);
    }

    [Fact]
    public void Unicode_operation_id_is_refused()
    {
        string json = TestScenario.RequestJson().Replace("operation-7", "opération-7");

        ProtocolDecodeResult result = StrictProtocolCodec.DecodeRequest(Encoding.UTF8.GetBytes(json));

        Assert.Equal(RefusalCode.MalformedMessage, result.RefusalCode);
    }

    [Theory]
    [MemberData(nameof(MalformedTerminals))]
    public void Malformed_terminal_values_return_typed_refusal(string json)
    {
        TerminalDecodeResult result = StrictProtocolCodec.DecodeTerminal(Encoding.UTF8.GetBytes(json), "operation-7", Nonce);

        Assert.Equal(RefusalCode.MalformedMessage, result.RefusalCode);
    }

    [Fact]
    public void Oversized_encoded_terminal_is_refused_on_encode()
    {
        string oversizedOperationId = new('a', 16_384);

        Assert.Throws<ArgumentException>(() =>
            StrictProtocolCodec.EncodeTerminal(oversizedOperationId, Nonce, TerminalResult.Completed()));
    }

    [Fact]
    public void Undefined_terminal_enums_are_refused_on_encode()
    {
        TerminalResult undefined = new((TerminalStatus)999, (RefusalCode)999, 0, 0, "arbitrary");

        Assert.Throws<ArgumentException>(() => StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, undefined));
    }

    [Theory]
    [MemberData(nameof(InvalidCrossStatusTerminals))]
    public void Invalid_cross_status_code_is_refused_on_encode(TerminalResult invalid)
    {
        Assert.Throws<ArgumentException>(() => StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, invalid));
    }

    [Theory]
    [MemberData(nameof(InvalidCrossStatusTerminalJson))]
    public void Invalid_cross_status_code_is_refused_on_decode(string json)
    {
        TerminalDecodeResult result = StrictProtocolCodec.DecodeTerminal(Encoding.UTF8.GetBytes(json), "operation-7", Nonce);

        Assert.Equal(RefusalCode.MalformedMessage, result.RefusalCode);
    }

    [Fact]
    public void External_terminal_status_is_exactly_the_five_specification_values()
    {
        string[] expected = ["Canceled", "Completed", "Refused", "RepairNeeded", "Unknown"];

        Assert.Equal(expected, Enum.GetNames<TerminalStatus>().Order().ToArray());
    }

    [Theory]
    [InlineData("completed", "Completed", true)]
    [InlineData("canceled", "Canceled", true)]
    [InlineData("refused", "Refused", true)]
    [InlineData("unknown", "Unknown", false)]
    [InlineData("repair-needed", "RepairNeeded", true)]
    public void Internal_outcomes_map_deterministically_to_external_status(string outcome, string expectedStatus, bool expectedDelivered)
    {
        AuthorizationScenario valid = TestScenario.Valid();
        AuthorizationScenario scenario = outcome switch
        {
            "completed" => valid,
            "canceled" => valid with { IsCancelled = true },
            "refused" => valid with { HelperApplyDecision = false },
            "unknown" => valid with { CrashPoint = CrashPoint.BeforeJournal },
            "repair-needed" => valid with { CrashPoint = CrashPoint.AfterJournal },
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(expectedStatus, result.Status.ToString());
        Assert.Equal(expectedDelivered, result.WasDelivered);
    }

    [Fact]
    public void Unconfirmed_dashboard_outcome_cannot_be_encoded_as_a_helper_terminal()
    {
        Assert.Throws<ArgumentException>(() =>
            StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, TerminalResult.Unconfirmed()));
    }

    [Fact]
    public void Pre_auth_refusal_is_not_claimed_as_an_authenticated_terminal_delivery()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            Dashboard = TestScenario.Dashboard() with { IsRemote = true }
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(TerminalStatus.Refused, result.Status);
        Assert.Equal(RefusalCode.RemotePeer, result.Code);
        Assert.False(result.WasDelivered);
        Assert.False(result.IsTerminal);
    }

    [Theory]
    [InlineData("Host EXAMPLE-HOST refused account example-user.")]
    [InlineData("Peer 10.0.0.42 returned an error.")]
    [InlineData("ACL D:(A;;GA;;;SY) was rejected.")]
    [InlineData("Files: taxes.xlsx, family.jpg")]
    [InlineData("System.InvalidOperationException: access failed")]
    public void Caller_controlled_public_diagnostics_are_refused(string privateText)
    {
        TerminalResult result = new(TerminalStatus.Refused, RefusalCode.UntrustedImage, 0, 0, privateText);

        Assert.Throws<ArgumentException>(() => StrictProtocolCodec.EncodeTerminal("operation-7", Nonce, result));
    }

    [Fact]
    public void Stale_revision_consumes_nonce_before_a_later_attempt()
    {
        AuthorizationBoundary boundary = new();
        AuthorizationScenario stale = TestScenario.Valid() with { CurrentRevision = 8 };

        TerminalResult first = boundary.Execute(stale);
        TerminalResult second = boundary.Execute(stale with { CurrentRevision = 7 });

        Assert.Equal(RefusalCode.StaleRevision, first.Code);
        Assert.Equal(RefusalCode.Replay, second.Code);
    }

    [Fact]
    public async Task Consent_lease_has_exactly_one_concurrent_winner()
    {
        ConsentLease lease = TestScenario.ConsentLease(createdAt: 2_000);

        int winners = await CountConcurrentWinners(() => lease.TryConsume(TestScenario.ConsentBinding(), 2_010).IsAuthorized);

        Assert.Equal(1, winners);
    }

    [Fact]
    public async Task Terminal_gate_has_exactly_one_concurrent_winner()
    {
        TerminalResponseGate gate = new();

        int winners = await CountConcurrentWinners(() => gate.TryWrite(TerminalResult.Completed()));

        Assert.Equal(1, winners);
        Assert.Equal(1, gate.ResponseCount);
    }

    [Fact]
    public void Integrated_terminal_emission_gate_exposes_only_one_delivered_result()
    {
        HelperPhaseTimeline timeline = TestScenario.Timeline();
        TerminalEmissionGate gate = new(timeline);

        TerminalResult first = gate.Expose(TerminalResult.Completed(), timeline.ApplyAt);
        TerminalResult second = gate.Expose(TerminalResult.Completed(), timeline.ApplyAt);

        Assert.True(first.WasDelivered);
        Assert.False(second.WasDelivered);
        Assert.Equal(1, gate.ResponseCount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("unconfirmed")]
    [InlineData("premature")]
    [InlineData("invalid-contract")]
    public void Failed_first_terminal_attempt_closes_the_emission_gate(string failure)
    {
        HelperPhaseTimeline timeline = TestScenario.Timeline();
        TerminalEmissionGate gate = new(timeline);
        TerminalResult invalidContract = new(
            TerminalStatus.Completed,
            RefusalCode.Replay,
            0,
            0,
            "caller-controlled text");
        (TerminalResult Candidate, long OutcomeReadyAt) firstAttempt = failure switch
        {
            "null" => (null!, timeline.ApplyAt),
            "unconfirmed" => (TerminalResult.Unconfirmed(), timeline.ApplyAt),
            "premature" => (TerminalResult.Completed(), timeline.TerminalWriteCompletedAt + 1),
            "invalid-contract" => (invalidContract, timeline.ApplyAt),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };

        TerminalResult first = gate.Expose(firstAttempt.Candidate, firstAttempt.OutcomeReadyAt);
        TerminalResult second = gate.Expose(TerminalResult.Completed(), timeline.ApplyAt);

        Assert.False(first.WasDelivered);
        Assert.False(second.WasDelivered);
        Assert.Equal(0, gate.ResponseCount);
    }

    [Fact]
    public async Task Concurrent_valid_and_invalid_terminal_attempts_close_after_one_claim()
    {
        HelperPhaseTimeline timeline = TestScenario.Timeline();
        TerminalEmissionGate gate = new(timeline);
        TerminalResult invalid = TerminalResult.Unconfirmed();
        const int participantCount = 32;
        using Barrier barrier = new(participantCount);

        Task<TerminalResult>[] attempts = Enumerable.Range(0, participantCount)
            .Select(index => Task.Run(() =>
            {
                barrier.SignalAndWait();
                TerminalResult candidate = index % 2 == 0 ? TerminalResult.Completed() : invalid;
                return gate.Expose(candidate, timeline.ApplyAt);
            }))
            .ToArray();

        TerminalResult[] results = await Task.WhenAll(attempts);
        TerminalResult afterRace = gate.Expose(TerminalResult.Completed(), timeline.ApplyAt);

        Assert.InRange(results.Count(result => result.WasDelivered), 0, 1);
        Assert.Equal(results.Count(result => result.WasDelivered), gate.ResponseCount);
        Assert.False(afterRace.WasDelivered);
    }

    [Fact]
    public async Task Concurrent_valid_and_null_terminal_attempts_close_after_one_claim()
    {
        HelperPhaseTimeline timeline = TestScenario.Timeline();
        TerminalEmissionGate gate = new(timeline);
        const int participantCount = 32;
        using Barrier barrier = new(participantCount);

        Task<TerminalResult>[] attempts = Enumerable.Range(0, participantCount)
            .Select(index => Task.Run(() =>
            {
                barrier.SignalAndWait();
                TerminalResult? candidate = index % 2 == 0 ? TerminalResult.Completed() : null;
                return gate.Expose(candidate, timeline.ApplyAt);
            }))
            .ToArray();

        TerminalResult[] results = await Task.WhenAll(attempts);
        TerminalResult afterRace = gate.Expose(TerminalResult.Completed(), timeline.ApplyAt);

        Assert.InRange(results.Count(result => result.WasDelivered), 0, 1);
        Assert.Equal(results.Count(result => result.WasDelivered), gate.ResponseCount);
        Assert.False(afterRace.WasDelivered);
    }

    [Fact]
    public async Task Secret_response_has_exactly_one_concurrent_winner()
    {
        OneShotSecretResponse response = new(Encoding.UTF8.GetBytes("concurrency-canary"));

        int winners = await CountConcurrentWinners(() => response.TryTake() is not null);

        Assert.Equal(1, winners);
        Assert.Equal(1, response.TakeCount);
    }

    [Theory]
    [InlineData("helper")]
    [InlineData("user")]
    [InlineData("session")]
    [InlineData("operation")]
    [InlineData("operationId")]
    [InlineData("nonce")]
    [InlineData("revision")]
    [InlineData("digest")]
    [InlineData("pipe")]
    public void Consent_refuses_a_change_to_every_bound_component(string component)
    {
        ConsentBinding expected = TestScenario.ConsentBinding();
        ConsentBinding changed = component switch
        {
            "helper" => expected with { HelperInstance = new ProcessInstance(101, 301) },
            "user" => expected with { UserSid = "S-1-5-21-9" },
            "session" => expected with { SessionId = 4 },
            "operation" => expected with { Operation = "OP-03" },
            "operationId" => expected with { OperationId = "operation-8" },
            "nonce" => expected with { Nonce = new string('f', 64) },
            "revision" => expected with { ExpectedRevision = 8 },
            "digest" => expected with { PlanDigest = new string('b', 64) },
            "pipe" => expected with { PipeInstanceId = "pipe-instance-8" },
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
        ConsentLease lease = new(expected, createdAt: 1_040);

        ConsentConsumeResult result = lease.TryConsume(changed, monotonicNow: 1_050);

        Assert.Equal(RefusalCode.BindingMismatch, result.Code);
        Assert.False(result.IsAuthorized);
    }

    [Fact]
    public void Execute_refuses_a_caller_extended_deadline()
    {
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            EncodedRequest = Encoding.UTF8.GetBytes(TestScenario.RequestJson().Replace("\"deadlineMonotonic\":1185", "\"deadlineMonotonic\":999999"))
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.BindingMismatch, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Theory]
    [InlineData("helper")]
    [InlineData("user")]
    [InlineData("session")]
    [InlineData("operation")]
    [InlineData("operationId")]
    [InlineData("nonce")]
    [InlineData("revision")]
    [InlineData("digest")]
    [InlineData("pipe")]
    public void Execute_refuses_each_apply_time_binding_mismatch(string component)
    {
        ConsentBinding displayed = TestScenario.ConsentBinding();
        ConsentBinding applied = component switch
        {
            "helper" => displayed with { HelperInstance = new ProcessInstance(101, 301) },
            "user" => displayed with { UserSid = "S-1-5-21-9" },
            "session" => displayed with { SessionId = 4 },
            "operation" => displayed with { Operation = "OP-03" },
            "operationId" => displayed with { OperationId = "operation-8" },
            "nonce" => displayed with { Nonce = new string('f', 64) },
            "revision" => displayed with { ExpectedRevision = 8 },
            "digest" => displayed with { PlanDigest = new string('b', 64) },
            "pipe" => displayed with { PipeInstanceId = "pipe-instance-8" },
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            DisplayedConsentBinding = displayed,
            ApplyConsentBinding = applied
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.BindingMismatch, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pipe-☃")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Execute_refuses_invalid_pipe_identity_even_when_display_and_apply_match(string pipeInstanceId)
    {
        ConsentBinding invalidPipe = TestScenario.ConsentBinding() with { PipeInstanceId = pipeInstanceId };
        AuthorizationScenario scenario = TestScenario.Valid() with
        {
            PipeInstanceId = pipeInstanceId,
            DisplayedConsentBinding = invalidPipe,
            ApplyConsentBinding = invalidPipe
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(RefusalCode.BindingMismatch, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Theory]
    [InlineData("pipe-launch", RefusalCode.RequestTimedOut, "Canceled", true)]
    [InlineData("dashboard-evidence", RefusalCode.RequestTimedOut, "Canceled", true)]
    [InlineData("helper-evidence", RefusalCode.RequestTimedOut, "Canceled", true)]
    [InlineData("authentication-total", RefusalCode.RequestTimedOut, "Canceled", true)]
    [InlineData("request-length", RefusalCode.RequestTimedOut, "Canceled", true)]
    [InlineData("request-body", RefusalCode.RequestTimedOut, "Canceled", true)]
    [InlineData("reobservation", RefusalCode.RequestTimedOut, "Canceled", true)]
    [InlineData("confirmation", RefusalCode.ConsentExpired, "Canceled", true)]
    [InlineData("terminal-write", RefusalCode.Crashed, "Unknown", false)]
    [InlineData("absolute-lifetime", RefusalCode.Crashed, "Unknown", false)]
    public void Execute_enforces_every_independent_helper_phase(
        string phase,
        RefusalCode expected,
        string expectedStatus,
        bool expectedDelivered)
    {
        HelperPhaseTimeline valid = TestScenario.Timeline();
        HelperPhaseTimeline breached = phase switch
        {
            "pipe-launch" => valid with { HelperLaunchedAt = 1_011 },
            "dashboard-evidence" => valid with { DashboardEvidenceCompletedAt = 1_011 },
            "helper-evidence" => valid with { HelperEvidenceCompletedAt = 1_016 },
            "authentication-total" => valid with
            {
                DashboardEvidenceStartedAt = 1_010,
                DashboardEvidenceCompletedAt = 1_015,
                HelperEvidenceStartedAt = 1_016,
                HelperEvidenceCompletedAt = 1_021,
                MutualAuthenticationCompletedAt = 1_021,
                RequestLengthReadStartedAt = 1_021,
                RequestLengthReadCompletedAt = 1_025,
                RequestBodyReadStartedAt = 1_025
            },
            "request-length" => valid with { RequestLengthReadCompletedAt = 1_021 },
            "request-body" => valid with { RequestBodyReadCompletedAt = 1_031 },
            "reobservation" => valid with { ReobservationCompletedAt = 1_056 },
            "confirmation" => valid with { ApplyAt = 1_171, TerminalWriteStartedAt = 1_171, TerminalWriteCompletedAt = 1_176 },
            "terminal-write" => valid with { TerminalWriteCompletedAt = 1_066 },
            "absolute-lifetime" => valid with { TerminalWriteStartedAt = 1_181, TerminalWriteCompletedAt = 1_186 },
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
        AuthorizationScenario scenario = TestScenario.Valid() with { Timeline = breached };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(expected, result.Code);
        Assert.Equal(expectedStatus, result.Status.ToString());
        Assert.Equal(expectedDelivered, result.WasDelivered);
        Assert.Equal(0, result.AuthorizedOperationCount);
    }

    [Theory]
    [InlineData("cancellation")]
    [InlineData("stale-revision")]
    [InlineData("malformed-request")]
    public void Early_post_auth_outcomes_are_unconfirmed_when_terminal_write_misses_absolute_lifetime(string outcome)
    {
        HelperPhaseTimeline lostTerminal = TestScenario.Timeline() with
        {
            TerminalWriteStartedAt = 2_000,
            TerminalWriteCompletedAt = 2_005
        };
        AuthorizationScenario valid = TestScenario.Valid() with { Timeline = lostTerminal };
        AuthorizationScenario scenario = outcome switch
        {
            "cancellation" => valid with { IsCancelled = true },
            "stale-revision" => valid with { CurrentRevision = 8 },
            "malformed-request" => valid with { EncodedRequest = Encoding.UTF8.GetBytes("{}") },
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

        TerminalResult result = new AuthorizationBoundary().Execute(scenario);

        Assert.Equal(TerminalStatus.Unknown, result.Status);
        Assert.Equal(RefusalCode.Crashed, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
        Assert.False(result.WasDelivered);
    }

    [Theory]
    [InlineData("write-duration")]
    [InlineData("absolute-lifetime")]
    public void Unconfirmed_terminal_write_never_surfaces_as_a_delivered_timeout(string failure)
    {
        HelperPhaseTimeline valid = TestScenario.Timeline();
        HelperPhaseTimeline lostTerminal = failure switch
        {
            "write-duration" => valid with { TerminalWriteCompletedAt = 1_066 },
            "absolute-lifetime" => valid with { TerminalWriteStartedAt = 1_181, TerminalWriteCompletedAt = 1_186 },
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };

        TerminalResult result = new AuthorizationBoundary().Execute(TestScenario.Valid() with { Timeline = lostTerminal });

        Assert.Equal(TerminalStatus.Unknown, result.Status);
        Assert.Equal(RefusalCode.Crashed, result.Code);
        Assert.Equal(0, result.AuthorizedOperationCount);
        Assert.False(result.WasDelivered);
    }

    [Fact]
    public void Expired_confirmation_cannot_be_emitted_before_the_timeout_is_observed()
    {
        HelperPhaseTimeline prematureTerminal = TestScenario.Timeline() with
        {
            ApplyAt = 1_171,
            TerminalWriteStartedAt = 1_050,
            TerminalWriteCompletedAt = 1_055
        };

        TerminalResult result = new AuthorizationBoundary().Execute(TestScenario.Valid() with { Timeline = prematureTerminal });

        Assert.Equal(TerminalStatus.Unknown, result.Status);
        Assert.Equal(RefusalCode.Crashed, result.Code);
        Assert.False(result.WasDelivered);
    }

    public static TheoryData<string> MalformedRequests() => new()
    {
        TestScenario.RequestJson().Replace("\"messageType\":\"authorize\",", "\"protocolVersion\":\"balls-helper/1\","),
        TestScenario.RequestJson().Replace("\"operationId\":\"operation-7\"", "\"operationId\":null"),
        TestScenario.RequestJson().Replace("\"nonce\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"", "\"nonce\":null"),
        TestScenario.RequestJson().Replace("\"sessionId\":3", "\"sessionId\":\"3\"")
    };

    public static TheoryData<string> MalformedTerminals()
    {
        string valid = ValidTerminalJson();
        return new()
        {
            valid.Replace("\"status\":\"completed\"", "\"status\":0"),
            valid.Replace("\"status\":\"completed\"", "\"status\":999"),
            valid.Replace("\"authorizedOperationCount\":1", "\"authorizedOperationCount\":-1"),
            valid.Replace("\"authorizedOperationCount\":1", "\"authorizedOperationCount\":2"),
            valid.Replace("\"code\":\"none\"", "\"code\":\"replay\""),
            valid.Replace("\"systemMutationCount\":0", "\"systemMutationCount\":-1"),
            valid.Replace("\"messageType\":\"terminal\",", "\"protocolVersion\":\"balls-helper/1\",")
        };
    }

    public static TheoryData<TerminalResult> InvalidCrossStatusTerminals() => new()
    {
        new(TerminalStatus.Completed, RefusalCode.Replay, 1, 0, "The isolated authorization model completed one operation; no system mutation adapter exists."),
        new(TerminalStatus.Canceled, RefusalCode.None, 0, 0, "The authorization was canceled before completion; no mutation occurred."),
        new(TerminalStatus.RepairNeeded, RefusalCode.None, 1, 0, "The protected transaction requires exact reconciliation before new work."),
        TerminalResult.Refused(RefusalCode.ConsentExpired),
        new(TerminalStatus.Unknown, RefusalCode.Replay, 0, 0, "The helper ended before a terminal outcome could be confirmed; no success is claimed.")
    };

    public static TheoryData<string> InvalidCrossStatusTerminalJson() => new()
    {
        TerminalJson("completed", "replay", 1, "The isolated authorization model completed one operation; no system mutation adapter exists."),
        TerminalJson("canceled", "none", 0, "The authorization was canceled before completion; no mutation occurred."),
        TerminalJson("repairNeeded", "none", 1, "The protected transaction requires exact reconciliation before new work."),
        TerminalJson("refused", "consentExpired", 0, "The request was refused with product code ConsentExpired; no private evidence is included."),
        TerminalJson("unknown", "replay", 0, "The helper ended before a terminal outcome could be confirmed; no success is claimed.")
    };

    private static string ValidTerminalJson() =>
        "{\"protocolVersion\":\"balls-helper/1\",\"messageType\":\"terminal\",\"operationId\":\"operation-7\",\"nonce\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"status\":\"completed\",\"code\":\"none\",\"publicMessage\":\"The isolated authorization model completed one operation; no system mutation adapter exists.\",\"authorizedOperationCount\":1,\"systemMutationCount\":0}";

    private static string TerminalJson(string status, string code, int authorizedCount, string message) =>
        $"{{\"protocolVersion\":\"balls-helper/1\",\"messageType\":\"terminal\",\"operationId\":\"operation-7\",\"nonce\":\"{Nonce}\",\"status\":\"{status}\",\"code\":\"{code}\",\"publicMessage\":\"{message}\",\"authorizedOperationCount\":{authorizedCount},\"systemMutationCount\":0}}";

    private static async Task<int> CountConcurrentWinners(Func<bool> attempt)
    {
        const int participantCount = 32;
        using Barrier barrier = new(participantCount);
        ConcurrentBag<bool> results = [];
        Task[] tasks = Enumerable.Range(0, participantCount)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    barrier.SignalAndWait();
                    results.Add(attempt());
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        await Task.WhenAll(tasks);
        return results.Count(result => result);
    }
}
