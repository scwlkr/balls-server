using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class MutationAndPrivacySafetyTests
{
    public static TheoryData<MutationGuardContext, MutationGuardRefusal> InvalidGuards => new()
    {
        { ValidGuard() with { ExplicitOptIn = false }, MutationGuardRefusal.OptInMissing },
        { ValidGuard() with { IsAdministrator = false }, MutationGuardRefusal.AdministratorMissing },
        { ValidGuard() with { DisposableVmMarker = null }, MutationGuardRefusal.DisposableVmMarkerMissing },
        { ValidGuard() with { SnapshotId = null }, MutationGuardRefusal.KnownSnapshotMissing },
        { ValidGuard() with { SnapshotIsKnownRestorable = false }, MutationGuardRefusal.KnownSnapshotMissing },
        { ValidGuard() with { TestNamespace = "BallsServer.Production." }, MutationGuardRefusal.InvalidNamespace },
        { ValidGuard() with { TestNamespace = "BallsServer.Test." }, MutationGuardRefusal.InvalidNamespace },
        { ValidGuard() with { ProductionIdentityInScope = true }, MutationGuardRefusal.ProductionIdentityInScope },
    };

    [Theory]
    [MemberData(nameof(InvalidGuards))]
    public void Every_missing_vm_mutation_guard_refuses(MutationGuardContext context, MutationGuardRefusal expected)
    {
        MutationGuardResult result = DevelopmentMutationGuard.Evaluate(context);

        Assert.False(result.EligibleForLaterVmMutation);
        Assert.Equal(expected, result.Refusal);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void Development_host_is_always_refused_even_when_markers_are_claimed()
    {
        MutationGuardContext context = ValidGuard() with { IsDevelopmentHost = true };

        MutationGuardResult result = DevelopmentMutationGuard.Evaluate(context);

        Assert.Equal(MutationGuardRefusal.DevelopmentHost, result.Refusal);
        Assert.False(result.EligibleForLaterVmMutation);
    }

    [Fact]
    public void Prototype_results_are_typed_and_redacted()
    {
        string output = PrototypeResult.Refused(ResourceRefusal.UnmanagedConflict).ToPublicText();

        Assert.Equal("Refused: unmanaged product identity conflict. No changes were made.", output);
        Assert.DoesNotContain("C:\\private\\managed", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-real-credential", output, StringComparison.Ordinal);
        Assert.DoesNotContain("S-1-5-21", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Prototype_exposes_no_mutation_executor()
    {
        Assert.DoesNotContain(typeof(DevelopmentMutationGuard).Assembly.ExportedTypes, type =>
            type.Name.Contains("Executor", StringComparison.Ordinal) ||
            type.GetMethods().Any(method => method.Name.StartsWith("CreateShare", StringComparison.Ordinal)));
    }

    private static MutationGuardContext ValidGuard() => new(
        ExplicitOptIn: true,
        IsAdministrator: true,
        DisposableVmMarker: "disposable-vm-1",
        SnapshotId: "snapshot-1",
        SnapshotIsKnownRestorable: true,
        TestNamespace: "BallsServer.Test.case-1",
        ProductionIdentityInScope: false,
        IsDevelopmentHost: false);
}
