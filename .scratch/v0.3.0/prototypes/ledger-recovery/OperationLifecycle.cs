namespace BallsServer.LedgerRecovery;

public enum OperationPhase
{
    Preview,
    AuthoritativeRevalidation,
    AwaitingHelperConsent,
    Planned,
    Started,
    PrimitiveVerification,
    CancellationPending,
    RollingBack,
    AtomicRevisionCommit,
    RepairNeeded,
    Recovering,
    Completed,
    Canceled,
    Refused,
    Unknown,
}

public static class OperationStateMachine
{
    private static readonly HashSet<(OperationPhase From, OperationPhase To)> AllowedTransitions =
    [
        (OperationPhase.Preview, OperationPhase.AuthoritativeRevalidation),
        (OperationPhase.Preview, OperationPhase.Canceled),
        (OperationPhase.Preview, OperationPhase.Refused),
        (OperationPhase.Preview, OperationPhase.Unknown),
        (OperationPhase.AuthoritativeRevalidation, OperationPhase.AwaitingHelperConsent),
        (OperationPhase.AuthoritativeRevalidation, OperationPhase.Canceled),
        (OperationPhase.AuthoritativeRevalidation, OperationPhase.Refused),
        (OperationPhase.AuthoritativeRevalidation, OperationPhase.Unknown),
        (OperationPhase.AwaitingHelperConsent, OperationPhase.Planned),
        (OperationPhase.AwaitingHelperConsent, OperationPhase.Canceled),
        (OperationPhase.AwaitingHelperConsent, OperationPhase.Refused),
        (OperationPhase.AwaitingHelperConsent, OperationPhase.Unknown),
        (OperationPhase.Planned, OperationPhase.Started),
        (OperationPhase.Planned, OperationPhase.Canceled),
        (OperationPhase.Planned, OperationPhase.RepairNeeded),
        (OperationPhase.Planned, OperationPhase.Unknown),
        (OperationPhase.Started, OperationPhase.PrimitiveVerification),
        (OperationPhase.Started, OperationPhase.CancellationPending),
        (OperationPhase.Started, OperationPhase.RollingBack),
        (OperationPhase.Started, OperationPhase.RepairNeeded),
        (OperationPhase.Started, OperationPhase.Unknown),
        (OperationPhase.PrimitiveVerification, OperationPhase.Started),
        (OperationPhase.PrimitiveVerification, OperationPhase.CancellationPending),
        (OperationPhase.PrimitiveVerification, OperationPhase.RollingBack),
        (OperationPhase.PrimitiveVerification, OperationPhase.RepairNeeded),
        (OperationPhase.PrimitiveVerification, OperationPhase.AtomicRevisionCommit),
        (OperationPhase.CancellationPending, OperationPhase.RollingBack),
        (OperationPhase.CancellationPending, OperationPhase.Canceled),
        (OperationPhase.CancellationPending, OperationPhase.RepairNeeded),
        (OperationPhase.RollingBack, OperationPhase.Canceled),
        (OperationPhase.RollingBack, OperationPhase.RepairNeeded),
        (OperationPhase.AtomicRevisionCommit, OperationPhase.Completed),
        (OperationPhase.AtomicRevisionCommit, OperationPhase.RepairNeeded),
        (OperationPhase.AtomicRevisionCommit, OperationPhase.Unknown),
        (OperationPhase.RepairNeeded, OperationPhase.Recovering),
        (OperationPhase.Recovering, OperationPhase.Started),
        (OperationPhase.Recovering, OperationPhase.RollingBack),
        (OperationPhase.Recovering, OperationPhase.Completed),
        (OperationPhase.Recovering, OperationPhase.Canceled),
        (OperationPhase.Recovering, OperationPhase.RepairNeeded),
        (OperationPhase.Recovering, OperationPhase.Unknown),
    ];

    public static bool CanTransition(OperationPhase from, OperationPhase to) =>
        AllowedTransitions.Contains((from, to));
}

public enum CrashBoundary
{
    BeforeJournalPlan,
    AfterJournalPlan,
    BeforePrimitiveJournal,
    AfterPrimitiveJournal,
    BeforePrimitive,
    AfterPrimitive,
    BeforePrimitiveVerificationJournal,
    AfterPrimitiveVerificationJournal,
    BeforePrimaryAtomicReplace,
    AfterPrimaryAtomicReplace,
    BeforeMirrorAtomicReplace,
    AfterMirrorAtomicReplace,
    BeforeRevisionAdvance,
    AfterRevisionAdvance,
}

