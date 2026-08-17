using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace BallsServer.ManagedResourceSafety;

public enum ResourceRefusal
{
    None,
    UnmanagedConflict,
    IdentityDrift,
    NoncompliantPrerequisite,
    PublicExposure,
    PolicyManaged,
    ScopeNotExpressible,
    Unknown,
    MissingTailscaleEvidence,
    AmbiguousInterface,
    FolderUseFailed,
    AceVerificationFailed,
    EffectiveAccessFailed,
    LimitedGrantAccessFailed,
    GuestOrAnonymousAccess,
}

public sealed record ProductGroupIdentity(string Name, string Sid, string StableObjectId, string Marker);

public sealed record ProductGroupPlan(bool Accepted, ResourceRefusal Refusal, ProductGroupIdentity? Identity);

public static class ProductIdentityPolicy
{
    public const string FixedGroupName = "Balls Server Access";
    public const string ProductGroupMarker = "BallsServer.Group.v1";
    public const string AdministratorsSid = "S-1-5-32-544";
    public const string SystemSid = "S-1-5-18";

    public static ProductGroupPlan PlanGroup(ProductGroupIdentity desired, ProductGroupIdentity? existing)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (desired.Name != FixedGroupName || !SharePolicy.IsCanonicalProductSid(desired.Sid) ||
            string.IsNullOrWhiteSpace(desired.StableObjectId) || desired.Marker != ProductGroupMarker)
        {
            return new(false, ResourceRefusal.UnmanagedConflict, null);
        }

        return existing is null || existing == desired
            ? new(true, ResourceRefusal.None, desired)
            : new(false, ResourceRefusal.UnmanagedConflict, null);
    }
}

public enum ShareAccess
{
    Read,
    Change,
    Full,
}

public sealed record ShareAccessEntry(string Principal, ShareAccess Access);

public sealed record ProductGrantIdentity(string Sid, string StableObjectId);

public sealed record ShareDesiredState(
    FolderIdentity FolderIdentity,
    ProductGroupIdentity GroupIdentity,
    ProductGrantIdentity GrantIdentity,
    string ProductMarker,
    long Revision,
    string AuthorizationToken,
    long ObservationEpoch);

public sealed record ShareOwnershipRecord(
    string Name,
    string StableObjectId,
    string ProductMarker,
    long Revision,
    string CanonicalFingerprint);

public sealed record ShareObservation(
    string Name,
    string StableObjectId,
    FolderIdentity FolderIdentity,
    IReadOnlyList<ShareAccessEntry> Permissions);

public enum SharePlanStep
{
    CreateWithExactDescriptor,
    ReobserveExactState,
}

public sealed record SharePlan(
    bool Accepted,
    ResourceRefusal Refusal,
    ShareDesiredState? Desired,
    IReadOnlyList<ShareAccessEntry> Permissions,
    IReadOnlyList<SharePlanStep> Steps,
    bool IsCreation,
    ShareOwnershipRecord? Ownership,
    PrerequisiteResult? BlockingPrerequisite,
    IReadOnlyList<string> Mutations)
{
    public string? Name => Desired is null ? null : SharePolicy.FixedShareName;

    public FolderIdentity? FolderIdentity => Desired?.FolderIdentity;
}

public sealed record ShareAuthorizationContext(
    string ObservationToken,
    long ObservationEpoch,
    long PlanRevision,
    FolderIdentity FolderIdentity,
    string GroupSid,
    string GroupStableObjectId,
    string ShareName,
    string ShareStableObjectId,
    string ShareDescriptorFingerprint,
    string GrantSid,
    string GrantStableObjectId);

public sealed record BoundFolderUseValidation(ShareAuthorizationContext Context, FolderUseValidation Result);

public sealed record BoundAceVerification(ShareAuthorizationContext Context, AceVerification Result);

public sealed record BoundEffectiveAccessVerification(ShareAuthorizationContext Context, EffectiveAccessVerification Result);

public sealed record BoundPrerequisiteResult(ShareAuthorizationContext Context, PrerequisiteResult Result);

public enum LimitedGrantAccessStatus
{
    Ready,
    ObservationUnavailable,
    AccessDenied,
    IdentityMismatch,
    Unknown,
}

public sealed record BoundLimitedGrantAccessObservation(
    ShareAuthorizationContext Context,
    ProductGrantIdentity ObservedGrant,
    bool Accepted,
    LimitedGrantAccessStatus Status,
    bool ObservationComplete,
    bool GrantCanRead,
    bool GrantCanChange,
    bool GuestCanAccess,
    bool AnonymousCanAccess,
    bool BlankPasswordCanAccess);

public sealed record ShareAuthorizationVerification(
    bool Accepted,
    ResourceRefusal Refusal,
    ShareOwnershipRecord? CapturedOwnership);

public static class SharePolicy
{
    public const string FixedShareName = "Balls";
    public const string ProductMarker = "BallsServer.Share.v1";
    private const string AuthorizationTokenPrefix = "BallsServer.Authorization.";
    private const long MaxSequence = int.MaxValue;

    public static SharePlan Plan(
        ShareDesiredState desired,
        SmbPrerequisiteObservation smb,
        ShareObservation? existing,
        ShareOwnershipRecord? ownership)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (!DesiredStateIsClosed(desired))
        {
            return Refuse(ResourceRefusal.UnmanagedConflict);
        }

