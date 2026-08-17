using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BallsServer.LedgerRecovery;

public enum ResourceKind
{
    ManagedFolder,
    Grant,
    Session,
    Share,
    LanFirewallRule,
    TailscaleFirewallRule,
    Ace,
    Group,
    VerificationFile,
    VerificationFileCleanup,
}

public enum HostOperation
{
    Op03 = 3,
    Op04 = 4,
    Op06 = 6,
    Op07 = 7,
    Op08 = 8,
    Op09 = 9,
    Op10 = 10,
    Op11 = 11,
    Op12 = 12,
    Op16 = 16,
    Op23 = 23,
    Op24 = 24,
    Op32 = 32,
    Op38 = 38,
    Op39 = 39,
}

public enum EndpointKind
{
    Local,
    Tailscale,
    Unc,
}

public enum JournalResult
{
    Planned,
    InProgress,
    Verified,
    Succeeded,
    Canceled,
    Refused,
    RepairNeeded,
    Failed,
    Unknown,
}

public sealed record JournalState(OperationPhase Phase, JournalResult Result);

public sealed record ResourceRecord(
    ResourceKind Kind,
    string StableId,
    string CanonicalFingerprint,
    HostOperation OwningOperation,
    string OwnershipMarker,
    string ContextBinding);

public sealed record EndpointSnapshot(
    EndpointKind Kind,
    string ValueFingerprint,
    long ObservationEpoch,
    string ContextBinding);

public sealed record JournalEntry(
    string ProtocolVersion,
    HostOperation Operation,
    string ProductHostId,
    string OperationId,
    long ExpectedRevision,
    string PlanDigest,
    string PipeInstanceFingerprint,
    string NonceFingerprint,
    string AuthorizationBindingFingerprint,
    string ObservationContext,
    long Sequence,
    string PreviousRecordHash,
    string RecordHash,
    OperationPhase Phase,
    JournalResult Result,
    DateTimeOffset Timestamp,
    bool AuthorizationConsumed)
{
    public static bool CanAuthorizeMutationOrReplay => false;
}

public sealed record AuditReference(string AuditId, DateTimeOffset Timestamp, string ChainHash);

public sealed record RevokedGrantTombstone(
    string GrantId,
    string AccountStableId,
    long CredentialRevision,
    DateTimeOffset RevokedAt,
    DateTimeOffset HostRemovedAt,
    string AuditReferenceId);

public sealed record LedgerDocument(
    int SchemaVersion,
    string ProductHostId,
    string HostStableId,
    string OwnerSid,
    long Revision,
    string DesiredStateFingerprint,
    ResourceRecord ManagedFolder,
    IReadOnlyList<ResourceRecord> Resources,
    IReadOnlyList<EndpointSnapshot> Endpoints,
    IReadOnlyList<JournalEntry> Journal,
    IReadOnlyList<AuditReference> AuditReferences,
    IReadOnlyList<RevokedGrantTombstone> Tombstones,
    DateTimeOffset? HostRemovedAt);

public enum LedgerValidationCode
{
    Valid,
    UnsupportedSchema,
    Malformed,
    NonMonotonicRevision,
    SecretMaterial,
    MissingIdentity,
    InvalidResource,
    InvalidEndpoint,
    InvalidJournal,
    InvalidAudit,
    InvalidTombstone,
    CrossRecordMismatch,
}

public sealed record LedgerValidationResult(LedgerValidationCode Code, int ForbiddenFieldCount);
public sealed record SecretScanResult(bool Safe, IReadOnlyList<string> ForbiddenFields);

