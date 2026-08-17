namespace BallsServer.LedgerRecovery;

public enum ProtectedCopyState
{
    Valid,
    Corrupt,
    Missing,
    WeakAcl,
    UnsupportedSchema,
}

public enum LedgerRecoveryDisposition
{
    Healthy,
    RebuildPrimaryFromMirror,
    RebuildMirrorFromPrimary,
    TotalLossReadOnly,
    UnknownReadOnly,
    RefusedUnsupportedSchema,
}

public sealed record LedgerRecoveryResult(
    LedgerRecoveryDisposition Disposition,
    bool AutomationReadOnly,
    bool AuthorizesWindowsMutation);

public sealed record ProtectedLedgerRecoveryEvidence(
    ProtectedCopyState Primary,
    ProtectedCopyState Mirror,
    bool JournalValid,
    bool CopiesEquivalent,
    long? PrimaryRevision,
    long? MirrorRevision,
    long? CommittedRevision,
    string? PrimaryHash,
    string? MirrorHash);

public static class LedgerRecoveryPolicy
{
    public static LedgerRecoveryResult Evaluate(ProtectedLedgerRecoveryEvidence? evidence)
    {
        LedgerRecoveryDisposition disposition;

        if (evidence is null || !Enum.IsDefined(evidence.Primary) || !Enum.IsDefined(evidence.Mirror) ||
            evidence.CommittedRevision is null || evidence.CommittedRevision < 0)
        {
            disposition = LedgerRecoveryDisposition.UnknownReadOnly;
        }
        else if (evidence.Primary == ProtectedCopyState.UnsupportedSchema || evidence.Mirror == ProtectedCopyState.UnsupportedSchema)
        {
            disposition = LedgerRecoveryDisposition.RefusedUnsupportedSchema;
        }
        else if (evidence.Primary is ProtectedCopyState.Corrupt or ProtectedCopyState.Missing &&
                 evidence.Mirror is ProtectedCopyState.Corrupt or ProtectedCopyState.Missing)
        {
            disposition = LedgerRecoveryDisposition.TotalLossReadOnly;
        }
        else if (!evidence.JournalValid || evidence.Primary == ProtectedCopyState.WeakAcl || evidence.Mirror == ProtectedCopyState.WeakAcl)
        {
            disposition = LedgerRecoveryDisposition.UnknownReadOnly;
        }
        else if (evidence.Primary == ProtectedCopyState.Valid && evidence.Mirror == ProtectedCopyState.Valid)
        {
            bool complete = ValidCopy(evidence.PrimaryRevision, evidence.PrimaryHash, evidence.CommittedRevision) &&
                ValidCopy(evidence.MirrorRevision, evidence.MirrorHash, evidence.CommittedRevision);
            disposition = complete && evidence.CopiesEquivalent && evidence.PrimaryHash == evidence.MirrorHash
                ? LedgerRecoveryDisposition.Healthy
                : LedgerRecoveryDisposition.UnknownReadOnly;
        }
        else if (evidence.Primary == ProtectedCopyState.Valid && evidence.Mirror is ProtectedCopyState.Corrupt or ProtectedCopyState.Missing &&
                 ValidCopy(evidence.PrimaryRevision, evidence.PrimaryHash, evidence.CommittedRevision))
        {
            disposition = LedgerRecoveryDisposition.RebuildMirrorFromPrimary;
        }
        else if (evidence.Mirror == ProtectedCopyState.Valid && evidence.Primary is ProtectedCopyState.Corrupt or ProtectedCopyState.Missing &&
                 ValidCopy(evidence.MirrorRevision, evidence.MirrorHash, evidence.CommittedRevision))
        {
            disposition = LedgerRecoveryDisposition.RebuildPrimaryFromMirror;
        }
        else
        {
            disposition = LedgerRecoveryDisposition.UnknownReadOnly;
        }

        bool readOnly = disposition != LedgerRecoveryDisposition.Healthy;
        return new(disposition, readOnly, AuthorizesWindowsMutation: false);
    }

    private static bool ValidCopy(long? revision, string? hash, long? committedRevision) =>
        revision == committedRevision && CanonicalLedgerValue.IsHash(hash);
}

