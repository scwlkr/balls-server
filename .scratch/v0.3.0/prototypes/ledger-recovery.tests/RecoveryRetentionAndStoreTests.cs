using BallsServer.LedgerRecovery;

namespace BallsServer.LedgerRecovery.Tests;

public sealed class RecoveryRetentionAndStoreTests
{
    [Theory]
    [InlineData(ProtectedCopyState.Valid, ProtectedCopyState.Valid, true, LedgerRecoveryDisposition.Healthy)]
    [InlineData(ProtectedCopyState.Corrupt, ProtectedCopyState.Valid, true, LedgerRecoveryDisposition.RebuildPrimaryFromMirror)]
    [InlineData(ProtectedCopyState.Valid, ProtectedCopyState.Corrupt, true, LedgerRecoveryDisposition.RebuildMirrorFromPrimary)]
    [InlineData(ProtectedCopyState.Missing, ProtectedCopyState.Valid, true, LedgerRecoveryDisposition.RebuildPrimaryFromMirror)]
    [InlineData(ProtectedCopyState.Valid, ProtectedCopyState.Missing, true, LedgerRecoveryDisposition.RebuildMirrorFromPrimary)]
    [InlineData(ProtectedCopyState.Corrupt, ProtectedCopyState.Corrupt, true, LedgerRecoveryDisposition.TotalLossReadOnly)]
    [InlineData(ProtectedCopyState.Missing, ProtectedCopyState.Missing, true, LedgerRecoveryDisposition.TotalLossReadOnly)]
    [InlineData(ProtectedCopyState.Corrupt, ProtectedCopyState.Corrupt, false, LedgerRecoveryDisposition.TotalLossReadOnly)]
    [InlineData(ProtectedCopyState.Missing, ProtectedCopyState.Missing, false, LedgerRecoveryDisposition.TotalLossReadOnly)]
    [InlineData(ProtectedCopyState.WeakAcl, ProtectedCopyState.Valid, true, LedgerRecoveryDisposition.UnknownReadOnly)]
    [InlineData(ProtectedCopyState.Valid, ProtectedCopyState.Valid, false, LedgerRecoveryDisposition.UnknownReadOnly)]
    [InlineData(ProtectedCopyState.UnsupportedSchema, ProtectedCopyState.Valid, true, LedgerRecoveryDisposition.RefusedUnsupportedSchema)]
    public void Primary_mirror_and_journal_recovery_is_fail_closed(
        ProtectedCopyState primary,
        ProtectedCopyState mirror,
        bool journalValid,
        LedgerRecoveryDisposition expected)
    {
        LedgerRecoveryResult result = LedgerRecoveryPolicy.Evaluate(new(primary, mirror, journalValid, CopiesEquivalent: true,
            PrimaryRevision: 7, MirrorRevision: 7, CommittedRevision: 7, ReviewData.Hash('1'), ReviewData.Hash('1')));

        Assert.Equal(expected, result.Disposition);
        Assert.False(result.AuthorizesWindowsMutation);
    }

    [Fact]
    public void Divergent_valid_copies_are_unknown_and_read_only()
    {
        LedgerRecoveryResult result = LedgerRecoveryPolicy.Evaluate(new(
            ProtectedCopyState.Valid, ProtectedCopyState.Valid, JournalValid: true, CopiesEquivalent: false,
            PrimaryRevision: 7, MirrorRevision: 7, CommittedRevision: 7, ReviewData.Hash('1'), ReviewData.Hash('2')));

        Assert.Equal(LedgerRecoveryDisposition.UnknownReadOnly, result.Disposition);
        Assert.True(result.AutomationReadOnly);
    }

    [Fact]
    public void Atomic_store_advances_revision_only_after_primary_and_mirror_replace()
    {
        LedgerDocument current = ReviewData.Ledger();
        InMemoryProtectedLedgerStore store = new(current);

        LedgerDocument next = current with
        {
            Revision = 8,
            Endpoints = current.Endpoints.Select(endpoint => endpoint with { ObservationEpoch = 8 }).ToArray(),
            Journal = JournalChain.CreateCompleted(HostOperation.Op03, ReviewData.HostId, ReviewData.OperationId('2'), 7,
                ReviewData.Hash('2'), ReviewData.Hash('3'), ReviewData.Hash('4'), ReviewData.Hash('5'), ReviewData.Context('1'),
                new DateTimeOffset(2026, 8, 14, 13, 0, 0, TimeSpan.Zero)),
        };
        StoreCommitResult result = store.Commit(next);

        Assert.Equal(StoreCommitDisposition.Committed, result.Disposition);
        Assert.Equal(8, store.Primary.Revision);
        Assert.Equal(8, store.Mirror.Revision);
        Assert.Equal(8, store.CommittedRevision);
        Assert.True(store.JournalValid);
    }

