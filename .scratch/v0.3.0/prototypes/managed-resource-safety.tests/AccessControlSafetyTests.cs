using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class AccessControlSafetyTests
{
    private const string GroupSid = "S-1-5-21-111-222-333-444";

    [Fact]
    public void Exact_product_ace_has_one_allow_modify_synchronize_tuple()
    {
        ProductAce ace = ProductAce.Exact(GroupSid);

        Assert.Equal(AceKind.Allow, ace.Kind);
        Assert.Equal(ProductRights.Modify | ProductRights.Synchronize, ace.Rights);
        Assert.Equal(InheritanceScope.Container | InheritanceScope.ObjectInherit, ace.Inheritance);
        Assert.Equal(PropagationScope.None, ace.Propagation);
        Assert.False(ace.IsInherited);
    }

    public static TheoryData<IReadOnlyList<ProductAce>, AceRefusal> Conflicts => new()
    {
        { [new(GroupSid, AceKind.Deny, ProductRights.Modify, InheritanceScope.Container | InheritanceScope.ObjectInherit, PropagationScope.None, false)], AceRefusal.DenyConflict },
        { [new(GroupSid, AceKind.Allow, ProductRights.Modify, InheritanceScope.Container | InheritanceScope.ObjectInherit, PropagationScope.None, false)], AceRefusal.AmbiguousEquivalent },
        { [new(GroupSid, AceKind.Allow, ProductRights.Modify | ProductRights.Synchronize, InheritanceScope.Container, PropagationScope.None, false)], AceRefusal.AmbiguousEquivalent },
        { [ProductAce.Exact(GroupSid)], AceRefusal.UnmanagedExactAce },
        { [ProductAce.Exact(GroupSid), ProductAce.Exact(GroupSid)], AceRefusal.DuplicateProductAce },
    };

    [Theory]
    [MemberData(nameof(Conflicts))]
    public void Conflicting_or_unowned_equivalent_aces_refuse(IReadOnlyList<ProductAce> entries, AceRefusal expected)
    {
        DaclSnapshot current = Snapshot(entries);

        AcePlan result = ProductAcePolicy.PlanAdd(current, GroupSid, ownership: null);

        Assert.False(result.Accepted);
        Assert.Equal(expected, result.Refusal);
    }

    [Fact]
    public void Add_round_trip_preserves_owner_control_flags_and_unrelated_multiset()
    {
        ProductAce first = new("S-1-5-18", AceKind.Allow, ProductRights.FullControl, InheritanceScope.None, PropagationScope.None, false);
        ProductAce duplicate = new("S-1-5-32-544", AceKind.Allow, ProductRights.FullControl, InheritanceScope.Container, PropagationScope.None, true);
        DaclSnapshot before = Snapshot([first, duplicate, duplicate]);

        AcePlan add = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);
        AceVerification verification = ProductAcePolicy.VerifyApplied(before, add.After!, GroupSid);

        Assert.True(add.Accepted);
        Assert.True(verification.Accepted);
        Assert.Equal(before.OwnerSid, add.After!.OwnerSid);
        Assert.Equal(before.ControlFlags, add.After.ControlFlags);
        Assert.Equal(ProductAcePolicy.UnrelatedMultisetFingerprint(before, GroupSid), ProductAcePolicy.UnrelatedMultisetFingerprint(add.After, GroupSid));
        Assert.Equal(3, add.After.Entries.Count(entry => entry.Sid != GroupSid));
    }

    [Fact]
    public void Effective_access_intent_requires_create_read_write_rename_and_delete()
    {
        DaclSnapshot before = Snapshot(
        [
            new("S-1-5-18", AceKind.Allow, ProductRights.FullControl, InheritanceScope.None, PropagationScope.None, false),
            new("S-1-5-32-544", AceKind.Allow, ProductRights.FullControl, InheritanceScope.None, PropagationScope.None, false),
        ]);
        AcePlan add = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);

        ProposedAccessIntent intent = ProductAcePolicy.EvaluateProposedIntent(before, add.After!, GroupSid);

        Assert.True(intent.CanCreate);
        Assert.True(intent.CanRead);
        Assert.True(intent.CanWrite);
        Assert.True(intent.CanRename);
        Assert.True(intent.CanDelete);
        Assert.True(intent.DescriptorOwnerAndControlFlagsPreserved);
    }

    [Fact]
    public void Effective_access_intent_detects_lost_administrator_control()
    {
        ProductAce system = new("S-1-5-18", AceKind.Allow, ProductRights.FullControl, InheritanceScope.None, PropagationScope.None, false);
        ProductAce administrators = new("S-1-5-32-544", AceKind.Allow, ProductRights.FullControl, InheritanceScope.None, PropagationScope.None, false);
        DaclSnapshot before = Snapshot([system, administrators]);
        AcePlan add = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);
        DaclSnapshot corrupted = add.After! with { Entries = [system, ProductAce.Exact(GroupSid)] };

        ProposedAccessIntent intent = ProductAcePolicy.EvaluateProposedIntent(before, corrupted, GroupSid);

        Assert.False(intent.DescriptorOwnerAndControlFlagsPreserved);
    }

    [Fact]
    public void Exact_reversible_removal_restores_original_descriptor()
    {
        DaclSnapshot before = Snapshot([new("S-1-5-18", AceKind.Allow, ProductRights.FullControl, InheritanceScope.None, PropagationScope.None, false)]);
        AcePlan add = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);
        AceOwnership ownership = add.Ownership!;

        AcePlan remove = ProductAcePolicy.PlanRemove(add.After!, ownership);

        Assert.True(remove.Accepted);
        Assert.Equal(before, remove.After);
    }

    [Theory]
    [InlineData("owner-drift", "ProtectedAutoInherited")]
    [InlineData("S-1-5-21-owner", "control-drift")]
    public void Owner_or_control_flag_drift_refuses_removal(string owner, string controlFlags)
    {
        DaclSnapshot before = Snapshot([]);
        AcePlan add = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);
        DaclSnapshot drifted = add.After! with { OwnerSid = owner, ControlFlags = controlFlags };

        AcePlan remove = ProductAcePolicy.PlanRemove(drifted, add.Ownership!);

        Assert.Equal(AceRefusal.DescriptorDrift, remove.Refusal);
    }

    [Fact]
    public void Unrelated_ace_drift_refuses_removal()
    {
        DaclSnapshot before = Snapshot([]);
        AcePlan add = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);
        DaclSnapshot drifted = add.After! with
        {
            Entries = [.. add.After.Entries, new("S-1-5-11", AceKind.Allow, ProductRights.Read, InheritanceScope.None, PropagationScope.None, false)],
        };

        AcePlan remove = ProductAcePolicy.PlanRemove(drifted, add.Ownership!);

        Assert.Equal(AceRefusal.UnrelatedAceDrift, remove.Refusal);
    }

    [Fact]
    public void Removal_requires_exact_stable_product_identity()
    {
        DaclSnapshot before = Snapshot([]);
        AcePlan add = ProductAcePolicy.PlanAdd(before, GroupSid, ownership: null);
        AceOwnership wrong = add.Ownership! with { GroupSid = "S-1-5-21-wrong" };

        AcePlan remove = ProductAcePolicy.PlanRemove(add.After!, wrong);

        Assert.Equal(AceRefusal.OwnershipMismatch, remove.Refusal);
    }

    private static DaclSnapshot Snapshot(IReadOnlyList<ProductAce> entries) =>
        new("S-1-5-21-owner", "ProtectedAutoInherited", entries);
}
