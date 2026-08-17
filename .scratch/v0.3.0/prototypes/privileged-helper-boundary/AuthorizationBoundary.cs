using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BallsServer.SecurityPrototype;

public enum RefusalCode
{
    None,
    MalformedMessage,
    MessageTooLarge,
    UnknownField,
    UnsupportedProtocol,
    BindingMismatch,
    StaleRevision,
    RemotePeer,
    UnexpectedProcess,
    WrongUser,
    WrongSession,
    WrongIntegrity,
    UntrustedImage,
    UntrustedHash,
    UntrustedSigner,
    DeadPeer,
    Replay,
    PlanChanged,
    Cancelled,
    RequestTimedOut,
    ConsentExpired,
    ConsentConsumed,
    HelperConsentRequired,
    Crashed
}

public enum TerminalStatus
{
    Canceled,
    Completed,
    RepairNeeded,
    Refused,
    Unknown
}

public enum IntegrityLevel
{
    Medium,
    High
}

public enum PeerRole
{
    Dashboard,
    Helper
}

public enum CrashPoint
{
    None,
    BeforeJournal,
    AfterJournal
}

public sealed record ProcessInstance(int ProcessId, long StartedAtTicks);

public sealed record PeerEvidence(
    PeerRole Role,
    int ProcessId,
    long StartedAtTicks,
    string UserSid,
    int SessionId,
    IntegrityLevel Integrity,
    bool IsElevated,
    string ImagePath,
    string ProductVersion,
    string Sha256,
    string? SignerThumbprint,
    bool IsAdministratorProtectedPath,
    bool IsAuthenticodeTrusted,
    bool IsRemote,
    bool IsAliveAfterVerification = true);

public sealed record HelperRequest(
    string ProtocolVersion,
    string MessageType,
    string Operation,
    string OperationId,
    string Nonce,
    long ExpectedRevision,
    string ProvisionalPlanDigest,
    string InitiatingUserSid,
    int SessionId,
    long DeadlineMonotonic);

public sealed record ProtocolDecodeResult(HelperRequest? Request, RefusalCode RefusalCode)
{
    public bool IsAccepted => Request is not null && RefusalCode == RefusalCode.None;
}

public sealed record TerminalDecodeResult(TerminalResult? Result, RefusalCode RefusalCode)
{
    public bool IsAccepted => Result is not null && RefusalCode == RefusalCode.None;
}