    [Theory]
    [InlineData(RecoveryScenario.StaleRevision, RecoveryAction.RefreshPreview)]
    [InlineData(RecoveryScenario.CorruptPrimary, RecoveryAction.RebuildPrimaryFromMirror)]
    [InlineData(RecoveryScenario.CorruptMirror, RecoveryAction.RebuildMirrorFromPrimary)]
    [InlineData(RecoveryScenario.TotalLedgerLoss, RecoveryAction.ReadOnlyManifest)]
    [InlineData(RecoveryScenario.Drift, RecoveryAction.RepairNeeded)]
    [InlineData(RecoveryScenario.MissingObject, RecoveryAction.RecreateOnlyWithUnambiguousStableProof)]
    [InlineData(RecoveryScenario.UnmanagedConflict, RecoveryAction.RefuseAndPreserve)]
    [InlineData(RecoveryScenario.Ambiguity, RecoveryAction.RefuseAndPreserve)]
    [InlineData(RecoveryScenario.AccessDenied, RecoveryAction.UnknownAndAdministratorHandoff)]
    [InlineData(RecoveryScenario.PolicyOverride, RecoveryAction.RefuseAndPolicyOwnerHandoff)]
    [InlineData(RecoveryScenario.UnknownObservation, RecoveryAction.UnknownAndAdministratorHandoff)]
    public void Every_required_recovery_scenario_has_an_exact_outcome(RecoveryScenario scenario, RecoveryAction expected)
    {
        ScenarioRecoveryResult result = RecoveryScenarioPolicy.Evaluate(scenario);

        Assert.Equal(expected, result.Action);
        Assert.False(result.AuthorizesAdoptionOrNameBasedDeletion);
    }

    [Fact]
    public void Total_loss_manifest_is_non_authoritative_object_by_object_and_preserves_data()
    {
        RecoveryManifest manifest = RecoveryManifestBuilder.ForTotalLoss(
        [
            new RecoveryCandidate(ResourceKind.Group, "group-object-id", "group-fingerprint"),
            new RecoveryCandidate(ResourceKind.Share, "share-object-id", "share-fingerprint"),
            new RecoveryCandidate(ResourceKind.Grant, "grant-object-id", "grant-fingerprint"),
            new RecoveryCandidate(ResourceKind.Ace, "ace-object-id", "ace-fingerprint"),
        ]);

        Assert.False(manifest.Authoritative);
        Assert.False(manifest.AuthorizesAdoption);
        Assert.False(manifest.AuthorizesDeletion);
        Assert.Equal([ResourceKind.Grant, ResourceKind.Share, ResourceKind.Ace, ResourceKind.Group], manifest.Items.Select(item => item.Kind));
        Assert.All(manifest.Items, item => Assert.True(item.RequiresAdministratorStableIdentityConfirmation));
        Assert.True(manifest.PreserveManagedFolder);
        Assert.True(manifest.PreserveUserFiles);
    }

    [Theory]
    [InlineData(89, 23, 59, RetentionDisposition.Retain)]
    [InlineData(90, 0, 0, RetentionDisposition.Purge)]
    [InlineData(91, 0, 0, RetentionDisposition.Purge)]
    public void Ninety_day_retention_boundary_is_exact(int days, int hours, int minutes, RetentionDisposition expected)
    {
        DateTimeOffset removedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = removedAt.AddDays(days).AddHours(hours).AddMinutes(minutes);

        RetentionPurgeResult result = RetentionStore.Purge(ReviewData.RetentionCollections(removedAt),
            new(removedAt, now, ChainValid: true, ClockTrusted: true, AccessAvailable: true, ActiveReferenceIds: []));

        Assert.Equal(expected, result.Disposition);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Untrusted_clock_or_active_reference_blocks_purge(bool activeReference, bool clockTrusted)
    {
        DateTimeOffset removedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        RetentionPurgeResult result = RetentionStore.Purge(ReviewData.RetentionCollections(removedAt),
            new(removedAt, removedAt.AddDays(100), ChainValid: true, ClockTrusted: clockTrusted, AccessAvailable: true,
                ActiveReferenceIds: activeReference ? ["retention:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"] : []));

        if (!clockTrusted || activeReference)
        {
            Assert.Equal(RetentionDisposition.CleanupNeeded, result.Disposition);
        }
    }
}