public enum StoreCommitDisposition
{
    Committed,
    RefusedStaleRevision,
    RefusedInvalidLedger,
    RefusedRevisionExhausted,
}

public sealed record StoreCommitResult(StoreCommitDisposition Disposition);

public sealed class InMemoryProtectedLedgerStore
{
    public InMemoryProtectedLedgerStore(LedgerDocument initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        Primary = initial;
        Mirror = initial;
        CommittedRevision = initial.Revision;
        JournalValid = true;
    }

    public LedgerDocument Primary { get; private set; }

    public LedgerDocument Mirror { get; private set; }

    public long CommittedRevision { get; private set; }

    public bool JournalValid { get; private set; }

    public StoreCommitResult Commit(LedgerDocument next)
    {
        if (next is null)
        {
            return new(StoreCommitDisposition.RefusedInvalidLedger);
        }

        if (CommittedRevision == long.MaxValue)
        {
            return new(StoreCommitDisposition.RefusedRevisionExhausted);
        }

        if (next.Revision != CommittedRevision + 1)
        {
            return new(StoreCommitDisposition.RefusedStaleRevision);
        }

        if (LedgerContract.Validate(next).Code != LedgerValidationCode.Valid)
        {
            return new(StoreCommitDisposition.RefusedInvalidLedger);
        }

        JournalValid = false;
        Primary = next;
        Mirror = next;
        CommittedRevision = next.Revision;
        JournalValid = true;
        return new(StoreCommitDisposition.Committed);
    }
}

public enum RecoveryScenario
{
    StaleRevision,
    CorruptPrimary,
    CorruptMirror,
    TotalLedgerLoss,
    Drift,
    MissingObject,
    UnmanagedConflict,
    Ambiguity,
    AccessDenied,
    PolicyOverride,
    UnknownObservation,
}

public enum RecoveryAction
{
    RefreshPreview,
    RebuildPrimaryFromMirror,
    RebuildMirrorFromPrimary,
    ReadOnlyManifest,
    RepairNeeded,
    RecreateOnlyWithUnambiguousStableProof,
    RefuseAndPreserve,
    UnknownAndAdministratorHandoff,
    RefuseAndPolicyOwnerHandoff,
}

public sealed record ScenarioRecoveryResult(
    RecoveryAction Action,
    bool AuthorizesAdoptionOrNameBasedDeletion);

public static class RecoveryScenarioPolicy
{
    public static ScenarioRecoveryResult Evaluate(RecoveryScenario scenario)
    {
        RecoveryAction action = scenario switch
        {
            RecoveryScenario.StaleRevision => RecoveryAction.RefreshPreview,
            RecoveryScenario.CorruptPrimary => RecoveryAction.RebuildPrimaryFromMirror,
            RecoveryScenario.CorruptMirror => RecoveryAction.RebuildMirrorFromPrimary,
            RecoveryScenario.TotalLedgerLoss => RecoveryAction.ReadOnlyManifest,
            RecoveryScenario.Drift => RecoveryAction.RepairNeeded,
            RecoveryScenario.MissingObject => RecoveryAction.RecreateOnlyWithUnambiguousStableProof,
            RecoveryScenario.UnmanagedConflict or RecoveryScenario.Ambiguity => RecoveryAction.RefuseAndPreserve,
            RecoveryScenario.AccessDenied or RecoveryScenario.UnknownObservation => RecoveryAction.UnknownAndAdministratorHandoff,
            RecoveryScenario.PolicyOverride => RecoveryAction.RefuseAndPolicyOwnerHandoff,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        return new(action, AuthorizesAdoptionOrNameBasedDeletion: false);
    }
}

public sealed record RecoveryCandidate(
    ResourceKind Kind,
    string ObservedStableId,
    string ObservedFingerprint,
    RecoveryObservationStatus ObservationStatus = RecoveryObservationStatus.Exact);

public enum RecoveryObservationStatus
{
    Exact,
    Unknown,
    AccessDenied,
    Incomplete,
    Ambiguous,
}

public enum AdministratorRecoveryInstruction
{
    InspectOnlyNoAction,
    ConfirmGrantStableIdentity,
    ConfirmSessionStableIdentity,
    ConfirmShareStableIdentity,
    ConfirmFirewallRuleStableIdentity,
    ConfirmAceStableIdentity,
    ConfirmGroupStableIdentity,
    ConfirmVerificationFileStableIdentity,
}

public sealed record RecoveryManifestItem(
    ResourceKind Kind,
    string ObservedStableId,
    string ObservedFingerprint,
    RecoveryObservationStatus ObservationStatus,
    AdministratorRecoveryInstruction Instruction,
    bool RequiresAdministratorStableIdentityConfirmation);

public sealed record RecoveryManifest(
    bool Authoritative,
    bool AuthorizesAdoption,
    bool AuthorizesDeletion,
    bool PreserveManagedFolder,
    bool PreserveUserFiles,
    IReadOnlyList<RecoveryManifestItem> Items);

public static class RecoveryManifestBuilder
{
    private static readonly Dictionary<ResourceKind, int> RecoveryOrder =
        new Dictionary<ResourceKind, int>
        {
            [ResourceKind.Grant] = 1,
            [ResourceKind.Session] = 2,
            [ResourceKind.Share] = 3,
            [ResourceKind.LanFirewallRule] = 4,
            [ResourceKind.TailscaleFirewallRule] = 5,
            [ResourceKind.Ace] = 6,
            [ResourceKind.Group] = 7,
            [ResourceKind.VerificationFile] = 8,
            [ResourceKind.VerificationFileCleanup] = 9,
            [ResourceKind.ManagedFolder] = 10,
        };