public sealed record CrashPoint(CrashBoundary Boundary, string? PrimitiveId);

public static class CrashBoundaryCatalog
{
    public static IReadOnlyList<CrashBoundary> AtomicBoundaries { get; } =
    [
        CrashBoundary.BeforeJournalPlan,
        CrashBoundary.AfterJournalPlan,
        CrashBoundary.BeforePrimaryAtomicReplace,
        CrashBoundary.AfterPrimaryAtomicReplace,
        CrashBoundary.BeforeMirrorAtomicReplace,
        CrashBoundary.AfterMirrorAtomicReplace,
        CrashBoundary.BeforeRevisionAdvance,
        CrashBoundary.AfterRevisionAdvance,
    ];

    public static IReadOnlyList<CrashPoint> ForPlan(IReadOnlyList<string> primitiveIds)
    {
        ArgumentNullException.ThrowIfNull(primitiveIds);
        List<CrashPoint> points = AtomicBoundaries.Select(boundary => new CrashPoint(boundary, null)).ToList();

        foreach (string primitiveId in primitiveIds)
        {
            points.Add(new(CrashBoundary.BeforePrimitiveJournal, primitiveId));
            points.Add(new(CrashBoundary.AfterPrimitiveJournal, primitiveId));
            points.Add(new(CrashBoundary.BeforePrimitive, primitiveId));
            points.Add(new(CrashBoundary.AfterPrimitive, primitiveId));
            points.Add(new(CrashBoundary.BeforePrimitiveVerificationJournal, primitiveId));
            points.Add(new(CrashBoundary.AfterPrimitiveVerificationJournal, primitiveId));
        }

        return points;
    }
}

public enum CrashRecoveryDisposition
{
    Undefined,
    RestartFromFreshPreview,
    ResumeReadOnlyReconciliation,
    VerifyExactPrimitiveThenRecover,
    FinishProtectedCopyRecovery,
    CompleteIdempotently,
}

public sealed record CrashRecoveryResult(
    CrashRecoveryDisposition Disposition,
    bool AutomationReadOnlyUntilRevalidated,
    bool RequiresFreshAuthorizationForMutation,
    bool LedgerAuthorizesMutation,
    bool DeleteManagedFolderOrFiles);

public enum CrashJournalRecordKind { Planned, PrimitiveStarted, PrimitiveVerified, RevisionCommitted }
public sealed record CrashJournalRecord(CrashJournalRecordKind Kind, string? PrimitiveId, long Sequence);

public sealed record DurableCrashState(
    IReadOnlyList<string> PlannedPrimitiveIds,
    IReadOnlyList<string> AppliedStableIds,
    IReadOnlyList<CrashJournalRecord> JournalRecords,
    long PrimaryRevision,
    long MirrorRevision,
    long? CommittedRevision,
    string PrimaryHash,
    string MirrorHash,
    string TransactionId);

public static class CrashRecoveryMatrix
{
    public static CrashRecoveryResult Evaluate(DurableCrashState? state)
    {
        CrashRecoveryDisposition disposition;
        if (state is null || state.PlannedPrimitiveIds is null || state.AppliedStableIds is null || state.JournalRecords is null || state.CommittedRevision is null ||
            state.PrimaryRevision < 0 || state.MirrorRevision < 0 || state.CommittedRevision < 0 ||
            !CanonicalLedgerValue.IsHash(state.PrimaryHash) || !CanonicalLedgerValue.IsHash(state.MirrorHash) ||
            !CanonicalLedgerValue.IsOperationId(state.TransactionId))
        {
            disposition = CrashRecoveryDisposition.Undefined;
        }
        else if (!ValidJournal(state) || state.CommittedRevision < 0)
        {
            disposition = CrashRecoveryDisposition.Undefined;
        }
        else if (state.CommittedRevision < long.MaxValue && state.PrimaryRevision == state.CommittedRevision + 1 &&
                 state.MirrorRevision is var mirror && (mirror == state.CommittedRevision || mirror == state.CommittedRevision + 1) &&
                 JournalReadyForCommit(state))
        {
            disposition = CrashRecoveryDisposition.FinishProtectedCopyRecovery;
        }
        else if (state.PrimaryRevision != state.CommittedRevision || state.MirrorRevision != state.CommittedRevision || state.PrimaryHash != state.MirrorHash)
        {
            disposition = CrashRecoveryDisposition.Undefined;
        }
        else if (state.JournalRecords.Count == 0)
        {
            disposition = CrashRecoveryDisposition.RestartFromFreshPreview;
        }
        else if (state.JournalRecords[^1].Kind == CrashJournalRecordKind.RevisionCommitted)
        {
            disposition = CrashRecoveryDisposition.CompleteIdempotently;
        }
        else if (JournalReadyForCommit(state))
        {
            disposition = CrashRecoveryDisposition.FinishProtectedCopyRecovery;
        }
        else
        {
            HashSet<string> verified = state.JournalRecords.Where(r => r.Kind == CrashJournalRecordKind.PrimitiveVerified)
                .Select(r => r.PrimitiveId!).ToHashSet(StringComparer.Ordinal);
            disposition = state.AppliedStableIds.Any(id => !verified.Contains(id))
                ? CrashRecoveryDisposition.VerifyExactPrimitiveThenRecover
                : CrashRecoveryDisposition.ResumeReadOnlyReconciliation;
        }

        return new(disposition, true, true, false, false);
    }

