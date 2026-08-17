using System.Security.Cryptography;
using System.Text;

namespace BallsServer.ManagedResourceSafety;

[Flags]
public enum ProductRights
{
    None = 0,
    Read = 1,
    Write = 2,
    Create = 4,
    Rename = 8,
    Delete = 16,
    Synchronize = 32,
    ChangePermissions = 64,
    Modify = Read | Write | Create | Rename | Delete,
    FullControl = Modify | Synchronize | ChangePermissions,
}

public enum AceKind
{
    Allow,
    Deny,
}

[Flags]
public enum InheritanceScope
{
    None = 0,
    Container = 1,
    ObjectInherit = 2,
}

public enum PropagationScope
{
    None,
    NoPropagate,
    InheritOnly,
}

public sealed record ProductAce(
    string Sid,
    AceKind Kind,
    ProductRights Rights,
    InheritanceScope Inheritance,
    PropagationScope Propagation,
    bool IsInherited)
{
    public static ProductAce Exact(string groupSid) => new(
        groupSid,
        AceKind.Allow,
        ProductRights.Modify | ProductRights.Synchronize,
        InheritanceScope.Container | InheritanceScope.ObjectInherit,
        PropagationScope.None,
        IsInherited: false);

    public string CanonicalTuple =>
        $"{Sid}|{Kind}|{(int)Rights}|{(int)Inheritance}|{Propagation}|{IsInherited}";
}

public sealed record DaclSnapshot(
    string OwnerSid,
    string ControlFlags,
    IReadOnlyList<ProductAce> Entries);

public enum AceRefusal
{
    None,
    DenyConflict,
    AmbiguousEquivalent,
    UnmanagedExactAce,
    DuplicateProductAce,
    DescriptorDrift,
    UnrelatedAceDrift,
    OwnershipMismatch,
}

public sealed record AceOwnership(
    string GroupSid,
    ProductAce ExactAce,
    string OwnerSid,
    string ControlFlags,
    string UnrelatedFingerprint,
    DaclSnapshot OriginalDescriptor);

public sealed record AcePlan(
    bool Accepted,
    AceRefusal Refusal,
    DaclSnapshot? After,
    AceOwnership? Ownership);

public sealed record AceVerification(bool Accepted, AceRefusal Refusal);

public sealed record ProposedAccessIntent(
    bool CanCreate,
    bool CanRead,
    bool CanWrite,
    bool CanRename,
    bool CanDelete,
    bool DescriptorOwnerAndControlFlagsPreserved);

public enum EffectivePrincipal
{
    ProductGroup,
    Owner,
    System,
    Administrators,
}

public sealed record PrincipalEffectiveAccess(
    EffectivePrincipal Principal,
    string Sid,
    bool ObservationAvailable,
    bool AccessDenied,
    ProductRights Rights);

public sealed record EffectiveAccessSnapshot(IReadOnlyList<PrincipalEffectiveAccess> Principals);

public enum EffectiveAccessRefusal
{
    None,
    ObservationUnavailable,
    AccessDenied,
    IdentityMismatch,
    ProductAccessInsufficient,
    ProductAccessExcessive,
    ControlAccessDrift,
}

public sealed record EffectiveAccessVerification(bool Accepted, EffectiveAccessRefusal Refusal);

