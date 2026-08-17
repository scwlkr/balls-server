using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class ReviewFixAdversarialTests
{
    private const string GroupSid = "S-1-5-21-111-222-333-444";
    private static readonly FolderIdentity Folder = new("volume-1", "file-1", "C:\\fixture", "descriptor-1");
    private static readonly ProductGroupIdentity Group = new("Balls Server Access", GroupSid, "group-object-1", "BallsServer.Group.v1");
    private static readonly ProductGrantIdentity Grant = new("S-1-5-21-111-222-333-555", "grant-object-1");

    [Fact]
    public void Lexical_traversal_target_is_not_contained()
    {
        SingleEntryTree tree = new(new(
            "C:\\fixture\\link",
            true,
            new("C:\\fixture\\..\\outside", "volume-1", "target-object-1"),
            true,
            "link-object-1"));

        DescendantLink link = Assert.Single(DescendantLinkDiscovery.Discover(new("C:\\fixture", "volume-1"), tree));

        Assert.False(link.ReportedTargetContained);
    }

    public static TheoryData<ProductAce> ExtraSameSidEntries => new()
    {
        { new(GroupSid, AceKind.Deny, ProductRights.Read, InheritanceScope.None, PropagationScope.None, false) },
        { new(GroupSid, AceKind.Allow, ProductRights.Read, InheritanceScope.None, PropagationScope.None, false) },
        { new(GroupSid, AceKind.Allow, ProductRights.Modify | ProductRights.Synchronize, InheritanceScope.None, PropagationScope.None, false) },
        { new(GroupSid, AceKind.Allow, ProductRights.Modify | ProductRights.Synchronize, InheritanceScope.Container | InheritanceScope.ObjectInherit, PropagationScope.NoPropagate, false) },
        { new(GroupSid, AceKind.Allow, ProductRights.Modify | ProductRights.Synchronize, InheritanceScope.Container | InheritanceScope.ObjectInherit, PropagationScope.None, true) },
        { ProductAce.Exact(GroupSid) },
    };

    [Theory]
    [MemberData(nameof(ExtraSameSidEntries))]
    public void Applied_ace_verification_rejects_every_extra_same_sid_entry(ProductAce extra)
    {
        DaclSnapshot before = Snapshot([]);
        DaclSnapshot after = before with { Entries = [ProductAce.Exact(GroupSid), extra] };

        AceVerification verification = ProductAcePolicy.VerifyApplied(before, after, GroupSid);

        Assert.False(verification.Accepted);
    }

    [Fact]
    public void Null_minimum_dialect_is_unknown_not_accepted()
    {
        SmbPrerequisiteObservation observation = CompliantSmb() with { MinimumDialect = null! };

        PrerequisiteResult result = HostPrerequisitePolicy.Validate(observation);

        Assert.False(result.Accepted);
        Assert.Equal(PrerequisiteRefusal.Unknown, result.Refusal);
    }

    [Fact]
    public void Share_cannot_self_attest_ownership_with_a_matching_embedded_record()
    {
        IReadOnlyList<ShareAccessEntry> permissions = ExactSharePermissions();
        ShareObservation hostile = new(
            "Balls",
            "attacker-selected-id",
            Folder,
            permissions);

        SharePlan result = SharePolicy.Plan(DesiredShare(), CompliantSmb(), hostile, ownership: null);

        Assert.False(result.Accepted);
        Assert.Equal(ResourceRefusal.UnmanagedConflict, result.Refusal);
    }

    [Fact]
    public void Firewall_cannot_self_attest_ownership_with_boolean_and_matching_name()
    {
        FirewallRuleObservation hostile = new(
            FirewallRule.ValidLan,
            IsBuiltIn: false,
            StableObjectId: FirewallRule.ValidLan.StableId);

        FirewallPlan result = FirewallPolicy.PlanLan(
            LanInterface(),
            FirewallPolicyState.LocalWritable,
            hostile,
            ownership: null);

        Assert.False(result.Accepted);
        Assert.Equal(ResourceRefusal.UnmanagedConflict, result.Refusal);
    }

    [Fact]
    public void Firewall_refusals_have_distinct_typed_administrator_guidance()
    {
        (FirewallPlan Plan, string Guidance)[] refusals =
        [
            (FirewallPolicy.PlanLan(LanInterface(), FirewallPolicyState.GroupPolicyManaged), "Administrator action: ask the responsible Group Policy owner to provide a writable product-specific rule store; Balls Server will not override policy."),
            (FirewallPolicy.PlanLan(LanInterface(), FirewallPolicyState.ObservationUnavailable), "Administrator action: complete the unavailable firewall policy and effective-state observations; uncertainty cannot authorize a rule."),
            (FirewallPolicy.PlanLan(LanInterface() with { StableId = "" }, FirewallPolicyState.LocalWritable), "Administrator action: identify one stable Private or Domain interface with exact local and private remote address ranges, then re-run observation."),
            (FirewallPolicy.PlanTailscale(TailscaleInterface(), FirewallPolicyState.LocalWritable, false), "Administrator action: complete the Tailscale-owned install or sign-in flow, then re-run read-only interface observation."),
            (FirewallPolicy.PlanLan(LanInterface(), FirewallPolicyState.ScopeNotExpressible), "Administrator action: provide a policy store that can express the exact interface, local-address, private-remote, profile, and TCP 445 scope; no broader rule is allowed."),
            (FirewallPolicy.PlanLan(LanInterface() with { Profile = NetworkProfile.Public }, FirewallPolicyState.LocalWritable), "Administrator action: correct the proposed rule to enabled Allow inbound TCP 445 on one Private or Domain interface and concrete private address ranges; Public or Any scope is refused."),
            (FirewallPolicy.PlanLan(
                LanInterface(),
                FirewallPolicyState.LocalWritable,
                new(FirewallRule.ValidLan, false, "foreign"),
                ownership: null), "Administrator action: inspect the exact stable rule object and protected ownership record; preserve the built-in or unmanaged rule and choose manual recovery."),
        ];

        Assert.Equal(refusals.Length, refusals.Select(result => result.Plan.Guidance).Distinct(StringComparer.Ordinal).Count());
        Assert.All(refusals, result =>
        {
            Assert.False(result.Plan.Accepted);
            Assert.Equal(result.Guidance, result.Plan.Guidance);
            Assert.StartsWith("Administrator action:", result.Plan.Guidance, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\", result.Plan.Guidance, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\\\\", result.Plan.Guidance, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("S-1-5-", result.Plan.Guidance, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PowerShell", result.Plan.Guidance, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Set-", result.Plan.Guidance, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static DaclSnapshot Snapshot(IReadOnlyList<ProductAce> entries) =>
        new("S-1-5-21-owner", "ProtectedAutoInherited", entries);

    private static IReadOnlyList<ShareAccessEntry> ExactSharePermissions() =>
    [
        new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
        new(GroupSid, ShareAccess.Change),
    ];

    private static ShareDesiredState DesiredShare() => new(Folder, Group, Grant, "BallsServer.Share.v1", 1, "BallsServer.Authorization.55555555555555555555555555555556", 1);

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

    private sealed class SingleEntryTree(TreeEntry entry) : IDescendantTree
    {
        public IReadOnlyList<TreeEntry> Enumerate(string directory) => [entry];
    }
}