        PrerequisiteResult prerequisite = HostPrerequisitePolicy.Validate(smb);
        if (!CanonicalSmbSuccess(prerequisite))
        {
            return Refuse(ResourceRefusal.NoncompliantPrerequisite, prerequisite);
        }

        ShareAccessEntry[] exactPermissions = ExactPermissions(desired.GroupIdentity.Sid);
        if (existing is null)
        {
            if (ownership is not null)
            {
                return Refuse(ResourceRefusal.UnmanagedConflict);
            }

            return new(
                true,
                ResourceRefusal.None,
                desired,
                exactPermissions,
                [SharePlanStep.CreateWithExactDescriptor, SharePlanStep.ReobserveExactState],
                IsCreation: true,
                Ownership: null,
                BlockingPrerequisite: null,
                Mutations: []);
        }

        if (ownership is null || !LiveShareMatches(desired, existing, exactPermissions, ownership))
        {
            return Refuse(ResourceRefusal.UnmanagedConflict);
        }

        return new(
            true,
            ResourceRefusal.None,
            desired,
            exactPermissions,
            [SharePlanStep.ReobserveExactState],
            IsCreation: false,
            ownership,
            BlockingPrerequisite: null,
            Mutations: []);
    }

    public static ShareAuthorizationVerification VerifyAuthorization(
        SharePlan plan,
        ShareObservation live,
        ShareAuthorizationContext context,
        BoundFolderUseValidation folderUse,
        BoundAceVerification ace,
        BoundEffectiveAccessVerification effectiveAccess,
        BoundPrerequisiteResult freshSmb,
        BoundLimitedGrantAccessObservation limitedGrant)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(folderUse);
        ArgumentNullException.ThrowIfNull(ace);
        ArgumentNullException.ThrowIfNull(effectiveAccess);
        ArgumentNullException.ThrowIfNull(freshSmb);
        ArgumentNullException.ThrowIfNull(limitedGrant);

        if (!CanonicalPlanSuccess(plan))
        {
            return RefusedAuthorization(ResourceRefusal.UnmanagedConflict);
        }

        ShareDesiredState desired = plan.Desired!;
        if (live.Name != FixedShareName ||
            string.IsNullOrWhiteSpace(live.StableObjectId) ||
            live.Permissions is null ||
            !FolderIdentityIsClosed(live.FolderIdentity))
        {
            return RefusedAuthorization(ResourceRefusal.IdentityDrift);
        }

        string liveFingerprint = Fingerprint(
            live.Name,
            live.StableObjectId,
            live.FolderIdentity,
            live.Permissions,
            desired.ProductMarker,
            desired.Revision);
        if (!ContextMatches(desired, live, liveFingerprint, context) ||
            folderUse.Context != context ||
            ace.Context != context ||
            effectiveAccess.Context != context ||
            freshSmb.Context != context ||
            limitedGrant.Context != context ||
            limitedGrant.ObservedGrant != desired.GrantIdentity)
        {
            return RefusedAuthorization(ResourceRefusal.IdentityDrift);
        }

        if (live.FolderIdentity != desired.FolderIdentity ||
            !live.Permissions.SequenceEqual(plan.Permissions) ||
            (!plan.IsCreation && (plan.Ownership is null || !LiveShareMatches(desired, live, plan.Permissions, plan.Ownership))))
        {
            return RefusedAuthorization(ResourceRefusal.IdentityDrift);
        }

        if (!CanonicalFolderSuccess(folderUse.Result))
        {
            return RefusedAuthorization(ResourceRefusal.FolderUseFailed);
        }

        if (ace.Result is not { Accepted: true, Refusal: AceRefusal.None })
        {
            return RefusedAuthorization(ResourceRefusal.AceVerificationFailed);
        }

        if (effectiveAccess.Result is not { Accepted: true, Refusal: EffectiveAccessRefusal.None })
        {
            return RefusedAuthorization(ResourceRefusal.EffectiveAccessFailed);
        }

        if (!CanonicalSmbSuccess(freshSmb.Result))
        {
            return RefusedAuthorization(ResourceRefusal.NoncompliantPrerequisite);
        }

        if (limitedGrant is not
            {
                Accepted: true,
                Status: LimitedGrantAccessStatus.Ready,
                ObservationComplete: true,
                GrantCanRead: true,
                GrantCanChange: true,
            })
        {
            return RefusedAuthorization(ResourceRefusal.LimitedGrantAccessFailed);
        }

        if (limitedGrant.GuestCanAccess || limitedGrant.AnonymousCanAccess || limitedGrant.BlankPasswordCanAccess)
        {
            return RefusedAuthorization(ResourceRefusal.GuestOrAnonymousAccess);
        }

        ShareOwnershipRecord captured = new(
            live.Name,
            live.StableObjectId,
            desired.ProductMarker,
            desired.Revision,
            Fingerprint(
                live.Name,
                live.StableObjectId,
                live.FolderIdentity,
                live.Permissions,
                desired.ProductMarker,
                desired.Revision));
        return new(true, ResourceRefusal.None, captured);
    }

    private static bool CanonicalPlanSuccess(SharePlan plan)
    {
        if (plan is not
            {
                Accepted: true,
                Refusal: ResourceRefusal.None,
                Desired: not null,
                Permissions: not null,
                Steps: not null,
                BlockingPrerequisite: null,
                Mutations.Count: 0,
            } ||
            !DesiredStateIsClosed(plan.Desired) ||
            !plan.Permissions.SequenceEqual(ExactPermissions(plan.Desired.GroupIdentity.Sid)))
        {
            return false;
        }

        return plan.IsCreation
            ? plan.Ownership is null &&
              plan.Steps.SequenceEqual(new[] { SharePlanStep.CreateWithExactDescriptor, SharePlanStep.ReobserveExactState })
            : plan.Ownership is not null &&
              plan.Steps.SequenceEqual(new[] { SharePlanStep.ReobserveExactState });
    }

    private static bool CanonicalFolderSuccess(FolderUseValidation result) =>
        result is
        {
            Accepted: true,
            Refusal: FolderRefusal.None,
            Guidance.Length: 0,
            Mutations.Count: 0,
        };

    private static bool CanonicalSmbSuccess(PrerequisiteResult result) =>
        result is
        {
            Accepted: true,
            Refusal: PrerequisiteRefusal.None,
            Guidance.Length: 0,
            Mutations.Count: 0,
        };

    private static bool ContextMatches(
        ShareDesiredState desired,
        ShareObservation live,
        string liveFingerprint,
        ShareAuthorizationContext context) =>
        context.ObservationToken == desired.AuthorizationToken &&
        context.ObservationEpoch == desired.ObservationEpoch &&
        context.PlanRevision == desired.Revision &&
        context.FolderIdentity == desired.FolderIdentity &&
        context.GroupSid == desired.GroupIdentity.Sid &&
        context.GroupStableObjectId == desired.GroupIdentity.StableObjectId &&
        context.ShareName == FixedShareName &&
        context.ShareName == live.Name &&
        context.ShareStableObjectId == live.StableObjectId &&
        context.ShareDescriptorFingerprint == liveFingerprint &&
        context.GrantSid == desired.GrantIdentity.Sid &&
        context.GrantStableObjectId == desired.GrantIdentity.StableObjectId;

    private static bool DesiredStateIsClosed(ShareDesiredState desired) =>
        desired.FolderIdentity is not null &&
        desired.GroupIdentity is not null &&
        desired.GrantIdentity is not null &&
        desired.ProductMarker == ProductMarker &&
        desired.Revision is > 0 and <= MaxSequence &&
        desired.ObservationEpoch is > 0 and <= MaxSequence &&
        FolderIdentityIsClosed(desired.FolderIdentity) &&
        CanonicalGroupIdentity(desired.GroupIdentity) &&
        IsCanonicalProductSid(desired.GrantIdentity.Sid) &&
        PrincipalsAreDistinctOnOneMachine(desired.GroupIdentity, desired.GrantIdentity) &&
        !string.IsNullOrWhiteSpace(desired.GrantIdentity.StableObjectId) &&
        IsCanonicalAuthorizationToken(desired.AuthorizationToken);

    private static bool CanonicalGroupIdentity(ProductGroupIdentity desired)
    {
        ProductGroupPlan plan = ProductIdentityPolicy.PlanGroup(desired, existing: null);
        return plan is { Accepted: true, Refusal: ResourceRefusal.None, Identity: not null } &&
            plan.Identity == desired;
    }

    private static bool IsCanonicalAuthorizationToken(string? value)
    {
        if (value is null || !value.StartsWith(AuthorizationTokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = value.AsSpan(AuthorizationTokenPrefix.Length);
        char[] characters = suffix.ToArray();
        return characters.Length == 32 &&
            characters.Any(character => character != '0') &&
            characters.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    public static bool IsCanonicalProductSid(string? value)
    {
        const string Prefix = "S-1-5-21-";
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] components = value[Prefix.Length..].Split('-');
        return components.Length == 4 && components.All(IsCanonicalPositiveDecimal);
    }

    private static bool IsCanonicalPositiveDecimal(string component) =>
        component.Length > 0 &&
        component.All(char.IsAsciiDigit) &&
        uint.TryParse(
            component,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out uint parsed) &&
        parsed > 0 &&
        component == parsed.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool PrincipalsAreDistinctOnOneMachine(
        ProductGroupIdentity group,
        ProductGrantIdentity grant)
    {
        string[] groupParts = group.Sid.Split('-');
        string[] grantParts = grant.Sid.Split('-');
        return groupParts.Length == 8 &&
            grantParts.Length == 8 &&
            groupParts.Take(7).SequenceEqual(grantParts.Take(7), StringComparer.Ordinal) &&
            groupParts[7] != grantParts[7] &&
            !string.Equals(group.StableObjectId, grant.StableObjectId, StringComparison.Ordinal);
    }

    private static bool FolderIdentityIsClosed(FolderIdentity? folder) =>
        folder is not null &&
        !string.IsNullOrWhiteSpace(folder.VolumeId) &&
        !string.IsNullOrWhiteSpace(folder.FileId) &&
        !string.IsNullOrWhiteSpace(folder.DescriptorFingerprint) &&
        CanonicalWindowsPath.TrySegments(folder.CanonicalPath, out _);

    public static string Fingerprint(
        string name,
        string stableObjectId,
        FolderIdentity folder,
        IReadOnlyList<ShareAccessEntry> permissions,
        string productMarker,
        long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableObjectId);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentException.ThrowIfNullOrWhiteSpace(productMarker);

        string canonical = string.Join(
            "\n",
            new[]
            {
                name,
                stableObjectId,
                folder.VolumeId,
                folder.FileId,
                folder.CanonicalPath,
                folder.DescriptorFingerprint,
                productMarker,
                revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }.Concat(permissions.Select(permission => $"{permission.Principal}|{permission.Access}")));
        return Hash(canonical);
    }

    private static ShareAccessEntry[] ExactPermissions(string groupSid) =>
    [
        new(ProductIdentityPolicy.AdministratorsSid, ShareAccess.Full),
        new(groupSid, ShareAccess.Change),
    ];

    private static bool LiveShareMatches(
        ShareDesiredState desired,
        ShareObservation live,
        IReadOnlyList<ShareAccessEntry> exactPermissions,
        ShareOwnershipRecord ownership)
    {
        if (live.Name != FixedShareName ||
            live.Name != ownership.Name ||
            live.StableObjectId != ownership.StableObjectId ||
            live.FolderIdentity != desired.FolderIdentity ||
            !live.Permissions.SequenceEqual(exactPermissions) ||
            ownership.ProductMarker != desired.ProductMarker ||
            ownership.Revision != desired.Revision)
        {
            return false;
        }

        string fingerprint = Fingerprint(
            live.Name,
            live.StableObjectId,
            live.FolderIdentity,
            live.Permissions,
            desired.ProductMarker,
            desired.Revision);
        return fingerprint == ownership.CanonicalFingerprint;
    }

    private static SharePlan Refuse(ResourceRefusal refusal, PrerequisiteResult? prerequisite = null) =>
        new(false, refusal, null, [], [], false, null, prerequisite, []);

    private static ShareAuthorizationVerification RefusedAuthorization(ResourceRefusal refusal) =>
        new(false, refusal, null);

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}

