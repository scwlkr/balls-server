using BallsServer.ClientLifecycle;

namespace BallsServer.ClientLifecycle.Tests;

public sealed class ClientLifecycleTests
{
    [Fact]
    public void Connect_inspects_the_exact_endpoint_mapping_letter_and_credential_before_its_one_attempt()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ActionPlan plan = ClientLifecyclePlanner.Plan(ClientAction.Connect, new("host-1", "grant-1", 1, endpoint), new(true, false, false, false), 'P');

        Assert.True(plan.Allowed);
        Assert.Equal(1, plan.AuthenticationAttempts);
        Assert.Equal(["InspectEndpoint", "InspectMapping", "InspectDriveLetter", "InspectCredential", "AuthenticateExactEndpoint"], plan.Steps.Take(5));
        Assert.Equal("MapExactUncInProcess", plan.Steps[^1]);
    }

    [Fact]
    public void Setup_code_and_endpoint_update_allow_only_one_exact_bound_endpoint()
    {
        Endpoint initial = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ClientState state = new("host-1", "grant-1", 1, initial);
        Endpoint tailscale = initial with { ServerName = "host.tailnet.ts.net" };

        Assert.True(new SetupCode(1, initial).IsValid);
        Assert.False(new SetupCode(1, initial with { ServerName = "10.0.0.1" }).IsValid);
        Assert.True(new EndpointUpdate(1, tailscale).IsBoundTo(state));
        Assert.False(new EndpointUpdate(1, tailscale with { GrantId = "grant-2" }).IsBoundTo(state));
    }

    [Fact]
    public void Collision_or_observation_failure_happens_before_any_authentication_attempt()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ClientState state = new("host-1", "grant-1", 1, endpoint);

        ActionPlan collision = ClientLifecyclePlanner.Plan(ClientAction.Connect, state, new(true, true, false, false), 'P');
        ActionPlan unavailable = ClientLifecyclePlanner.Plan(ClientAction.Check, state, new(false, false, false, false));

        Assert.Equal(RecoveryCategory.Collision, collision.Category);
        Assert.Equal(0, collision.AuthenticationAttempts);
        Assert.Equal(RecoveryCategory.PathUnavailable, unavailable.Category);
        Assert.Equal(0, unavailable.AuthenticationAttempts);
    }

    [Fact]
    public void Authentication_failures_are_typed_but_have_the_same_non_oracular_public_message()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ActionPlan invalid = ClientLifecyclePlanner.Plan(ClientAction.Check, new("host-1", "grant-1", 1, endpoint), new(true, false, false, false));
        ActionPlan locked = invalid with { Category = ClientLifecyclePlanner.CategorizeAuthentication(AuthenticationResult.LockedAccount) };

        Assert.Equal(RecoveryCategory.InvalidCredential, ClientLifecyclePlanner.CategorizeAuthentication(AuthenticationResult.InvalidCredential));
        Assert.Equal(RecoveryCategory.LockedAccount, locked.Category);
        Assert.Equal(invalid.PublicMessage, locked.PublicMessage);
    }

    [Fact]
    public void Save_uses_only_the_exact_server_provider_target_separately_from_the_mapping_unc()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ClientState state = ClientLifecyclePlanner.SelectSave(new("host-1", "grant-1", 1, endpoint), true);

        Assert.Equal("host.lan", endpoint.ProviderTarget);
        Assert.Equal("\\\\host.lan\\Balls", endpoint.MappingUnc);
        Assert.True(ClientLifecyclePlanner.CanSaveCredential(state, "host.lan"));
        Assert.False(ClientLifecyclePlanner.CanSaveCredential(state, "\\\\host.lan\\Balls"));
        Assert.False(ClientLifecyclePlanner.CanSaveCredential(state, "*"));
    }

    [Fact]
    public void Reconnect_can_be_selected_only_after_save_and_cleared_independently()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ClientState state = new("host-1", "grant-1", 1, endpoint);

        Assert.False(ClientLifecyclePlanner.SelectReconnect(state, true).ReconnectSelected);
        state = ClientLifecyclePlanner.SelectSave(state, true);
        Assert.True(ClientLifecyclePlanner.SelectReconnect(state, true).ReconnectSelected);
        Assert.False(ClientLifecyclePlanner.SelectReconnect(ClientLifecyclePlanner.SelectReconnect(state, true), false).ReconnectSelected);
    }

    [Fact]
    public void Fixed_Balls_share_is_required_by_endpoint_setup_update_and_mapping_plans()
    {
        Endpoint otherShare = new("host-1", "grant-1", 1, "host.lan", "OtherShare", "HOST\\grant-1");
        ClientState state = new("host-1", "grant-1", 1, otherShare);

        Assert.False(otherShare.IsExact);
        Assert.False(new SetupCode(1, otherShare).IsValid);
        Assert.False(new EndpointUpdate(1, otherShare).IsBoundTo(state));
        Assert.False(ClientLifecyclePlanner.Plan(ClientAction.Connect, state, new(true, false, false, false), 'P').Allowed);
    }

    [Fact]
    public void First_save_selects_reconnect_by_default_and_explicit_clear_is_independent()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ClientState saved = ClientLifecyclePlanner.SelectSave(new("host-1", "grant-1", 1, endpoint), true);

        Assert.True(saved.SaveSelected);
        Assert.True(saved.ReconnectSelected);
        Assert.False(ClientLifecyclePlanner.SelectReconnect(saved, false).ReconnectSelected);
    }

    [Fact]
    public void Cleanup_touches_only_recorded_exact_targets_and_not_found_is_success()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ClientState owned = new("host-1", "grant-1", 1, endpoint, "host.lan", 'P');

        Assert.True(ClientLifecyclePlanner.PlanCleanup(owned, credentialFound: false, mappingFound: false).Success);
        Assert.True(ClientLifecyclePlanner.PlanCleanup(owned, true, true).Success);
        Assert.False(ClientLifecyclePlanner.PlanCleanup(owned with { ProductRecordedCredentialTarget = "other.lan" }, true, false).Success);
    }

    [Fact]
    public void Verification_uses_one_unique_owned_file_and_keeps_cleanup_leftover_private()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        VerificationPlan failed = ClientLifecyclePlanner.PlanVerification(endpoint, "operation-opaque-1", cleanupSucceeded: false);

        Assert.Equal(".ballsserver-verify-operation-opaque-1.tmp", failed.TemporaryFileName);
        Assert.Equal("\\\\host.lan\\Balls\\.ballsserver-verify-operation-opaque-1.tmp", failed.PrivateLeftoverPath);
        Assert.DoesNotContain("host.lan", failed.PublicMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_requires_imported_bound_update_and_preserves_current_state_on_refusal()
    {
        Endpoint lan = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        Endpoint tailnet = lan with { ServerName = "host.tailnet.ts.net" };
        ClientState state = new("host-1", "grant-1", 1, lan);

        ActionPlan accepted = ClientLifecyclePlanner.Plan(ClientAction.Switch, state, new(true, false, false, false), 'P', new EndpointUpdate(1, tailnet));
        ActionPlan refused = ClientLifecyclePlanner.Plan(ClientAction.Switch, state, new(true, false, false, false), 'P', new EndpointUpdate(1, tailnet with { CredentialRevision = 2 }));

        Assert.True(accepted.Allowed);
        Assert.Equal("ImportEndpointUpdate", accepted.Steps[0]);
        Assert.Equal("\\\\host.tailnet.ts.net\\Balls", accepted.CandidateMappingUnc);
        Assert.False(refused.Allowed);
        Assert.Equal("host.lan", state.SelectedEndpoint!.ServerName);
    }

    [Fact]
    public void Host_revocation_and_development_host_harness_are_refused_without_execution()
    {
        Endpoint endpoint = new("host-1", "grant-1", 1, "host.lan", "Balls", "HOST\\grant-1");
        ActionPlan revoked = ClientLifecyclePlanner.Plan(ClientAction.Check, new("host-1", "grant-1", 1, endpoint, HostRevoked: true), new(true, false, false, false));
        VmHarnessResult denied = ClientLifecyclePlanner.GuardVmHarness(new(true, true, true, "snapshot-1", "BallsServer.Test.abc"));

        Assert.Equal(RecoveryCategory.HostRevoked, revoked.Category);
        Assert.False(denied.Allowed);
    }
}