public static class StrictProtocolCodec
{
    public const int MaximumRequestBytes = 16_384;

    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "protocolVersion",
        "messageType",
        "operation",
        "operationId",
        "nonce",
        "expectedRevision",
        "provisionalPlanDigest",
        "initiatingUserSid",
        "sessionId",
        "deadlineMonotonic"
    };

    private static readonly HashSet<string> AllowedTerminalFields = new(StringComparer.Ordinal)
    {
        "protocolVersion",
        "messageType",
        "operationId",
        "nonce",
        "status",
        "code",
        "publicMessage",
        "authorizedOperationCount",
        "systemMutationCount"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static StrictProtocolCodec()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public static byte[] EncodeRequest(HelperRequest request) => JsonSerializer.SerializeToUtf8Bytes(request, SerializerOptions);

    public static ProtocolDecodeResult DecodeRequest(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length == 0)
        {
            return new(null, RefusalCode.MalformedMessage);
        }

        if (encoded.Length > MaximumRequestBytes)
        {
            return new(null, RefusalCode.MessageTooLarge);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(encoded.ToArray());
            JsonElement root = document.RootElement;
            RefusalCode fieldValidation = ValidateFields(root, AllowedFields);
            if (fieldValidation != RefusalCode.None)
            {
                return new(null, fieldValidation);
            }

            if (!TryReadRequiredString(root, "protocolVersion", out string protocolVersion) ||
                !TryReadRequiredString(root, "messageType", out string messageType) ||
                !TryReadRequiredString(root, "operation", out string operation) ||
                !TryReadRequiredString(root, "operationId", out string operationId) ||
                !TryReadRequiredString(root, "nonce", out string nonce) ||
                !TryReadRequiredInt64(root, "expectedRevision", out long expectedRevision) ||
                !TryReadRequiredString(root, "provisionalPlanDigest", out string provisionalPlanDigest) ||
                !TryReadRequiredString(root, "initiatingUserSid", out string initiatingUserSid) ||
                !TryReadRequiredInt32(root, "sessionId", out int sessionId) ||
                !TryReadRequiredInt64(root, "deadlineMonotonic", out long deadlineMonotonic))
            {
                return new(null, RefusalCode.MalformedMessage);
            }

            HelperRequest request = new(
                protocolVersion,
                messageType,
                operation,
                operationId,
                nonce,
                expectedRevision,
                provisionalPlanDigest,
                initiatingUserSid,
                sessionId,
                deadlineMonotonic);
            if (request.ProtocolVersion != "balls-helper/1" ||
                request.MessageType != "authorize" ||
                request.Operation != "OP-02")
            {
                return new(null, RefusalCode.UnsupportedProtocol);
            }

            if (!IsBoundedAsciiIdentifier(request.OperationId) ||
                !IsLowerHex(request.Nonce, 64) ||
                !IsLowerHex(request.ProvisionalPlanDigest, 64) ||
                request.InitiatingUserSid.Length is < 1 or > 184 ||
                request.SessionId < 0 ||
                request.ExpectedRevision < 0 ||
                request.DeadlineMonotonic < 0)
            {
                return new(null, RefusalCode.MalformedMessage);
            }

            return new(request, RefusalCode.None);
        }
        catch (JsonException)
        {
            return new(null, RefusalCode.MalformedMessage);
        }
    }

    public static byte[] EncodeTerminal(string operationId, string nonce, TerminalResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(result);
        if (!IsBoundedAsciiIdentifier(operationId))
        {
            throw new ArgumentException("The terminal operation ID must be 1..64 ASCII characters.", nameof(operationId));
        }

        if (!IsLowerHex(nonce, 64))
        {
            throw new ArgumentException("The terminal nonce must be 32 bytes encoded as lowercase hexadecimal.", nameof(nonce));
        }

        if (!result.HasValidExternalContract())
        {
            throw new ArgumentException("The terminal result violates its closed status, code, count, or diagnostic contract.", nameof(result));
        }

        HelperTerminalEnvelope envelope = new(
            "balls-helper/1",
            "terminal",
            operationId,
            nonce,
            result.Status,
            result.Code,
            result.PublicMessage,
            result.AuthorizedOperationCount,
            result.SystemMutationCount);
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (encoded.Length > MaximumRequestBytes)
        {
            throw new ArgumentException("The encoded terminal response exceeds the protocol limit.", nameof(result));
        }

        return encoded;
    }

    public static TerminalDecodeResult DecodeTerminal(ReadOnlySpan<byte> encoded, string expectedOperationId, string expectedNonce)
    {
        if (encoded.Length == 0)
        {
            return new(null, RefusalCode.MalformedMessage);
        }

        if (encoded.Length > MaximumRequestBytes)
        {
            return new(null, RefusalCode.MessageTooLarge);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(encoded.ToArray());
            JsonElement root = document.RootElement;
            RefusalCode fieldValidation = ValidateFields(root, AllowedTerminalFields);
            if (fieldValidation != RefusalCode.None)
            {
                return new(null, fieldValidation);
            }

            if (!TryReadRequiredString(root, "protocolVersion", out string protocolVersion) ||
                !TryReadRequiredString(root, "messageType", out string messageType) ||
                !TryReadRequiredString(root, "operationId", out string operationId) ||
                !TryReadRequiredString(root, "nonce", out string nonce) ||
                !TryReadRequiredString(root, "status", out string statusText) ||
                !TryReadRequiredString(root, "code", out string codeText) ||
                !TryReadRequiredString(root, "publicMessage", out string publicMessage) ||
                !TryReadRequiredInt32(root, "authorizedOperationCount", out int authorizedOperationCount) ||
                !TryReadRequiredInt32(root, "systemMutationCount", out int systemMutationCount))
            {
                return new(null, RefusalCode.MalformedMessage);
            }

            if (protocolVersion != "balls-helper/1" || messageType != "terminal")
            {
                return new(null, RefusalCode.UnsupportedProtocol);
            }

            if (!TryParseWireEnum(statusText, out TerminalStatus status) ||
                !TryParseWireEnum(codeText, out RefusalCode code) ||
                !IsBoundedAsciiIdentifier(operationId) ||
                !IsLowerHex(nonce, 64))
            {
                return new(null, RefusalCode.MalformedMessage);
            }

            if (operationId != expectedOperationId || nonce != expectedNonce)
            {
                return new(null, RefusalCode.BindingMismatch);
            }

            TerminalResult result = new(status, code, authorizedOperationCount, systemMutationCount, publicMessage);
            if (!result.HasValidExternalContract())
            {
                return new(null, RefusalCode.MalformedMessage);
            }

            return new(result, RefusalCode.None);
        }
        catch (JsonException)
        {
            return new(null, RefusalCode.MalformedMessage);
        }
    }

    private static RefusalCode ValidateFields(JsonElement root, HashSet<string> allowedFields)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return RefusalCode.MalformedMessage;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                return RefusalCode.UnknownField;
            }

            if (!seen.Add(property.Name))
            {
                return RefusalCode.MalformedMessage;
            }
        }

        return seen.SetEquals(allowedFields) ? RefusalCode.None : RefusalCode.MalformedMessage;
    }

    private static bool TryReadRequiredString(JsonElement root, string name, out string value)
    {
        JsonElement element = root.GetProperty(name);
        value = element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
        return element.ValueKind == JsonValueKind.String && value.Length > 0;
    }

    private static bool TryReadRequiredInt64(JsonElement root, string name, out long value)
    {
        JsonElement element = root.GetProperty(name);
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private static bool TryReadRequiredInt32(JsonElement root, string name, out int value)
    {
        JsonElement element = root.GetProperty(name);
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static bool TryParseWireEnum<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (JsonNamingPolicy.CamelCase.ConvertName(candidate.ToString()) == text)
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsBoundedAsciiIdentifier(string value) =>
        value.Length is >= 1 and <= 64 && value.All(character => character is >= (char)0x21 and <= (char)0x7e);

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record HelperTerminalEnvelope(
        string ProtocolVersion,
        string MessageType,
        string OperationId,
        string Nonce,
        TerminalStatus Status,
        RefusalCode Code,
        string PublicMessage,
        int AuthorizedOperationCount,
        int SystemMutationCount);
}

public sealed record AuthorizationScenario(
    byte[] EncodedRequest,
    PeerEvidence Dashboard,
    PeerEvidence Helper,
    ProcessInstance ExpectedDashboard,
    ProcessInstance ExpectedHelper,
    string ExpectedUserSid,
    int ExpectedSessionId,
    long CurrentRevision,
    string AuthoritativePlanDigest,
    bool IsCancelled,
    bool HelperApplyDecision,
    bool DashboardClaimedApproval,
    CrashPoint CrashPoint)
{
    public string PipeInstanceId { get; init; } = string.Empty;

    public HelperPhaseTimeline? Timeline { get; init; }

    public ConsentBinding? DisplayedConsentBinding { get; init; }

    public ConsentBinding? ApplyConsentBinding { get; init; }
}

public sealed record HelperPhaseTimeline(
    long PipeCreatedAt,
    long HelperLaunchedAt,
    long DashboardEvidenceStartedAt,
    long DashboardEvidenceCompletedAt,
    long HelperEvidenceStartedAt,
    long HelperEvidenceCompletedAt,
    long MutualAuthenticationCompletedAt,
    long RequestLengthReadStartedAt,
    long RequestLengthReadCompletedAt,
    long RequestBodyReadStartedAt,
    long RequestBodyReadCompletedAt,
    long ReobservationStartedAt,
    long ReobservationCompletedAt,
    long PlanDisplayedAt,
    long ApplyAt,
    long TerminalWriteStartedAt,
    long TerminalWriteCompletedAt)
{
    public const long PipeLaunchBudgetSeconds = 10;
    public const long EvidenceQueryBudgetSeconds = 5;
    public const long MutualAuthenticationBudgetSeconds = 15;
    public const long RequestLengthReadBudgetSeconds = 5;
    public const long RequestBodyReadBudgetSeconds = 5;
    public const long ReobservationBudgetSeconds = 30;
    public const long ConfirmationBudgetSeconds = 120;
    public const long TerminalWriteBudgetSeconds = 5;
    public const long AbsoluteHelperLifetimeSeconds = 180;

    public bool TryGetHelperDeadline(out long deadline) =>
        TryAddBounded(HelperLaunchedAt, AbsoluteHelperLifetimeSeconds, out deadline);

    public RefusalCode ValidateBeforeReobservation()
    {
        if (!IsWithinPhase(PipeCreatedAt, HelperLaunchedAt, PipeLaunchBudgetSeconds) ||
            !StartsAfter(HelperLaunchedAt, DashboardEvidenceStartedAt) ||
            !IsWithinPhase(DashboardEvidenceStartedAt, DashboardEvidenceCompletedAt, EvidenceQueryBudgetSeconds) ||
            !StartsAfter(DashboardEvidenceCompletedAt, HelperEvidenceStartedAt) ||
            !IsWithinPhase(HelperEvidenceStartedAt, HelperEvidenceCompletedAt, EvidenceQueryBudgetSeconds) ||
            MutualAuthenticationCompletedAt < HelperEvidenceCompletedAt ||
            !IsWithinPhase(HelperLaunchedAt, MutualAuthenticationCompletedAt, MutualAuthenticationBudgetSeconds) ||
            !StartsAfter(MutualAuthenticationCompletedAt, RequestLengthReadStartedAt) ||
            !IsWithinPhase(RequestLengthReadStartedAt, RequestLengthReadCompletedAt, RequestLengthReadBudgetSeconds) ||
            !StartsAfter(RequestLengthReadCompletedAt, RequestBodyReadStartedAt) ||
            !IsWithinPhase(RequestBodyReadStartedAt, RequestBodyReadCompletedAt, RequestBodyReadBudgetSeconds) ||
            ReobservationStartedAt < RequestBodyReadCompletedAt)
        {
            return RefusalCode.RequestTimedOut;
        }

        return RefusalCode.None;
    }

    public RefusalCode ValidateAfterReobservation()
    {
        if (!IsWithinPhase(ReobservationStartedAt, ReobservationCompletedAt, ReobservationBudgetSeconds) ||
            PlanDisplayedAt < ReobservationCompletedAt)
        {
            return RefusalCode.RequestTimedOut;
        }

        if (!IsWithinPhase(PlanDisplayedAt, ApplyAt, ConfirmationBudgetSeconds))
        {
            return RefusalCode.ConsentExpired;
        }

        return RefusalCode.None;
    }

    public RefusalCode ValidateTerminalEmission(long outcomeReadyAt)
    {
        if (TerminalWriteStartedAt < outcomeReadyAt ||
            !IsWithinPhase(TerminalWriteStartedAt, TerminalWriteCompletedAt, TerminalWriteBudgetSeconds) ||
            !TryGetHelperDeadline(out long deadline) ||
            TerminalWriteCompletedAt > deadline)
        {
            return RefusalCode.RequestTimedOut;
        }

        return RefusalCode.None;
    }

    private static bool StartsAfter(long priorCompletedAt, long nextStartedAt) =>
        priorCompletedAt >= 0 && nextStartedAt >= priorCompletedAt;

    private static bool IsWithinPhase(long startedAt, long completedAt, long budgetSeconds) =>
        startedAt >= 0 && completedAt >= startedAt && TryAddBounded(startedAt, budgetSeconds, out long deadline) && completedAt <= deadline;

    private static bool TryAddBounded(long value, long increment, out long result)
    {
        if (value < 0 || value > long.MaxValue - increment)
        {
            result = 0;
            return false;
        }

        result = value + increment;
        return true;
    }
}

public sealed record TerminalResult(
    TerminalStatus Status,
    RefusalCode Code,
    int AuthorizedOperationCount,
    int SystemMutationCount,
    string PublicMessage)
{
    public bool WasDelivered { get; init; } = true;

    public bool IsTerminal => WasDelivered && Enum.IsDefined(Status);

    public static TerminalResult Completed() => new(
        TerminalStatus.Completed,
        RefusalCode.None,
        1,
        0,
        DiagnosticFor(TerminalStatus.Completed, RefusalCode.None));

    public static TerminalResult Refused(RefusalCode code) => new(
        TerminalStatus.Refused,
        code,
        0,
        0,
        DiagnosticFor(TerminalStatus.Refused, code));

    public static TerminalResult Canceled(RefusalCode code) => new(
        TerminalStatus.Canceled,
        code,
        0,
        0,
        DiagnosticFor(TerminalStatus.Canceled, code));

    public static TerminalResult Unknown(RefusalCode code) => new(
        TerminalStatus.Unknown,
        code,
        0,
        0,
        DiagnosticFor(TerminalStatus.Unknown, code));

    public static TerminalResult RepairNeeded(RefusalCode code) => new(
        TerminalStatus.RepairNeeded,
        code,
        1,
        0,
        DiagnosticFor(TerminalStatus.RepairNeeded, code));

    public static TerminalResult Unconfirmed() => Unknown(RefusalCode.Crashed) with { WasDelivered = false };

    internal bool HasValidExternalContract()
    {
        if (!WasDelivered || !Enum.IsDefined(Status) || !Enum.IsDefined(Code) || SystemMutationCount != 0 || AuthorizedOperationCount is < 0 or > 1)
        {
            return false;
        }

        bool validPair = (Status, Code, AuthorizedOperationCount, SystemMutationCount) switch
        {
            (TerminalStatus.Completed, RefusalCode.None, 1, 0) => true,
            (TerminalStatus.Canceled, RefusalCode.Cancelled or RefusalCode.RequestTimedOut or RefusalCode.ConsentExpired, 0, 0) => true,
            (TerminalStatus.RepairNeeded, RefusalCode.Crashed, 1, 0) => true,
            (TerminalStatus.Unknown, RefusalCode.Crashed, 0, 0) => true,
            (TerminalStatus.Refused,
                RefusalCode.MalformedMessage or
                RefusalCode.MessageTooLarge or
                RefusalCode.UnknownField or
                RefusalCode.UnsupportedProtocol or
                RefusalCode.BindingMismatch or
                RefusalCode.StaleRevision or
                RefusalCode.RemotePeer or
                RefusalCode.UnexpectedProcess or
                RefusalCode.WrongUser or
                RefusalCode.WrongSession or
                RefusalCode.WrongIntegrity or
                RefusalCode.UntrustedImage or
                RefusalCode.UntrustedHash or
                RefusalCode.UntrustedSigner or
                RefusalCode.DeadPeer or
                RefusalCode.Replay or
                RefusalCode.PlanChanged or
                RefusalCode.ConsentConsumed or
                RefusalCode.HelperConsentRequired,
                0,
                0) => true,
            _ => false
        };
        return validPair && PublicMessage == DiagnosticFor(Status, Code);
    }

    private static string DiagnosticFor(TerminalStatus status, RefusalCode code) => status switch
    {
        TerminalStatus.Completed => "The isolated authorization model completed one operation; no system mutation adapter exists.",
        TerminalStatus.Canceled => code == RefusalCode.RequestTimedOut
            ? "The authorization window ended before completion; no mutation occurred."
            : "The authorization was canceled before completion; no mutation occurred.",
        TerminalStatus.RepairNeeded => "The protected transaction requires exact reconciliation before new work.",
        TerminalStatus.Refused => $"The request was refused with product code {code}; no private evidence is included.",
        TerminalStatus.Unknown => "The helper ended before a terminal outcome could be confirmed; no success is claimed.",
        _ => string.Empty
    };
}

public sealed class AuthorizationBoundary
{
    private const string ExpectedOperationId = "operation-7";
    private const string ExpectedNonce = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string DashboardImage = @"C:\Program Files\Balls Server\BallsServer.exe";
    private const string HelperImage = @"C:\Program Files\Balls Server\BallsServer.Helper.exe";
    private const string DashboardHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string HelperHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string TrustedSigner = "AA11";

    private readonly ConcurrentDictionary<string, byte> consumedNonces = new(StringComparer.Ordinal);

    public TerminalResult Execute(AuthorizationScenario scenario)
    {
        RefusalCode peerFailure = AuthenticatePeer(
            scenario.Dashboard,
            PeerRole.Dashboard,
            scenario.ExpectedDashboard,
            scenario.ExpectedUserSid,
            scenario.ExpectedSessionId,
            IntegrityLevel.Medium,
            false,
            DashboardImage,
            DashboardHash);
        if (peerFailure != RefusalCode.None)
        {
            return TerminalResult.Refused(peerFailure) with { WasDelivered = false };
        }

        peerFailure = AuthenticatePeer(
            scenario.Helper,
            PeerRole.Helper,
            scenario.ExpectedHelper,
            scenario.ExpectedUserSid,
            scenario.ExpectedSessionId,
            IntegrityLevel.High,
            true,
            HelperImage,
            HelperHash);
        if (peerFailure != RefusalCode.None)
        {
            return TerminalResult.Refused(peerFailure) with { WasDelivered = false };
        }

        TerminalEmissionGate emissionGate = new(scenario.Timeline);
        TerminalResult Emit(TerminalResult candidate, long outcomeReadyAt) =>
            emissionGate.Expose(candidate, outcomeReadyAt);

        if (scenario.Timeline is null ||
            !IsBoundedPipeInstanceId(scenario.PipeInstanceId) ||
            scenario.DisplayedConsentBinding is null ||
            scenario.ApplyConsentBinding is null)
        {
            return Emit(
                TerminalResult.Refused(RefusalCode.BindingMismatch),
                scenario.Timeline?.MutualAuthenticationCompletedAt ?? 0);
        }

        HelperPhaseTimeline timeline = scenario.Timeline;
        RefusalCode phaseFailure = timeline.ValidateBeforeReobservation();
        if (phaseFailure != RefusalCode.None)
        {
            return Emit(TerminalResult.Canceled(phaseFailure), timeline.RequestBodyReadCompletedAt);
        }

        ProtocolDecodeResult decoded = StrictProtocolCodec.DecodeRequest(scenario.EncodedRequest);
        if (!decoded.IsAccepted)
        {
            return Emit(TerminalResult.Refused(decoded.RefusalCode), timeline.RequestBodyReadCompletedAt);
        }

        HelperRequest request = decoded.Request!;
        if (request.OperationId != ExpectedOperationId || request.Nonce != ExpectedNonce)
        {
            return Emit(TerminalResult.Refused(RefusalCode.BindingMismatch), timeline.RequestBodyReadCompletedAt);
        }

        if (request.InitiatingUserSid != scenario.ExpectedUserSid || request.SessionId != scenario.ExpectedSessionId)
        {
            return Emit(TerminalResult.Refused(RefusalCode.BindingMismatch), timeline.RequestBodyReadCompletedAt);
        }

        if (!timeline.TryGetHelperDeadline(out long helperDeadline) ||
            request.DeadlineMonotonic != helperDeadline)
        {
            return Emit(TerminalResult.Refused(RefusalCode.BindingMismatch), timeline.RequestBodyReadCompletedAt);
        }

        if (!consumedNonces.TryAdd(request.Nonce, 0))
        {
            return Emit(TerminalResult.Refused(RefusalCode.Replay), timeline.ReobservationStartedAt);
        }

        if (request.ExpectedRevision != scenario.CurrentRevision)
        {
            return Emit(TerminalResult.Refused(RefusalCode.StaleRevision), timeline.ReobservationStartedAt);
        }

        if (scenario.IsCancelled)
        {
            return Emit(TerminalResult.Canceled(RefusalCode.Cancelled), timeline.ReobservationStartedAt);
        }

        phaseFailure = timeline.ValidateAfterReobservation();
        if (phaseFailure != RefusalCode.None)
        {
            long outcomeReadyAt = phaseFailure == RefusalCode.ConsentExpired
                ? timeline.ApplyAt
                : timeline.ReobservationCompletedAt;
            return Emit(TerminalResult.Canceled(phaseFailure), outcomeReadyAt);
        }

        if (!string.Equals(request.ProvisionalPlanDigest, scenario.AuthoritativePlanDigest, StringComparison.Ordinal))
        {
            return Emit(TerminalResult.Refused(RefusalCode.PlanChanged), timeline.ReobservationCompletedAt);
        }

        ConsentBinding authoritativeDisplayBinding = new(
            scenario.ExpectedHelper,
            scenario.ExpectedUserSid,
            scenario.ExpectedSessionId,
            request.Operation,
            request.OperationId,
            request.Nonce,
            request.ExpectedRevision,
            scenario.AuthoritativePlanDigest,
            scenario.PipeInstanceId);
        if (scenario.DisplayedConsentBinding != authoritativeDisplayBinding)
        {
            return Emit(TerminalResult.Refused(RefusalCode.BindingMismatch), timeline.PlanDisplayedAt);
        }

        if (scenario.CrashPoint == CrashPoint.BeforeJournal)
        {
            return Emit(TerminalResult.Unconfirmed(), timeline.PlanDisplayedAt);
        }

        if (!scenario.HelperApplyDecision)
        {
            return Emit(TerminalResult.Refused(RefusalCode.HelperConsentRequired), timeline.ApplyAt);
        }

        ConsentLease lease = new(scenario.DisplayedConsentBinding, timeline.PlanDisplayedAt);
        ConsentConsumeResult consent = lease.TryConsume(scenario.ApplyConsentBinding, timeline.ApplyAt);
        if (!consent.IsAuthorized)
        {
            TerminalResult refusedConsent = consent.Code == RefusalCode.ConsentExpired
                ? TerminalResult.Canceled(consent.Code)
                : TerminalResult.Refused(consent.Code);
            return Emit(refusedConsent, timeline.ApplyAt);
        }

        if (scenario.CrashPoint == CrashPoint.AfterJournal)
        {
            return Emit(TerminalResult.RepairNeeded(RefusalCode.Crashed), timeline.ApplyAt);
        }

        return Emit(TerminalResult.Completed(), timeline.ApplyAt);
    }

    private static bool IsBoundedPipeInstanceId(string value) =>
        value.Length is >= 1 and <= 64 && value.All(character => character is >= (char)0x21 and <= (char)0x7e);

    private static RefusalCode AuthenticatePeer(
        PeerEvidence peer,
        PeerRole expectedRole,
        ProcessInstance expectedProcess,
        string expectedUserSid,
        int expectedSessionId,
        IntegrityLevel expectedIntegrity,
        bool expectedElevation,
        string expectedImage,
        string expectedHash)
    {
        if (peer.IsRemote)
        {
            return RefusalCode.RemotePeer;
        }

        if (peer.Role != expectedRole || peer.ProcessId != expectedProcess.ProcessId || peer.StartedAtTicks != expectedProcess.StartedAtTicks)
        {
            return RefusalCode.UnexpectedProcess;
        }

        if (!string.Equals(peer.UserSid, expectedUserSid, StringComparison.Ordinal))
        {
            return RefusalCode.WrongUser;
        }

        if (peer.SessionId != expectedSessionId)
        {
            return RefusalCode.WrongSession;
        }

        if (peer.Integrity != expectedIntegrity || peer.IsElevated != expectedElevation)
        {
            return RefusalCode.WrongIntegrity;
        }

        if (!peer.IsAdministratorProtectedPath || !string.Equals(peer.ImagePath, expectedImage, StringComparison.OrdinalIgnoreCase))
        {
            return RefusalCode.UntrustedImage;
        }

        if (!string.Equals(peer.ProductVersion, "0.4.0.0", StringComparison.Ordinal) || !string.Equals(peer.Sha256, expectedHash, StringComparison.Ordinal))
        {
            return RefusalCode.UntrustedHash;
        }

        if (!peer.IsAuthenticodeTrusted || !string.Equals(peer.SignerThumbprint, TrustedSigner, StringComparison.Ordinal))
        {
            return RefusalCode.UntrustedSigner;
        }

        return peer.IsAliveAfterVerification ? RefusalCode.None : RefusalCode.DeadPeer;
    }
}

public sealed record ConsentBinding(
    ProcessInstance HelperInstance,
    string UserSid,
    int SessionId,
    string Operation,
    string OperationId,
    string Nonce,
    long ExpectedRevision,
    string PlanDigest,
    string PipeInstanceId);

public sealed record ConsentConsumeResult(bool IsAuthorized, RefusalCode Code);

public sealed class ConsentLease(ConsentBinding expected, long createdAt)
{
    public const long LifetimeSeconds = 120;

    private int consumed;

    public ConsentConsumeResult TryConsume(ConsentBinding actual, long monotonicNow)
    {
        if (actual != expected)
        {
            return new(false, RefusalCode.BindingMismatch);
        }

        if (monotonicNow > createdAt + LifetimeSeconds)
        {
            return new(false, RefusalCode.ConsentExpired);
        }

        return Interlocked.CompareExchange(ref consumed, 1, 0) == 0
            ? new(true, RefusalCode.None)
            : new(false, RefusalCode.ConsentConsumed);
    }
}

public sealed class TerminalResponseGate
{
    private int written;

    public int ResponseCount => Volatile.Read(ref written);

    public bool TryWrite(TerminalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Interlocked.CompareExchange(ref written, 1, 0) == 0;
    }
}

public sealed class TerminalEmissionGate(HelperPhaseTimeline? timeline)
{
    private int attemptClaimed;
    private readonly TerminalResponseGate responseGate = new();

    public int ResponseCount => responseGate.ResponseCount;

    public TerminalResult Expose(TerminalResult? candidate, long outcomeReadyAt)
    {
        if (Interlocked.CompareExchange(ref attemptClaimed, 1, 0) != 0 ||
            candidate is null ||
            !candidate.HasValidExternalContract() ||
            timeline is null ||
            timeline.ValidateTerminalEmission(outcomeReadyAt) != RefusalCode.None ||
            !responseGate.TryWrite(candidate))
        {
            return TerminalResult.Unconfirmed();
        }

        return candidate;
    }
}

public sealed class OneShotSecretResponse
{
    private byte[]? secret;

    public OneShotSecretResponse(byte[] secret)
    {
        this.secret = secret.ToArray();
    }

    private int takeCount;

    public int TakeCount => Volatile.Read(ref takeCount);

    public byte[]? TryTake()
    {
        byte[]? result = Interlocked.Exchange(ref secret, null);
        if (result is null)
        {
            return null;
        }

        Volatile.Write(ref takeCount, 1);
        return result;
    }
}

public sealed record DevelopmentGuard(bool DisposableVmMarker, bool KnownSnapshot, string TestNamespace);

public static class DevelopmentTrustPolicy
{
    public static bool AcceptUnsigned(PeerEvidence peer, DevelopmentGuard guard) =>
        !peer.IsAuthenticodeTrusted &&
        peer.SignerThumbprint is null &&
        guard.DisposableVmMarker &&
        guard.KnownSnapshot &&
        guard.TestNamespace.StartsWith("BallsServer.Test.", StringComparison.Ordinal) &&
        guard.TestNamespace.Length > "BallsServer.Test.".Length;
}