public sealed record SmbPrerequisiteObservation(
    bool ObservationComplete,
    bool ServerRunning,
    bool Smb1Disabled,
    bool Smb2And3Enabled,
    Version? MinimumDialect,
    bool SigningPreserved,
    bool PolicyManaged,
    bool GuestAnonymousOrBlankPasswordAccepted,
    Version? MaximumDialect = null,
    bool DialectBoundsComplete = true,
    bool DialectBoundsMalformed = false);

public enum PrerequisiteRefusal
{
    None,
    ServerStopped,
    Smb1Enabled,
    SmbDisabled,
    DialectBelowSmb3,
    SigningNotPreserved,
    PolicyManaged,
    GuestOrAnonymousAccess,
    Unknown,
}

public sealed record PrerequisiteResult(
    bool Accepted,
    PrerequisiteRefusal Refusal,
    string Guidance,
    IReadOnlyList<string> Mutations);

public static class HostPrerequisitePolicy
{
    private static readonly Version Smb30 = new(3, 0);
    private static readonly Version Smb302 = new(3, 0, 2);
    private static readonly Version Smb311 = new(3, 1, 1);

    public static PrerequisiteResult Validate(SmbPrerequisiteObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        PrerequisiteRefusal refusal = observation switch
        {
            { ObservationComplete: false } => PrerequisiteRefusal.Unknown,
            { PolicyManaged: true } => PrerequisiteRefusal.PolicyManaged,
            { ServerRunning: false } => PrerequisiteRefusal.ServerStopped,
            { Smb1Disabled: false } => PrerequisiteRefusal.Smb1Enabled,
            { Smb2And3Enabled: false } => PrerequisiteRefusal.SmbDisabled,
            { DialectBoundsComplete: false } or { DialectBoundsMalformed: true } => PrerequisiteRefusal.Unknown,
            { MinimumDialect: null } or { MaximumDialect: null } => PrerequisiteRefusal.Unknown,
            _ when observation.MaximumDialect! < observation.MinimumDialect! => PrerequisiteRefusal.Unknown,
            _ when observation.MinimumDialect! < Smb30 => PrerequisiteRefusal.DialectBelowSmb3,
            _ when !IsKnownDialect(observation.MinimumDialect!) ||
                !IsKnownDialect(observation.MaximumDialect!) => PrerequisiteRefusal.Unknown,
            { SigningPreserved: false } => PrerequisiteRefusal.SigningNotPreserved,
            { GuestAnonymousOrBlankPasswordAccepted: true } => PrerequisiteRefusal.GuestOrAnonymousAccess,
            _ => PrerequisiteRefusal.None,
        };

        return refusal == PrerequisiteRefusal.None
            ? new(true, refusal, string.Empty, [])
            : new(false, refusal, Guidance(refusal), []);
    }