public static class ProductAcePolicy
{
    public static AcePlan PlanAdd(DaclSnapshot current, string groupSid, AceOwnership? ownership)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupSid);

        ProductAce exact = ProductAce.Exact(groupSid);
        ProductAce[] matching = current.Entries.Where(entry => entry.Sid == groupSid).ToArray();
        int exactCount = matching.Count(entry => entry == exact);

        if (matching.Any(entry => entry.Kind == AceKind.Deny))
        {
            return Refuse(AceRefusal.DenyConflict);
        }

        if (exactCount > 1)
        {
            return Refuse(AceRefusal.DuplicateProductAce);
        }

        if (matching.Any(entry => entry != exact))
        {
            return Refuse(AceRefusal.AmbiguousEquivalent);
        }

        if (exactCount == 1)
        {
            if (ownership is null || !OwnershipMatches(current, ownership))
            {
                return Refuse(AceRefusal.UnmanagedExactAce);
            }

            return new(true, AceRefusal.None, current, ownership);
        }

        string unrelated = UnrelatedMultisetFingerprint(current, groupSid);
        DaclSnapshot after = current with { Entries = [.. current.Entries, exact] };
        AceOwnership createdOwnership = new(
            groupSid,
            exact,
            current.OwnerSid,
            current.ControlFlags,
            unrelated,
            current);
        return new(true, AceRefusal.None, after, createdOwnership);
    }

    public static AceVerification VerifyApplied(DaclSnapshot before, DaclSnapshot after, string groupSid)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        ProductAce exact = ProductAce.Exact(groupSid);
        ProductAce[] sameSid = after.Entries.Where(entry => entry.Sid == groupSid).ToArray();
        if (sameSid.Length != 1 || sameSid[0] != exact)
        {
            return new(false, AceRefusal.DuplicateProductAce);
        }

        if (after.OwnerSid != before.OwnerSid || after.ControlFlags != before.ControlFlags)
        {
            return new(false, AceRefusal.DescriptorDrift);
        }

        if (UnrelatedMultisetFingerprint(before, groupSid) != UnrelatedMultisetFingerprint(after, groupSid))
        {
            return new(false, AceRefusal.UnrelatedAceDrift);
        }

        return new(true, AceRefusal.None);
    }

    public static AcePlan PlanRemove(DaclSnapshot current, AceOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(ownership);

        if (ownership.ExactAce != ProductAce.Exact(ownership.GroupSid))
        {
            return Refuse(AceRefusal.OwnershipMismatch);
        }

        if (current.OwnerSid != ownership.OwnerSid || current.ControlFlags != ownership.ControlFlags)
        {
            return Refuse(AceRefusal.DescriptorDrift);
        }

        if (UnrelatedMultisetFingerprint(current, ownership.GroupSid) != ownership.UnrelatedFingerprint)
        {
            return Refuse(AceRefusal.UnrelatedAceDrift);
        }

        ProductAce[] matching = current.Entries.Where(entry => entry.Sid == ownership.GroupSid).ToArray();
        if (matching.Length != 1 || matching[0] != ownership.ExactAce)
        {
            return Refuse(AceRefusal.OwnershipMismatch);
        }

        return new(true, AceRefusal.None, ownership.OriginalDescriptor, ownership);
    }

    public static ProposedAccessIntent EvaluateProposedIntent(DaclSnapshot before, DaclSnapshot descriptor, string groupSid)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(descriptor);

        ProductAce? ace = descriptor.Entries.SingleOrDefault(entry => entry == ProductAce.Exact(groupSid));
        ProductRights rights = ace?.Rights ?? ProductRights.None;
        return new(
            rights.HasFlag(ProductRights.Create),
            rights.HasFlag(ProductRights.Read),
            rights.HasFlag(ProductRights.Write),
            rights.HasFlag(ProductRights.Rename),
            rights.HasFlag(ProductRights.Delete),
            DescriptorOwnerAndControlFlagsPreserved: ace is not null &&
                VerifyApplied(before, descriptor, groupSid) is { Accepted: true, Refusal: AceRefusal.None });
    }

    public static EffectiveAccessVerification VerifyEffectiveAccess(
        EffectiveAccessSnapshot before,
        EffectiveAccessSnapshot after,
        string ownerSid,
        string groupSid)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupSid);

        Dictionary<EffectivePrincipal, PrincipalEffectiveAccess>? beforeMap = BuildPrincipalMap(before);
        Dictionary<EffectivePrincipal, PrincipalEffectiveAccess>? afterMap = BuildPrincipalMap(after);
        if (beforeMap is null || afterMap is null ||
            beforeMap.Values.Any(value => !value.ObservationAvailable) ||
            afterMap.Values.Any(value => !value.ObservationAvailable))
        {
            return new(false, EffectiveAccessRefusal.ObservationUnavailable);
        }

        if (beforeMap.Values.Any(value => value.AccessDenied) || afterMap.Values.Any(value => value.AccessDenied))
        {
            return new(false, EffectiveAccessRefusal.AccessDenied);
        }

        if (!IdentityMatches(beforeMap, ownerSid, groupSid) || !IdentityMatches(afterMap, ownerSid, groupSid))
        {
            return new(false, EffectiveAccessRefusal.IdentityMismatch);
        }

        ProductRights required = ProductRights.Modify | ProductRights.Synchronize;
        ProductRights productRights = afterMap[EffectivePrincipal.ProductGroup].Rights;
        if ((productRights & required) != required)
        {
            return new(false, EffectiveAccessRefusal.ProductAccessInsufficient);
        }

        if ((productRights & ~required) != ProductRights.None)
        {
            return new(false, EffectiveAccessRefusal.ProductAccessExcessive);
        }

        foreach (EffectivePrincipal principal in new[]
                 {
                     EffectivePrincipal.Owner,
                     EffectivePrincipal.System,
                     EffectivePrincipal.Administrators,
                 })
        {
            ProductRights retained = beforeMap[principal].Rights;
            if ((afterMap[principal].Rights & retained) != retained)
            {
                return new(false, EffectiveAccessRefusal.ControlAccessDrift);
            }
        }

        return new(true, EffectiveAccessRefusal.None);
    }

    public static string UnrelatedMultisetFingerprint(DaclSnapshot descriptor, string groupSid)
    {
        string canonical = string.Join(
            "\n",
            descriptor.Entries
                .Where(entry => entry.Sid != groupSid)
                .Select(entry => entry.CanonicalTuple)
                .OrderBy(value => value, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool OwnershipMatches(DaclSnapshot current, AceOwnership ownership) =>
        current.OwnerSid == ownership.OwnerSid &&
        current.ControlFlags == ownership.ControlFlags &&
        current.Entries.Count(entry => entry.Sid == ownership.GroupSid) == 1 &&
        current.Entries.Single(entry => entry.Sid == ownership.GroupSid) == ownership.ExactAce &&
        UnrelatedMultisetFingerprint(current, ownership.GroupSid) == ownership.UnrelatedFingerprint;

    private static Dictionary<EffectivePrincipal, PrincipalEffectiveAccess>? BuildPrincipalMap(
        EffectiveAccessSnapshot snapshot)
    {
        if (snapshot.Principals.Count != 4 ||
            snapshot.Principals.GroupBy(value => value.Principal).Any(group => group.Count() != 1) ||
            Enum.GetValues<EffectivePrincipal>().Any(principal => snapshot.Principals.All(value => value.Principal != principal)))
        {
            return null;
        }

        return snapshot.Principals.ToDictionary(value => value.Principal);
    }

    private static bool IdentityMatches(
        Dictionary<EffectivePrincipal, PrincipalEffectiveAccess> principals,
        string ownerSid,
        string groupSid) =>
        principals[EffectivePrincipal.ProductGroup].Sid == groupSid &&
        principals[EffectivePrincipal.Owner].Sid == ownerSid &&
        principals[EffectivePrincipal.System].Sid == ProductIdentityPolicy.SystemSid &&
        principals[EffectivePrincipal.Administrators].Sid == ProductIdentityPolicy.AdministratorsSid;

    private static AcePlan Refuse(AceRefusal refusal) => new(false, refusal, null, null);
}