public static class CanonicalLedgerValue
{
    private static readonly Regex Host = new("^host:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Machine = new("^machine:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Resource = new("^rid:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Marker = new("^marker:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Context = new("^ctx:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Operation = new("^opid:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Primitive = new("^primitive:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Audit = new("^audit:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Retention = new("^retention:[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hash = new("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sid = new("^S-1-5-21-[1-9][0-9]{0,9}-[1-9][0-9]{0,9}-[1-9][0-9]{0,9}-[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant);

    public static bool IsHostId(string? value) => value is not null && Host.IsMatch(value);
    public static bool IsMachineId(string? value) => value is not null && Machine.IsMatch(value);
    public static bool IsResourceId(string? value) => value is not null && Resource.IsMatch(value);
    public static bool IsMarker(string? value) => value is not null && Marker.IsMatch(value);
    public static bool IsContext(string? value) => value is not null && Context.IsMatch(value);
    public static bool IsOperationId(string? value) => value is not null && Operation.IsMatch(value);
    public static bool IsPrimitiveId(string? value) => value is not null && Primitive.IsMatch(value);
    public static bool IsAuditId(string? value) => value is not null && Audit.IsMatch(value);
    public static bool IsRetentionId(string? value) => value is not null && Retention.IsMatch(value);
    public static bool IsHash(string? value) => value is not null && Hash.IsMatch(value);
    public static bool IsOwnerSid(string? value)
    {
        if (value is null || !Sid.IsMatch(value)) return false;
        return value[9..].Split('-').All(component => uint.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    public static bool OperationMatches(ResourceKind kind, HostOperation operation) => kind switch
    {
        ResourceKind.ManagedFolder => operation == HostOperation.Op06,
        ResourceKind.Group => operation == HostOperation.Op07,
        ResourceKind.Ace => operation == HostOperation.Op08,
        ResourceKind.Share => operation == HostOperation.Op09,
        ResourceKind.LanFirewallRule => operation == HostOperation.Op10,
        ResourceKind.TailscaleFirewallRule => operation == HostOperation.Op11,
        ResourceKind.Grant => operation == HostOperation.Op12,
        ResourceKind.Session => operation == HostOperation.Op16,
        ResourceKind.VerificationFile => operation == HostOperation.Op32,
        ResourceKind.VerificationFileCleanup => operation == HostOperation.Op38,
        _ => false,
    };
}

public static class SecretMaterialScanner
{
    private static readonly HashSet<string> ForbiddenExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwordDerivedIdentifier", "recoverableSecretHint", "setupCode", "credentialPayload",
        "secretMaterial", "credentialBlob", "tailscaleKey",
    };
    private static readonly HashSet<string> Canaries = new(StringComparer.Ordinal) { "not-a-real-credential", "hunter2" };

    public static SecretScanResult ScanJson(string? json)
    {
        if (json is null)
        {
            return new(false, ["<malformed-json>"]);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            List<string> findings = [];
            Visit(document.RootElement, findings, "");
            return new(findings.Count == 0, findings);
        }
        catch (JsonException)
        {
            return new(false, ["<malformed-json>"]);
        }
    }

    private static void Visit(JsonElement element, List<string> findings, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string propertyPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                if (!names.Add(property.Name))
                {
                    findings.Add($"{propertyPath}<duplicate>");
                }

                if (IsForbidden(property.Name))
                {
                    findings.Add(property.Name);
                }

                Visit(property.Value, findings, propertyPath);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                Visit(item, findings, $"{path}[{index++}]");
            }
        }
        else if (element.ValueKind == JsonValueKind.String && element.GetString() is string value && Canaries.Contains(value))
        {
            findings.Add("<secret-canary>");
        }
    }

    private static bool IsForbidden(string name) =>
        ForbiddenExact.Contains(name) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains("credential", StringComparison.OrdinalIgnoreCase) &&
         (name.Contains("value", StringComparison.OrdinalIgnoreCase) || name.Contains("data", StringComparison.OrdinalIgnoreCase) ||
          name.Contains("payload", StringComparison.OrdinalIgnoreCase) || name.Contains("blob", StringComparison.OrdinalIgnoreCase) ||
          name.Contains("token", StringComparison.OrdinalIgnoreCase) || name.Contains("key", StringComparison.OrdinalIgnoreCase)));
}

public static class JournalChain
{
    public const string ProtocolVersion = "balls-helper/1";
    public const string GenesisHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    public static IReadOnlyList<JournalEntry> CreateCompleted(
        HostOperation operation, string productHostId, string operationId, long revision, string planDigest,
        string pipeFingerprint, string nonceFingerprint, string authorizationBindingFingerprint, string context,
        DateTimeOffset timestamp)
    {
        JournalState[] states =
        [
            new(OperationPhase.Planned, JournalResult.Planned),
            new(OperationPhase.Started, JournalResult.InProgress),
            new(OperationPhase.PrimitiveVerification, JournalResult.Verified),
            new(OperationPhase.AtomicRevisionCommit, JournalResult.Verified),
            new(OperationPhase.Completed, JournalResult.Succeeded),
        ];
        return Create(operation, productHostId, operationId, revision, planDigest, pipeFingerprint, nonceFingerprint,
            authorizationBindingFingerprint, context, timestamp, states);
    }

    public static IReadOnlyList<JournalEntry> Create(
        HostOperation operation, string productHostId, string operationId, long expectedRevision, string planDigest,
        string pipeFingerprint, string nonceFingerprint, string authorizationBindingFingerprint, string context,
        DateTimeOffset timestamp, IReadOnlyList<JournalState> states)
    {
        states ??= [];
        List<JournalEntry> entries = [];
        string previous = GenesisHash;
        for (int index = 0; index < states.Count; index++)
        {
            JournalEntry entry = new(ProtocolVersion, operation, productHostId, operationId, expectedRevision, planDigest,
                pipeFingerprint, nonceFingerprint, authorizationBindingFingerprint, context, index, previous, "", states[index].Phase,
                states[index].Result, timestamp.AddSeconds(index), AuthorizationConsumed: true);
            entry = entry with { RecordHash = ComputeHash(entry) };
            entries.Add(entry);
            previous = entry.RecordHash;
        }

        return entries;
    }

    public static string ComputeHash(JournalEntry entry)
    {
        string canonical = string.Join('|', entry.ProtocolVersion, (int)entry.Operation, entry.ProductHostId, entry.OperationId,
            entry.ExpectedRevision.ToString(CultureInfo.InvariantCulture), entry.PlanDigest, entry.PipeInstanceFingerprint,
            entry.NonceFingerprint, entry.AuthorizationBindingFingerprint, entry.ObservationContext,
            entry.Sequence.ToString(CultureInfo.InvariantCulture), entry.PreviousRecordHash, (int)entry.Phase, (int)entry.Result,
            entry.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), entry.AuthorizationConsumed ? "1" : "0");
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
    }
}

public static class LedgerContract
{
    public const int CurrentSchemaVersion = 1;

    public static LedgerValidationResult Validate(LedgerDocument? ledger)
    {
        try
        {
            return ValidateCore(ledger);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException or JsonException)
        {
            return new(LedgerValidationCode.Malformed, 0);
        }
    }

    private static LedgerValidationResult ValidateCore(LedgerDocument? ledger)
    {
        if (ledger is null)
        {
            return new(LedgerValidationCode.Malformed, 0);
        }
        if (ledger.SchemaVersion < 0) return new(LedgerValidationCode.Malformed, 0);
        if (ledger.SchemaVersion != CurrentSchemaVersion) return new(LedgerValidationCode.UnsupportedSchema, 0);
        if (ledger.Revision < 0) return new(LedgerValidationCode.NonMonotonicRevision, 0);
        if (ledger.Resources is null || ledger.Endpoints is null || ledger.Journal is null || ledger.AuditReferences is null || ledger.Tombstones is null || ledger.ManagedFolder is null)
            return new(LedgerValidationCode.Malformed, 0);
        if (!CanonicalLedgerValue.IsHostId(ledger.ProductHostId) || !CanonicalLedgerValue.IsMachineId(ledger.HostStableId) ||
            !CanonicalLedgerValue.IsOwnerSid(ledger.OwnerSid) || !CanonicalLedgerValue.IsHash(ledger.DesiredStateFingerprint))
            return new(LedgerValidationCode.MissingIdentity, 0);
        if (!ValidResource(ledger.ManagedFolder) || ledger.ManagedFolder.Kind != ResourceKind.ManagedFolder || ledger.Resources.Any(r => !ValidResource(r) || r.Kind == ResourceKind.ManagedFolder))
            return new(LedgerValidationCode.InvalidResource, 0);
        if (ledger.Resources.Append(ledger.ManagedFolder).GroupBy(r => r.StableId, StringComparer.Ordinal).Any(g => g.Count() != 1))
            return new(LedgerValidationCode.CrossRecordMismatch, 0);
        if (ledger.Resources.Any(resource => resource.ContextBinding != ledger.ManagedFolder.ContextBinding))
            return new(LedgerValidationCode.CrossRecordMismatch, 0);
        if (ledger.Endpoints.Any(e => e is null || !Enum.IsDefined(e.Kind) || !CanonicalLedgerValue.IsHash(e.ValueFingerprint) || e.ObservationEpoch < 0 || e.ObservationEpoch > ledger.Revision || !CanonicalLedgerValue.IsContext(e.ContextBinding)) ||
            ledger.Endpoints.GroupBy(e => e.Kind).Any(g => g.Count() != 1))
            return new(LedgerValidationCode.InvalidEndpoint, 0);
        if (ledger.Endpoints.Any(endpoint => endpoint.ContextBinding != ledger.ManagedFolder.ContextBinding))
            return new(LedgerValidationCode.CrossRecordMismatch, 0);
        if (!ValidJournal(ledger)) return new(LedgerValidationCode.InvalidJournal, 0);
        if (ledger.Journal.Any(entry => entry.ObservationContext != ledger.ManagedFolder.ContextBinding)) return new(LedgerValidationCode.CrossRecordMismatch, 0);
        if (!ValidAudit(ledger.AuditReferences)) return new(LedgerValidationCode.InvalidAudit, 0);
        if (!ValidTombstones(ledger)) return new(LedgerValidationCode.InvalidTombstone, 0);

        SecretScanResult scan = SecretMaterialScanner.ScanJson(JsonSerializer.Serialize(ledger));
        return scan.Safe ? new(LedgerValidationCode.Valid, 0) : new(LedgerValidationCode.SecretMaterial, scan.ForbiddenFields.Count);
    }

    private static bool ValidResource(ResourceRecord? resource) => resource is not null && Enum.IsDefined(resource.Kind) &&
        Enum.IsDefined(resource.OwningOperation) && CanonicalLedgerValue.IsResourceId(resource.StableId) &&
        CanonicalLedgerValue.IsHash(resource.CanonicalFingerprint) && CanonicalLedgerValue.IsMarker(resource.OwnershipMarker) &&
        CanonicalLedgerValue.IsContext(resource.ContextBinding) && CanonicalLedgerValue.OperationMatches(resource.Kind, resource.OwningOperation);

    private static bool ValidJournal(LedgerDocument ledger)
    {
        if (ledger.Revision == 0) return ledger.Journal.Count == 0;
        if (ledger.Journal.Count == 0) return false;
        JournalEntry? first = ledger.Journal[0];
        if (first is null || first.Phase != OperationPhase.Planned || first.Result != JournalResult.Planned) return false;
        int terminalCount = 0;
        DateTimeOffset previousTimestamp = DateTimeOffset.MinValue;
        for (int index = 0; index < ledger.Journal.Count; index++)
        {
            JournalEntry? entry = ledger.Journal[index];
            if (entry is null) return false;
            if (entry.ProtocolVersion != JournalChain.ProtocolVersion || !Enum.IsDefined(entry.Operation) || !Enum.IsDefined(entry.Phase) || !Enum.IsDefined(entry.Result) ||
                entry.ProductHostId != ledger.ProductHostId || entry.Operation != first.Operation || entry.OperationId != first.OperationId || entry.ExpectedRevision != ledger.Revision - 1 ||
                entry.PlanDigest != first.PlanDigest || entry.PipeInstanceFingerprint != first.PipeInstanceFingerprint || entry.NonceFingerprint != first.NonceFingerprint ||
                entry.AuthorizationBindingFingerprint != first.AuthorizationBindingFingerprint || entry.ObservationContext != first.ObservationContext ||
                !CanonicalLedgerValue.IsOperationId(entry.OperationId) || !CanonicalLedgerValue.IsHash(entry.PlanDigest) ||
                !CanonicalLedgerValue.IsHash(entry.PipeInstanceFingerprint) || !CanonicalLedgerValue.IsHash(entry.NonceFingerprint) ||
                !CanonicalLedgerValue.IsHash(entry.AuthorizationBindingFingerprint) || !CanonicalLedgerValue.IsContext(entry.ObservationContext) ||
                entry.Sequence != index || entry.PreviousRecordHash != (index == 0 ? JournalChain.GenesisHash : ledger.Journal[index - 1].RecordHash) ||
                !CanonicalLedgerValue.IsHash(entry.RecordHash) || entry.RecordHash != JournalChain.ComputeHash(entry) || entry.Timestamp < DateTimeOffset.UnixEpoch || entry.Timestamp < previousTimestamp || !entry.AuthorizationConsumed)
                return false;
            if (index > 0 && (ledger.Journal[index - 1] is null || !OperationStateMachine.CanTransition(ledger.Journal[index - 1].Phase, entry.Phase))) return false;
            if (!ResultMatchesPhase(entry.Phase, entry.Result)) return false;
            if (entry.Phase is OperationPhase.Completed or OperationPhase.Canceled or OperationPhase.Refused or OperationPhase.Unknown) terminalCount++;
            previousTimestamp = entry.Timestamp;
        }
        OperationPhase finalPhase = ledger.Journal[^1].Phase;
        bool repairNeededTerminal = finalPhase == OperationPhase.RepairNeeded;
        return terminalCount == (repairNeededTerminal ? 0 : 1) &&
            finalPhase is OperationPhase.Completed or OperationPhase.Canceled or OperationPhase.Refused or OperationPhase.RepairNeeded or OperationPhase.Unknown;
    }

    private static bool ResultMatchesPhase(OperationPhase phase, JournalResult result) => phase switch
    {
        OperationPhase.Planned => result == JournalResult.Planned,
        OperationPhase.Started => result == JournalResult.InProgress,
        OperationPhase.PrimitiveVerification => result is JournalResult.Verified or JournalResult.Failed or JournalResult.Unknown,
        OperationPhase.CancellationPending => result == JournalResult.InProgress,
        OperationPhase.RollingBack => result is JournalResult.InProgress or JournalResult.Verified or JournalResult.Failed or JournalResult.Unknown,
        OperationPhase.AtomicRevisionCommit => result == JournalResult.Verified,
        OperationPhase.Completed => result == JournalResult.Succeeded,
        OperationPhase.Canceled => result == JournalResult.Canceled,
        OperationPhase.Refused => result == JournalResult.Refused,
        OperationPhase.RepairNeeded => result == JournalResult.RepairNeeded,
        OperationPhase.Recovering => result is JournalResult.InProgress or JournalResult.Verified or JournalResult.Failed or JournalResult.Unknown,
        OperationPhase.Unknown => result == JournalResult.Unknown,
        _ => false,
    };

    private static bool ValidAudit(IReadOnlyList<AuditReference> records)
    {
        DateTimeOffset previous = DateTimeOffset.MinValue;
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (AuditReference record in records)
        {
            if (record is null || !CanonicalLedgerValue.IsAuditId(record.AuditId) || !CanonicalLedgerValue.IsHash(record.ChainHash) || !ids.Add(record.AuditId) || record.Timestamp < DateTimeOffset.UnixEpoch || record.Timestamp < previous) return false;
            previous = record.Timestamp;
        }
        return true;
    }

    private static bool ValidTombstones(LedgerDocument ledger)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> audits = ledger.AuditReferences.Select(a => a.AuditId).ToHashSet(StringComparer.Ordinal);
        foreach (RevokedGrantTombstone tombstone in ledger.Tombstones)
        {
            if (tombstone is null || !CanonicalLedgerValue.IsResourceId(tombstone.GrantId) || !CanonicalLedgerValue.IsResourceId(tombstone.AccountStableId) ||
                tombstone.CredentialRevision < 0 || tombstone.CredentialRevision > ledger.Revision || tombstone.RevokedAt > tombstone.HostRemovedAt ||
                ledger.HostRemovedAt is null || tombstone.HostRemovedAt != ledger.HostRemovedAt || !audits.Contains(tombstone.AuditReferenceId) || !ids.Add(tombstone.GrantId)) return false;
        }
        return ledger.HostRemovedAt is null || ledger.Tombstones.All(t => t.HostRemovedAt == ledger.HostRemovedAt);
    }
}

public static class ProtectedStatePolicy
{
    public const string HostStateSddl = "O:SYG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;<OWNER-SID>)";
    public const string HostWriters = "Helper, Administrators, and SYSTEM only";
    public const bool UserWritableCopyCanAuthorizeMutation = false;
}

public sealed record ClientIntentRecord(string ProductHostId, string GrantId, long CredentialRevision, string MappingFingerprint, string ProviderCredentialTargetFingerprint)
{
    public static bool CanAuthorizeHostMutation => false;
    public static bool IsHostOwnershipEvidence => false;
}

public enum ClientIntentValidationCode { ValidCurrentUserIntent, Malformed }
public sealed record ClientIntentValidationResult(ClientIntentValidationCode Code, bool CanBecomeProtectedHostAuthority);
public static class ClientIntentContract
{
    public static ClientIntentValidationResult Validate(ClientIntentRecord? intent) => intent is not null &&
        CanonicalLedgerValue.IsHostId(intent.ProductHostId) && CanonicalLedgerValue.IsResourceId(intent.GrantId) && intent.CredentialRevision >= 0 &&
        CanonicalLedgerValue.IsHash(intent.MappingFingerprint) && CanonicalLedgerValue.IsHash(intent.ProviderCredentialTargetFingerprint)
            ? new(ClientIntentValidationCode.ValidCurrentUserIntent, false)
            : new(ClientIntentValidationCode.Malformed, false);
}