    public static string Guidance(PrerequisiteRefusal refusal) => refusal switch
    {
        PrerequisiteRefusal.ServerStopped => "Administrator action: restore the Windows Server service outside Balls Server, then re-run read-only observation.",
        PrerequisiteRefusal.Smb1Enabled => "Administrator action: disable SMB1 outside Balls Server, then re-run read-only observation.",
        PrerequisiteRefusal.SmbDisabled => "Administrator action: enable the supported SMB 2/3 server capability outside Balls Server, then re-run read-only observation.",
        PrerequisiteRefusal.DialectBelowSmb3 => "Administrator action: require a minimum SMB dialect of 3.0 outside Balls Server, then re-run read-only observation.",
        PrerequisiteRefusal.SigningNotPreserved => "Administrator action: restore SMB signing protections outside Balls Server, then re-run read-only observation.",
        PrerequisiteRefusal.PolicyManaged => "Administrator action: ask the responsible policy owner to prove a compliant setting; Balls Server will not override policy.",
        PrerequisiteRefusal.GuestOrAnonymousAccess => "Administrator action: disable guest, anonymous, and blank-password SMB outside Balls Server, then re-run read-only observation.",
        _ => "Administrator action: complete the unavailable SMB dialect, service, and policy observations; uncertainty cannot authorize setup.",
    };

