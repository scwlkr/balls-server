using System.Text.Json;
using BallsServer.LedgerRecovery;

namespace BallsServer.LedgerRecovery.Tests;

public sealed class ReviewFixAdversarialTests
{
    [Fact]
    public void Closed_ledger_contract_refuses_null_collections_undefined_enums_and_password_shaped_identity()
    {
        LedgerDocument valid = ReviewData.Ledger();

        Assert.Equal(LedgerValidationCode.Malformed, LedgerContract.Validate(null).Code);
        Assert.Equal(LedgerValidationCode.Malformed, LedgerContract.Validate(valid with { Resources = null! }).Code);
        Assert.Equal(LedgerValidationCode.InvalidResource, LedgerContract.Validate(valid with
        {
            Resources = [valid.Resources[0] with { Kind = (ResourceKind)999 }],
        }).Code);
        Assert.Equal(LedgerValidationCode.InvalidResource, LedgerContract.Validate(valid with
        {
            Resources = [valid.Resources[0] with { StableId = "hunter2" }],
        }).Code);
    }

    [Fact]
    public void Json_scanner_rejects_duplicate_keys_at_every_depth_and_credential_payload_names()
    {
        SecretScanResult duplicate = SecretMaterialScanner.ScanJson("{\"outer\":{\"id\":1,\"id\":2}}");
        SecretScanResult credential = SecretMaterialScanner.ScanJson("{\"providerCredentialValue\":\"opaque\"}");

        Assert.False(duplicate.Safe);
        Assert.Contains("outer.id<duplicate>", duplicate.ForbiddenFields);
        Assert.False(credential.Safe);
        Assert.Contains("providerCredentialValue", credential.ForbiddenFields);
    }

