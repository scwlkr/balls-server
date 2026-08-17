using BallsServer.LedgerRecovery;

namespace BallsServer.LedgerRecovery.Tests;

public sealed class ReconciliationAndRollbackTests
{
    public static TheoryData<bool, LiveEvidenceKind, ReconciliationClass> CrossProduct => new()
    {
        { true, LiveEvidenceKind.Exact, ReconciliationClass.OwnedConformant },
        { true, LiveEvidenceKind.Drifted, ReconciliationClass.OwnedDrifted },
        { true, LiveEvidenceKind.Absent, ReconciliationClass.Missing },
        { true, LiveEvidenceKind.Foreign, ReconciliationClass.UnmanagedConflict },
        { true, LiveEvidenceKind.Multiple, ReconciliationClass.Ambiguous },
        { true, LiveEvidenceKind.Unknown, ReconciliationClass.Unknown },
        { true, LiveEvidenceKind.AccessDenied, ReconciliationClass.Unknown },
        { true, LiveEvidenceKind.PolicyOverride, ReconciliationClass.Unknown },
        { false, LiveEvidenceKind.Exact, ReconciliationClass.UnmanagedConflict },
        { false, LiveEvidenceKind.Drifted, ReconciliationClass.UnmanagedConflict },
        { false, LiveEvidenceKind.Absent, ReconciliationClass.Missing },
        { false, LiveEvidenceKind.Foreign, ReconciliationClass.UnmanagedConflict },
        { false, LiveEvidenceKind.Multiple, ReconciliationClass.Ambiguous },
        { false, LiveEvidenceKind.Unknown, ReconciliationClass.Unknown },
        { false, LiveEvidenceKind.AccessDenied, ReconciliationClass.Unknown },
        { false, LiveEvidenceKind.PolicyOverride, ReconciliationClass.Unknown },
    };

    [Theory]
    [MemberData(nameof(CrossProduct))]
    public void Reconciliation_cross_product_is_deterministic(bool protectedRecord, LiveEvidenceKind liveKind, ReconciliationClass expected)
    {
        LedgerDocument ledger = ReviewData.Ledger();
        ResourceRecord desired = ledger.Resources[0];
        ProtectedOwnershipRecord? ownership = protectedRecord ? ProtectedOwnershipRecord.Create(desired, ledger.ProductHostId, ledger.Revision) : null;
        LiveResourceEvidence live = liveKind == LiveEvidenceKind.Absent
            ? new(liveKind, null, null, null)
            : new(liveKind, desired.StableId,
                liveKind is LiveEvidenceKind.Exact ? desired.CanonicalFingerprint : ReviewData.Hash('9'), desired.ContextBinding);

        Assert.Equal(expected, ReconciliationEngine.Reconcile(new(desired, ownership, live, ledger.ProductHostId, ledger.Revision)).Classification);
    }

    [Fact]
    public void Live_managed_boolean_or_friendly_name_cannot_prove_ownership()
    {
        LedgerDocument ledger = ReviewData.Ledger();
        ResourceRecord desired = ledger.Resources[0];
        ReconciliationInput input = new(desired, null,
            new(LiveEvidenceKind.Exact, desired.StableId, desired.CanonicalFingerprint, desired.ContextBinding, true, "Balls"), ledger.ProductHostId, ledger.Revision);

        Assert.Equal(ReconciliationClass.UnmanagedConflict, ReconciliationEngine.Reconcile(input).Classification);
    }

    [Theory]
    [InlineData(true, true, RollbackDisposition.RemoveExactCurrentTransactionObject)]
    [InlineData(false, true, RollbackDisposition.PreserveNoOwnership)]
    [InlineData(true, false, RollbackDisposition.RepairNeeded)]
    public void Rollback_removes_only_unchanged_current_transaction_objects(bool created, bool exact, RollbackDisposition expected)
    {
        AppliedPrimitive primitive = ReviewData.AppliedPrimitive() with { CreatedByCurrentTransaction = created };
        LiveResourceEvidence live = new(LiveEvidenceKind.Exact, exact ? primitive.StableId : ReviewData.ResourceId('9'),
            primitive.PostconditionFingerprint, primitive.ContextBinding);
        RollbackRequest request = new(primitive, primitive.OperationId, primitive.Revision, primitive.ContextBinding, true, true, false, false, true, live);

        Assert.Equal(expected, RollbackPolicy.Evaluate(request));
    }

    [Theory]
    [InlineData(RemovalPoint.ExplicitOwnedRemoval, true)]
    [InlineData(RemovalPoint.RecoveryCleanup, true)]
    [InlineData(RemovalPoint.Setup, false)]
    [InlineData(RemovalPoint.Repair, false)]
    public void Not_found_is_success_only_at_documented_removal_points(RemovalPoint point, bool expected) =>
        Assert.Equal(expected, RemovalPolicy.NotFoundIsSuccess(point));

    [Theory]
    [InlineData(ReconciliationClass.OwnedConformant, DesiredResourceState.Present, true, ConvergenceDisposition.VerifyNoChange)]
    [InlineData(ReconciliationClass.Missing, DesiredResourceState.Present, false, ConvergenceDisposition.CreateOnceAfterAuthorization)]
    [InlineData(ReconciliationClass.Missing, DesiredResourceState.Absent, false, ConvergenceDisposition.PreserveUnownedAbsence)]
    [InlineData(ReconciliationClass.OwnedConformant, DesiredResourceState.Absent, true, ConvergenceDisposition.RemoveExactOwnedObject)]
    [InlineData(ReconciliationClass.OwnedDrifted, DesiredResourceState.Present, true, ConvergenceDisposition.RepairNeeded)]
    [InlineData(ReconciliationClass.UnmanagedConflict, DesiredResourceState.Present, false, ConvergenceDisposition.Refused)]
    [InlineData(ReconciliationClass.Ambiguous, DesiredResourceState.Present, false, ConvergenceDisposition.Refused)]
    [InlineData(ReconciliationClass.Unknown, DesiredResourceState.Present, false, ConvergenceDisposition.Unknown)]
    public void Repeated_setup_and_removal_converge_without_duplicates(ReconciliationClass classification, DesiredResourceState desired,
        bool hasOwnership, ConvergenceDisposition expected)
    {
        LedgerDocument ledger = ReviewData.Ledger();
        ResourceRecord resource = ledger.Resources[0];
        ProtectedOwnershipRecord? ownership = hasOwnership ? ProtectedOwnershipRecord.Create(resource, ledger.ProductHostId, ledger.Revision) : null;
        LiveResourceEvidence live = classification switch
        {
            ReconciliationClass.Missing => new(LiveEvidenceKind.Absent, null, null, null),
            ReconciliationClass.OwnedDrifted => new(LiveEvidenceKind.Drifted, resource.StableId, ReviewData.Hash('9'), resource.ContextBinding),
            ReconciliationClass.Ambiguous => new(LiveEvidenceKind.Multiple, resource.StableId, ReviewData.Hash('9'), resource.ContextBinding),
            ReconciliationClass.Unknown => new(LiveEvidenceKind.Unknown, resource.StableId, ReviewData.Hash('9'), resource.ContextBinding),
            _ => new(LiveEvidenceKind.Exact, resource.StableId, resource.CanonicalFingerprint, resource.ContextBinding),
        };
        ConvergenceResult result = ConvergencePolicy.Evaluate(new(resource, ownership, live, ledger.ProductHostId, ledger.Revision), desired);
        Assert.Equal(expected, result.Disposition);
        Assert.InRange(result.MaximumCreates, 0, 1);
        Assert.False(result.AdoptsUnmanagedObject);
    }
}
