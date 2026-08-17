using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class ReviewFixRound3Tests
{
    private const string GroupSid = "S-1-5-21-111-222-333-444";
    private static readonly FolderIdentity Folder = new("volume-1", "file-1", "C:\\fixture", "descriptor-1");
    private static readonly ProductGroupIdentity Group = new(
        ProductIdentityPolicy.FixedGroupName,
        GroupSid,
        "group-object-1",
        "BallsServer.Group.v1");
    private static readonly ProductGrantIdentity Grant = new("S-1-5-21-111-222-333-555", "grant-object-1");

    public static TheoryData<DesiredMutation> DesiredMutations => new()
    {
        DesiredMutation.FolderVolume,
        DesiredMutation.FolderFile,
        DesiredMutation.FolderPath,
        DesiredMutation.FolderDescriptor,
        DesiredMutation.GroupName,
        DesiredMutation.GroupSid,
        DesiredMutation.GroupObject,
        DesiredMutation.GroupMarker,
        DesiredMutation.GrantSid,
        DesiredMutation.GrantObject,
        DesiredMutation.ProductMarker,
        DesiredMutation.Revision,
        DesiredMutation.AuthorizationToken,
        DesiredMutation.ObservationEpoch,
    };

    [Theory]
    [MemberData(nameof(DesiredMutations))]
    public void Forged_accepted_share_plan_revalidates_every_desired_state_field(DesiredMutation mutation)
    {
        ShareDesiredState desired = Mutate(ValidDesired(), mutation);
        ShareObservation live = ExactLive(desired);
        SharePlan forged = AcceptedCreationPlan(desired);
        ShareAuthorizationContext context = Context(desired, live);

        ShareAuthorizationVerification result = Verify(forged, live, context);

        Assert.False(result.Accepted);
        Assert.Null(result.CapturedOwnership);
    }

    [Fact]
    public void Share_plan_and_accepted_plan_use_the_same_closed_desired_state_contract()
    {
        ShareDesiredState invalid = ValidDesired() with { AuthorizationToken = "BallsServer.Authorization.not-random" };
        ShareObservation live = ExactLive(invalid);

        Assert.False(SharePolicy.Plan(invalid, CompliantSmb(), null, null).Accepted);
        Assert.False(Verify(AcceptedCreationPlan(invalid), live, Context(invalid, live)).Accepted);
    }

    [Fact]
    public void Share_plan_and_verification_reject_principal_aliases_and_identity_collisions()
    {
        ShareDesiredState valid = ValidDesired();
        ShareDesiredState[] invalidStates =
        [
            valid with { GroupIdentity = valid.GroupIdentity with { Sid = "S-1-5-21-0111-222-333-444" } },
            valid with { GrantIdentity = valid.GrantIdentity with { Sid = "S-1-5-21-111-222-333-0444" } },
            valid with { GrantIdentity = valid.GrantIdentity with { Sid = "S-1-5-21-999-222-333-555" } },
            valid with { GrantIdentity = valid.GrantIdentity with { StableObjectId = valid.GroupIdentity.StableObjectId } },
        ];

        foreach (ShareDesiredState invalid in invalidStates)
        {
            ShareObservation live = ExactLive(invalid);

            Assert.False(SharePolicy.Plan(invalid, CompliantSmb(), null, null).Accepted);
            Assert.False(Verify(AcceptedCreationPlan(invalid), live, Context(invalid, live)).Accepted);
        }
    }

    public static TheoryData<RetainedMutation> RetainedTupleMutations => new()
    {
        RetainedMutation.NonNoneRefusal,
        RetainedMutation.UndefinedRefusal,
        RetainedMutation.Guidance,
        RetainedMutation.Mutation,
    };

    [Theory]
    [MemberData(nameof(RetainedTupleMutations))]
    public void Folder_use_rejects_every_contradictory_retained_success_tuple(RetainedMutation mutation)
    {
        FolderObservation current = ValidFolder();
        FolderValidation retained = ManagedFolderPolicy.Validate(current);
        retained = mutation switch
        {
            RetainedMutation.NonNoneRefusal => retained with { Refusal = FolderRefusal.IdentityDrift },
            RetainedMutation.UndefinedRefusal => retained with { Refusal = (FolderRefusal)999 },
            RetainedMutation.Guidance => retained with { Guidance = "contradictory" },
            RetainedMutation.Mutation => retained with { Mutations = ["contradictory"] },
            _ => throw new InvalidOperationException(),
        };

        FolderUseValidation result = ManagedFolderPolicy.ValidateAtUse(retained, current);

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.IdentityDrift, result.Refusal);
    }

    [Fact]
    public void Folder_use_rejects_duplicate_retained_link_evidence_even_when_current_repeats_it()
    {
        DescendantLink link = ValidLink();
        FolderObservation current = ValidFolder() with { DescendantLinks = [link, link] };
        FolderValidation retained = new(true, FolderRefusal.None, string.Empty, Folder, [link, link], []);

        FolderUseValidation result = ManagedFolderPolicy.ValidateAtUse(retained, current);

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.IdentityDrift, result.Refusal);
    }

    [Fact]
    public void Folder_use_rejects_incomplete_retained_identity_and_link_evidence()
    {
        DescendantLink link = ValidLink();
        FolderObservation current = ValidFolder() with { DescendantLinks = [link] };
        FolderValidation incompleteIdentity = new(
            true,
            FolderRefusal.None,
            string.Empty,
            Folder with { DescriptorFingerprint = string.Empty },
            [link],
            []);
        FolderValidation incompleteLink = new(
            true,
            FolderRefusal.None,
            string.Empty,
            Folder,
            [link with { StableTargetObjectId = string.Empty }],
            []);

        Assert.False(ManagedFolderPolicy.ValidateAtUse(incompleteIdentity, current).Accepted);
        Assert.False(ManagedFolderPolicy.ValidateAtUse(incompleteLink, current).Accepted);
    }

    [Theory]
    [InlineData("nested//link")]
    [InlineData("nested/link/")]
    [InlineData("nested\\link")]
    [InlineData("nested/\\link")]
    public void Folder_validation_rejects_noncanonical_relative_link_encodings(string relativePath)
    {
        DescendantLink alias = ValidLink() with { RelativePath = relativePath };
        FolderObservation observation = ValidFolder() with { DescendantLinks = [alias] };

        FolderValidation result = ManagedFolderPolicy.Validate(observation);

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.UnresolvedDescendantReparse, result.Refusal);
    }

    [Fact]
    public void Folder_validation_requires_canonical_link_order()
    {
        DescendantLink later = ValidLink() with
        {
            RelativePath = "z/link",
            CanonicalLinkPath = "C:\\fixture\\z\\link",
            StableLinkObjectId = "link-object-z",
        };
        DescendantLink earlier = ValidLink() with
        {
            RelativePath = "a/link",
            CanonicalLinkPath = "C:\\fixture\\a\\link",
            StableLinkObjectId = "link-object-a",
        };

        FolderValidation result = ManagedFolderPolicy.Validate(ValidFolder() with { DescendantLinks = [later, earlier] });

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.UnresolvedDescendantReparse, result.Refusal);
    }

    public static IEnumerable<object[]> PublicResultTuples()
    {
        foreach (bool accepted in new[] { false, true })
        {
            foreach (ResourceRefusal refusal in Enum.GetValues<ResourceRefusal>().Append((ResourceRefusal)999))
            {
                string expected = (accepted, refusal) switch
                {
                    (true, ResourceRefusal.None) => "Verified: the isolated plan is internally consistent. No changes were made.",
                    (true, _) => "Unknown: contradictory safety result. No changes were made.",
                    (false, ResourceRefusal.None) => "Unknown: contradictory safety result. No changes were made.",
                    (false, ResourceRefusal.UnmanagedConflict) => "Refused: unmanaged product identity conflict. No changes were made.",
                    (false, ResourceRefusal.PublicExposure) => "Refused: private TCP 445 scope could not be proven. No changes were made.",
                    (false, _) when Enum.IsDefined(refusal) => "Refused: required safety evidence was unavailable. No changes were made.",
                    _ => "Unknown: unrecognized safety result. No changes were made.",
                };
                yield return [accepted, refusal, expected];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PublicResultTuples))]
    public void Public_result_formats_verified_only_for_the_canonical_success_tuple(
        bool accepted,
        ResourceRefusal refusal,
        string expected)
    {
        string output = new PrototypeResult(accepted, refusal).ToPublicText();

        Assert.Equal(expected, output);
        Assert.DoesNotContain("C:\\", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("S-1-5-21", output, StringComparison.OrdinalIgnoreCase);
    }

    private static ShareDesiredState ValidDesired() => new(
        Folder,
        Group,
        Grant,
        "BallsServer.Share.v1",
        1,
        "BallsServer.Authorization.0123456789abcdef0123456789abcdef",
        1);

    private static ShareDesiredState Mutate(ShareDesiredState value, DesiredMutation mutation) => mutation switch
    {
        DesiredMutation.FolderVolume => value with { FolderIdentity = value.FolderIdentity with { VolumeId = string.Empty } },
        DesiredMutation.FolderFile => value with { FolderIdentity = value.FolderIdentity with { FileId = string.Empty } },
        DesiredMutation.FolderPath => value with { FolderIdentity = value.FolderIdentity with { CanonicalPath = "C:\\fixture\\..\\outside" } },
        DesiredMutation.FolderDescriptor => value with { FolderIdentity = value.FolderIdentity with { DescriptorFingerprint = string.Empty } },
        DesiredMutation.GroupName => value with { GroupIdentity = value.GroupIdentity with { Name = "Equivalent" } },
        DesiredMutation.GroupSid => value with { GroupIdentity = value.GroupIdentity with { Sid = string.Empty } },
        DesiredMutation.GroupObject => value with { GroupIdentity = value.GroupIdentity with { StableObjectId = string.Empty } },
        DesiredMutation.GroupMarker => value with { GroupIdentity = value.GroupIdentity with { Marker = "foreign" } },
        DesiredMutation.GrantSid => value with { GrantIdentity = value.GrantIdentity with { Sid = string.Empty } },
        DesiredMutation.GrantObject => value with { GrantIdentity = value.GrantIdentity with { StableObjectId = string.Empty } },
        DesiredMutation.ProductMarker => value with { ProductMarker = "foreign" },
        DesiredMutation.Revision => value with { Revision = long.MaxValue },
        DesiredMutation.AuthorizationToken => value with { AuthorizationToken = string.Empty },
        DesiredMutation.ObservationEpoch => value with { ObservationEpoch = long.MaxValue },
        _ => throw new InvalidOperationException(),
    };

    private static SharePlan AcceptedCreationPlan(ShareDesiredState desired) => new(
        true,
        ResourceRefusal.None,
        desired,
        [
            new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
            new(desired.GroupIdentity.Sid, ShareAccess.Change),
        ],
        [SharePlanStep.CreateWithExactDescriptor, SharePlanStep.ReobserveExactState],
        IsCreation: true,
        Ownership: null,
        BlockingPrerequisite: null,
        Mutations: []);

    private static ShareObservation ExactLive(ShareDesiredState desired) => new(
        SharePolicy.FixedShareName,
        "share-object-1",
        desired.FolderIdentity,
        [
            new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
            new(desired.GroupIdentity.Sid, ShareAccess.Change),
        ]);

    private static ShareAuthorizationContext Context(ShareDesiredState desired, ShareObservation live) => new(
        desired.AuthorizationToken,
        desired.ObservationEpoch,
        desired.Revision,
        desired.FolderIdentity,
        desired.GroupIdentity.Sid,
        desired.GroupIdentity.StableObjectId,
        live.Name,
        live.StableObjectId,
        SharePolicy.Fingerprint(
            live.Name,
            live.StableObjectId,
            live.FolderIdentity,
            live.Permissions,
            desired.ProductMarker,
            desired.Revision),
        desired.GrantIdentity.Sid,
        desired.GrantIdentity.StableObjectId);

    private static ShareAuthorizationVerification Verify(
        SharePlan plan,
        ShareObservation live,
        ShareAuthorizationContext context) => SharePolicy.VerifyAuthorization(
            plan,
            live,
            context,
            new(context, new(true, FolderRefusal.None, string.Empty, [])),
            new(context, new(true, AceRefusal.None)),
            new(context, new(true, EffectiveAccessRefusal.None)),
            new(context, new(true, PrerequisiteRefusal.None, string.Empty, [])),
            new(
                context,
                plan.Desired!.GrantIdentity,
                Accepted: true,
                LimitedGrantAccessStatus.Ready,
                ObservationComplete: true,
                GrantCanRead: true,
                GrantCanChange: true,
                GuestCanAccess: false,
                AnonymousCanAccess: false,
                BlankPasswordCanAccess: false));

    private static FolderObservation ValidFolder() => new(
        "C:\\fixture",
        FolderPathKind.Local,
        Exists: true,
        IsDirectory: true,
        IsDriveRoot: false,
        IsProtectedSystemLocation: false,
        IsFixedVolume: true,
        FileSystem: "NTFS",
        RootIsReparsePoint: false,
        AncestorIsReparsePoint: false,
        DescendantScanComplete: true,
        Folder,
        []);

    private static DescendantLink ValidLink() => new(
        "nested/link",
        "C:\\fixture\\nested\\link",
        "C:\\fixture\\target",
        TargetEvidenceComplete: true,
        ReportedTargetContained: true,
        StableLinkObjectId: "link-object-1",
        StableTargetObjectId: "target-object-1",
        TargetVolumeId: "volume-1");

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

    public enum DesiredMutation
    {
        FolderVolume,
        FolderFile,
        FolderPath,
        FolderDescriptor,
        GroupName,
        GroupSid,
        GroupObject,
        GroupMarker,
        GrantSid,
        GrantObject,
        ProductMarker,
        Revision,
        AuthorizationToken,
        ObservationEpoch,
    }

    public enum RetainedMutation
    {
        NonNoneRefusal,
        UndefinedRefusal,
        Guidance,
        Mutation,
    }
}