    private static bool JournalReadyForCommit(DurableCrashState state)
    {
        if (state.JournalRecords.Count == 0 || state.JournalRecords[0].Kind != CrashJournalRecordKind.Planned ||
            state.AppliedStableIds.Count != state.AppliedStableIds.Distinct(StringComparer.Ordinal).Count()) return false;
        HashSet<string> started = state.JournalRecords.Where(record => record.Kind == CrashJournalRecordKind.PrimitiveStarted)
            .Select(record => record.PrimitiveId!).ToHashSet(StringComparer.Ordinal);
        HashSet<string> verified = state.JournalRecords.Where(record => record.Kind == CrashJournalRecordKind.PrimitiveVerified)
            .Select(record => record.PrimitiveId!).ToHashSet(StringComparer.Ordinal);
        return state.PlannedPrimitiveIds.All(id => started.Contains(id) && verified.Contains(id) && state.AppliedStableIds.Contains(id, StringComparer.Ordinal)) &&
            started.Count == state.PlannedPrimitiveIds.Count && verified.Count == state.PlannedPrimitiveIds.Count;
    }

    private static bool ValidJournal(DurableCrashState state)
    {
        if (state.PlannedPrimitiveIds.Any(id => !ValidPrimitiveId(id)) ||
            state.PlannedPrimitiveIds.Count != state.PlannedPrimitiveIds.Distinct(StringComparer.Ordinal).Count() ||
            state.AppliedStableIds.Any(id => !ValidPrimitiveId(id) || !state.PlannedPrimitiveIds.Contains(id, StringComparer.Ordinal)) ||
            state.AppliedStableIds.Count != state.AppliedStableIds.Distinct(StringComparer.Ordinal).Count()) return false;
        if (state.JournalRecords.Count == 0) return state.AppliedStableIds.Count == 0;
        HashSet<string> started = new(StringComparer.Ordinal);
        HashSet<string> verified = new(StringComparer.Ordinal);
        bool committed = false;
        for (int index = 0; index < state.JournalRecords.Count; index++)
        {
            CrashJournalRecord record = state.JournalRecords[index];
            if (!Enum.IsDefined(record.Kind) || record.Sequence != index || committed) return false;
            switch (record.Kind)
            {
                case CrashJournalRecordKind.Planned when index == 0 && record.PrimitiveId is null:
                    break;
                case CrashJournalRecordKind.PrimitiveStarted when ValidPrimitiveId(record.PrimitiveId) && started.Count < state.PlannedPrimitiveIds.Count &&
                    state.PlannedPrimitiveIds[started.Count] == record.PrimitiveId && started.Add(record.PrimitiveId!):
                    break;
                case CrashJournalRecordKind.PrimitiveVerified when ValidPrimitiveId(record.PrimitiveId) && started.Contains(record.PrimitiveId!) && verified.Add(record.PrimitiveId!):
                    break;
                case CrashJournalRecordKind.RevisionCommitted when record.PrimitiveId is null &&
                    started.SetEquals(state.PlannedPrimitiveIds) && verified.SetEquals(state.PlannedPrimitiveIds) &&
                    state.AppliedStableIds.Count == state.PlannedPrimitiveIds.Count && state.AppliedStableIds.All(started.Contains):
                    committed = true;
                    break;
                default:
                    return false;
            }
        }
        return state.AppliedStableIds.All(started.Contains) && verified.All(state.AppliedStableIds.Contains);
    }

