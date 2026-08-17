using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class ReviewFixContractTests
{
    private const string GroupSid = "S-1-5-21-111-222-333-444";
    private const string OwnerSid = "S-1-5-21-111-222-333-1001";
    private static readonly FolderIdentity Folder = new("volume-1", "file-1", "C:\\fixture", "descriptor-1");
    private static readonly ProductGroupIdentity Group = new("Balls Server Access", GroupSid, "group-object-1", "BallsServer.Group.v1");
    private static readonly ProductGrantIdentity Grant = new("S-1-5-21-111-222-333-555", "grant-object-1");

    public static TheoryData<string, string, bool> CanonicalTargets => new()
    {
        { "C:\\fixture\\..\\outside", "volume-1", false },
        { "C:\\fixture:alternate", "volume-1", false },
        { "\\\\?\\C:\\fixture\\child", "volume-1", false },
        { "\\\\.\\PhysicalDrive0", "volume-1", false },
        { "\\Device\\HarddiskVolume1\\fixture", "volume-1", false },
        { "fixture\\child", "volume-1", false },
        { "C:\\fixture\\CON", "volume-1", false },
        { "D:\\fixture\\child", "volume-2", false },
        { "C:\\fixture\\child", "VOLUME-1", false },
        { "c:/FIXTURE/child", "volume-1", true },
        { "C:\\fixture\\nested\\child", "volume-1", true },
    };

    [Theory]
    [MemberData(nameof(CanonicalTargets))]
    public void Descendant_containment_requires_canonical_segments_and_exact_volume(string targetPath, string targetVolume, bool expectedContained)
    {
        DescendantRootIdentity root = new("C:\\fixture", "volume-1");
        CanonicalPathIdentity target = new(targetPath, targetVolume, "target-file-1");
        CanonicalTree tree = new(new("C:\\fixture\\link", true, target, CanonicalizationComplete: true, "link-object-1"));

        DescendantLink link = Assert.Single(DescendantLinkDiscovery.Discover(root, tree));

        Assert.Equal(expectedContained, link.ReportedTargetContained);
        Assert.Equal(targetVolume, link.TargetVolumeId);
        Assert.Equal("target-file-1", link.StableTargetObjectId);
    }

    [Theory]
    [MemberData(nameof(ReviewFixAdversarialTests.ExtraSameSidEntries), MemberType = typeof(ReviewFixAdversarialTests))]
    public void Owned_idempotency_and_removal_reject_every_extra_same_sid_entry(ProductAce extra)
    {
        DaclSnapshot before = Snapshot([]);
        AcePlan added = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);
        DaclSnapshot hostile = added.After! with { Entries = [.. added.After.Entries, extra] };

        Assert.False(ProductAcePolicy.PlanAdd(hostile, GroupSid, added.Ownership).Accepted);
        Assert.False(ProductAcePolicy.PlanRemove(hostile, added.Ownership!).Accepted);
    }

    [Fact]
    public void Fresh_effective_access_proves_least_product_rights_and_preserved_control_principals()
    {
        EffectiveAccessSnapshot before = AccessSnapshot(productRights: ProductRights.None);
        EffectiveAccessSnapshot after = AccessSnapshot(productRights: ProductRights.Modify | ProductRights.Synchronize);

        EffectiveAccessVerification result = ProductAcePolicy.VerifyEffectiveAccess(before, after, OwnerSid, GroupSid);

        Assert.True(result.Accepted);
        Assert.Equal(EffectiveAccessRefusal.None, result.Refusal);
    }

    public static TheoryData<EffectiveAccessSnapshot, EffectiveAccessSnapshot, EffectiveAccessRefusal> UnsafeEffectiveAccess => new()
    {
        { AccessSnapshot(ProductRights.None, productAvailable: false), AccessSnapshot(ProductRights.Modify | ProductRights.Synchronize), EffectiveAccessRefusal.ObservationUnavailable },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.Modify | ProductRights.Synchronize, productDenied: true), EffectiveAccessRefusal.AccessDenied },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.Read), EffectiveAccessRefusal.ProductAccessInsufficient },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.FullControl), EffectiveAccessRefusal.ProductAccessExcessive },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.Modify | ProductRights.Synchronize, ownerRights: ProductRights.Read), EffectiveAccessRefusal.ControlAccessDrift },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.Modify | ProductRights.Synchronize, systemRights: ProductRights.Read), EffectiveAccessRefusal.ControlAccessDrift },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.Modify | ProductRights.Synchronize, administratorRights: ProductRights.Read), EffectiveAccessRefusal.ControlAccessDrift },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.Modify | ProductRights.Synchronize, ownerSid: "S-1-5-21-wrong"), EffectiveAccessRefusal.IdentityMismatch },
        { AccessSnapshot(ProductRights.None), AccessSnapshot(ProductRights.Modify | ProductRights.Synchronize, systemAvailable: false), EffectiveAccessRefusal.ObservationUnavailable },
    };

    [Theory]
    [MemberData(nameof(UnsafeEffectiveAccess))]
    public void Effective_access_fails_closed_for_unavailable_denied_insufficient_excessive_or_drifted_state(
        EffectiveAccessSnapshot before,
        EffectiveAccessSnapshot after,
        EffectiveAccessRefusal expected)
    {
        EffectiveAccessVerification result = ProductAcePolicy.VerifyEffectiveAccess(before, after, OwnerSid, GroupSid);

        Assert.False(result.Accepted);
        Assert.Equal(expected, result.Refusal);
    }

    [Fact]
    public void New_share_plan_has_no_ownership_until_exact_post_creation_verification()
    {
        ShareDesiredState desired = DesiredShare();

        SharePlan plan = SharePolicy.Plan(desired, CompliantSmb(), existing: null, ownership: null);

        Assert.True(plan.Accepted);
        Assert.True(plan.IsCreation);
        Assert.Null(plan.Ownership);
        Assert.Equal(ProductIdentityPolicy.AdministratorsSid, plan.Permissions[0].Principal);
    }

    [Fact]
    public void Existing_share_requires_independent_protected_ledger_ownership()
    {
        ShareDesiredState desired = DesiredShare();
        ShareObservation live = ExactLiveShare();

        SharePlan result = SharePolicy.Plan(desired, CompliantSmb(), live, ownership: null);

        Assert.False(result.Accepted);
        Assert.Equal(ResourceRefusal.UnmanagedConflict, result.Refusal);
    }

    [Fact]
    public void Share_fingerprint_binds_name_stable_id_folder_descriptor_marker_and_revision()
    {
        ShareDesiredState desired = DesiredShare();
        ShareObservation live = ExactLiveShare();
        string fingerprint = SharePolicy.Fingerprint(
            live.Name,
            live.StableObjectId,
            live.FolderIdentity,
            live.Permissions,
            desired.ProductMarker,
            desired.Revision);
        ShareOwnershipRecord ownership = new(
            live.Name,
            live.StableObjectId,
            desired.ProductMarker,
            desired.Revision,
            fingerprint);

        Assert.True(SharePolicy.Plan(desired, CompliantSmb(), live, ownership).Accepted);
        Assert.False(SharePolicy.Plan(desired, CompliantSmb(), live, ownership with { Revision = 2 }).Accepted);
        Assert.False(SharePolicy.Plan(desired, CompliantSmb(), live, ownership with { ProductMarker = "foreign" }).Accepted);
        Assert.False(SharePolicy.Plan(desired, CompliantSmb(), live with { StableObjectId = "replacement" }, ownership).Accepted);
        Assert.False(SharePolicy.Plan(desired, CompliantSmb(), live with { Name = "Equivalent" }, ownership).Accepted);
    }

    public static TheoryData<ShareVerificationMutation> UnsafeShareIntersection => new()
    {
        ShareVerificationMutation.ShareIdentity,
        ShareVerificationMutation.FolderUse,
        ShareVerificationMutation.Ace,
        ShareVerificationMutation.EffectiveAccess,
        ShareVerificationMutation.Smb,
        ShareVerificationMutation.LimitedGrant,
        ShareVerificationMutation.Guest,
        ShareVerificationMutation.Anonymous,
        ShareVerificationMutation.BlankPassword,
    };

    [Theory]
    [MemberData(nameof(UnsafeShareIntersection))]
    public void Share_authorization_success_requires_every_fresh_intersection_postcondition(ShareVerificationMutation mutation)
    {
        ShareVerificationScenario scenario = ValidShareVerification();
        scenario = mutation switch
        {
            ShareVerificationMutation.ShareIdentity => scenario with { Live = scenario.Live with { Name = "Equivalent" } },
            ShareVerificationMutation.FolderUse => scenario with { FolderUse = scenario.FolderUse with { Result = new(false, FolderRefusal.IdentityDrift, "refused", []) } },
            ShareVerificationMutation.Ace => scenario with { Ace = scenario.Ace with { Result = new(false, AceRefusal.OwnershipMismatch) } },
            ShareVerificationMutation.EffectiveAccess => scenario with { Effective = scenario.Effective with { Result = new(false, EffectiveAccessRefusal.AccessDenied) } },
            ShareVerificationMutation.Smb => scenario with { Smb = scenario.Smb with { Result = new(false, PrerequisiteRefusal.ServerStopped, "Administrator action: restore service.", []) } },
            ShareVerificationMutation.LimitedGrant => scenario with { LimitedGrant = scenario.LimitedGrant with { GrantCanChange = false } },
            ShareVerificationMutation.Guest => scenario with { LimitedGrant = scenario.LimitedGrant with { GuestCanAccess = true } },
            ShareVerificationMutation.Anonymous => scenario with { LimitedGrant = scenario.LimitedGrant with { AnonymousCanAccess = true } },
            ShareVerificationMutation.BlankPassword => scenario with { LimitedGrant = scenario.LimitedGrant with { BlankPasswordCanAccess = true } },
            _ => scenario,
        };

        ShareAuthorizationVerification result = SharePolicy.VerifyAuthorization(
            scenario.Plan,
            scenario.Live,
            scenario.Context,
            scenario.FolderUse,
            scenario.Ace,
            scenario.Effective,
            scenario.Smb,
            scenario.LimitedGrant);

        Assert.False(result.Accepted);
        Assert.Null(result.CapturedOwnership);
    }

    [Fact]
    public void Share_authorization_captures_ownership_only_after_complete_success()
    {
        ShareVerificationScenario scenario = ValidShareVerification();

        ShareAuthorizationVerification result = SharePolicy.VerifyAuthorization(
            scenario.Plan,
            scenario.Live,
            scenario.Context,
            scenario.FolderUse,
            scenario.Ace,
            scenario.Effective,
            scenario.Smb,
            scenario.LimitedGrant);

        Assert.True(result.Accepted);
        Assert.NotNull(result.CapturedOwnership);
        Assert.Equal(scenario.Live.StableObjectId, result.CapturedOwnership.StableObjectId);
    }

    public static TheoryData<FirewallRule> FirewallExpressionDrift => new()
    {
        { FirewallRule.ValidLan with { Enabled = false } },
        { FirewallRule.ValidLan with { StableId = "foreign-rule-id" } },
        { FirewallRule.ValidLan with { Name = "Foreign rule name" } },
        { FirewallRule.ValidLan with { Action = FirewallAction.Block } },
        { FirewallRule.ValidLan with { Action = (FirewallAction)999 } },
        { FirewallRule.ValidLan with { PolicyStore = FirewallPolicyStore.GroupPolicy } },
        { FirewallRule.ValidLan with { PolicyStore = (FirewallPolicyStore)999 } },
        { FirewallRule.ValidLan with { ProductMarker = "foreign" } },
        { FirewallRule.ValidLan with { Revision = 2 } },
        { FirewallRule.ValidLan with { Direction = Direction.Outbound } },
        { FirewallRule.ValidLan with { Direction = (Direction)999 } },
        { FirewallRule.ValidLan with { Protocol = Protocol.Udp } },
        { FirewallRule.ValidLan with { Protocol = (Protocol)999 } },
        { FirewallRule.ValidLan with { LocalPort = 1445 } },
        { FirewallRule.ValidLan with { Profile = NetworkProfile.Public } },
        { FirewallRule.ValidLan with { Profile = (NetworkProfile)999 } },
        { FirewallRule.ValidLan with { InterfaceIds = ["Any"] } },
        { FirewallRule.ValidLan with { InterfaceObservationToken = "BallsServer.FirewallObservation.foreign" } },
        { FirewallRule.ValidLan with { InterfaceObservationEpoch = 2 } },
        { FirewallRule.ValidLan with { LocalAddressRanges = ["Any"] } },
        { FirewallRule.ValidLan with { RemoteAddressRanges = ["Any"] } },
    };

    [Theory]
    [MemberData(nameof(FirewallExpressionDrift))]
    public void Firewall_verification_rejects_every_expression_field_drift(FirewallRule observed)
    {
        FirewallRule expected = FirewallRule.ValidLan;
        FirewallOwnershipRecord ownership = new(
            "windows-rule-object-1",
            expected.ProductMarker,
            expected.Revision,
            FirewallPolicy.Fingerprint(expected));
        FirewallRuleObservation live = new(observed, IsBuiltIn: false, "windows-rule-object-1");

        FirewallVerification result = FirewallPolicy.Verify(expected, live, ownership);

        Assert.False(result.Accepted);
    }

    [Fact]
    public void Firewall_creation_captures_independent_ownership_only_after_exact_live_verification()
    {
        FirewallRule expected = FirewallRule.ValidLan;
        FirewallRuleObservation live = new(expected, IsBuiltIn: false, "windows-rule-object-1");

        FirewallVerification result = FirewallPolicy.CaptureCreated(expected, live);

        Assert.True(result.Accepted);
        Assert.NotNull(result.CapturedOwnership);
        Assert.Equal("windows-rule-object-1", result.CapturedOwnership.StableObjectId);
    }

    [Theory]
    [InlineData(null, "3.1.1", true, false, PrerequisiteRefusal.Unknown)]
    [InlineData("3.0", null, true, false, PrerequisiteRefusal.Unknown)]
    [InlineData("3.0", "3.1.1", false, false, PrerequisiteRefusal.Unknown)]
    [InlineData("3.0", "3.1.1", true, true, PrerequisiteRefusal.Unknown)]
    [InlineData("2.1", "3.1.1", true, false, PrerequisiteRefusal.DialectBelowSmb3)]
    [InlineData("3.0", "3.2", true, false, PrerequisiteRefusal.Unknown)]
    [InlineData("3.1.1", "3.0", true, false, PrerequisiteRefusal.Unknown)]
    [InlineData("3.0", "3.0", true, false, PrerequisiteRefusal.None)]
    [InlineData("3.0", "3.1.1", true, false, PrerequisiteRefusal.None)]
    public void Smb_dialect_bounds_fail_closed_and_accept_only_complete_known_smb3_ranges(
        string? minimum,
        string? maximum,
        bool complete,
        bool malformed,
        PrerequisiteRefusal expected)
    {
        SmbPrerequisiteObservation observation = CompliantSmb() with
        {
            MinimumDialect = minimum is null ? null : Version.Parse(minimum),
            MaximumDialect = maximum is null ? null : Version.Parse(maximum),
            DialectBoundsComplete = complete,
            DialectBoundsMalformed = malformed,
        };

        PrerequisiteResult result = HostPrerequisitePolicy.Validate(observation);

        Assert.Equal(expected, result.Refusal);
        Assert.Equal(expected == PrerequisiteRefusal.None, result.Accepted);
    }

    [Theory]
    [InlineData(PrerequisiteRefusal.ServerStopped, "Administrator action: restore the Windows Server service outside Balls Server, then re-run read-only observation.")]
    [InlineData(PrerequisiteRefusal.Smb1Enabled, "Administrator action: disable SMB1 outside Balls Server, then re-run read-only observation.")]
    [InlineData(PrerequisiteRefusal.SmbDisabled, "Administrator action: enable the supported SMB 2/3 server capability outside Balls Server, then re-run read-only observation.")]
    [InlineData(PrerequisiteRefusal.DialectBelowSmb3, "Administrator action: require a minimum SMB dialect of 3.0 outside Balls Server, then re-run read-only observation.")]
    [InlineData(PrerequisiteRefusal.SigningNotPreserved, "Administrator action: restore SMB signing protections outside Balls Server, then re-run read-only observation.")]
    [InlineData(PrerequisiteRefusal.GuestOrAnonymousAccess, "Administrator action: disable guest, anonymous, and blank-password SMB outside Balls Server, then re-run read-only observation.")]
    [InlineData(PrerequisiteRefusal.PolicyManaged, "Administrator action: ask the responsible policy owner to prove a compliant setting; Balls Server will not override policy.")]
    public void Share_planning_preserves_exact_typed_smb_refusal_and_guidance(PrerequisiteRefusal refusal, string expectedGuidance)
    {
        SmbPrerequisiteObservation observation = SmbFor(refusal);

        SharePlan plan = SharePolicy.Plan(DesiredShare(), observation, existing: null, ownership: null);

        Assert.False(plan.Accepted);
        Assert.Equal(refusal, plan.BlockingPrerequisite!.Refusal);
        Assert.Equal(expectedGuidance, plan.BlockingPrerequisite.Guidance);
        Assert.DoesNotContain("PowerShell", plan.BlockingPrerequisite.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-", plan.BlockingPrerequisite.Guidance, StringComparison.OrdinalIgnoreCase);
    }

    private static DaclSnapshot Snapshot(IReadOnlyList<ProductAce> entries) => new(OwnerSid, "ProtectedAutoInherited", entries);

    private static EffectiveAccessSnapshot AccessSnapshot(
        ProductRights productRights,
        bool productAvailable = true,
        bool productDenied = false,
        ProductRights ownerRights = ProductRights.FullControl,
        ProductRights systemRights = ProductRights.FullControl,
        ProductRights administratorRights = ProductRights.FullControl,
        bool systemAvailable = true,
        string ownerSid = OwnerSid) => new(
        [
            new(EffectivePrincipal.ProductGroup, GroupSid, productAvailable, productDenied, productRights),
            new(EffectivePrincipal.Owner, ownerSid, true, false, ownerRights),
            new(EffectivePrincipal.System, ProductIdentityPolicy.SystemSid, systemAvailable, false, systemRights),
            new(EffectivePrincipal.Administrators, ProductIdentityPolicy.AdministratorsSid, true, false, administratorRights),
        ]);

    private static ShareDesiredState DesiredShare() => new(Folder, Group, Grant, "BallsServer.Share.v1", 1, "BallsServer.Authorization.44444444444444444444444444444445", 1);

    private static IReadOnlyList<ShareAccessEntry> ExactSharePermissions() =>
    [
        new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
        new(GroupSid, ShareAccess.Change),
    ];

    private static ShareObservation ExactLiveShare() => new("Balls", "share-object-1", Folder, ExactSharePermissions());

    private static ShareVerificationScenario ValidShareVerification()
    {
        ShareDesiredState desired = DesiredShare();
        SharePlan plan = SharePolicy.Plan(desired, CompliantSmb(), existing: null, ownership: null);
        ShareObservation live = ExactLiveShare();
        ShareAuthorizationContext context = AuthorizationContext(desired, live);
        return new(
            plan,
            live,
            context,
            new(context, new(true, FolderRefusal.None, string.Empty, [])),
            new(context, new(true, AceRefusal.None)),
            new(context, new(true, EffectiveAccessRefusal.None)),
            new(context, HostPrerequisitePolicy.Validate(CompliantSmb())),
            new(context, Grant, true, LimitedGrantAccessStatus.Ready, true, true, true, false, false, false));
    }

    private static ShareAuthorizationContext AuthorizationContext(ShareDesiredState desired, ShareObservation live) => new(
        desired.AuthorizationToken,
        desired.ObservationEpoch,
        desired.Revision,
        desired.FolderIdentity,
        desired.GroupIdentity.Sid,
        desired.GroupIdentity.StableObjectId,
        live.Name,
        live.StableObjectId,
        SharePolicy.Fingerprint(live.Name, live.StableObjectId, live.FolderIdentity, live.Permissions, desired.ProductMarker, desired.Revision),
        desired.GrantIdentity.Sid,
        desired.GrantIdentity.StableObjectId);

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

    private static SmbPrerequisiteObservation SmbFor(PrerequisiteRefusal refusal) => refusal switch
    {
        PrerequisiteRefusal.ServerStopped => CompliantSmb() with { ServerRunning = false },
        PrerequisiteRefusal.Smb1Enabled => CompliantSmb() with { Smb1Disabled = false },
        PrerequisiteRefusal.SmbDisabled => CompliantSmb() with { Smb2And3Enabled = false },
        PrerequisiteRefusal.DialectBelowSmb3 => CompliantSmb() with { MinimumDialect = new Version(2, 1) },
        PrerequisiteRefusal.SigningNotPreserved => CompliantSmb() with { SigningPreserved = false },
        PrerequisiteRefusal.GuestOrAnonymousAccess => CompliantSmb() with { GuestAnonymousOrBlankPasswordAccepted = true },
        PrerequisiteRefusal.PolicyManaged => CompliantSmb() with { PolicyManaged = true },
        _ => CompliantSmb(),
    };

    public enum ShareVerificationMutation
    {
        ShareIdentity,
        FolderUse,
        Ace,
        EffectiveAccess,
        Smb,
        LimitedGrant,
        Guest,
        Anonymous,
        BlankPassword,
    }

    private sealed record ShareVerificationScenario(
        SharePlan Plan,
        ShareObservation Live,
        ShareAuthorizationContext Context,
        BoundFolderUseValidation FolderUse,
        BoundAceVerification Ace,
        BoundEffectiveAccessVerification Effective,
        BoundPrerequisiteResult Smb,
        BoundLimitedGrantAccessObservation LimitedGrant);

    private sealed class CanonicalTree(TreeEntry entry) : IDescendantTree
    {
        public IReadOnlyList<TreeEntry> Enumerate(string directory) => [entry];
    }
}
