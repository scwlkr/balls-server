namespace BallsServer.ManagedResourceSafety;

public sealed record MutationGuardContext(
    bool ExplicitOptIn,
    bool IsAdministrator,
    string? DisposableVmMarker,
    string? SnapshotId,
    bool SnapshotIsKnownRestorable,
    string? TestNamespace,
    bool ProductionIdentityInScope,
    bool IsDevelopmentHost);

public enum MutationGuardRefusal
{
    None,
    DevelopmentHost,
    OptInMissing,
    AdministratorMissing,
    DisposableVmMarkerMissing,
    KnownSnapshotMissing,
    InvalidNamespace,
    ProductionIdentityInScope,
}

public sealed record MutationGuardResult(
    bool EligibleForLaterVmMutation,
    MutationGuardRefusal Refusal,
    IReadOnlyList<string> Mutations);

public static class DevelopmentMutationGuard
{
    private const string RequiredPrefix = "BallsServer.Test.";

    public static MutationGuardResult Evaluate(MutationGuardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MutationGuardRefusal refusal = context switch
        {
            { IsDevelopmentHost: true } => MutationGuardRefusal.DevelopmentHost,
            { ExplicitOptIn: false } => MutationGuardRefusal.OptInMissing,
            { IsAdministrator: false } => MutationGuardRefusal.AdministratorMissing,
            { DisposableVmMarker: null or "" } => MutationGuardRefusal.DisposableVmMarkerMissing,
            { SnapshotId: null or "" } or { SnapshotIsKnownRestorable: false } => MutationGuardRefusal.KnownSnapshotMissing,
            { TestNamespace: null or "" } => MutationGuardRefusal.InvalidNamespace,
            _ when !context.TestNamespace.StartsWith(RequiredPrefix, StringComparison.Ordinal) ||
                context.TestNamespace.Length == RequiredPrefix.Length => MutationGuardRefusal.InvalidNamespace,
            { ProductionIdentityInScope: true } => MutationGuardRefusal.ProductionIdentityInScope,
            _ => MutationGuardRefusal.None,
        };

        return new(refusal == MutationGuardRefusal.None, refusal, []);
    }
}

public sealed record PrototypeResult(bool Accepted, ResourceRefusal Refusal)
{
    public static PrototypeResult Refused(ResourceRefusal refusal) => new(false, refusal);

    public string ToPublicText() => (Accepted, Refusal) switch
    {
        (true, ResourceRefusal.None) => "Verified: the isolated plan is internally consistent. No changes were made.",
        (true, _) => "Unknown: contradictory safety result. No changes were made.",
        (false, ResourceRefusal.None) => "Unknown: contradictory safety result. No changes were made.",
        (false, ResourceRefusal.UnmanagedConflict) => "Refused: unmanaged product identity conflict. No changes were made.",
        (false, ResourceRefusal.PublicExposure) => "Refused: private TCP 445 scope could not be proven. No changes were made.",
        (false, _) when Enum.IsDefined(Refusal) => "Refused: required safety evidence was unavailable. No changes were made.",
        _ => "Unknown: unrecognized safety result. No changes were made.",
    };
}