    private static bool IsKnownDialect(Version dialect) =>
        dialect == Smb30 || dialect == Smb302 || dialect == Smb311;
}

public enum NetworkProfile
{
    Any,
    Public,
    Private,
    Domain,
}

public enum Direction
{
    Inbound,
    Outbound,
}

public enum Protocol
{
    Any,
    Tcp,
    Udp,
}

public enum FirewallAction
{
    Allow,
    Block,
}

public enum FirewallPolicyStore
{
    LocalPersistent,
    GroupPolicy,
    Unknown,
}

public sealed record NetworkInterfaceObservation(
    string StableId,
    NetworkProfile Profile,
    bool IsTailscale,
    bool ObservationComplete = true,
    IReadOnlyList<string>? LocalAddressRanges = null,
    IReadOnlyList<string>? RemoteAddressRanges = null,
    string? ObservationToken = null,
    long ObservationEpoch = 0);

public sealed record FirewallRule(
    string StableId,
    string Name,
    bool Enabled,
    FirewallAction Action,
    FirewallPolicyStore PolicyStore,
    string ProductMarker,
    long Revision,
    Direction Direction,
    Protocol Protocol,
    int LocalPort,
    NetworkProfile Profile,
    string InterfaceObservationToken,
    long InterfaceObservationEpoch,
    IReadOnlyList<string> InterfaceIds,
    IReadOnlyList<string> LocalAddressRanges,
    IReadOnlyList<string> RemoteAddressRanges)
{
    public static FirewallRule ValidLan => new(
        "BallsServer.Firewall.Lan.Smb445.v1",
        "Balls Server SMB 445 - private LAN",
        Enabled: true,
        FirewallAction.Allow,
        FirewallPolicyStore.LocalPersistent,
        "BallsServer.Firewall.Lan.v1",
        Revision: 1,
        Direction.Inbound,
        Protocol.Tcp,
        445,
        NetworkProfile.Private,
        "BallsServer.FirewallObservation.lan",
        1,
        ["lan-interface-1"],
        ["192.168.1.10/32"],
        ["192.168.1.0/24"]);
}

public sealed record FirewallOwnershipRecord(
    string StableObjectId,
    string ProductMarker,
    long Revision,
    string CanonicalFingerprint);

public sealed record FirewallRuleObservation(FirewallRule Rule, bool IsBuiltIn, string StableObjectId);

public enum FirewallPolicyState
{
    LocalWritable,
    GroupPolicyManaged,
    ScopeNotExpressible,
    ObservationUnavailable,
}

public sealed record FirewallPlan(
    bool Accepted,
    ResourceRefusal Refusal,
    FirewallRule? Rule,
    FirewallOwnershipRecord? Ownership,
    string Guidance,
    IReadOnlyList<string> Mutations);

public sealed record FirewallVerification(
    bool Accepted,
    ResourceRefusal Refusal,
    FirewallOwnershipRecord? CapturedOwnership = null);

public static class FirewallPolicy
{
    private const string ObservationTokenPrefix = "BallsServer.FirewallObservation.";
    private static readonly string[] TailscalePrivateRanges = ["100.64.0.0/10", "fd7a:115c:a1e0::/48"];

    public static FirewallPlan PlanLan(
        NetworkInterfaceObservation networkInterface,
        FirewallPolicyState policyState,
        FirewallRuleObservation? existing = null,
        FirewallOwnershipRecord? ownership = null)
    {
        ResourceRefusal policyRefusal = PolicyRefusal(policyState);
        if (policyRefusal != ResourceRefusal.None)
        {
            return Refuse(policyRefusal);
        }

        if (networkInterface.Profile is NetworkProfile.Public or NetworkProfile.Any)
        {
            return Refuse(ResourceRefusal.PublicExposure);
        }

        if (!TryCanonicalInterface(networkInterface, expectTailscale: false, out string[] localRanges, out string[] remoteRanges))
        {
            return Refuse(ResourceRefusal.AmbiguousInterface);
        }

        FirewallRule desired = FirewallRule.ValidLan with
        {
            Profile = networkInterface.Profile,
            InterfaceObservationToken = networkInterface.ObservationToken!,
            InterfaceObservationEpoch = networkInterface.ObservationEpoch,
            InterfaceIds = [networkInterface.StableId],
            LocalAddressRanges = localRanges,
            RemoteAddressRanges = remoteRanges,
        };
        return PlanOwnedRule(desired, existing, ownership);
    }

