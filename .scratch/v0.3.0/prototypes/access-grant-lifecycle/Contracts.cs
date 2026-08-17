using System.Security.Cryptography;
using System.Text;

namespace BallsServer.AccessGrantLifecycle;

public enum GrantState { PendingTransfer, Active, RepairNeeded, Revoked }
public enum TransferOutcome { Acknowledged, Lost, Hidden, TimedOut, Crashed, Unread, Failed }
public enum GrantResultCode { Completed, Refused, RepairNeeded }

public sealed record GrantRequest(
    string ProductHostId,
    string GrantId,
    string AccountSid,
    string ProductGroupSid,
    string SelectedEndpoint,
    string ShareName,
    string QualifiedSamAccount,
    DateTimeOffset GeneratedAt);

public sealed record AccessAccountObservation(
    bool IsAdministrator,
    IReadOnlyList<string> GroupSids,
    bool CanChangePassword,
    bool ExpiryObserved,
    bool LockoutObserved,
    bool NetworkLogonPolicyObserved);

public static class AccessAccountPolicy
{
    public static bool IsConformant(AccessAccountObservation? observation, string productGroupSid) => observation is not null &&
        !observation.IsAdministrator && !observation.CanChangePassword && observation.ExpiryObserved && observation.LockoutObserved &&
        observation.NetworkLogonPolicyObserved && observation.GroupSids.Count == 1 && observation.GroupSids[0] == productGroupSid;
}

public sealed record GrantResult(GrantResultCode Code, GrantState State, bool Succeeded, string PublicMessage)
{
    public static GrantResult Completed(GrantState state) => new(GrantResultCode.Completed, state, true, $"Grant is {state}.");
    public static GrantResult Refused(GrantState state) => new(GrantResultCode.Refused, state, false, "Grant action was refused.");
    public static GrantResult Repair(GrantState state) => new(GrantResultCode.RepairNeeded, state, false, "Grant needs recovery.");
}

public sealed class ActivationAuthorization
{
    private int consumed;

    internal ActivationAuthorization(string operationId, string grantId, long credentialRevision)
    {
        OperationId = operationId;
        GrantId = grantId;
        CredentialRevision = credentialRevision;
    }

    public string OperationId { get; }
    public string GrantId { get; }
    public long CredentialRevision { get; }

    internal static ActivationAuthorization IssueFromAuthoritativeHelper(string operationId, string grantId, long credentialRevision) =>
        new(operationId, grantId, credentialRevision);

    public bool TryConsume(string grantId, long credentialRevision) =>
        Interlocked.CompareExchange(ref consumed, 1, 0) == 0 &&
        !string.IsNullOrWhiteSpace(OperationId) && GrantId == grantId && CredentialRevision == credentialRevision;
}

public sealed class AccessGrant
{
    private AccessGrant(GrantRequest request)
    {
        Request = request;
        CredentialRevision = 1;
        State = GrantState.PendingTransfer;
        IssueFreshSecret("create");
    }

    public GrantRequest Request { get; }
    public long CredentialRevision { get; private set; }
    public GrantState State { get; private set; }
    public bool Disabled => State is GrantState.PendingTransfer or GrantState.RepairNeeded or GrantState.Revoked;
    public bool MembershipRemoved { get; private set; }
    public bool OptionalDeletionRequested { get; private set; }
    public long SecretGenerationCount { get; private set; }
    public int LastRandomByteCount { get; private set; }

    public static AccessGrant Create(GrantRequest request) => new(Validate(request));

    public GrantResult Activate(ActivationAuthorization? authorization)
    {
        if (State != GrantState.PendingTransfer || authorization is null || !authorization.TryConsume(Request.GrantId, CredentialRevision)) return GrantResult.Refused(State);
        State = GrantState.Active;
        return GrantResult.Completed(State);
    }

    public GrantResult Rotate(string explicitOwnerActionId)
    {
        if (string.IsNullOrWhiteSpace(explicitOwnerActionId) || State == GrantState.Revoked)
            return GrantResult.Refused(State);
        IssueFreshSecret(explicitOwnerActionId);
        CredentialRevision++;
        State = GrantState.PendingTransfer;
        return GrantResult.Completed(State);
    }