    public static RecoveryManifest ForTotalLoss(IReadOnlyList<RecoveryCandidate> candidates)
    {
        candidates ??= [];

        IReadOnlyList<RecoveryManifestItem> items = candidates
            .Where(candidate => candidate.Kind != ResourceKind.ManagedFolder)
            .Where(candidate => Enum.IsDefined(candidate.Kind) && Enum.IsDefined(candidate.ObservationStatus))
            .OrderBy(candidate => RecoveryOrder[candidate.Kind])
            .ThenBy(candidate => candidate.ObservedStableId, StringComparer.Ordinal)
            .Select(candidate => new RecoveryManifestItem(
                candidate.Kind,
                candidate.ObservedStableId,
                candidate.ObservedFingerprint,
                candidate.ObservationStatus,
                InstructionFor(candidate),
                RequiresAdministratorStableIdentityConfirmation: true))
            .ToArray();

        return new(
            Authoritative: false,
            AuthorizesAdoption: false,
            AuthorizesDeletion: false,
            PreserveManagedFolder: true,
            PreserveUserFiles: true,
            items);
    }

    private static AdministratorRecoveryInstruction InstructionFor(RecoveryCandidate candidate)
    {
        if (candidate.ObservationStatus != RecoveryObservationStatus.Exact)
        {
            return AdministratorRecoveryInstruction.InspectOnlyNoAction;
        }

        return candidate.Kind switch
        {
            ResourceKind.Grant => AdministratorRecoveryInstruction.ConfirmGrantStableIdentity,
            ResourceKind.Session => AdministratorRecoveryInstruction.ConfirmSessionStableIdentity,
            ResourceKind.Share => AdministratorRecoveryInstruction.ConfirmShareStableIdentity,
            ResourceKind.LanFirewallRule or ResourceKind.TailscaleFirewallRule => AdministratorRecoveryInstruction.ConfirmFirewallRuleStableIdentity,
            ResourceKind.Ace => AdministratorRecoveryInstruction.ConfirmAceStableIdentity,
            ResourceKind.Group => AdministratorRecoveryInstruction.ConfirmGroupStableIdentity,
            ResourceKind.VerificationFile or ResourceKind.VerificationFileCleanup => AdministratorRecoveryInstruction.ConfirmVerificationFileStableIdentity,
            _ => AdministratorRecoveryInstruction.InspectOnlyNoAction,
        };
    }
}

public enum RetentionDisposition
{
    Retain,
    Purge,
    CleanupNeeded,
}

internal static class RetentionPolicy
{
    public const int RetentionDays = 90;
}

public enum RetentionRecordKind
{
    Tombstone,
    Audit,
    SupersededEvidence,
}

public sealed record RetentionRecord(
    RetentionRecordKind Kind,
    string RecordId,
    DateTimeOffset HostRemovedAt,
    string EvidenceFingerprint);

public sealed record RetentionCollections(
    IReadOnlyList<RetentionRecord> Tombstones,
    IReadOnlyList<RetentionRecord> AuditRecords,
    IReadOnlyList<RetentionRecord> SupersededEvidence);

public sealed record RetentionPurgeRequest(
    DateTimeOffset HostRemovedAt,
    DateTimeOffset Now,
    bool ChainValid,
    bool ClockTrusted,
    bool AccessAvailable,
    IReadOnlyList<string> ActiveReferenceIds);

public sealed record RetentionPurgeResult(
    RetentionDisposition Disposition,
    DateTimeOffset Deadline,
    RetentionCollections Remaining,
    IReadOnlyList<string> PurgedRecordIds,
    bool ActiveStateUntouched,
    bool ManagedFolderAndUserFilesUntouched);

public static class RetentionStore
{
    public static RetentionPurgeResult Purge(RetentionCollections? collections, RetentionPurgeRequest? request)
    {
        DateTimeOffset fallback = request?.HostRemovedAt ?? DateTimeOffset.MinValue;
        if (collections is null || request is null || collections.Tombstones is null || collections.AuditRecords is null ||
            collections.SupersededEvidence is null || request.ActiveReferenceIds is null)
        {
            return new(RetentionDisposition.CleanupNeeded, fallback, collections ?? new([], [], []), [], true, true);
        }

        RetentionRecord[] allRecords = [.. collections.Tombstones, .. collections.AuditRecords, .. collections.SupersededEvidence];
        bool collectionsValid = collections.Tombstones.All(record => record is not null && record.Kind == RetentionRecordKind.Tombstone) &&
            collections.AuditRecords.All(record => record is not null && record.Kind == RetentionRecordKind.Audit) &&
            collections.SupersededEvidence.All(record => record is not null && record.Kind == RetentionRecordKind.SupersededEvidence) &&
            allRecords.All(record => record.HostRemovedAt == request.HostRemovedAt && CanonicalLedgerValue.IsRetentionId(record.RecordId) &&
                CanonicalLedgerValue.IsHash(record.EvidenceFingerprint)) &&
            allRecords.Select(record => record.RecordId).Distinct(StringComparer.Ordinal).Count() == allRecords.Length;
        if (!collectionsValid)
        {
            return new(RetentionDisposition.CleanupNeeded, request.HostRemovedAt, collections, [], true, true);
        }

        DateTimeOffset deadline;
        try
        {
            deadline = request.HostRemovedAt.AddDays(RetentionPolicy.RetentionDays);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new(RetentionDisposition.CleanupNeeded, request.HostRemovedAt, collections, [], true, true);
        }

        bool valid = request.ChainValid && request.ClockTrusted && request.AccessAvailable && request.Now >= request.HostRemovedAt;
        if (!valid || request.ActiveReferenceIds.Count > 0 || request.Now < deadline)
        {
            RetentionDisposition disposition = valid && request.ActiveReferenceIds.Count == 0 ? RetentionDisposition.Retain : RetentionDisposition.CleanupNeeded;
            return new(disposition, deadline, collections, [], true, true);
        }

        HashSet<string> active = request.ActiveReferenceIds.ToHashSet(StringComparer.Ordinal);
        List<string> purged = [];
        IReadOnlyList<RetentionRecord> Keep(IReadOnlyList<RetentionRecord> records)
        {
            List<RetentionRecord> kept = [];
            foreach (RetentionRecord record in records)
            {
                if (active.Contains(record.RecordId) || record.HostRemovedAt.AddDays(RetentionPolicy.RetentionDays) > request.Now)
                {
                    kept.Add(record);
                }
                else
                {
                    purged.Add(record.RecordId);
                }
            }
            return kept;
        }

        RetentionCollections remaining = new(Keep(collections.Tombstones), Keep(collections.AuditRecords), Keep(collections.SupersededEvidence));
        if (purged.Count == 0)
        {
            remaining = collections;
        }
        RetentionDisposition result = active.Count > 0 ? RetentionDisposition.Purge : RetentionDisposition.Purge;
        return new(result, deadline, remaining, purged, true, true);
    }
}