    public static FirewallPlan PlanTailscale(
        NetworkInterfaceObservation networkInterface,
        FirewallPolicyState policyState,
        bool tailscaleEvidencePresent,
        FirewallRuleObservation? existing = null,
        FirewallOwnershipRecord? ownership = null)
    {
        ResourceRefusal policyRefusal = PolicyRefusal(policyState);
        if (policyRefusal != ResourceRefusal.None)
        {
            return Refuse(policyRefusal);
        }

        if (!tailscaleEvidencePresent)
        {
            return Refuse(ResourceRefusal.MissingTailscaleEvidence);
        }

        if (networkInterface.Profile is NetworkProfile.Public or NetworkProfile.Any)
        {
            return Refuse(ResourceRefusal.PublicExposure);
        }

        if (!TryCanonicalInterface(networkInterface, expectTailscale: true, out string[] localRanges, out string[] remoteRanges))
        {
            return Refuse(ResourceRefusal.AmbiguousInterface);
        }

        FirewallRule desired = new(
            "BallsServer.Firewall.Tailscale.Smb445.v1",
            "Balls Server SMB 445 - Tailscale",
            Enabled: true,
            FirewallAction.Allow,
            FirewallPolicyStore.LocalPersistent,
            "BallsServer.Firewall.Tailscale.v1",
            Revision: 1,
            Direction.Inbound,
            Protocol.Tcp,
            445,
            networkInterface.Profile,
            networkInterface.ObservationToken!,
            networkInterface.ObservationEpoch,
            [networkInterface.StableId],
            localRanges,
            remoteRanges);
        return PlanOwnedRule(desired, existing, ownership);
    }

    public static FirewallVerification Verify(
        FirewallRule expected,
        FirewallRuleObservation observed,
        FirewallOwnershipRecord ownership)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(ownership);

        if (observed.IsBuiltIn || !IsNarrow(expected) || !IsNarrow(observed.Rule))
        {
            return new(false, observed.IsBuiltIn ? ResourceRefusal.UnmanagedConflict : ResourceRefusal.PublicExposure);
        }

        string expectedFingerprint = Fingerprint(expected);
        if (ownership.StableObjectId != observed.StableObjectId ||
            ownership.ProductMarker != expected.ProductMarker ||
            ownership.Revision != expected.Revision ||
            ownership.CanonicalFingerprint != expectedFingerprint ||
            Fingerprint(observed.Rule) != expectedFingerprint)
        {
            return new(false, ResourceRefusal.IdentityDrift);
        }

