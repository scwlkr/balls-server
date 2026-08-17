using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class ShareAndFirewallSafetyTests
{
    private static readonly FolderIdentity Folder = new("volume-1", "file-1", "C:\\fixture", "descriptor-1");
    private static readonly ProductGroupIdentity Group = new("Balls Server Access", "S-1-5-21-111-222-333-444", "group-object-1", "BallsServer.Group.v1");
    private static readonly ProductGrantIdentity Grant = new("S-1-5-21-111-222-333-555", "grant-object-1");

    [Fact]
    public void Product_group_requires_stable_sid_object_and_marker_not_name_equivalence()
    {
        ProductGroupPlan plan = ProductIdentityPolicy.PlanGroup(Group, new(Group.Name, Group.Sid, "different-object", Group.Marker));

        Assert.False(plan.Accepted);
        Assert.Equal(ResourceRefusal.UnmanagedConflict, plan.Refusal);
    }

    [Theory]
    [InlineData("foreign name", "S-1-5-21-111-222-333-444", "group-object-1", "marker-1")]
    [InlineData("Balls Server Access", "S-1-5-21-wrong", "group-object-1", "marker-1")]
    [InlineData("Balls Server Access", "S-1-5-21-111-222-333-444", "group-object-1", "foreign-marker")]
    public void Equivalent_group_name_sid_or_marker_never_transfers_ownership(string name, string sid, string objectId, string marker)
    {
        ProductGroupPlan plan = ProductIdentityPolicy.PlanGroup(Group, new(name, sid, objectId, marker));

        Assert.Equal(ResourceRefusal.UnmanagedConflict, plan.Refusal);
    }

    [Fact]
    public void Fixed_share_plan_uses_atomic_exact_descriptor_and_stable_ids()
    {
        SharePlan plan = SharePolicy.Plan(DesiredShare(), CompliantSmb(), existing: null, ownership: null);

        Assert.True(plan.Accepted);
        Assert.Equal("Balls", plan.Name);
        Assert.Equal(Folder, plan.FolderIdentity);
        Assert.Equal(
            [
                new ShareAccessEntry(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
                new ShareAccessEntry(Group.Sid, ShareAccess.Change),
            ],
            plan.Permissions);
        Assert.Equal([SharePlanStep.CreateWithExactDescriptor, SharePlanStep.ReobserveExactState], plan.Steps);
        Assert.Null(plan.Ownership);
        Assert.DoesNotContain(plan.Permissions, permission => permission.Principal is "Everyone" or "Guest" or "Anonymous");
    }

    public static TheoryData<ShareObservation> UnsafeShares => new()
    {
        { new("Balls", "foreign-id", Folder, ExactPermissions()) },
        { new("Balls", "owned-id", Folder with { FileId = "other" }, ExactPermissions()) },
        { new("Balls", "owned-id", Folder, [.. ExactPermissions(), new("Everyone", ShareAccess.Read)]) },
        { new("Balls", "owned-id", Folder, [new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full), new(Group.Sid, ShareAccess.Full)]) },
    };

    [Theory]
    [MemberData(nameof(UnsafeShares))]
    public void Unmanaged_or_nonexact_share_state_refuses(ShareObservation share)
    {
        SharePlan result = SharePolicy.Plan(DesiredShare(), CompliantSmb(), share, ownership: null);

        Assert.False(result.Accepted);
        Assert.Equal(ResourceRefusal.UnmanagedConflict, result.Refusal);
    }

    [Fact]
    public void Owned_share_reobservation_requires_independent_canonical_fingerprint()
    {
        ShareDesiredState desired = DesiredShare();
        ShareObservation exact = new("Balls", "owned-id", Folder, ExactPermissions());
        ShareOwnershipRecord ownership = Ownership(desired, exact);

        Assert.True(SharePolicy.Plan(desired, CompliantSmb(), exact, ownership).Accepted);
        Assert.Equal(
            ResourceRefusal.UnmanagedConflict,
            SharePolicy.Plan(desired, CompliantSmb(), exact, ownership with { CanonicalFingerprint = "drift" }).Refusal);
    }

    public static TheoryData<SmbPrerequisiteObservation, PrerequisiteRefusal> UnsafeSmb => new()
    {
        { CompliantSmb() with { ServerRunning = false }, PrerequisiteRefusal.ServerStopped },
        { CompliantSmb() with { Smb1Disabled = false }, PrerequisiteRefusal.Smb1Enabled },
        { CompliantSmb() with { Smb2And3Enabled = false }, PrerequisiteRefusal.SmbDisabled },
        { CompliantSmb() with { MinimumDialect = new Version(2, 1) }, PrerequisiteRefusal.DialectBelowSmb3 },
        { CompliantSmb() with { SigningPreserved = false }, PrerequisiteRefusal.SigningNotPreserved },
        { CompliantSmb() with { PolicyManaged = true }, PrerequisiteRefusal.PolicyManaged },
        { CompliantSmb() with { GuestAnonymousOrBlankPasswordAccepted = true }, PrerequisiteRefusal.GuestOrAnonymousAccess },
        { CompliantSmb() with { ObservationComplete = false }, PrerequisiteRefusal.Unknown },
    };

    [Theory]
    [MemberData(nameof(UnsafeSmb))]
    public void Noncompliant_or_unprovable_smb_returns_exact_administrator_guidance(
        SmbPrerequisiteObservation observation,
        PrerequisiteRefusal expected)
    {
        PrerequisiteResult result = HostPrerequisitePolicy.Validate(observation);

        Assert.False(result.Accepted);
        Assert.Equal(expected, result.Refusal);
        Assert.Equal(HostPrerequisitePolicy.Guidance(expected), result.Guidance);
        Assert.StartsWith("Administrator action:", result.Guidance, StringComparison.Ordinal);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void Lan_and_tailscale_rules_are_separate_exact_private_plans()
    {
        FirewallPlan lan = FirewallPolicy.PlanLan(LanInterface(), FirewallPolicyState.LocalWritable);
        FirewallPlan tailscale = FirewallPolicy.PlanTailscale(TailscaleInterface(), FirewallPolicyState.LocalWritable, tailscaleEvidencePresent: true);

        Assert.True(lan.Accepted);
        Assert.True(tailscale.Accepted);
        Assert.NotEqual(lan.Rule!.StableId, tailscale.Rule!.StableId);
        Assert.Equal(["192.168.1.0/24"], lan.Rule.RemoteAddressRanges);
        Assert.Equal(["100.64.0.0/10", "fd7a:115c:a1e0::/48"], tailscale.Rule.RemoteAddressRanges);
        Assert.All([lan.Rule, tailscale.Rule], rule =>
        {
            Assert.True(rule.Enabled);
            Assert.Equal(FirewallAction.Allow, rule.Action);
            Assert.Equal(FirewallPolicyStore.LocalPersistent, rule.PolicyStore);
            Assert.Equal(Direction.Inbound, rule.Direction);
            Assert.Equal(Protocol.Tcp, rule.Protocol);
            Assert.Equal(445, rule.LocalPort);
            Assert.NotEqual(NetworkProfile.Public, rule.Profile);
            Assert.DoesNotContain("Any", rule.InterfaceIds);
            Assert.DoesNotContain("Any", rule.LocalAddressRanges);
            Assert.DoesNotContain("Any", rule.RemoteAddressRanges);
        });
    }

    public static TheoryData<FirewallRule> UnsafeRules => new()
    {
        { FirewallRule.ValidLan with { Profile = NetworkProfile.Public } },
        { FirewallRule.ValidLan with { Profile = NetworkProfile.Any } },
        { FirewallRule.ValidLan with { RemoteAddressRanges = ["Any"] } },
        { FirewallRule.ValidLan with { InterfaceIds = ["Any"] } },
        { FirewallRule.ValidLan with { LocalPort = 0 } },
        { FirewallRule.ValidLan with { Protocol = Protocol.Any } },
        { FirewallRule.ValidLan with { Direction = Direction.Outbound } },
    };

    [Theory]
    [MemberData(nameof(UnsafeRules))]
    public void Public_any_or_broad_firewall_expression_refuses(FirewallRule rule)
    {
        FirewallVerification result = FirewallPolicy.CaptureCreated(rule, new(rule, false, "rule-object-1"));

        Assert.False(result.Accepted);
        Assert.NotEqual(ResourceRefusal.None, result.Refusal);
    }

    [Theory]
    [InlineData(FirewallPolicyState.GroupPolicyManaged)]
    [InlineData(FirewallPolicyState.ScopeNotExpressible)]
    [InlineData(FirewallPolicyState.ObservationUnavailable)]
    public void Policy_limits_or_unprovable_scope_refuse_without_mutation(FirewallPolicyState state)
    {
        FirewallPlan result = FirewallPolicy.PlanLan(LanInterface(), state);

        Assert.False(result.Accepted);
        Assert.StartsWith("Administrator action:", result.Guidance, StringComparison.Ordinal);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void Built_in_or_unmanaged_firewall_rule_is_never_edited()
    {
        FirewallRule desired = FirewallRule.ValidLan;
        FirewallOwnershipRecord ownership = FirewallOwnership(desired, "rule-object-1");

        Assert.False(FirewallPolicy.PlanLan(LanInterface(), FirewallPolicyState.LocalWritable, new(desired, true, "rule-object-1"), ownership).Accepted);
        Assert.False(FirewallPolicy.PlanLan(LanInterface(), FirewallPolicyState.LocalWritable, new(desired, false, "rule-object-1"), ownership: null).Accepted);
    }

    [Fact]
    public void Missing_or_ambiguous_tailscale_evidence_refuses()
    {
        FirewallPlan missing = FirewallPolicy.PlanTailscale(TailscaleInterface(), FirewallPolicyState.LocalWritable, tailscaleEvidencePresent: false);
        FirewallPlan ambiguous = FirewallPolicy.PlanTailscale(LanInterface(), FirewallPolicyState.LocalWritable, tailscaleEvidencePresent: true);

        Assert.Equal(ResourceRefusal.MissingTailscaleEvidence, missing.Refusal);
        Assert.Equal(ResourceRefusal.AmbiguousInterface, ambiguous.Refusal);
        Assert.NotEqual(missing.Guidance, ambiguous.Guidance);
    }

    [Fact]
    public void Reobservation_requires_exact_rule_identity_and_configuration()
    {
        FirewallRule expected = FirewallRule.ValidLan;
        FirewallOwnershipRecord ownership = FirewallOwnership(expected, "rule-object-1");
        FirewallRule changed = expected with { InterfaceIds = ["lan-interface-2"] };

        FirewallVerification result = FirewallPolicy.Verify(expected, new(changed, false, "rule-object-1"), ownership);

        Assert.Equal(ResourceRefusal.IdentityDrift, result.Refusal);
    }

    private static ShareDesiredState DesiredShare() => new(
        Folder,
        Group,
        Grant,
        "BallsServer.Share.v1",
        1,
        "BallsServer.Authorization.11111111111111111111111111111112",
        1);

    private static ShareAccessEntry[] ExactPermissions() =>
    [
        new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
        new(Group.Sid, ShareAccess.Change),
    ];

    private static ShareOwnershipRecord Ownership(ShareDesiredState desired, ShareObservation live) => new(
        live.Name,
        live.StableObjectId,
        desired.ProductMarker,
        desired.Revision,
        SharePolicy.Fingerprint(live.Name, live.StableObjectId, live.FolderIdentity, live.Permissions, desired.ProductMarker, desired.Revision));

    private static FirewallOwnershipRecord FirewallOwnership(FirewallRule rule, string objectId) => new(
        objectId,
        rule.ProductMarker,
        rule.Revision,
        FirewallPolicy.Fingerprint(rule));

    private static NetworkInterfaceObservation LanInterface() => new(
        "lan-interface-1",
        NetworkProfile.Private,
        IsTailscale: false,
        ObservationComplete: true,
        LocalAddressRanges: ["192.168.1.10/32"],
        RemoteAddressRanges: ["192.168.1.0/24"],
        ObservationToken: "BallsServer.FirewallObservation.lan",
        ObservationEpoch: 1);

    private static NetworkInterfaceObservation TailscaleInterface() => new(
        "tailscale-interface-1",
        NetworkProfile.Private,
        IsTailscale: true,
        ObservationComplete: true,
        LocalAddressRanges: ["100.64.1.2/32"],
        RemoteAddressRanges: ["100.64.0.0/10", "fd7a:115c:a1e0::/48"],
        ObservationToken: "BallsServer.FirewallObservation.tailscale",
        ObservationEpoch: 1);

    private static SmbPrerequisiteObservation CompliantSmb() => new(
        ObservationComplete: true,
        ServerRunning: true,
        Smb1Disabled: true,
        Smb2And3Enabled: true,
        MinimumDialect: new Version(3, 0),
        SigningPreserved: true,
        PolicyManaged: false,
        GuestAnonymousOrBlankPasswordAccepted: false,
        MaximumDialect: new Version(3, 1, 1),
        DialectBoundsComplete: true,
        DialectBoundsMalformed: false);
}
