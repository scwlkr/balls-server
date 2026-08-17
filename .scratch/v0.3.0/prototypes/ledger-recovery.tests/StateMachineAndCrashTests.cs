using BallsServer.LedgerRecovery;

namespace BallsServer.LedgerRecovery.Tests;

public sealed class StateMachineAndCrashTests
{
    [Fact]
    public void Every_state_pair_matches_the_explicit_transition_contract()
    {
        HashSet<(OperationPhase From, OperationPhase To)> expected =
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

        foreach (OperationPhase from in Enum.GetValues<OperationPhase>())
        {
            foreach (OperationPhase to in Enum.GetValues<OperationPhase>())
            {
                Assert.Equal(expected.Contains((from, to)), OperationStateMachine.CanTransition(from, to));
            }
        }
    }

    [Fact]
    public void Crash_catalog_covers_both_sides_of_each_primitive_and_atomic_boundary()
    {
        string[] primitives = ["group", "ace", "share"];

        IReadOnlyList<CrashPoint> points = CrashBoundaryCatalog.ForPlan(primitives);

        foreach (string primitive in primitives)
        {
            Assert.Contains(new CrashPoint(CrashBoundary.BeforePrimitive, primitive), points);
            Assert.Contains(new CrashPoint(CrashBoundary.AfterPrimitive, primitive), points);
            Assert.Contains(new CrashPoint(CrashBoundary.BeforePrimitiveJournal, primitive), points);
            Assert.Contains(new CrashPoint(CrashBoundary.AfterPrimitiveJournal, primitive), points);
            Assert.Contains(new CrashPoint(CrashBoundary.BeforePrimitiveVerificationJournal, primitive), points);
            Assert.Contains(new CrashPoint(CrashBoundary.AfterPrimitiveVerificationJournal, primitive), points);
        }

        foreach (CrashBoundary boundary in CrashBoundaryCatalog.AtomicBoundaries)
        {
            Assert.Contains(new CrashPoint(boundary, null), points);
        }
    }

    [Fact]
    public void Every_crash_injection_has_one_fail_closed_recovery_result()
    {
        IReadOnlyList<CrashPoint> points = CrashBoundaryCatalog.ForPlan(["group", "ace", "share"]);

        foreach (CrashPoint point in points)
        {
            CrashExecutionSnapshot snapshot = new InMemoryTransactionPrototype(7).ExecuteUntilCrash(["group", "ace", "share"], point).Snapshot!;
            CrashRecoveryResult result = CrashRecoveryMatrix.Evaluate(snapshot.DurableState);

            Assert.NotEqual(CrashRecoveryDisposition.Undefined, result.Disposition);
            Assert.True(result.AutomationReadOnlyUntilRevalidated);
            Assert.True(result.RequiresFreshAuthorizationForMutation);
            Assert.False(result.LedgerAuthorizesMutation);
            Assert.False(result.DeleteManagedFolderOrFiles);
        }
    }

    [Fact]
    public void In_memory_transaction_injects_a_real_crash_at_every_catalog_point()
    {
        string[] primitives = ["group", "ace", "share"];
        InMemoryTransactionPrototype prototype = new(initialRevision: 7);

        foreach (CrashPoint point in CrashBoundaryCatalog.ForPlan(primitives))
        {
            CrashExecutionResult execution = prototype.ExecuteUntilCrash(primitives, point);
            CrashExecutionSnapshot snapshot = execution.Snapshot!;

            Assert.Equal(CrashExecutionCode.Snapshot, execution.Code);
            Assert.Equal(point, snapshot.InjectedAt);
            Assert.Equal(snapshot.AppliedStableIds.Count, snapshot.AppliedStableIds.Distinct(StringComparer.Ordinal).Count());
            Assert.InRange(snapshot.CommittedRevision, 7, 8);
            Assert.False(snapshot.Recovery.LedgerAuthorizesMutation);
        }
    }

    [Fact]
    public void Crash_after_primitive_records_one_live_effect_and_crash_after_revision_is_committed()
    {
        string[] primitives = ["group", "ace", "share"];
        InMemoryTransactionPrototype prototype = new(initialRevision: 7);

        CrashExecutionSnapshot afterGroup = prototype.ExecuteUntilCrash(
            primitives,
            new CrashPoint(CrashBoundary.AfterPrimitive, "group")).Snapshot!;
        CrashExecutionSnapshot afterRevision = prototype.ExecuteUntilCrash(
            primitives,
            new CrashPoint(CrashBoundary.AfterRevisionAdvance, null)).Snapshot!;

        Assert.Equal(["group"], afterGroup.AppliedStableIds);
        Assert.Equal(7, afterGroup.CommittedRevision);
        Assert.Equal(["group", "ace", "share"], afterRevision.AppliedStableIds);
        Assert.Equal(8, afterRevision.PrimaryRevision);
        Assert.Equal(8, afterRevision.MirrorRevision);
        Assert.Equal(8, afterRevision.CommittedRevision);
    }

}