        return new(true, ResourceRefusal.None, ownership);
    }

    public static FirewallVerification CaptureCreated(FirewallRule expected, FirewallRuleObservation observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);

        if (observed.IsBuiltIn || string.IsNullOrWhiteSpace(observed.StableObjectId) ||
            !IsNarrow(expected) || !IsNarrow(observed.Rule) || Fingerprint(expected) != Fingerprint(observed.Rule))
        {
            return new(false, observed.IsBuiltIn ? ResourceRefusal.UnmanagedConflict : ResourceRefusal.IdentityDrift);
        }

        FirewallOwnershipRecord captured = new(
            observed.StableObjectId,
            expected.ProductMarker,
            expected.Revision,
            Fingerprint(expected));
        return new(true, ResourceRefusal.None, captured);
    }

    public static string Fingerprint(FirewallRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        string canonical = string.Join(
            "\n",
            new[]
            {
                rule.StableId,
                rule.Name,
                rule.Enabled.ToString(System.Globalization.CultureInfo.InvariantCulture),
                rule.Action.ToString(),
                rule.PolicyStore.ToString(),
                rule.ProductMarker,
                rule.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                rule.Direction.ToString(),
                rule.Protocol.ToString(),
                rule.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                rule.Profile.ToString(),
                rule.InterfaceObservationToken,
                rule.InterfaceObservationEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Join(",", rule.InterfaceIds),
                string.Join(",", rule.LocalAddressRanges),
                string.Join(",", rule.RemoteAddressRanges),
            });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string Guidance(ResourceRefusal refusal) => refusal switch
    {
        ResourceRefusal.PolicyManaged => "Administrator action: ask the responsible Group Policy owner to provide a writable product-specific rule store; Balls Server will not override policy.",
        ResourceRefusal.Unknown => "Administrator action: complete the unavailable firewall policy and effective-state observations; uncertainty cannot authorize a rule.",
        ResourceRefusal.AmbiguousInterface => "Administrator action: identify one stable Private or Domain interface with exact local and private remote address ranges, then re-run observation.",
        ResourceRefusal.MissingTailscaleEvidence => "Administrator action: complete the Tailscale-owned install or sign-in flow, then re-run read-only interface observation.",
        ResourceRefusal.ScopeNotExpressible => "Administrator action: provide a policy store that can express the exact interface, local-address, private-remote, profile, and TCP 445 scope; no broader rule is allowed.",
        ResourceRefusal.UnmanagedConflict => "Administrator action: inspect the exact stable rule object and protected ownership record; preserve the built-in or unmanaged rule and choose manual recovery.",
        ResourceRefusal.PublicExposure => "Administrator action: correct the proposed rule to enabled Allow inbound TCP 445 on one Private or Domain interface and concrete private address ranges; Public or Any scope is refused.",
        _ => "Administrator action: re-observe the exact product firewall rule identity and every canonical expression field before retrying.",
    };

    private static FirewallPlan PlanOwnedRule(
        FirewallRule desired,
        FirewallRuleObservation? existing,
        FirewallOwnershipRecord? ownership)
    {
        if (!IsNarrow(desired))
        {
            return Refuse(ResourceRefusal.PublicExposure);
        }

        if (existing is null)
        {
            return ownership is null
                ? new(true, ResourceRefusal.None, desired, null, string.Empty, [])
                : Refuse(ResourceRefusal.UnmanagedConflict);
        }

        FirewallVerification verification = ownership is null
            ? new(false, ResourceRefusal.UnmanagedConflict)
            : Verify(desired, existing, ownership);
        if (verification is not { Accepted: true, Refusal: ResourceRefusal.None, CapturedOwnership: not null } ||
            verification.CapturedOwnership != ownership)
        {
            return Refuse(ResourceRefusal.UnmanagedConflict);
        }

        return new(true, ResourceRefusal.None, desired, ownership, string.Empty, []);
    }

    private static bool TryCanonicalInterface(
        NetworkInterfaceObservation value,
        bool expectTailscale,
        out string[] localRanges,
        out string[] remoteRanges)
    {
        localRanges = [];
        remoteRanges = [];
        if (!value.ObservationComplete ||
            value.IsTailscale != expectTailscale ||
            value.Profile is not (NetworkProfile.Private or NetworkProfile.Domain) ||
            string.IsNullOrWhiteSpace(value.StableId) ||
            string.Equals(value.StableId, "Any", StringComparison.OrdinalIgnoreCase) ||
            value.ObservationToken is null ||
            !value.ObservationToken.StartsWith(ObservationTokenPrefix, StringComparison.Ordinal) ||
            value.ObservationToken.Length == ObservationTokenPrefix.Length ||
            value.ObservationEpoch <= 0 ||
            value.LocalAddressRanges is not { Count: 1 } ||
            value.RemoteAddressRanges is null ||
            !CanonicalCidr.TryParse(value.LocalAddressRanges[0], requireNetworkAddress: false, out CanonicalCidr local) ||
            !local.IsHostAddress ||
            !(expectTailscale ? local.IsTailscalePrivate : local.IsPrivateLan))
        {
            return false;
        }

        if (expectTailscale)
        {
            if (!value.RemoteAddressRanges.SequenceEqual(TailscalePrivateRanges, StringComparer.Ordinal) ||
                value.RemoteAddressRanges.Any(range =>
                    !CanonicalCidr.TryParse(range, requireNetworkAddress: true, out CanonicalCidr parsed) ||
                    !parsed.IsTailscalePrivate))
            {
                return false;
            }
        }
        else
        {
            if (value.RemoteAddressRanges.Count != 1 ||
                !CanonicalCidr.TryParse(value.RemoteAddressRanges[0], requireNetworkAddress: true, out CanonicalCidr remote) ||
                !remote.IsPrivateLan ||
                !remote.IsSubnet ||
                !remote.ContainsUsableHost(local))
            {
                return false;
            }
        }

        localRanges = [local.Canonical];
        remoteRanges = value.RemoteAddressRanges.ToArray();
        return true;
    }

    private static bool IsNarrow(FirewallRule rule) =>
        rule.Enabled &&
        rule.Action == FirewallAction.Allow &&
        rule.PolicyStore == FirewallPolicyStore.LocalPersistent &&
        !string.IsNullOrWhiteSpace(rule.ProductMarker) &&
        rule.Revision > 0 &&
        rule.Direction == Direction.Inbound &&
        rule.Protocol == Protocol.Tcp &&
        rule.LocalPort == 445 &&
        rule.Profile is NetworkProfile.Private or NetworkProfile.Domain &&
        rule.InterfaceObservationToken.StartsWith(ObservationTokenPrefix, StringComparison.Ordinal) &&
        rule.InterfaceObservationToken.Length > ObservationTokenPrefix.Length &&
        rule.InterfaceObservationEpoch > 0 &&
        rule.InterfaceIds.Count == 1 &&
        !string.Equals(rule.InterfaceIds[0], "Any", StringComparison.OrdinalIgnoreCase) &&
        (rule.StableId == FirewallRule.ValidLan.StableId
            ? CanonicalCidr.IsExactLanExpression(rule.LocalAddressRanges, rule.RemoteAddressRanges)
            : rule.StableId == "BallsServer.Firewall.Tailscale.Smb445.v1" &&
              CanonicalCidr.IsExactTailscaleExpression(rule.LocalAddressRanges, rule.RemoteAddressRanges));

    private static ResourceRefusal PolicyRefusal(FirewallPolicyState state) => state switch
    {
        FirewallPolicyState.LocalWritable => ResourceRefusal.None,
        FirewallPolicyState.GroupPolicyManaged => ResourceRefusal.PolicyManaged,
        FirewallPolicyState.ScopeNotExpressible => ResourceRefusal.ScopeNotExpressible,
        FirewallPolicyState.ObservationUnavailable => ResourceRefusal.Unknown,
        _ => ResourceRefusal.Unknown,
    };

    private static FirewallPlan Refuse(ResourceRefusal refusal) =>
        new(false, refusal, null, null, Guidance(refusal), []);
}