    public GrantResult RecordTransfer(TransferOutcome outcome)
    {
        if (State != GrantState.PendingTransfer) return GrantResult.Refused(State);
        if (outcome == TransferOutcome.Acknowledged) return GrantResult.Completed(State);
        State = GrantState.RepairNeeded;
        return GrantResult.Repair(State);
    }

    public GrantResult Revoke(bool optionalDelete)
    {
        if (State == GrantState.Revoked) return GrantResult.Completed(State);
        MembershipRemoved = true;
        OptionalDeletionRequested = optionalDelete;
        State = GrantState.Revoked;
        return GrantResult.Completed(State);
    }

    private static GrantRequest Validate(GrantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (new[] { request.ProductHostId, request.GrantId, request.AccountSid, request.ProductGroupSid, request.SelectedEndpoint, request.ShareName, request.QualifiedSamAccount }
            .Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Grant identity is incomplete.", nameof(request));
        return request;
    }

    private void IssueFreshSecret(string explicitOwnerActionId)
    {
        using SecretBuffer generated = SecretGenerator.GenerateForExplicitOwnerAction(explicitOwnerActionId);
        SecretGenerationCount++;
        LastRandomByteCount = generated.RandomByteCount;
    }
}

public static class SecretGenerator
{
    public const int MinimumRandomBytes = 32;