    [Fact]
    public void Journal_chain_rejects_tamper_reorder_duplicate_and_cross_transaction_records()
    {
        LedgerDocument valid = ReviewData.Ledger();
        JournalEntry[] tampered = valid.Journal.ToArray();
        tampered[1] = tampered[1] with { PlanDigest = ReviewData.Hash('9') };
        JournalEntry[] reordered = [valid.Journal[1], valid.Journal[0], .. valid.Journal.Skip(2)];
        JournalEntry[] duplicated = [.. valid.Journal, valid.Journal[^1]];
        JournalEntry[] crossed = valid.Journal.ToArray();
        crossed[1] = crossed[1] with { OperationId = ReviewData.OperationId('9') };

        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = tampered }).Code);
        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = reordered }).Code);
        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = duplicated }).Code);
        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = crossed }).Code);
    }

    [Fact]
    public void Closed_graph_rejects_every_null_collection_duplicate_and_cross_context()
    {
        LedgerDocument valid = ReviewData.Ledger();
        LedgerDocument[] nullCollections =
        [
            valid with { Resources = null! }, valid with { Endpoints = null! }, valid with { Journal = null! },
            valid with { AuditReferences = null! }, valid with { Tombstones = null! }, valid with { ManagedFolder = null! },
        ];
        Assert.All(nullCollections, ledger => Assert.Equal(LedgerValidationCode.Malformed, LedgerContract.Validate(ledger).Code));

        Assert.Equal(LedgerValidationCode.CrossRecordMismatch, LedgerContract.Validate(valid with { Resources = [valid.Resources[0], valid.Resources[0]] }).Code);
        Assert.Equal(LedgerValidationCode.CrossRecordMismatch, LedgerContract.Validate(valid with
        {
            Resources = [valid.Resources[0] with { ContextBinding = ReviewData.Context('9') }],
        }).Code);
        Assert.Equal(LedgerValidationCode.InvalidEndpoint, LedgerContract.Validate(valid with
        {
            Endpoints = [valid.Endpoints[0], valid.Endpoints[0]],
        }).Code);
    }

    [Fact]
    public void Closed_graph_rejects_malformed_endpoint_audit_and_tombstone_fields()
    {
        LedgerDocument valid = ReviewData.Ledger();
        Assert.Equal(LedgerValidationCode.InvalidEndpoint, LedgerContract.Validate(valid with
        {
            Endpoints = [valid.Endpoints[0] with { Kind = (EndpointKind)999 }],
        }).Code);
        Assert.Equal(LedgerValidationCode.InvalidEndpoint, LedgerContract.Validate(valid with
        {
            Endpoints = [valid.Endpoints[0] with { ObservationEpoch = 8 }],
        }).Code);
        Assert.Equal(LedgerValidationCode.InvalidAudit, LedgerContract.Validate(valid with
        {
            AuditReferences = [valid.AuditReferences[0] with { ChainHash = "bad" }],
        }).Code);

        DateTimeOffset removed = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        RevokedGrantTombstone tombstone = new(ReviewData.ResourceId('a'), ReviewData.ResourceId('b'), 7,
            removed.AddMinutes(-1), removed, valid.AuditReferences[0].AuditId);
        LedgerDocument removedLedger = valid with { HostRemovedAt = removed, Tombstones = [tombstone] };
        Assert.Equal(LedgerValidationCode.Valid, LedgerContract.Validate(removedLedger).Code);
        Assert.Equal(LedgerValidationCode.InvalidTombstone, LedgerContract.Validate(removedLedger with
        {
            Tombstones = [tombstone with { RevokedAt = removed.AddMinutes(1) }],
        }).Code);
        Assert.Equal(LedgerValidationCode.InvalidTombstone, LedgerContract.Validate(removedLedger with
        {
            Tombstones = [tombstone with { AuditReferenceId = "audit:99999999999999999999999999999999" }],
        }).Code);
    }

    [Fact]
    public void Every_resource_field_is_closed_and_operation_kind_is_exact()
    {
        LedgerDocument valid = ReviewData.Ledger();
        ResourceRecord resource = valid.Resources[0];
        ResourceRecord[] invalid =
        [
            resource with { Kind = (ResourceKind)999 },
            resource with { StableId = "bad" },
            resource with { CanonicalFingerprint = "bad" },
            resource with { OwningOperation = (HostOperation)999 },
            resource with { OwningOperation = HostOperation.Op09 },
            resource with { OwnershipMarker = "bad" },
            resource with { ContextBinding = "bad" },
        ];

        Assert.All(invalid, item => Assert.Equal(LedgerValidationCode.InvalidResource,
            LedgerContract.Validate(valid with { Resources = [item] }).Code));
    }

    [Fact]
    public void Every_journal_binding_field_sequence_result_and_timestamp_is_closed()
    {
        LedgerDocument valid = ReviewData.Ledger();
        JournalEntry entry = valid.Journal[1];
        JournalEntry[] invalidEntries =
        [
            entry with { ProtocolVersion = "other" }, entry with { Operation = (HostOperation)999 },
            entry with { ProductHostId = "bad" }, entry with { OperationId = "bad" },
            entry with { ExpectedRevision = -1 }, entry with { PlanDigest = "bad" },
            entry with { PipeInstanceFingerprint = "bad" }, entry with { NonceFingerprint = "bad" },
            entry with { AuthorizationBindingFingerprint = "bad" }, entry with { ObservationContext = "bad" },
            entry with { Sequence = -1 }, entry with { PreviousRecordHash = "bad" }, entry with { RecordHash = "bad" },
            entry with { Phase = (OperationPhase)999 }, entry with { Result = (JournalResult)999 },
            entry with { Timestamp = DateTimeOffset.MinValue }, entry with { AuthorizationConsumed = false },
        ];

        foreach (JournalEntry invalid in invalidEntries)
        {
            JournalEntry[] journal = valid.Journal.ToArray();
            journal[1] = invalid;
            Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = journal }).Code);
        }
    }

    [Fact]
    public void Audit_uniqueness_order_and_tombstone_revision_removal_binding_are_closed()
    {
        LedgerDocument valid = ReviewData.Ledger();
        AuditReference later = new("audit:22222222222222222222222222222222", valid.AuditReferences[0].Timestamp.AddMinutes(1), ReviewData.Hash('9'));
        Assert.Equal(LedgerValidationCode.InvalidAudit, LedgerContract.Validate(valid with
        {
            AuditReferences = [later, valid.AuditReferences[0]],
        }).Code);
        Assert.Equal(LedgerValidationCode.InvalidAudit, LedgerContract.Validate(valid with
        {
            AuditReferences = [valid.AuditReferences[0], valid.AuditReferences[0]],
        }).Code);

        DateTimeOffset removed = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        RevokedGrantTombstone tombstone = new(ReviewData.ResourceId('a'), ReviewData.ResourceId('b'), 7,
            removed.AddMinutes(-1), removed, valid.AuditReferences[0].AuditId);
        LedgerDocument removedLedger = valid with { HostRemovedAt = removed, Tombstones = [tombstone] };
        Assert.Equal(LedgerValidationCode.InvalidTombstone, LedgerContract.Validate(removedLedger with
        {
            Tombstones = [tombstone with { CredentialRevision = 8 }],
        }).Code);
        Assert.Equal(LedgerValidationCode.InvalidTombstone, LedgerContract.Validate(removedLedger with
        {
            Tombstones = [tombstone with { HostRemovedAt = removed.AddDays(1) }],
        }).Code);
    }

    [Fact]
    public void Journal_chain_rejects_omission_noncanonical_phase_and_second_terminal()
    {
        LedgerDocument valid = ReviewData.Ledger();
        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = valid.Journal.Skip(1).ToArray() }).Code);
        JournalEntry[] undefined = valid.Journal.ToArray();
        undefined[1] = undefined[1] with { Phase = (OperationPhase)999 };
        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = undefined }).Code);
        JournalEntry[] secondTerminal = valid.Journal.ToArray();
        secondTerminal[1] = secondTerminal[1] with { Phase = OperationPhase.Refused };
        secondTerminal[1] = secondTerminal[1] with { RecordHash = JournalChain.ComputeHash(secondTerminal[1]) };
        Assert.Equal(LedgerValidationCode.InvalidJournal, LedgerContract.Validate(valid with { Journal = secondTerminal }).Code);
    }

    [Fact]
    public void Protected_ownership_must_bind_exact_resource_operation_revision_marker_and_context()
    {
        LedgerDocument ledger = ReviewData.Ledger();
        ResourceRecord desired = ledger.Resources[0];
        ProtectedOwnershipRecord proof = ProtectedOwnershipRecord.Create(desired, ledger.ProductHostId, ledger.Revision);
        LiveResourceEvidence live = new(LiveEvidenceKind.Exact, desired.StableId, desired.CanonicalFingerprint, desired.ContextBinding);

        Assert.Equal(ReconciliationClass.OwnedConformant, ReconciliationEngine.Reconcile(new(desired, proof, live, ledger.ProductHostId, ledger.Revision)).Classification);
        Assert.NotEqual(ReconciliationClass.OwnedConformant, ReconciliationEngine.Reconcile(new(desired, proof with { Revision = ledger.Revision - 1 }, live, ledger.ProductHostId, ledger.Revision)).Classification);
        Assert.NotEqual(ReconciliationClass.OwnedConformant, ReconciliationEngine.Reconcile(new(desired, proof with { OwningOperation = HostOperation.Op24 }, live, ledger.ProductHostId, ledger.Revision)).Classification);
        Assert.NotEqual(ReconciliationClass.OwnedConformant, ReconciliationEngine.Reconcile(new(desired, proof with { ProofHash = ReviewData.Hash('0') }, live, ledger.ProductHostId, ledger.Revision)).Classification);
    }

    [Fact]
    public void Self_consistently_hashed_but_malformed_ownership_can_never_conform()
    {
        LedgerDocument ledger = ReviewData.Ledger();
        ResourceRecord malformed = ledger.Resources[0] with { StableId = "hunter2" };
        ProtectedOwnershipRecord proof = ProtectedOwnershipRecord.Create(malformed, ledger.ProductHostId, ledger.Revision);
        LiveResourceEvidence live = new(LiveEvidenceKind.Exact, malformed.StableId, malformed.CanonicalFingerprint, malformed.ContextBinding);

        Assert.Equal(ReconciliationClass.Unknown,
            ReconciliationEngine.Reconcile(new(malformed, proof, live, ledger.ProductHostId, ledger.Revision)).Classification);
    }

    [Fact]
    public void Unowned_absence_never_becomes_owned_cleanup_success()
    {
        ResourceRecord desired = ReviewData.Ledger().Resources[0];
        LiveResourceEvidence absent = new(LiveEvidenceKind.Absent, null, null, null);
        ReconciliationInput input = new(desired, null, absent, ReviewData.HostId, 7);
        ReconciliationResult reconciliation = ReconciliationEngine.Reconcile(input);

        Assert.Equal(ReconciliationClass.Missing, reconciliation.Classification);
        ConvergenceResult result = ConvergencePolicy.Evaluate(input, DesiredResourceState.Absent);
        Assert.Equal(ConvergenceDisposition.PreserveUnownedAbsence, result.Disposition);
        Assert.False(result.ReportsOwnedRemoval);
    }

    [Fact]
    public void Rollback_requires_every_creation_binding_and_clear_observation()
    {
        AppliedPrimitive primitive = ReviewData.AppliedPrimitive();
        LiveResourceEvidence live = new(LiveEvidenceKind.Exact, primitive.StableId, primitive.PostconditionFingerprint, primitive.ContextBinding);
        RollbackRequest valid = new(primitive, primitive.OperationId, primitive.Revision, primitive.ContextBinding, CreationRecordValid: true,
            DependenciesUnchanged: true, InCurrentUse: false, PolicyBlocked: false, ObservationComplete: true, live);

        Assert.Equal(RollbackDisposition.RemoveExactCurrentTransactionObject, RollbackPolicy.Evaluate(valid));
        Assert.Equal(RollbackDisposition.RepairNeeded, RollbackPolicy.Evaluate(valid with { ActiveOperationId = ReviewData.OperationId('9') }));
        Assert.Equal(RollbackDisposition.RepairNeeded, RollbackPolicy.Evaluate(valid with { ContextBinding = ReviewData.Context('9') }));
        Assert.Equal(RollbackDisposition.Unknown, RollbackPolicy.Evaluate(valid with { ObservationComplete = false }));
        Assert.Equal(RollbackDisposition.RepairNeeded, RollbackPolicy.Evaluate(valid with { InCurrentUse = true }));
    }

    [Fact]
    public void Replay_requires_exact_terminal_operation_digest_revision_and_context()
    {
        ReplayEvidence valid = new(HostOperation.Op03, ReviewData.OperationId('1'), ReviewData.Hash('1'), 7, ReviewData.Context('1'),
            OperationPhase.Completed, EvidenceComplete: true);

        Assert.Equal(ReplayDisposition.CompletedWithoutMutation, ReplayPolicy.Evaluate(valid, valid).Disposition);
        Assert.Equal(ReplayDisposition.RefusedStaleRevision, ReplayPolicy.Evaluate(valid, valid with { Revision = 8 }).Disposition);
        Assert.Equal(ReplayDisposition.RefusedContextMismatch, ReplayPolicy.Evaluate(valid, valid with { ObservationContext = ReviewData.Context('2') }).Disposition);
        Assert.Equal(ReplayDisposition.RefusedUnknownOperation, ReplayPolicy.Evaluate(valid, valid with { Operation = (HostOperation)999 }).Disposition);
        Assert.Equal(ReplayDisposition.RefusedIncompleteEvidence, ReplayPolicy.Evaluate(valid, valid with { EvidenceComplete = false }).Disposition);
    }

    [Fact]
    public void Crash_recovery_uses_durable_state_and_refuses_missing_commit_marker()
    {
        InMemoryTransactionPrototype prototype = new(7);
        IReadOnlyList<string> primitives = ["p1", "p2", "p3"];
        CrashExecutionSnapshot afterPrimary = prototype.ExecuteUntilCrash(primitives, new(CrashBoundary.AfterPrimaryAtomicReplace, null)).Snapshot!;
        CrashExecutionSnapshot afterMirror = prototype.ExecuteUntilCrash(primitives, new(CrashBoundary.AfterMirrorAtomicReplace, null)).Snapshot!;

        Assert.Equal((8L, 7L, 7L), (afterPrimary.PrimaryRevision, afterPrimary.MirrorRevision, afterPrimary.CommittedRevision));
        Assert.Equal(CrashRecoveryDisposition.FinishProtectedCopyRecovery, CrashRecoveryMatrix.Evaluate(afterPrimary.DurableState).Disposition);
        Assert.Equal((8L, 8L, 7L), (afterMirror.PrimaryRevision, afterMirror.MirrorRevision, afterMirror.CommittedRevision));
        Assert.Equal(CrashRecoveryDisposition.FinishProtectedCopyRecovery, CrashRecoveryMatrix.Evaluate(afterMirror.DurableState).Disposition);
        Assert.Equal(CrashRecoveryDisposition.Undefined, CrashRecoveryMatrix.Evaluate(afterMirror.DurableState with { CommittedRevision = null }).Disposition);
        Assert.Equal(CrashRecoveryDisposition.Undefined, CrashRecoveryMatrix.Evaluate(afterMirror.DurableState with
        {
            JournalRecords = [new(CrashJournalRecordKind.Planned, null, 0), new(CrashJournalRecordKind.PrimitiveStarted, "p1", 1)],
        }).Disposition);
    }

    [Fact]
    public void Exhausted_revision_returns_typed_refusal_without_throwing()
    {
        LedgerDocument max = ReviewData.Ledger() with { Revision = long.MaxValue };
        InMemoryProtectedLedgerStore store = new(max);

        StoreCommitResult result = store.Commit(max);

        Assert.Equal(StoreCommitDisposition.RefusedRevisionExhausted, result.Disposition);
    }

    [Fact]
    public void Consent_refusal_and_undefined_phases_are_fail_closed()
    {
        Assert.True(OperationStateMachine.CanTransition(OperationPhase.AwaitingHelperConsent, OperationPhase.Refused));
        Assert.False(OperationStateMachine.CanTransition((OperationPhase)999, OperationPhase.Completed));
        Assert.False(OperationStateMachine.CanTransition(OperationPhase.Completed, (OperationPhase)999));
    }

    [Fact]
    public void Host_resource_kind_is_closed_and_client_intent_cannot_be_converted_to_host_authority()
    {
        Assert.DoesNotContain(Enum.GetNames<ResourceKind>(), name => name.Contains("Client", StringComparison.Ordinal));
        ClientIntentRecord client = ReviewData.ClientIntent();

        Assert.False(ClientIntentContract.Validate(client).CanBecomeProtectedHostAuthority);
        Assert.Equal(ClientIntentValidationCode.ValidCurrentUserIntent, ClientIntentContract.Validate(client).Code);
    }

    [Fact]
    public void Total_loss_manifest_keeps_uncertain_observations_visible_but_non_actionable_and_excludes_folder()
    {
        RecoveryManifest manifest = RecoveryManifestBuilder.ForTotalLoss(
        [
            new(ResourceKind.ManagedFolder, ReviewData.ResourceId('1'), ReviewData.Hash('1'), RecoveryObservationStatus.Exact),
            new(ResourceKind.Share, ReviewData.ResourceId('2'), ReviewData.Hash('2'), RecoveryObservationStatus.Exact),
            new(ResourceKind.LanFirewallRule, ReviewData.ResourceId('3'), ReviewData.Hash('3'), RecoveryObservationStatus.AccessDenied),
        ]);

        Assert.DoesNotContain(manifest.Items, item => item.Kind == ResourceKind.ManagedFolder);
        Assert.Contains(manifest.Items, item => item.ObservationStatus == RecoveryObservationStatus.AccessDenied && item.Instruction == AdministratorRecoveryInstruction.InspectOnlyNoAction);
        Assert.Contains(manifest.Items, item => item.Kind == ResourceKind.Share && item.Instruction == AdministratorRecoveryInstruction.ConfirmShareStableIdentity);
        Assert.True(manifest.PreserveManagedFolder);
        Assert.True(manifest.PreserveUserFiles);
    }

    [Fact]
    public void Retention_purges_only_eligible_unreferenced_records_and_is_idempotent()
    {
        DateTimeOffset removed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        RetentionCollections collections = ReviewData.RetentionCollections(removed);
        RetentionPurgeRequest request = new(removed, removed.AddDays(90), ChainValid: true, ClockTrusted: true,
            AccessAvailable: true, ActiveReferenceIds: []);

        RetentionPurgeResult first = RetentionStore.Purge(collections, request);
        RetentionPurgeResult second = RetentionStore.Purge(first.Remaining, request);

        Assert.Equal(RetentionDisposition.Purge, first.Disposition);
        Assert.Empty(first.Remaining.Tombstones);
        Assert.Empty(first.Remaining.AuditRecords);
        Assert.Empty(first.Remaining.SupersededEvidence);
        Assert.Equal(first.Remaining, second.Remaining);
        Assert.Empty(second.PurgedRecordIds);
        Assert.True(first.ActiveStateUntouched);
        Assert.True(first.ManagedFolderAndUserFilesUntouched);
    }

    [Fact]
    public void Active_retention_reference_preserves_every_collection_and_requires_cleanup()
    {
        DateTimeOffset removed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        RetentionCollections collections = ReviewData.RetentionCollections(removed);

        RetentionPurgeResult result = RetentionStore.Purge(collections,
            new(removed, removed.AddDays(91), true, true, true, ["retention:22222222222222222222222222222222"]));

        Assert.Equal(RetentionDisposition.CleanupNeeded, result.Disposition);
        Assert.Same(collections, result.Remaining);
        Assert.Empty(result.PurgedRecordIds);
    }

    [Fact]
    public void Cross_removal_or_wrong_collection_retention_record_is_preserved_as_cleanup_needed()
    {
        DateTimeOffset removed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        RetentionCollections collections = ReviewData.RetentionCollections(removed);
        RetentionCollections malformed = collections with
        {
            Tombstones = [collections.Tombstones[0] with { Kind = RetentionRecordKind.Audit }],
        };

        RetentionPurgeResult result = RetentionStore.Purge(malformed, new(removed, removed.AddDays(91), true, true, true, []));

        Assert.Equal(RetentionDisposition.CleanupNeeded, result.Disposition);
        Assert.Same(malformed, result.Remaining);
    }

    [Theory]
    [InlineData(false, false, false, HelperConsentDecision.Canceled)]
    [InlineData(true, false, false, HelperConsentDecision.Refused)]
    [InlineData(false, true, false, HelperConsentDecision.Refused)]
    [InlineData(false, false, true, HelperConsentDecision.Refused)]
    public void Consent_changed_binding_expiry_second_apply_and_cancel_are_closed(
        bool changedBinding, bool expired, bool consumed, HelperConsentDecision expected)
    {
        DateTimeOffset now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        HelperConsentAttempt attempt = new(ReviewData.Hash('1'), changedBinding ? ReviewData.Hash('2') : ReviewData.Hash('1'),
            expired ? now.AddSeconds(-1) : now.AddMinutes(1), now, consumed, Canceled: !changedBinding && !expired && !consumed);

        Assert.Equal(expected, HelperConsentPolicy.Evaluate(attempt));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Retention_preserves_bytes_when_chain_time_or_access_is_untrusted(bool chain, bool clock, bool access)
    {
        DateTimeOffset removed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        RetentionCollections collections = ReviewData.RetentionCollections(removed);
        string before = JsonSerializer.Serialize(collections);

        RetentionPurgeResult result = RetentionStore.Purge(collections,
            new(removed, removed.AddDays(91), chain, clock, access, []));

        Assert.Equal(RetentionDisposition.CleanupNeeded, result.Disposition);
        Assert.Equal(before, JsonSerializer.Serialize(result.Remaining));
    }
}

