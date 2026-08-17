using BallsServer.AccessGrantLifecycle;

namespace BallsServer.AccessGrantLifecycle.Tests;

public sealed class LifecycleContractTests
{
    [Fact]
    public void Create_starts_disabled_until_a_fresh_exact_authorization_is_explicitly_activated()
    {
        AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());

        Assert.Equal(GrantState.PendingTransfer, grant.State);
        Assert.True(grant.Disabled);
        Assert.False(grant.Activate(null).Succeeded);
        Assert.False(grant.Activate(ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-1", grant.Request.GrantId, grant.CredentialRevision + 1)).Succeeded);
        Assert.True(grant.Activate(ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-2", grant.Request.GrantId, grant.CredentialRevision)).Succeeded);
        Assert.Equal(GrantState.Active, grant.State);
    }

    [Fact]
    public void Create_requires_the_single_product_group_non_admin_and_observed_policy()
    {
        AccessAccountObservation conformant = new(false, [GrantFacts.ValidRequest().ProductGroupSid], false, true, true, true);
        AccessAccountObservation extraGroup = conformant with { GroupSids = [GrantFacts.ValidRequest().ProductGroupSid, "S-1-5-32-544"] };

        Assert.True(AccessAccountPolicy.IsConformant(conformant, GrantFacts.ValidRequest().ProductGroupSid));
        Assert.False(AccessAccountPolicy.IsConformant(extraGroup, GrantFacts.ValidRequest().ProductGroupSid));
        Assert.False(AccessAccountPolicy.IsConformant(conformant with { IsAdministrator = true }, GrantFacts.ValidRequest().ProductGroupSid));
        Assert.False(AccessAccountPolicy.IsConformant(conformant with { CanChangePassword = true }, GrantFacts.ValidRequest().ProductGroupSid));
        Assert.False(AccessAccountPolicy.IsConformant(conformant with { NetworkLogonPolicyObserved = false }, GrantFacts.ValidRequest().ProductGroupSid));
    }

    [Fact]
    public void Random_passwords_have_32_byte_entropy_and_each_explicit_action_is_fresh()
    {
        using SecretBuffer first = SecretGenerator.GenerateForExplicitOwnerAction("owner-action-1");
        using SecretBuffer second = SecretGenerator.GenerateForExplicitOwnerAction("owner-action-2");

        Assert.Equal(SecretGenerator.MinimumRandomBytes, first.RandomByteCount);
        Assert.NotEqual(first.RevealForTransientUse(), second.RevealForTransientUse());
        Assert.Throws<ArgumentException>(() => SecretGenerator.GenerateForExplicitOwnerAction(""));
    }

    [Fact]
    public void Rotate_requires_an_explicit_action_keeps_the_grant_disabled_and_advances_once()
    {
        AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());
        Assert.True(grant.Activate(ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-1", grant.Request.GrantId, grant.CredentialRevision)).Succeeded);
        Assert.False(grant.Rotate("").Succeeded);

        GrantResult rotation = grant.Rotate("owner-retry-2");

        Assert.True(rotation.Succeeded);
        Assert.Equal(2, grant.CredentialRevision);
        Assert.True(grant.Disabled);
        Assert.Equal(GrantState.PendingTransfer, grant.State);
    }

    [Theory]
    [InlineData(TransferOutcome.Lost)]
    [InlineData(TransferOutcome.Hidden)]
    [InlineData(TransferOutcome.TimedOut)]
    [InlineData(TransferOutcome.Crashed)]
    [InlineData(TransferOutcome.Unread)]
    [InlineData(TransferOutcome.Failed)]
    public void Any_unacknowledged_transfer_never_enables_the_undisclosed_credential(TransferOutcome outcome)
    {
        AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());

        GrantResult result = grant.RecordTransfer(outcome);

        Assert.Equal(GrantResultCode.RepairNeeded, result.Code);
        Assert.True(grant.Disabled);
        Assert.False(grant.Activate(ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-1", grant.Request.GrantId, grant.CredentialRevision)).Succeeded);
    }

    [Fact]
    public void Setup_code_has_exactly_one_selected_endpoint_and_minimum_fields()
    {
        using SetupCode code = GrantFacts.SetupCode();

        Assert.True(code.HasOnlyMinimumFields);
        Assert.Equal(SetupCode.CurrentSchemaVersion, code.SchemaVersion);
        Assert.Equal("\\\\host\\Balls", code.SelectedEndpoint);
        Assert.Equal("HOST\\balls-grant-1", code.QualifiedSamAccount);
    }

    [Fact]
    public void One_read_ipc_binds_user_pipe_nonce_operation_grant_and_revision()
    {
        TransferBinding binding = GrantFacts.Binding();
        using OneReadSecretTransfer transfer = new(binding, GrantFacts.SetupCode());
        TransferBinding wrongNonce = binding with { Nonce = "nonce-2" };

        Assert.False(transfer.Read(wrongNonce).Delivered);
        SecretResponse response = transfer.Read(binding);
        Assert.True(response.Delivered);
        Assert.NotNull(response.SetupCode);
        Assert.False(transfer.Read(binding).Delivered);
        response.SetupCode!.Dispose();
    }

    [Fact]
    public async Task One_read_ipc_has_exactly_one_atomic_concurrent_winner()
    {
        TransferBinding binding = GrantFacts.Binding();
        using OneReadSecretTransfer transfer = new(binding, GrantFacts.SetupCode());

        SecretResponse[] responses = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => transfer.Read(binding))));

        SecretResponse delivered = Assert.Single(responses, response => response.Delivered);
        delivered.SetupCode!.Dispose();
    }

    [Fact]
    public void Transfer_refuses_setup_code_with_a_different_grant_or_revision()
    {
        TransferBinding binding = GrantFacts.Binding();
        using SetupCode wrongGrant = GrantFacts.SetupCode() with { GrantId = "grant-opaque-2" };
        using SetupCode wrongRevision = GrantFacts.SetupCode(revision: 2);

        Assert.Throws<ArgumentException>(() => new OneReadSecretTransfer(binding, wrongGrant));
        Assert.Throws<ArgumentException>(() => new OneReadSecretTransfer(binding, wrongRevision));
    }

    [Fact]
    public void Activation_authorization_is_bound_to_operation_grant_and_current_revision_and_consumed_once()
    {
        AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());
        ActivationAuthorization wrongOperation = ActivationAuthorization.IssueFromAuthoritativeHelper("", grant.Request.GrantId, grant.CredentialRevision);
        ActivationAuthorization wrongGrant = ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-2", "grant-opaque-2", grant.CredentialRevision);
        ActivationAuthorization wrongRevision = ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-3", grant.Request.GrantId, grant.CredentialRevision + 1);
        ActivationAuthorization authorization = ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-4", grant.Request.GrantId, grant.CredentialRevision);

        Assert.False(grant.Activate(wrongOperation).Succeeded);
        Assert.False(grant.Activate(wrongGrant).Succeeded);
        Assert.False(grant.Activate(wrongRevision).Succeeded);
        Assert.True(grant.Activate(authorization).Succeeded);
        Assert.False(grant.Activate(authorization).Succeeded);
    }

    [Fact]
    public void Create_and_rotate_only_use_internal_csprng_generation_and_never_accept_caller_secret_bytes()
    {
        AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());
        long initialGeneration = grant.SecretGenerationCount;
        Assert.True(grant.Activate(ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-1", grant.Request.GrantId, grant.CredentialRevision)).Succeeded);

        Assert.True(grant.Rotate("owner-retry-2").Succeeded);

        Assert.Equal(initialGeneration + 1, grant.SecretGenerationCount);
        Assert.Equal(SecretGenerator.MinimumRandomBytes, grant.LastRandomByteCount);
        Assert.DoesNotContain(typeof(AccessGrant).GetMethods(), method => (method.Name is "Create" or "Rotate") && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(byte[]) || parameter.ParameterType.ToString().Contains("ReadOnlySpan", StringComparison.Ordinal)));
    }

    [Fact]
    public void Disposed_unread_transfer_clears_the_only_secret_copy()
    {
        SetupCode code = GrantFacts.SetupCode();
        SecretBuffer password = code.Password;
        OneReadSecretTransfer transfer = new(GrantFacts.Binding(), code);

        transfer.Dispose();

        Assert.True(password.IsCleared);
        Assert.False(transfer.Read(GrantFacts.Binding()).Delivered);
    }

    [Fact]
    public void Display_requires_warning_and_hide_or_timeout_destroys_the_copy_without_activation()
    {
        using TransientSetupCodeView view = new(GrantFacts.SetupCode(), "S-1-5-21-100-1", 4);

        Assert.False(view.TryOpenWarningAcknowledged(false));
        Assert.True(view.TryOpenWarningAcknowledged(true));
        view.HideOrTimeout();

        Assert.False(view.IsVisible);
        Assert.Null(view.TakeForDisplay(true));
    }

    [Fact]
    public void Clipboard_clear_preserves_any_newer_user_value()
    {
        ClipboardOwnership ownership = ClipboardLifecycle.CopyExplicitly("setup-code", true, "S-1-5-21-100-1", 4)!;

        Assert.Equal("new-user-value", ClipboardLifecycle.ClearOnlyUnchanged(ownership, "new-user-value", "S-1-5-21-100-1", 4));
        Assert.Null(ClipboardLifecycle.ClearOnlyUnchanged(ownership, "setup-code", "S-1-5-21-100-1", 4));
        Assert.Null(ClipboardLifecycle.CopyExplicitly("setup-code", false, "S-1-5-21-100-1", 4));
    }

    [Fact]
    public void Qr_is_explicit_memory_only_and_disposed_with_the_view()
    {
        using SetupCode code = GrantFacts.SetupCode();
        MemoryOnlyQr? denied = MemoryOnlyQr.RenderExplicitly(code, false);
        using MemoryOnlyQr allowed = MemoryOnlyQr.RenderExplicitly(code, true)!;

        Assert.Null(denied);
        Assert.False(allowed.IsDisposed);
        allowed.Dispose();
        Assert.True(allowed.IsDisposed);
    }

    [Fact]
    public void Revoke_is_distinct_from_optional_deletion_and_never_restores_access()
    {
        AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());

        GrantResult result = grant.Revoke(optionalDelete: true);

        Assert.True(result.Succeeded);
        Assert.True(grant.MembershipRemoved);
        Assert.True(grant.OptionalDeletionRequested);
        Assert.True(grant.Disabled);
        Assert.False(grant.Activate(ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-1", grant.Request.GrantId, grant.CredentialRevision)).Succeeded);
    }

    [Fact]
    public void Session_closure_selects_only_explicitly_confirmed_sessions_for_the_one_grant()
    {
        AttributableSession[] sessions =
        [
            new("session-1", "grant-opaque-1", "S-1-5-21-100-101", "share-1"),
            new("session-2", "grant-opaque-2", "S-1-5-21-100-102", "share-1"),
            new("session-3", "grant-opaque-1", "S-1-5-21-100-101", "share-2"),
        ];

        IReadOnlyList<AttributableSession> selected = SessionClosure.SelectExact(sessions, "grant-opaque-1", "S-1-5-21-100-101", "share-1", ["session-1", "session-2"]);

        Assert.Single(selected);
        Assert.Equal("session-1", selected[0].SessionId);
    }

    [Fact]
    public void Public_status_and_scanner_never_expose_secret_material_or_a_secret_sink()
    {
        Assert.True(SecretFlowScanner.IsSafePublicText(GrantResult.Refused(GrantState.PendingTransfer).PublicMessage));
        Assert.False(SecretFlowScanner.IsSafePublicText("password=not-a-real-credential"));
        Assert.Contains("ledger", SecretFlowScanner.ForbiddenSinkNames);
        Assert.Contains("artifact", SecretFlowScanner.ForbiddenSinkNames);
    }

    [Fact]
    public void Activation_authorization_is_opaque_to_public_callers_and_only_friend_helper_seam_can_issue_it()
    {
        Assert.Empty(typeof(ActivationAuthorization).GetConstructors());
        Assert.DoesNotContain(typeof(ActivationAuthorization).GetMethods(), method => method.IsPublic && method.IsStatic && method.ReturnType == typeof(ActivationAuthorization));

        AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());
        Assert.True(grant.Activate(ActivationAuthorization.IssueFromAuthoritativeHelper("activation-op-1", grant.Request.GrantId, grant.CredentialRevision)).Succeeded);
    }
}