internal sealed record CanonicalCidr(
    IPAddress Address,
    IPAddress Network,
    int PrefixLength,
    string Canonical)
{
    public bool IsHostAddress =>
        (Address.AddressFamily == AddressFamily.InterNetwork && PrefixLength == 32) ||
        (Address.AddressFamily == AddressFamily.InterNetworkV6 && PrefixLength == 128);

    public bool IsSubnet =>
        (Address.AddressFamily == AddressFamily.InterNetwork && PrefixLength < 32) ||
        (Address.AddressFamily == AddressFamily.InterNetworkV6 && PrefixLength < 128);

    public bool IsPrivateLan => IsPrivateLanRange(Address, Network, PrefixLength);

    public bool IsTailscalePrivate => IsInNetwork(Address, TailscaleV4Network, 10) || IsInNetwork(Address, TailscaleV6Network, 48);

    private static readonly IPAddress TailscaleV4Network = IPAddress.Parse("100.64.0.0");
    private static readonly IPAddress TailscaleV6Network = IPAddress.Parse("fd7a:115c:a1e0::");
    private static readonly string[] TailscalePrivateRanges = ["100.64.0.0/10", "fd7a:115c:a1e0::/48"];

    public bool Contains(CanonicalCidr address) =>
        Address.AddressFamily == address.Address.AddressFamily &&
        IsInNetwork(address.Address, Network, PrefixLength);

    public bool ContainsUsableHost(CanonicalCidr address)
    {
        if (!Contains(address))
        {
            return false;
        }

        if (Address.AddressFamily != AddressFamily.InterNetwork || PrefixLength >= 31)
        {
            return true;
        }

        byte[] broadcastBytes = Network.GetAddressBytes();
        for (int index = 0; index < broadcastBytes.Length; index++)
        {
            int remaining = PrefixLength - (index * 8);
            byte mask = remaining >= 8 ? (byte)0xFF : remaining <= 0 ? (byte)0 : (byte)(0xFF << (8 - remaining));
            broadcastBytes[index] |= (byte)~mask;
        }

        IPAddress broadcast = new(broadcastBytes);
        return !address.Address.Equals(Network) && !address.Address.Equals(broadcast);
    }

    public static bool TryParse(string value, bool requireNetworkAddress, out CanonicalCidr result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
        {
            return false;
        }

        string[] parts = value.Split('/');
        if (parts.Length != 2 ||
            parts[1].Length == 0 ||
            parts[1].Any(character => !char.IsAsciiDigit(character)) ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int prefix) ||
            !IPAddress.TryParse(parts[0], out IPAddress? address) ||
            address.IsIPv4MappedToIPv6 ||
            !string.Equals(address.ToString(), parts[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int bits = address.AddressFamily == AddressFamily.InterNetwork ? 32 :
            address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 0;
        if (bits == 0 || prefix <= 0 || prefix > bits || IsForbiddenAddress(address))
        {
            return false;
        }

        byte[] networkBytes = address.GetAddressBytes();
        ApplyMask(networkBytes, prefix);
        IPAddress network = new(networkBytes);
        if (requireNetworkAddress && !address.Equals(network))
        {
            return false;
        }

        string canonicalAddress = requireNetworkAddress ? network.ToString() : address.ToString();
        result = new(address, network, prefix, $"{canonicalAddress}/{prefix}");
        return string.Equals(result.Canonical, value, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExactLanExpression(IReadOnlyList<string> locals, IReadOnlyList<string> remotes) =>
        locals.Count == 1 &&
        remotes.Count == 1 &&
        TryParse(locals[0], requireNetworkAddress: false, out CanonicalCidr local) &&
        local.IsHostAddress &&
        local.IsPrivateLan &&
        TryParse(remotes[0], requireNetworkAddress: true, out CanonicalCidr remote) &&
        remote.IsPrivateLan &&
        remote.IsSubnet &&
        remote.ContainsUsableHost(local);

    public static bool IsExactTailscaleExpression(IReadOnlyList<string> locals, IReadOnlyList<string> remotes) =>
        locals.Count == 1 &&
        TryParse(locals[0], requireNetworkAddress: false, out CanonicalCidr local) &&
        local.IsHostAddress &&
        local.IsTailscalePrivate &&
        remotes.SequenceEqual(TailscalePrivateRanges, StringComparer.Ordinal);

    private static bool IsForbiddenAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] is 0 or 127 || bytes[0] >= 224 || (bytes[0] == 169 && bytes[1] == 254)
            : bytes[0] == 0xFF || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
    }

    private static bool IsPrivateLanRange(IPAddress address, IPAddress network, int prefix)
    {
        byte[] bytes = address.GetAddressBytes();
        byte[] networkBytes = network.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return (bytes[0] == 10 && networkBytes[0] == 10 && prefix >= 8) ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31 &&
                 networkBytes[0] == 172 && networkBytes[1] is >= 16 and <= 31 && prefix >= 12) ||
                (bytes[0] == 192 && bytes[1] == 168 &&
                 networkBytes[0] == 192 && networkBytes[1] == 168 && prefix >= 16);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
            (bytes[0] & 0xFE) == 0xFC &&
            (networkBytes[0] & 0xFE) == 0xFC &&
            prefix >= 7;
    }

    private static bool IsInNetwork(IPAddress address, IPAddress network, int prefix)
    {
        if (address.AddressFamily != network.AddressFamily)
        {
            return false;
        }

        byte[] addressBytes = address.GetAddressBytes();
        byte[] networkBytes = network.GetAddressBytes();
        ApplyMask(addressBytes, prefix);
        ApplyMask(networkBytes, prefix);
        return addressBytes.SequenceEqual(networkBytes);
    }

    private static void ApplyMask(byte[] bytes, int prefix)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            int remaining = prefix - (index * 8);
            byte mask = remaining >= 8 ? (byte)0xFF : remaining <= 0 ? (byte)0 : (byte)(0xFF << (8 - remaining));
            bytes[index] &= mask;
        }
    }
}
