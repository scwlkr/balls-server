using BallsServer.LedgerRecovery;

namespace BallsServer.LedgerRecovery.Tests;

public sealed class ReviewFixRound2Tests
{
    public static TheoryData<ResourceKind, HostOperation> AuthoritativeResourceOperations => new()
    {
        { ResourceKind.ManagedFolder, HostOperation.Op06 },
        { ResourceKind.Group, HostOperation.Op07 },
        { ResourceKind.Ace, HostOperation.Op08 },
        { ResourceKind.Share, HostOperation.Op09 },
        { ResourceKind.LanFirewallRule, HostOperation.Op10 },
        { ResourceKind.TailscaleFirewallRule, HostOperation.Op11 },
        { ResourceKind.Grant, HostOperation.Op12 },
        { ResourceKind.Session, HostOperation.Op16 },
        { ResourceKind.VerificationFile, HostOperation.Op32 },
        { ResourceKind.VerificationFileCleanup, HostOperation.Op38 },
    };

    [Theory]
    [MemberData(nameof(AuthoritativeResourceOperations))]
    public void Resource_kind_accepts_only_its_authoritative_operation(ResourceKind kind, HostOperation operation)
    {
        Assert.True(CanonicalLedgerValue.OperationMatches(kind, operation));
        Assert.False(CanonicalLedgerValue.OperationMatches(kind, HostOperation.Op39));
        Assert.False(CanonicalLedgerValue.OperationMatches((ResourceKind)999, operation));
    }

    [Fact]
    public void Malformed_desired_unowned_absence_is_unknown_and_cannot_create()
    {
        LedgerDocument ledger = ReviewData.Ledger();
        ResourceRecord malformed = ledger.Resources[0] with { StableId = "hunter2" };
        ReconciliationInput input = new(malformed, null, new(LiveEvidenceKind.Absent, null, null, null), ledger.ProductHostId, ledger.Revision);

        ReconciliationResult result = ReconciliationEngine.Reconcile(input);
        ConvergenceResult convergence = ConvergencePolicy.Evaluate(input, DesiredResourceState.Present);

        Assert.Equal(ReconciliationClass.Unknown, result.Classification);
        Assert.Equal(OwnershipProvenance.Invalid, result.Provenance);
        Assert.Equal(ConvergenceDisposition.Unknown, convergence.Disposition);
        Assert.Equal(0, convergence.MaximumCreates);
    }

    [Fact]
    public void Missing_result_retains_proven_or_unowned_provenance_through_absent_convergence()
    {
        LedgerDocument ledger = ReviewData.Ledger();
        ResourceRecord desired = ledger.Resources[0];
        LiveResourceEvidence absent = new(LiveEvidenceKind.Absent, null, null, null);
        ReconciliationInput unownedInput = new(desired, null, absent, ledger.ProductHostId, ledger.Revision);
        ReconciliationResult unowned = ReconciliationEngine.Reconcile(unownedInput);
        ProtectedOwnershipRecord proof = ProtectedOwnershipRecord.Create(desired, ledger.ProductHostId, ledger.Revision);
        ReconciliationInput ownedInput = new(desired, proof, absent, ledger.ProductHostId, ledger.Revision);
        ReconciliationResult owned = ReconciliationEngine.Reconcile(ownedInput);

        Assert.Equal(OwnershipProvenance.Unowned, unowned.Provenance);
        Assert.Equal(ConvergenceDisposition.PreserveUnownedAbsence,
            ConvergencePolicy.Evaluate(unownedInput, DesiredResourceState.Absent).Disposition);
        Assert.Equal(OwnershipProvenance.Protected, owned.Provenance);
        Assert.Equal(ConvergenceDisposition.RemoveExactOwnedObject,
            ConvergencePolicy.Evaluate(ownedInput, DesiredResourceState.Absent).Disposition);
    }

