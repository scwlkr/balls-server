using BallsServer.ManagedResourceSafety;

MutationGuardResult guard = DevelopmentMutationGuard.Evaluate(new(
    ExplicitOptIn: false,
    IsAdministrator: false,
    DisposableVmMarker: null,
    SnapshotId: null,
    SnapshotIsKnownRestorable: false,
    TestNamespace: null,
    ProductionIdentityInScope: true,
    IsDevelopmentHost: true));

if (guard is not
    {
        EligibleForLaterVmMutation: false,
        Refusal: MutationGuardRefusal.DevelopmentHost,
        Mutations.Count: 0,
    })
{
    Console.Error.WriteLine("FAIL: isolated denial guard did not fail closed.");
    return 1;
}

Console.WriteLine("PASS: isolated managed-resource-safety model loaded; denial guard active; no mutation executed.");
return 0;
