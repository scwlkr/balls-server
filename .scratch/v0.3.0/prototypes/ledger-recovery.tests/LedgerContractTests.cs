using System.Text.Json;
using BallsServer.LedgerRecovery;

namespace BallsServer.LedgerRecovery.Tests;

public sealed class LedgerContractTests
{
    [Fact]
    public void Complete_ledger_schema_validates_without_secret_material()
    {
        LedgerDocument ledger = ReviewData.Ledger();

        LedgerValidationResult result = LedgerContract.Validate(ledger);

        Assert.Equal(LedgerValidationCode.Valid, result.Code);
        Assert.Equal(0, result.ForbiddenFieldCount);
    }

    [Theory]
    [InlineData(0, LedgerValidationCode.UnsupportedSchema)]
    [InlineData(2, LedgerValidationCode.UnsupportedSchema)]
    [InlineData(-1, LedgerValidationCode.Malformed)]
    public void Unknown_or_malformed_schema_is_refused_without_migration(
        int schemaVersion,
        LedgerValidationCode expected)
    {
        LedgerDocument ledger = ReviewData.Ledger() with { SchemaVersion = schemaVersion };

        Assert.Equal(expected, LedgerContract.Validate(ledger).Code);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("passwordDerivedIdentifier")]
    [InlineData("passwordHash")]
    [InlineData("recoverableSecretHint")]
    [InlineData("secretHint")]
    [InlineData("setupCode")]
    [InlineData("credentialPayload")]
    [InlineData("secretMaterial")]
    public void Forbidden_secret_fields_are_rejected(string fieldName)
    {
        string json = $"{{\"schemaVersion\":1,\"{fieldName}\":\"not-a-real-credential\"}}";

        SecretScanResult result = SecretMaterialScanner.ScanJson(json);

        Assert.False(result.Safe);
        Assert.Contains(fieldName, result.ForbiddenFields);
    }

    [Fact]
    public void Secret_canary_is_rejected_even_under_an_innocent_field_name()
    {
        const string json = "{\"note\":\"not-a-real-credential\"}";

        SecretScanResult result = SecretMaterialScanner.ScanJson(json);

        Assert.False(result.Safe);
        Assert.Contains("<secret-canary>", result.ForbiddenFields);
    }

    [Fact]
    public void Journal_preserves_helper_binding_evidence_without_reusing_authority()
    {
        JournalEntry entry = ReviewData.Ledger().Journal[0];

        Assert.Equal(JournalChain.ProtocolVersion, entry.ProtocolVersion);
        Assert.Equal(ReviewData.Hash('5'), entry.AuthorizationBindingFingerprint);
        Assert.Equal(ReviewData.Hash('4'), entry.NonceFingerprint);
        Assert.Equal(ReviewData.Hash('3'), entry.PipeInstanceFingerprint);
        Assert.True(entry.AuthorizationConsumed);
        Assert.False(JournalEntry.CanAuthorizeMutationOrReplay);
    }

    [Fact]
    public void Current_user_client_intent_cannot_authorize_host_mutation()
    {
        ClientIntentRecord intent = new(
            ReviewData.HostId,
            ReviewData.ResourceId('1'),
            7,
            ReviewData.Hash('1'),
            ReviewData.Hash('2'));

        Assert.NotNull(intent);
        Assert.False(ClientIntentRecord.CanAuthorizeHostMutation);
        Assert.False(ClientIntentRecord.IsHostOwnershipEvidence);
    }

    [Theory]
    [InlineData(0, LedgerValidationCode.Valid)]
    [InlineData(1, LedgerValidationCode.Valid)]
    [InlineData(-1, LedgerValidationCode.NonMonotonicRevision)]
    public void Revision_must_be_monotonic_and_non_negative(long revision, LedgerValidationCode expected)
    {
        LedgerDocument ledger = ReviewData.Ledger() with
        {
            Revision = revision,
            Endpoints = ReviewData.Ledger().Endpoints.Select(endpoint => endpoint with { ObservationEpoch = Math.Max(revision, 0) }).ToArray(),
            Journal = revision > 0 ? JournalChain.CreateCompleted(HostOperation.Op03, ReviewData.HostId, ReviewData.OperationId('1'), revision - 1,
                ReviewData.Hash('2'), ReviewData.Hash('3'), ReviewData.Hash('3'), ReviewData.Hash('4'), ReviewData.Context('1'),
                new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)) : revision == 0 ? [] : ReviewData.Ledger().Journal,
        };

        Assert.Equal(expected, LedgerContract.Validate(ledger).Code);
    }

    [Fact]
    public void Host_state_acl_contract_is_restrictive_and_user_copy_is_non_authoritative()
    {
        Assert.Equal(
            "O:SYG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;<OWNER-SID>)",
            ProtectedStatePolicy.HostStateSddl);
        Assert.Equal(
            "Helper, Administrators, and SYSTEM only",
            ProtectedStatePolicy.HostWriters);
        Assert.False(ProtectedStatePolicy.UserWritableCopyCanAuthorizeMutation);
    }
}
