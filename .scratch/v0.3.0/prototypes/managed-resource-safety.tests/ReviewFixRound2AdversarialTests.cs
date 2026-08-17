using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class ReviewFixRound2AdversarialTests
{
    private const string GroupSid = "S-1-5-21-111-222-333-444";
    private static readonly FolderIdentity Folder = new("volume-1", "file-1", "C:\\fixture", "descriptor-1");
    private static readonly ProductGroupIdentity Group = new("Balls Server Access", GroupSid, "group-object-1", "BallsServer.Group.v1");
    private static readonly ProductGrantIdentity Grant = new("S-1-5-21-111-222-333-555", "grant-object-1");

    [Theory]
    [InlineData("link", "C:\\fixture\\..\\outside", "volume-1")]
    [InlineData("link", "D:\\outside", "volume-2")]
    [InlineData("..\\outside\\link", "C:\\fixture\\child", "volume-1")]
    public void Final_folder_validation_rejects_forged_containment_and_noncanonical_link_paths(
        string relativeLinkPath,
        string targetPath,
        string targetVolume)
    {
        DescendantLink forged = new(
            relativeLinkPath,
            relativeLinkPath.StartsWith("..", StringComparison.Ordinal)
                ? "C:\\fixture\\..\\outside\\link"
                : "C:\\fixture\\link",
            targetPath,
            TargetEvidenceComplete: true,
            ReportedTargetContained: true,
            StableLinkObjectId: "link-object-1",
            StableTargetObjectId: "target-object-1",
            TargetVolumeId: targetVolume);
        FolderObservation observation = ValidFolder() with { DescendantLinks = [forged] };

        FolderValidation result = ManagedFolderPolicy.Validate(observation);

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.UnresolvedDescendantReparse, result.Refusal);
    }

    [Theory]
    [InlineData("/C:/fixture/link")]
    [InlineData("C:\\\\fixture\\link")]
    [InlineData("C:\\fixture\\link\\")]
    [InlineData("C:fixture\\link")]
    public void Final_folder_validation_rejects_noncanonical_absolute_link_aliases(string canonicalLinkPath)
    {
        DescendantLink link = new(
            "link",
            canonicalLinkPath,
            "C:\\fixture\\target",
            TargetEvidenceComplete: true,
            ReportedTargetContained: true,
            StableLinkObjectId: "link-object-1",
            StableTargetObjectId: "target-object-1",
            TargetVolumeId: "volume-1");

        FolderValidation result = ManagedFolderPolicy.Validate(ValidFolder() with { DescendantLinks = [link] });

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.UnresolvedDescendantReparse, result.Refusal);
    }

    [Fact]
    public void Undefined_folder_path_kind_fails_closed()
    {
        FolderValidation result = ManagedFolderPolicy.Validate(ValidFolder() with { PathKind = (FolderPathKind)999 });

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.Unknown, result.Refusal);
    }

    [Theory]
    [InlineData("volume")]
    [InlineData("file")]
    [InlineData("descriptor")]
    public void Folder_identity_requires_closed_stable_evidence(string missingField)
    {
        FolderIdentity identity = missingField switch
        {
            "volume" => Folder with { VolumeId = string.Empty },
            "file" => Folder with { FileId = string.Empty },
            "descriptor" => Folder with { DescriptorFingerprint = string.Empty },
            _ => throw new InvalidOperationException(),
        };

        Assert.False(ManagedFolderPolicy.Validate(ValidFolder() with { Identity = identity }).Accepted);
    }

    [Fact]
    public void Undefined_firewall_policy_state_fails_closed()
    {
        FirewallPlan result = FirewallPolicy.PlanLan(LanInterface(), (FirewallPolicyState)999);

        Assert.False(result.Accepted);
        Assert.Equal(ResourceRefusal.Unknown, result.Refusal);
    }

    public static TheoryData<string> InvalidLanRemoteCidrs => new()
    {
        { "10.0.0.0/0" },
        { "192.168.999.0/24" },
        { "172.16.0.0/0" },
        { "192.168.1.0/24 " },
    };

    [Theory]
    [MemberData(nameof(InvalidLanRemoteCidrs))]
    public void Lan_rule_rejects_semantically_invalid_or_noncanonical_remote_cidrs(string remoteCidr)
    {
        FirewallPlan result = FirewallPolicy.PlanLan(
            LanInterface() with { RemoteAddressRanges = [remoteCidr] },
            FirewallPolicyState.LocalWritable);

        Assert.False(result.Accepted);
    }

    [Fact]
    public void Lan_rule_rejects_remote_subnet_that_does_not_contain_selected_interface_host()
    {
        FirewallPlan result = FirewallPolicy.PlanLan(
            LanInterface() with { RemoteAddressRanges = ["10.0.0.0/8"] },
            FirewallPolicyState.LocalWritable);

        Assert.False(result.Accepted);
    }

    [Theory]
    [InlineData("192.168.1.0/32", "192.168.1.0/24")]
    [InlineData("192.168.1.10/32", "192.168.1.10/32")]
    public void Lan_rule_requires_a_concrete_host_distinct_from_a_canonical_subnet(string local, string remote)
    {
        FirewallPlan result = FirewallPolicy.PlanLan(
            LanInterface() with { LocalAddressRanges = [local], RemoteAddressRanges = [remote] },
            FirewallPolicyState.LocalWritable);

        Assert.False(result.Accepted);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("object")]
    [InlineData("marker")]
    public void Share_plan_rejects_an_invalid_product_group_identity(string changedField)
    {
        ProductGroupIdentity group = changedField switch
        {
            "name" => Group with { Name = "Equivalent" },
            "object" => Group with { StableObjectId = string.Empty },
            "marker" => Group with { Marker = string.Empty },
            _ => throw new InvalidOperationException(),
        };
        ShareDesiredState desired = DesiredShare() with { GroupIdentity = group };

        Assert.False(SharePolicy.Plan(desired, CompliantSmb(), existing: null, ownership: null).Accepted);
    }

    public static TheoryData<ShareVerificationContradiction> ContradictorySuccesses => new()
    {
        { ShareVerificationContradiction.Folder },
        { ShareVerificationContradiction.Ace },
        { ShareVerificationContradiction.Effective },
        { ShareVerificationContradiction.Smb },
    };

    [Theory]
    [MemberData(nameof(ContradictorySuccesses))]
    public void Share_authorization_rejects_accepted_results_with_non_success_codes(ShareVerificationContradiction contradiction)
    {
        ShareDesiredState desired = DesiredShare();
        SharePlan plan = SharePolicy.Plan(desired, CompliantSmb(), existing: null, ownership: null);
        ShareObservation live = new("Balls", "share-object-1", Folder, ExactPermissions());
        ShareAuthorizationContext context = AuthorizationContext(desired, live);
        FolderUseValidation folder = contradiction == ShareVerificationContradiction.Folder
            ? new(true, FolderRefusal.IdentityDrift, "contradictory", [])
            : new(true, FolderRefusal.None, string.Empty, []);
        AceVerification ace = contradiction == ShareVerificationContradiction.Ace
            ? new(true, AceRefusal.OwnershipMismatch)
            : new(true, AceRefusal.None);
        EffectiveAccessVerification effective = contradiction == ShareVerificationContradiction.Effective
            ? new(true, EffectiveAccessRefusal.AccessDenied)
            : new(true, EffectiveAccessRefusal.None);
        PrerequisiteResult smb = contradiction == ShareVerificationContradiction.Smb
            ? new(true, PrerequisiteRefusal.ServerStopped, "contradictory", [])
            : HostPrerequisitePolicy.Validate(CompliantSmb());

        ShareAuthorizationVerification result = SharePolicy.VerifyAuthorization(
            plan,
            live,
            context,
            new(context, folder),
            new(context, ace),
            new(context, effective),
            new(context, smb),
            new(context, Grant, true, LimitedGrantAccessStatus.Ready, true, true, true, false, false, false));

        Assert.False(result.Accepted);
    }

    private static FolderObservation ValidFolder() => new(
        RequestedPath: "C:\\fixture",
        PathKind: FolderPathKind.Local,
        Exists: true,
        IsDirectory: true,
        IsDriveRoot: false,
        IsProtectedSystemLocation: false,
        IsFixedVolume: true,
        FileSystem: "NTFS",
        RootIsReparsePoint: false,
        AncestorIsReparsePoint: false,
        DescendantScanComplete: true,
        Identity: Folder,
        DescendantLinks: []);

    private static ShareDesiredState DesiredShare() => new(Folder, Group, Grant, "BallsServer.Share.v1", 1, "BallsServer.Authorization.33333333333333333333333333333334", 1);

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

    private static IReadOnlyList<ShareAccessEntry> ExactPermissions() =>
    [
        new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
        new(GroupSid, ShareAccess.Change),
    ];

    private static NetworkInterfaceObservation LanInterface() => new(
        "lan-interface-1",
        NetworkProfile.Private,
        IsTailscale: false,
        ObservationComplete: true,
        LocalAddressRanges: ["192.168.1.10/32"],
        RemoteAddressRanges: ["192.168.1.0/24"],
        ObservationToken: "BallsServer.FirewallObservation.lan",
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

    public enum ShareVerificationContradiction
    {
        Folder,
        Ace,
        Effective,
        Smb,
    }
}
