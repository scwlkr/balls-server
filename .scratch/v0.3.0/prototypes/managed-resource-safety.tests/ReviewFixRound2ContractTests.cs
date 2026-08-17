using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class ReviewFixRound2ContractTests
{
    private const string GroupSid = "S-1-5-21-111-222-333-444";
    private static readonly FolderIdentity Folder = new("volume-1", "file-1", "C:\\fixture", "descriptor-1");
    private static readonly ProductGroupIdentity Group = new("Balls Server Access", GroupSid, "group-object-1", "BallsServer.Group.v1");
    private static readonly ProductGrantIdentity Grant = new("S-1-5-21-111-222-333-555", "grant-object-1");

    [Fact]
    public void Final_folder_validation_requires_distinct_link_and_target_object_identities()
    {
        DescendantLink link = new(
            "nested/link",
            "C:\\fixture\\nested\\link",
            "C:\\fixture\\target",
            TargetEvidenceComplete: true,
            ReportedTargetContained: true,
            StableLinkObjectId: "link-object-1",
            StableTargetObjectId: "target-object-1",
            TargetVolumeId: "volume-1");

        FolderValidation result = ManagedFolderPolicy.Validate(ValidFolder() with { DescendantLinks = [link] });

        Assert.True(result.Accepted);
        Assert.NotEqual(link.StableLinkObjectId, link.StableTargetObjectId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Link_object_and_target_object_identity_drift_each_refuse_at_use(bool driftLink, bool driftTarget)
    {
        DescendantLink original = ValidLink();
        FolderObservation before = ValidFolder() with { DescendantLinks = [original] };
        DescendantLink changed = original with
        {
            StableLinkObjectId = driftLink ? "link-object-2" : original.StableLinkObjectId,
            StableTargetObjectId = driftTarget ? "target-object-2" : original.StableTargetObjectId,
        };

        FolderUseValidation result = ManagedFolderPolicy.ValidateAtUse(
            ManagedFolderPolicy.Validate(before),
            before with { DescendantLinks = [changed] });

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.IdentityDrift, result.Refusal);
    }

    public static TheoryData<AuthorizationContextMutation> ContextMutations => new()
    {
        AuthorizationContextMutation.Token,
        AuthorizationContextMutation.Epoch,
        AuthorizationContextMutation.PlanRevision,
        AuthorizationContextMutation.FolderVolume,
        AuthorizationContextMutation.FolderFile,
        AuthorizationContextMutation.FolderDescriptor,
        AuthorizationContextMutation.GroupSid,
        AuthorizationContextMutation.GroupObject,
        AuthorizationContextMutation.ShareName,
        AuthorizationContextMutation.ShareObject,
        AuthorizationContextMutation.ShareDescriptor,
        AuthorizationContextMutation.GrantSid,
        AuthorizationContextMutation.GrantObject,
    };

    [Theory]
    [MemberData(nameof(ContextMutations))]
    public void Share_authorization_rejects_every_context_identity_revision_and_fingerprint_mutation(AuthorizationContextMutation mutation)
    {
        ShareScenario scenario = ValidShareScenario();
        ShareAuthorizationContext changed = Mutate(scenario.Context, mutation);

        ShareAuthorizationVerification result = Verify(scenario with
        {
            Context = changed,
            Folder = scenario.Folder with { Context = changed },
            Ace = scenario.Ace with { Context = changed },
            Effective = scenario.Effective with { Context = changed },
            Smb = scenario.Smb with { Context = changed },
            Limited = scenario.Limited with { Context = changed },
        });

        Assert.False(result.Accepted);
        Assert.Null(result.CapturedOwnership);
    }

    [Theory]
    [InlineData(BoundEvidenceSource.Folder)]
    [InlineData(BoundEvidenceSource.Ace)]
    [InlineData(BoundEvidenceSource.Effective)]
    [InlineData(BoundEvidenceSource.Smb)]
    [InlineData(BoundEvidenceSource.LimitedGrant)]
    public void Share_authorization_rejects_cross_context_evidence_mixing(BoundEvidenceSource source)
    {
        ShareScenario scenario = ValidShareScenario();
        ShareAuthorizationContext foreign = scenario.Context with { ObservationToken = "BallsServer.Authorization.foreign" };
        scenario = source switch
        {
            BoundEvidenceSource.Folder => scenario with { Folder = scenario.Folder with { Context = foreign } },
            BoundEvidenceSource.Ace => scenario with { Ace = scenario.Ace with { Context = foreign } },
            BoundEvidenceSource.Effective => scenario with { Effective = scenario.Effective with { Context = foreign } },
            BoundEvidenceSource.Smb => scenario with { Smb = scenario.Smb with { Context = foreign } },
            BoundEvidenceSource.LimitedGrant => scenario with { Limited = scenario.Limited with { Context = foreign } },
            _ => throw new InvalidOperationException(),
        };

        Assert.False(Verify(scenario).Accepted);
    }

    [Fact]
    public void Share_authorization_rejects_observation_for_a_different_limited_grant()
    {
        ShareScenario scenario = ValidShareScenario();
        BoundLimitedGrantAccessObservation foreign = scenario.Limited with
        {
            ObservedGrant = scenario.Limited.ObservedGrant with { StableObjectId = "grant-object-2" },
        };

        Assert.False(Verify(scenario with { Limited = foreign }).Accepted);
    }

    [Fact]
    public void Share_authorization_rejects_contradictory_limited_grant_success_tuple()
    {
        ShareScenario scenario = ValidShareScenario();
        BoundLimitedGrantAccessObservation contradictory = scenario.Limited with
        {
            Accepted = true,
            Status = LimitedGrantAccessStatus.AccessDenied,
        };

        Assert.False(Verify(scenario with { Limited = contradictory }).Accepted);
    }

    [Fact]
    public void Undefined_limited_grant_status_fails_closed()
    {
        ShareScenario scenario = ValidShareScenario();

        Assert.False(Verify(scenario with
        {
            Limited = scenario.Limited with { Status = (LimitedGrantAccessStatus)999 },
        }).Accepted);
    }

    [Fact]
    public void Share_authorization_returns_typed_refusal_for_incomplete_live_identity()
    {
        ShareScenario scenario = ValidShareScenario();

        ShareAuthorizationVerification result = Verify(scenario with
        {
            Live = scenario.Live with { StableObjectId = string.Empty },
        });

        Assert.False(result.Accepted);
        Assert.Equal(ResourceRefusal.IdentityDrift, result.Refusal);
    }

    [Theory]
    [InlineData("volume")]
    [InlineData("file")]
    [InlineData("path")]
    [InlineData("descriptor")]
    public void Share_plan_rejects_incomplete_or_noncanonical_folder_identity(string changedField)
    {
        ShareDesiredState desired = ValidShareScenario().Plan.Desired!;
        FolderIdentity folder = changedField switch
        {
            "volume" => Folder with { VolumeId = string.Empty },
            "file" => Folder with { FileId = string.Empty },
            "path" => Folder with { CanonicalPath = "relative\\fixture" },
            "descriptor" => Folder with { DescriptorFingerprint = string.Empty },
            _ => throw new InvalidOperationException(),
        };

        Assert.False(SharePolicy.Plan(desired with { FolderIdentity = folder }, CompliantSmb(), null, null).Accepted);
    }

    [Fact]
    public void Helper_owned_context_namespaces_require_a_nonempty_unique_suffix()
    {
        ShareDesiredState desired = ValidShareScenario().Plan.Desired! with
        {
            AuthorizationToken = "BallsServer.Authorization.",
        };
        NetworkInterfaceObservation network = LanInterface("192.168.1.10/32", "192.168.1.0/24") with
        {
            ObservationToken = "BallsServer.FirewallObservation.",
        };

        Assert.False(SharePolicy.Plan(desired, CompliantSmb(), null, null).Accepted);
        Assert.False(FirewallPolicy.PlanLan(network, FirewallPolicyState.LocalWritable).Accepted);
    }

    [Fact]
    public void Share_authorization_rejects_a_caller_forged_accepted_plan_with_broad_permissions_or_missing_steps()
    {
        ShareScenario scenario = ValidShareScenario();
        IReadOnlyList<ShareAccessEntry> broad =
        [
            new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
            new(GroupSid, ShareAccess.Change),
            new("Everyone", ShareAccess.Read),
        ];
        ShareObservation live = scenario.Live with { Permissions = broad };
        ShareAuthorizationContext context = scenario.Context with
        {
            ShareDescriptorFingerprint = SharePolicy.Fingerprint(
                live.Name,
                live.StableObjectId,
                live.FolderIdentity,
                live.Permissions,
                scenario.Plan.Desired!.ProductMarker,
                scenario.Plan.Desired.Revision),
        };
        SharePlan forged = scenario.Plan with { Permissions = broad, Steps = [] };

        ShareAuthorizationVerification result = Verify(scenario with
        {
            Plan = forged,
            Live = live,
            Context = context,
            Folder = scenario.Folder with { Context = context },
            Ace = scenario.Ace with { Context = context },
            Effective = scenario.Effective with { Context = context },
            Smb = scenario.Smb with { Context = context },
            Limited = scenario.Limited with { Context = context },
        });

        Assert.False(result.Accepted);
    }

    public static TheoryData<NetworkInterfaceObservation, bool> SemanticLanCidrs => new()
    {
        { LanInterface("10.0.0.1/32", "10.0.0.0/8"), true },
        { LanInterface("10.0.0.1/32", "10.0.0.0/7"), false },
        { LanInterface("10.255.255.254/32", "10.0.0.0/8"), true },
        { LanInterface("172.16.0.1/32", "172.16.0.0/12"), true },
        { LanInterface("172.31.255.254/32", "172.16.0.0/12"), true },
        { LanInterface("192.168.1.10/32", "192.168.1.0/24"), true },
        { LanInterface("192.168.1.10/24", "192.168.1.0/24"), false },
        { LanInterface("127.0.0.1/32", "127.0.0.0/8"), false },
        { LanInterface("169.254.1.1/32", "169.254.0.0/16"), false },
        { LanInterface("224.0.0.1/32", "224.0.0.0/4"), false },
        { LanInterface("0.0.0.0/32", "0.0.0.0/8"), false },
        { LanInterface("8.8.8.8/32", "8.8.8.0/24"), false },
        { LanInterface("192.168.1.10/32", "192.168.1.1/24"), false },
        { LanInterface("fd12:3456:789a::1/128", "fd12:3456:789a::/64"), true },
        { LanInterface("fd12:3456:789a::1/128", "fc00::/6"), false },
    };

    [Theory]
    [MemberData(nameof(SemanticLanCidrs))]
    public void Lan_cidr_math_accepts_only_canonical_private_host_and_matching_subnet(NetworkInterfaceObservation observation, bool expected)
    {
        FirewallPlan result = FirewallPolicy.PlanLan(observation, FirewallPolicyState.LocalWritable);

        Assert.Equal(expected, result.Accepted);
    }

    [Fact]
    public void Tailscale_accepts_ipv6_host_boundary_only_with_exact_approved_observed_ranges()
    {
        NetworkInterfaceObservation observation = new(
            "tailscale-interface-1",
            NetworkProfile.Private,
            IsTailscale: true,
            ObservationComplete: true,
            LocalAddressRanges: ["fd7a:115c:a1e0::1/128"],
            RemoteAddressRanges: ["100.64.0.0/10", "fd7a:115c:a1e0::/48"],
            ObservationToken: "BallsServer.FirewallObservation.tailscale",
            ObservationEpoch: 4);

        Assert.True(FirewallPolicy.PlanTailscale(observation, FirewallPolicyState.LocalWritable, tailscaleEvidencePresent: true).Accepted);
        Assert.False(FirewallPolicy.PlanTailscale(
            observation with { RemoteAddressRanges = ["100.64.0.0/10"] },
            FirewallPolicyState.LocalWritable,
            tailscaleEvidencePresent: true).Accepted);
    }

    [Fact]
    public void Firewall_rule_binds_fresh_interface_observation_context()
    {
        NetworkInterfaceObservation observation = LanInterface("192.168.1.10/32", "192.168.1.0/24");
        FirewallPlan plan = FirewallPolicy.PlanLan(observation, FirewallPolicyState.LocalWritable);

        Assert.True(plan.Accepted);
        Assert.Equal(observation.ObservationToken, plan.Rule!.InterfaceObservationToken);
        Assert.Equal(observation.ObservationEpoch, plan.Rule.InterfaceObservationEpoch);
    }

    private static ShareAuthorizationVerification Verify(ShareScenario scenario) => SharePolicy.VerifyAuthorization(
        scenario.Plan,
        scenario.Live,
        scenario.Context,
        scenario.Folder,
        scenario.Ace,
        scenario.Effective,
        scenario.Smb,
        scenario.Limited);

    private static ShareScenario ValidShareScenario()
    {
        ShareDesiredState desired = new(
            Folder,
            Group,
            Grant,
            "BallsServer.Share.v1",
            Revision: 7,
            AuthorizationToken: "BallsServer.Authorization.22222222222222222222222222222223",
            ObservationEpoch: 42);
        SharePlan plan = SharePolicy.Plan(desired, CompliantSmb(), existing: null, ownership: null);
        ShareObservation live = new("Balls", "share-object-1", Folder, ExactPermissions());
        string descriptor = SharePolicy.Fingerprint(
            live.Name,
            live.StableObjectId,
            live.FolderIdentity,
            live.Permissions,
            desired.ProductMarker,
            desired.Revision);
        ShareAuthorizationContext context = new(
            desired.AuthorizationToken,
            desired.ObservationEpoch,
            desired.Revision,
            Folder,
            GroupSid,
            Group.StableObjectId,
            live.Name,
            live.StableObjectId,
            descriptor,
            Grant.Sid,
            Grant.StableObjectId);

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

    private static ShareAuthorizationContext Mutate(ShareAuthorizationContext value, AuthorizationContextMutation mutation) => mutation switch
    {
        AuthorizationContextMutation.Token => value with { ObservationToken = "BallsServer.Authorization.changed" },
        AuthorizationContextMutation.Epoch => value with { ObservationEpoch = value.ObservationEpoch + 1 },
        AuthorizationContextMutation.PlanRevision => value with { PlanRevision = value.PlanRevision + 1 },
        AuthorizationContextMutation.FolderVolume => value with { FolderIdentity = value.FolderIdentity with { VolumeId = "volume-2" } },
        AuthorizationContextMutation.FolderFile => value with { FolderIdentity = value.FolderIdentity with { FileId = "file-2" } },
        AuthorizationContextMutation.FolderDescriptor => value with { FolderIdentity = value.FolderIdentity with { DescriptorFingerprint = "descriptor-2" } },
        AuthorizationContextMutation.GroupSid => value with { GroupSid = "S-1-5-21-foreign" },
        AuthorizationContextMutation.GroupObject => value with { GroupStableObjectId = "group-object-2" },
        AuthorizationContextMutation.ShareName => value with { ShareName = "Other" },
        AuthorizationContextMutation.ShareObject => value with { ShareStableObjectId = "share-object-2" },
        AuthorizationContextMutation.ShareDescriptor => value with { ShareDescriptorFingerprint = "share-descriptor-2" },
        AuthorizationContextMutation.GrantSid => value with { GrantSid = "S-1-5-21-other-grant" },
        AuthorizationContextMutation.GrantObject => value with { GrantStableObjectId = "grant-object-2" },
        _ => throw new InvalidOperationException(),
    };

    private static DescendantLink ValidLink() => new(
        "nested/link",
        "C:\\fixture\\nested\\link",
        "C:\\fixture\\target",
        true,
        true,
        "link-object-1",
        "target-object-1",
        "volume-1");

    private static FolderObservation ValidFolder() => new(
        "C:\\fixture",
        FolderPathKind.Local,
        true,
        true,
        false,
        false,
        true,
        "NTFS",
        false,
        false,
        true,
        Folder,
        []);

    private static IReadOnlyList<ShareAccessEntry> ExactPermissions() =>
    [
        new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
        new(GroupSid, ShareAccess.Change),
    ];

    private static NetworkInterfaceObservation LanInterface(string local, string remote) => new(
        "lan-interface-1",
        NetworkProfile.Private,
        IsTailscale: false,
        ObservationComplete: true,
        LocalAddressRanges: [local],
        RemoteAddressRanges: [remote],
        ObservationToken: "BallsServer.FirewallObservation.lan",
        ObservationEpoch: 3);

    private static SmbPrerequisiteObservation CompliantSmb() => new(
        true,
        true,
        true,
        true,
        new Version(3, 0),
        true,
        false,
        false,
        new Version(3, 1, 1),
        true,
        false);

    public enum AuthorizationContextMutation
    {
        Token,
        Epoch,
        PlanRevision,
        FolderVolume,
        FolderFile,
        FolderDescriptor,
        GroupSid,
        GroupObject,
        ShareName,
        ShareObject,
        ShareDescriptor,
        GrantSid,
        GrantObject,
    }

    public enum BoundEvidenceSource
    {
        Folder,
        Ace,
        Effective,
        Smb,
        LimitedGrant,
    }

    private sealed record ShareScenario(
        SharePlan Plan,
        ShareObservation Live,
        ShareAuthorizationContext Context,
        BoundFolderUseValidation Folder,
        BoundAceVerification Ace,
        BoundEffectiveAccessVerification Effective,
        BoundPrerequisiteResult Smb,
        BoundLimitedGrantAccessObservation Limited);
}