    private static bool ValidPrimitiveId(string? id) => id is { Length: > 0 and <= 64 } &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record CrashExecutionSnapshot(
    CrashPoint InjectedAt,
    IReadOnlyList<string> AppliedStableIds,
    IReadOnlyList<CrashJournalRecord> JournalRecords,
    long PrimaryRevision,
    long MirrorRevision,
    long CommittedRevision,
    CrashRecoveryResult Recovery,
    DurableCrashState DurableState);

public enum CrashExecutionCode { Snapshot, RefusedRevisionExhausted, InvalidInput }
public sealed record CrashExecutionResult(CrashExecutionCode Code, CrashExecutionSnapshot? Snapshot);

public sealed class InMemoryTransactionPrototype
{
    private readonly long initialRevision;

    public InMemoryTransactionPrototype(long initialRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialRevision);

        this.initialRevision = initialRevision;
    }

    public CrashExecutionResult ExecuteUntilCrash(
        IReadOnlyList<string> primitiveIds,
        CrashPoint injection)
    {
        if (primitiveIds is null || injection is null || !CrashBoundaryCatalog.ForPlan(primitiveIds).Contains(injection))
        {
            return new(CrashExecutionCode.InvalidInput, null);
        }
        if (initialRevision == long.MaxValue)
        {
            return new(CrashExecutionCode.RefusedRevisionExhausted, null);
        }

        List<string> applied = [];
        List<CrashJournalRecord> journal = [];
        long primaryRevision = initialRevision;
        long mirrorRevision = initialRevision;
        long? committedRevision = initialRevision;
        const string oldHash = "sha256:7777777777777777777777777777777777777777777777777777777777777777";
        const string newHash = "sha256:8888888888888888888888888888888888888888888888888888888888888888";
        const string transactionId = "opid:11111111111111111111111111111111";
        string primaryHash = oldHash;
        string mirrorHash = oldHash;

        CrashExecutionSnapshot? TryCrash(CrashBoundary boundary, string? primitiveId = null)
        {
            CrashPoint candidate = new(boundary, primitiveId);
            if (candidate != injection)
            {
                return null;
            }

            DurableCrashState durable = new(primitiveIds.ToArray(), applied.ToArray(), journal.ToArray(), primaryRevision, mirrorRevision,
                committedRevision, primaryHash, mirrorHash, transactionId);
            return new(
                candidate,
                applied.ToArray(),
                journal.ToArray(),
                primaryRevision,
                mirrorRevision,
                committedRevision ?? -1,
                CrashRecoveryMatrix.Evaluate(durable),
                durable);
        }

        CrashExecutionSnapshot? crashed = TryCrash(CrashBoundary.BeforeJournalPlan);
        if (crashed is not null)
        {
            return new(CrashExecutionCode.Snapshot, crashed);
        }

        journal.Add(new(CrashJournalRecordKind.Planned, null, journal.Count));
        crashed = TryCrash(CrashBoundary.AfterJournalPlan);
        if (crashed is not null)
        {
            return new(CrashExecutionCode.Snapshot, crashed);
        }

        foreach (string primitiveId in primitiveIds)
        {
            crashed = TryCrash(CrashBoundary.BeforePrimitiveJournal, primitiveId);
            if (crashed is not null)
            {
                return new(CrashExecutionCode.Snapshot, crashed);
            }

            journal.Add(new(CrashJournalRecordKind.PrimitiveStarted, primitiveId, journal.Count));
            crashed = TryCrash(CrashBoundary.AfterPrimitiveJournal, primitiveId);
            if (crashed is not null)
            {
                return new(CrashExecutionCode.Snapshot, crashed);
            }

            crashed = TryCrash(CrashBoundary.BeforePrimitive, primitiveId);
            if (crashed is not null)
            {
                return new(CrashExecutionCode.Snapshot, crashed);
            }

            applied.Add(primitiveId);
            crashed = TryCrash(CrashBoundary.AfterPrimitive, primitiveId);
            if (crashed is not null)
            {
                return new(CrashExecutionCode.Snapshot, crashed);
            }

            crashed = TryCrash(CrashBoundary.BeforePrimitiveVerificationJournal, primitiveId);
            if (crashed is not null)
            {
                return new(CrashExecutionCode.Snapshot, crashed);
            }

            journal.Add(new(CrashJournalRecordKind.PrimitiveVerified, primitiveId, journal.Count));
            crashed = TryCrash(CrashBoundary.AfterPrimitiveVerificationJournal, primitiveId);
            if (crashed is not null)
            {
                return new(CrashExecutionCode.Snapshot, crashed);
            }
        }

        crashed = TryCrash(CrashBoundary.BeforePrimaryAtomicReplace);
        if (crashed is not null)
        {
            return new(CrashExecutionCode.Snapshot, crashed);
        }

        primaryRevision = checked(initialRevision + 1);
        primaryHash = newHash;
        crashed = TryCrash(CrashBoundary.AfterPrimaryAtomicReplace);
        if (crashed is not null)
        {
            return new(CrashExecutionCode.Snapshot, crashed);
        }

        crashed = TryCrash(CrashBoundary.BeforeMirrorAtomicReplace);
        if (crashed is not null)
        {
            return new(CrashExecutionCode.Snapshot, crashed);
        }

        mirrorRevision = checked(initialRevision + 1);
        mirrorHash = newHash;
        crashed = TryCrash(CrashBoundary.AfterMirrorAtomicReplace);
        if (crashed is not null)
        {
            return new(CrashExecutionCode.Snapshot, crashed);
        }

        crashed = TryCrash(CrashBoundary.BeforeRevisionAdvance);
        if (crashed is not null)
        {
            return new(CrashExecutionCode.Snapshot, crashed);
        }

        committedRevision = checked(initialRevision + 1);
        journal.Add(new(CrashJournalRecordKind.RevisionCommitted, null, journal.Count));
        CrashExecutionSnapshot? final = TryCrash(CrashBoundary.AfterRevisionAdvance);
        return final is null ? new(CrashExecutionCode.InvalidInput, null) : new(CrashExecutionCode.Snapshot, final);
    }
}