    public static TheoryData<JournalState[]> ValidJournalProgressions => new()
    {
        {
            [
                new(OperationPhase.Planned, JournalResult.Planned),
                new(OperationPhase.Started, JournalResult.InProgress),
                new(OperationPhase.PrimitiveVerification, JournalResult.Verified),
                new(OperationPhase.AtomicRevisionCommit, JournalResult.Verified),
                new(OperationPhase.Completed, JournalResult.Succeeded),
            ]
        },
        {
            [
                new(OperationPhase.Planned, JournalResult.Planned),
                new(OperationPhase.Started, JournalResult.InProgress),
                new(OperationPhase.PrimitiveVerification, JournalResult.Failed),
                new(OperationPhase.RollingBack, JournalResult.Verified),
                new(OperationPhase.Canceled, JournalResult.Canceled),
            ]
        },
        {
            [
                new(OperationPhase.Planned, JournalResult.Planned),
                new(OperationPhase.Started, JournalResult.InProgress),
                new(OperationPhase.RepairNeeded, JournalResult.RepairNeeded),
            ]
        },
        {
            [
                new(OperationPhase.Planned, JournalResult.Planned),
                new(OperationPhase.Started, JournalResult.InProgress),
                new(OperationPhase.RepairNeeded, JournalResult.RepairNeeded),
                new(OperationPhase.Recovering, JournalResult.InProgress),
                new(OperationPhase.Completed, JournalResult.Succeeded),
            ]
        },
        {
            [
                new(OperationPhase.Planned, JournalResult.Planned),
                new(OperationPhase.Started, JournalResult.InProgress),
                new(OperationPhase.Unknown, JournalResult.Unknown),
            ]
        },
    };