    public static SecretBuffer GenerateForExplicitOwnerAction(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) throw new ArgumentException("An explicit owner action is required.", nameof(actionId));
        byte[] bytes = RandomNumberGenerator.GetBytes(MinimumRandomBytes);
        try { return SecretBuffer.FromRandomBytes(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

public sealed class SecretBuffer : IDisposable
{
    private byte[]? bytes;

    private SecretBuffer(byte[] value, int randomByteCount = 0) { bytes = value; RandomByteCount = randomByteCount; }
    public bool IsCleared => bytes is null;
    public int Length => bytes?.Length ?? 0;
    public int RandomByteCount { get; }
    public static SecretBuffer FromUtf8(string value) => new(Encoding.UTF8.GetBytes(value));
    public static SecretBuffer FromRandomBytes(ReadOnlySpan<byte> randomBytes) => new(Encoding.UTF8.GetBytes(Convert.ToBase64String(randomBytes)), randomBytes.Length);
    public string RevealForTransientUse() => bytes is null ? throw new InvalidOperationException("Secret was cleared.") : Encoding.UTF8.GetString(bytes);
    public void Dispose()
    {
        if (bytes is null) return;
        CryptographicOperations.ZeroMemory(bytes);
        bytes = null;
    }
}

public sealed record SetupCode(
    int SchemaVersion,
    string ProductHostId,
    string GrantId,
    string ProductHostLabel,
    string SelectedEndpoint,
    string ShareName,
    string QualifiedSamAccount,
    long CredentialRevision,
    DateTimeOffset GeneratedAt,
    SecretBuffer Password) : IDisposable
{
    public const int CurrentSchemaVersion = 1;
    public void Dispose() => Password.Dispose();
    public bool HasOnlyMinimumFields => SchemaVersion == CurrentSchemaVersion &&
        new[] { ProductHostId, GrantId, ProductHostLabel, SelectedEndpoint, ShareName, QualifiedSamAccount }.All(value => !string.IsNullOrWhiteSpace(value)) &&
        CredentialRevision > 0;
}

public sealed record TransferBinding(string InitiatingUserSid, int SessionId, string PipeInstance, string Nonce, string OperationId, string GrantId, long Revision);
public sealed record SecretResponse(bool Delivered, SetupCode? SetupCode, string PublicStatus);

public sealed class OneReadSecretTransfer : IDisposable
{
    private readonly TransferBinding binding;
    private SetupCode? setupCode;
    public OneReadSecretTransfer(TransferBinding binding, SetupCode setupCode)
    {
        this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        this.setupCode = setupCode ?? throw new ArgumentNullException(nameof(setupCode));
        if (setupCode.GrantId != binding.GrantId || setupCode.CredentialRevision != binding.Revision)
            throw new ArgumentException("Setup code binding mismatch.", nameof(setupCode));
    }

    public SecretResponse Read(TransferBinding caller)
    {
        if (caller is null || caller != binding)
            return new(false, null, "Secret transfer refused.");
        SetupCode? response = Interlocked.Exchange(ref setupCode, null);
        return response is null ? new(false, null, "Secret transfer refused.") : new(true, response, "Secret transfer delivered.");
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref setupCode, null)?.Dispose();
    }
}

public sealed class TransientSetupCodeView : IDisposable
{
    private SetupCode? code;
    public TransientSetupCodeView(SetupCode code, string initiatingUserSid, int sessionId) { this.code = code; InitiatingUserSid = initiatingUserSid; SessionId = sessionId; }
    public string InitiatingUserSid { get; }
    public int SessionId { get; }
    public bool IsVisible => code is not null;
    public bool TryOpenWarningAcknowledged(bool warningAcknowledged) => warningAcknowledged && code is not null;
    public SetupCode? TakeForDisplay(bool warningAcknowledged) => TryOpenWarningAcknowledged(warningAcknowledged) ? code : null;
    public void HideOrTimeout() { code?.Dispose(); code = null; }
    public void Dispose() => HideOrTimeout();
}

public sealed record ClipboardOwnership(string InitiatingUserSid, int SessionId, string ExactValue);
public static class ClipboardLifecycle
{
    public static ClipboardOwnership? CopyExplicitly(string value, bool warned, string userSid, int sessionId) =>
        warned && !string.IsNullOrEmpty(value) ? new ClipboardOwnership(userSid, sessionId, value) : null;

    public static string? ClearOnlyUnchanged(ClipboardOwnership? ownership, string? currentValue, string userSid, int sessionId) =>
        ownership is not null && ownership.InitiatingUserSid == userSid && ownership.SessionId == sessionId &&
        string.Equals(ownership.ExactValue, currentValue, StringComparison.Ordinal) ? null : currentValue;
}

public sealed class MemoryOnlyQr : IDisposable
{
    private byte[]? payload;
    private MemoryOnlyQr(byte[] payload) => this.payload = payload;
    public bool IsDisposed => payload is null;
    public static MemoryOnlyQr? RenderExplicitly(SetupCode? code, bool warned) => code is { HasOnlyMinimumFields: true } && warned
        ? new(Encoding.UTF8.GetBytes(code.SelectedEndpoint + "|" + code.CredentialRevision)) : null;
    public void Dispose()
    {
        if (payload is null) return;
        CryptographicOperations.ZeroMemory(payload);
        payload = null;
    }
}

public sealed record AttributableSession(string SessionId, string GrantId, string AccountSid, string ShareIdentity);
public static class SessionClosure
{
    public static IReadOnlyList<AttributableSession> SelectExact(
        IEnumerable<AttributableSession> observed, string grantId, string accountSid, string shareIdentity, IEnumerable<string> confirmedIds) =>
        observed.Where(session => session.GrantId == grantId && session.AccountSid == accountSid && session.ShareIdentity == shareIdentity && confirmedIds.Contains(session.SessionId, StringComparer.Ordinal)).ToArray();
}

public static class SecretFlowScanner
{
    private static readonly string[] ForbiddenSinks = ["arguments", "environment", "stdout", "stderr", "logs", "audit", "diagnostics", "configuration", "ledger", "crash", "artifact"];
    public static bool IsSafePublicText(string? text) => string.IsNullOrEmpty(text) || (!text.Contains("not-a-real-credential", StringComparison.Ordinal) && !text.Contains("password=", StringComparison.OrdinalIgnoreCase));
    public static IReadOnlyList<string> ForbiddenSinkNames => ForbiddenSinks;
}

public static class GrantFacts
{
    public static GrantRequest ValidRequest() => new("host-opaque-1", "grant-opaque-1", "S-1-5-21-100-101", "S-1-5-21-100-200", "\\\\host\\Balls", "Balls", "HOST\\balls-grant-1", new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
    public static SetupCode SetupCode(long revision = 1) => new(BallsServer.AccessGrantLifecycle.SetupCode.CurrentSchemaVersion, "host-opaque-1", "grant-opaque-1", "Owner host", "\\\\host\\Balls", "Balls", "HOST\\balls-grant-1", revision, new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero), SecretBuffer.FromUtf8("not-a-real-credential"));
    public static TransferBinding Binding(long revision = 1) => new("S-1-5-21-100-1", 4, "pipe-1", "nonce-1", "operation-1", "grant-opaque-1", revision);
}