internal static class ReviewData
{
    internal const string HostId = "host:11111111111111111111111111111111";

    internal static string Hash(char value) => $"sha256:{new string(value, 64)}";
    internal static string Context(char value) => $"ctx:{new string(value, 32)}";
    internal static string ResourceId(char value) => $"rid:{new string(value, 32)}";
    internal static string OperationId(char value) => $"opid:{new string(value, 32)}";

    internal static LedgerDocument Ledger()
    {
        ResourceRecord folder = new(ResourceKind.ManagedFolder, ResourceId('f'), Hash('f'), HostOperation.Op06,
            "marker:ffffffffffffffffffffffffffffffff", Context('1'));
        ResourceRecord group = new(ResourceKind.Group, ResourceId('1'), Hash('1'), HostOperation.Op07,
            "marker:11111111111111111111111111111111", Context('1'));
        IReadOnlyList<JournalEntry> journal = JournalChain.CreateCompleted(
            HostOperation.Op03, HostId, OperationId('1'), 6, Hash('2'), Hash('3'), Hash('4'), Hash('5'),
            Context('1'), new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

        return new(1, HostId, "machine:11111111111111111111111111111111", "S-1-5-21-111-222-333-1001", 7,
            Hash('6'), folder, [group], [new(EndpointKind.Local, Hash('7'), 7, Context('1'))], journal,
            [new("audit:11111111111111111111111111111111", new DateTimeOffset(2026, 8, 14, 12, 1, 0, TimeSpan.Zero), Hash('8'))], [], null);
    }

    internal static AppliedPrimitive AppliedPrimitive() => new(OperationId('1'), "primitive:11111111111111111111111111111111",
        ResourceKind.Group, ResourceId('1'), Hash('1'), Context('1'), 7, CreatedByCurrentTransaction: true);

    internal static ClientIntentRecord ClientIntent() => new(HostId, ResourceId('1'), 7, Hash('1'), Hash('2'));

    internal static RetentionCollections RetentionCollections(DateTimeOffset removed) => new(
        [new(RetentionRecordKind.Tombstone, "retention:11111111111111111111111111111111", removed, Hash('1'))],
        [new(RetentionRecordKind.Audit, "retention:22222222222222222222222222222222", removed, Hash('2')),
            new(RetentionRecordKind.Audit, "retention:33333333333333333333333333333333", removed, Hash('3'))],
        [new(RetentionRecordKind.SupersededEvidence, "retention:44444444444444444444444444444444", removed, Hash('4'))]);
}