public enum ReplayDisposition
{
    CompletedWithoutMutation,
    RefusedDigestMismatch,
    RefusedUnknownOperation,
    RefusedNonTerminal,
    RefusedStaleRevision,
    RefusedContextMismatch,
    RefusedIncompleteEvidence,
}

public enum HelperConsentDecision { Planned, Canceled, Refused }
public sealed record HelperConsentAttempt(
    string ExpectedBindingFingerprint,
    string PresentedBindingFingerprint,
    DateTimeOffset ExpiresAt,
    DateTimeOffset Now,
    bool AlreadyConsumed,
    bool Canceled);
public static class HelperConsentPolicy
{
    public static HelperConsentDecision Evaluate(HelperConsentAttempt? attempt)
    {
        if (attempt is null || !CanonicalLedgerValue.IsHash(attempt.ExpectedBindingFingerprint) ||
            !CanonicalLedgerValue.IsHash(attempt.PresentedBindingFingerprint) || attempt.ExpectedBindingFingerprint != attempt.PresentedBindingFingerprint ||
            attempt.Now > attempt.ExpiresAt || attempt.AlreadyConsumed)
            return HelperConsentDecision.Refused;
        return attempt.Canceled ? HelperConsentDecision.Canceled : HelperConsentDecision.Planned;
    }
}

public sealed record ReplayResult(ReplayDisposition Disposition, int PrimitivesExecuted);

public static class ReplayPolicy
{
    public static ReplayResult Evaluate(ReplayEvidence expected, ReplayEvidence observed)
    {
        if (expected is null || observed is null || !expected.EvidenceComplete || !observed.EvidenceComplete ||
            !CanonicalLedgerValue.IsOperationId(expected.OperationId) || !CanonicalLedgerValue.IsOperationId(observed.OperationId) ||
            !CanonicalLedgerValue.IsHash(expected.PlanDigest) || !CanonicalLedgerValue.IsHash(observed.PlanDigest) ||
            !CanonicalLedgerValue.IsContext(expected.ObservationContext) || !CanonicalLedgerValue.IsContext(observed.ObservationContext) ||
            expected.Revision < 0 || observed.Revision < 0)
            return new(ReplayDisposition.RefusedIncompleteEvidence, 0);
        if (!Enum.IsDefined(observed.Operation) || expected.Operation != observed.Operation || expected.OperationId != observed.OperationId)
            return new(ReplayDisposition.RefusedUnknownOperation, 0);
        if (expected.PlanDigest != observed.PlanDigest)
            return new(ReplayDisposition.RefusedDigestMismatch, 0);
        if (expected.Revision != observed.Revision)
            return new(ReplayDisposition.RefusedStaleRevision, 0);
        if (expected.ObservationContext != observed.ObservationContext)
            return new(ReplayDisposition.RefusedContextMismatch, 0);
        if (observed.TerminalPhase != OperationPhase.Completed)
            return new(ReplayDisposition.RefusedNonTerminal, 0);
        return new(ReplayDisposition.CompletedWithoutMutation, 0);
    }

}

public sealed record ReplayEvidence(
    HostOperation Operation,
    string OperationId,
    string PlanDigest,
    long Revision,
    string ObservationContext,
    OperationPhase TerminalPhase,
    bool EvidenceComplete);