    [Theory]
    [MemberData(nameof(ValidJournalProgressions))]
    public void Journal_binds_request_revision_N_to_committed_ledger_N_plus_one_and_accepts_exact_progressions(JournalState[] progression)
    {
        LedgerDocument ledger = ReviewData.Ledger();
        IReadOnlyList<JournalEntry> journal = JournalChain.Create(HostOperation.Op03, ledger.ProductHostId,
            ReviewData.OperationId('6'), expectedRevision: 6, ReviewData.Hash('2'), ReviewData.Hash('3'), ReviewData.Hash('4'),
            ReviewData.Hash('5'), ReviewData.Context('1'), new DateTimeOffset(2026, 8, 14, 14, 0, 0, TimeSpan.Zero), progression);

        Assert.Equal(LedgerValidationCode.Valid, LedgerContract.Validate(ledger with { Journal = journal }).Code);
        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(ledger with
        {
            Journal = JournalChain.Create(HostOperation.Op03, ledger.ProductHostId, ReviewData.OperationId('6'), expectedRevision: 7,
                ReviewData.Hash('2'), ReviewData.Hash('3'), ReviewData.Hash('4'), ReviewData.Hash('5'), ReviewData.Context('1'),
                new DateTimeOffset(2026, 8, 14, 14, 0, 0, TimeSpan.Zero), progression),
        }).Code);
    }

    [Fact]
    public void Journal_rejects_a_result_that_is_invalid_for_its_phase()
    {
        LedgerDocument ledger = ReviewData.Ledger();
        JournalState[] invalid =
        [
            new(OperationPhase.Planned, JournalResult.Planned),
            new(OperationPhase.Started, JournalResult.InProgress),
            new(OperationPhase.Completed, JournalResult.Failed),
        ];

        IReadOnlyList<JournalEntry> journal = JournalChain.Create(HostOperation.Op03, ledger.ProductHostId,
            ReviewData.OperationId('6'), 6, ReviewData.Hash('2'), ReviewData.Hash('3'), ReviewData.Hash('4'), ReviewData.Hash('5'),
            ReviewData.Context('1'), new DateTimeOffset(2026, 8, 14, 14, 0, 0, TimeSpan.Zero), invalid);

        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(ledger with { Journal = journal }).Code);
    }

    [Fact]
    public void All_twenty_six_typed_crash_snapshots_have_independently_expected_recovery()
    {
        Dictionary<CrashBoundary, CrashRecoveryDisposition> expected = new()
        {
            [CrashBoundary.BeforeJournalPlan] = CrashRecoveryDisposition.RestartFromFreshPreview,
            [CrashBoundary.AfterJournalPlan] = CrashRecoveryDisposition.ResumeReadOnlyReconciliation,
            [CrashBoundary.BeforePrimitiveJournal] = CrashRecoveryDisposition.ResumeReadOnlyReconciliation,
            [CrashBoundary.AfterPrimitiveJournal] = CrashRecoveryDisposition.ResumeReadOnlyReconciliation,
            [CrashBoundary.BeforePrimitive] = CrashRecoveryDisposition.ResumeReadOnlyReconciliation,
            [CrashBoundary.AfterPrimitive] = CrashRecoveryDisposition.VerifyExactPrimitiveThenRecover,
            [CrashBoundary.BeforePrimitiveVerificationJournal] = CrashRecoveryDisposition.VerifyExactPrimitiveThenRecover,
            [CrashBoundary.AfterPrimitiveVerificationJournal] = CrashRecoveryDisposition.ResumeReadOnlyReconciliation,
            [CrashBoundary.BeforePrimaryAtomicReplace] = CrashRecoveryDisposition.FinishProtectedCopyRecovery,
            [CrashBoundary.AfterPrimaryAtomicReplace] = CrashRecoveryDisposition.FinishProtectedCopyRecovery,
            [CrashBoundary.BeforeMirrorAtomicReplace] = CrashRecoveryDisposition.FinishProtectedCopyRecovery,
            [CrashBoundary.AfterMirrorAtomicReplace] = CrashRecoveryDisposition.FinishProtectedCopyRecovery,
            [CrashBoundary.BeforeRevisionAdvance] = CrashRecoveryDisposition.FinishProtectedCopyRecovery,
            [CrashBoundary.AfterRevisionAdvance] = CrashRecoveryDisposition.CompleteIdempotently,
        };
        string[] primitives = ["p1", "p2", "p3"];
        InMemoryTransactionPrototype prototype = new(7);
        IReadOnlyList<CrashPoint> points = CrashBoundaryCatalog.ForPlan(primitives);

        Assert.Equal(26, points.Count);
        foreach (CrashPoint point in points)
        {
            CrashExecutionResult execution = prototype.ExecuteUntilCrash(primitives, point);
            CrashRecoveryDisposition expectedDisposition = point.Boundary == CrashBoundary.AfterPrimitiveVerificationJournal &&
                point.PrimitiveId == primitives[^1]
                    ? CrashRecoveryDisposition.FinishProtectedCopyRecovery
                    : expected[point.Boundary];
            Assert.Equal(CrashExecutionCode.Snapshot, execution.Code);
            Assert.NotNull(execution.Snapshot);
            Assert.Equal(expectedDisposition, execution.Snapshot.Recovery.Disposition);
            Assert.True(execution.Snapshot.Recovery.AutomationReadOnlyUntilRevalidated);
            Assert.False(execution.Snapshot.Recovery.LedgerAuthorizesMutation);
            Assert.Equal(execution.Snapshot.DurableState.JournalRecords.Count,
                execution.Snapshot.DurableState.JournalRecords.Select(record => record.Sequence).Distinct().Count());
        }
    }

    [Fact]
    public void Crash_recovery_rejects_unknown_typed_journal_record_and_revision_exhaustion_without_throwing()
    {
        string[] primitives = ["p1", "p2", "p3"];
        CrashExecutionResult execution = new InMemoryTransactionPrototype(7).ExecuteUntilCrash(
            primitives, new(CrashBoundary.AfterJournalPlan, null));
        Assert.NotNull(execution.Snapshot);
        DurableCrashState malformed = execution.Snapshot.DurableState with
        {
            JournalRecords = [.. execution.Snapshot.DurableState.JournalRecords, new((CrashJournalRecordKind)999, null, 1)],
        };

        Assert.Equal(CrashRecoveryDisposition.Undefined, CrashRecoveryMatrix.Evaluate(malformed).Disposition);
        DurableCrashState reordered = execution.Snapshot.DurableState with
        {
            JournalRecords = [new(CrashJournalRecordKind.Planned, null, 0), new(CrashJournalRecordKind.PrimitiveStarted, "p2", 1)],
        };
        Assert.Equal(CrashRecoveryDisposition.Undefined, CrashRecoveryMatrix.Evaluate(reordered).Disposition);
        CrashExecutionResult exhausted = new InMemoryTransactionPrototype(long.MaxValue).ExecuteUntilCrash(
            primitives, new(CrashBoundary.AfterPrimaryAtomicReplace, null));
        Assert.Equal(CrashExecutionCode.RefusedRevisionExhausted, exhausted.Code);
        Assert.Null(exhausted.Snapshot);
    }

    [Fact]
    public void Revision_commit_rejects_a_p1_only_subplan_when_the_exact_plan_is_p1_p2_p3()
    {
        DurableCrashState incomplete = new(
            ["p1", "p2", "p3"],
            ["p1"],
            [
                new(CrashJournalRecordKind.Planned, null, 0),
                new(CrashJournalRecordKind.PrimitiveStarted, "p1", 1),
                new(CrashJournalRecordKind.PrimitiveVerified, "p1", 2),
                new(CrashJournalRecordKind.RevisionCommitted, null, 3),
            ],
            7,
            7,
            7,
            ReviewData.Hash('1'),
            ReviewData.Hash('1'),
            ReviewData.OperationId('1'));

        Assert.Equal(CrashRecoveryDisposition.Undefined, CrashRecoveryMatrix.Evaluate(incomplete).Disposition);
    }

    [Fact]
    public void Protected_copy_recovery_requires_complete_revision_hash_and_commit_evidence()
    {
        ProtectedLedgerRecoveryEvidence valid = new(ProtectedCopyState.Valid, ProtectedCopyState.Valid,
            JournalValid: true, CopiesEquivalent: true, PrimaryRevision: 7, MirrorRevision: 7, CommittedRevision: 7,
            ReviewData.Hash('1'), ReviewData.Hash('1'));

        Assert.Equal(LedgerRecoveryDisposition.Healthy, LedgerRecoveryPolicy.Evaluate(valid).Disposition);
        Assert.Equal(LedgerRecoveryDisposition.UnknownReadOnly,
            LedgerRecoveryPolicy.Evaluate(valid with { CommittedRevision = null }).Disposition);
        Assert.Equal(LedgerRecoveryDisposition.UnknownReadOnly,
            LedgerRecoveryPolicy.Evaluate(valid with { MirrorRevision = 6 }).Disposition);
        Assert.Equal(LedgerRecoveryDisposition.UnknownReadOnly,
            LedgerRecoveryPolicy.Evaluate(valid with { MirrorHash = ReviewData.Hash('2') }).Disposition);
    }

    [Fact]
    public void Public_policy_surface_exposes_only_complete_proof_signatures()
    {
        Type[] crashParameters = typeof(CrashRecoveryMatrix).GetMethods().Where(method => method.IsPublic && method.Name == "Evaluate")
            .Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Type[] replayParameters = typeof(ReplayPolicy).GetMethods().Where(method => method.IsPublic && method.Name == "Evaluate")
            .Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Type[] copyParameters = typeof(LedgerRecoveryPolicy).GetMethods().Where(method => method.IsPublic && method.Name == "Evaluate")
            .Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        System.Reflection.MethodInfo convergenceMethod = typeof(ConvergencePolicy).GetMethods()
            .Single(method => method.IsPublic && method.Name == "Evaluate");
        Type[] convergenceParameters = convergenceMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Type? weakRetention = typeof(RetentionStore).Assembly.GetType("BallsServer.LedgerRecovery.RetentionPolicy");

        Assert.Equal([typeof(DurableCrashState)], crashParameters);
        Assert.Equal([typeof(ReplayEvidence), typeof(ReplayEvidence)], replayParameters);
        Assert.Equal([typeof(ProtectedLedgerRecoveryEvidence)], copyParameters);
        Assert.Equal([typeof(ReconciliationInput), typeof(DesiredResourceState)], convergenceParameters);
        Assert.True(weakRetention is null || !weakRetention.IsPublic);

        LedgerDocument ledger = ReviewData.Ledger();
        ReconciliationInput rawProof = new(ledger.Resources[0], null, new(LiveEvidenceKind.Absent, null, null, null),
            ledger.ProductHostId, ledger.Revision);
        ConvergenceResult result = Assert.IsType<ConvergenceResult>(convergenceMethod.Invoke(null,
            [rawProof, DesiredResourceState.Absent]));
        Assert.Equal(ConvergenceDisposition.PreserveUnownedAbsence, result.Disposition);
    }

    [Fact]
    public void Retention_accepts_only_closed_non_secret_fingerprints_and_preserves_structured_evidence_exactly()
    {
        DateTimeOffset removed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        RetentionCollections valid = new(
            [new(RetentionRecordKind.Tombstone, "retention:11111111111111111111111111111111", removed, EvidenceFingerprint: ReviewData.Hash('1'))],
            [new(RetentionRecordKind.Audit, "retention:22222222222222222222222222222222", removed, EvidenceFingerprint: ReviewData.Hash('2'))],
            [new(RetentionRecordKind.SupersededEvidence, "retention:33333333333333333333333333333333", removed, EvidenceFingerprint: ReviewData.Hash('3'))]);
        string before = System.Text.Json.JsonSerializer.Serialize(valid);

        RetentionPurgeResult retained = RetentionStore.Purge(valid,
            new(removed, removed.AddDays(89), true, true, true, []));
        RetentionCollections secret = valid with
        {
            AuditRecords = [valid.AuditRecords[0] with { EvidenceFingerprint = "hunter2" }],
        };
        RetentionPurgeResult refused = RetentionStore.Purge(secret,
            new(removed, removed.AddDays(91), true, true, true, []));

        Assert.Equal(RetentionDisposition.Retain, retained.Disposition);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(retained.Remaining));
        Assert.Equal(RetentionDisposition.CleanupNeeded, refused.Disposition);
        Assert.Same(secret, refused.Remaining);
        Assert.DoesNotContain(typeof(RetentionRecord).GetProperties(), property => property.Name.Contains("Bytes", StringComparison.Ordinal));
    }
}
