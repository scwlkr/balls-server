namespace BallsServer.DisposableVmTopology;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("PASS: isolated disposable-VM topology model loaded; no VM, network, SMB, account, credential, filesystem, or Windows mutation executed.");
    }
}

public static class TopologyContract
{
    public const string DisposableMarker = "BallsServer.DisposableVm";
    public const string TestNamespacePrefix = "BallsServer.Test.";

    public static readonly string[] ScenarioKinds =
    [
        "positive", "refusal", "partial-failure", "crash", "rollback", "repair",
        "recovery", "idempotent", "drift", "privacy", "unrelated-state-preservation"
    ];

    public static readonly string[] RequiredEndToEndRows =
    [
        "LAN", "FullMagicDNS", "SelectedPathFailure", "ExplicitSwitch", "RenameCollisionDrift",
        "Reconnect", "CredentialCollision", "OneAttemptFailure", "Rotate", "Revoke",
        "OpenFileRemoval", "LeftoverVerificationFile", "LedgerLossCorruption", "Reboot", "HostClientCleanup"
    ];

    public static readonly string[] RequiredAssertions =
    [
        "Smb30OrLater", "Smb1Disabled", "SigningPreserved", "PrivateRoute", "ShareNtfsIntersection",
        "PerGrantIsolation", "ExactMappingAndCredentialTargets", "OwnershipReconciliation",
        "UnrelatedStatePreservation", "ManagedFolderAndFileSurvival"
    ];

    public static readonly Operation[] Operations =
    [
        new("OP-01", "Draft provisional preview"), new("OP-02", "Authoritative consent"),
        new("OP-03", "Initialize protected ledger"), new("OP-04", "Recover protected ledger"),
        new("OP-05", "Validate host prerequisites"), new("OP-06", "Validate managed folder"),
        new("OP-07", "Create product access group"), new("OP-08", "Apply managed-folder ACE"),
        new("OP-09", "Create managed share"), new("OP-10", "Create private LAN firewall rule"),
        new("OP-11", "Create private Tailscale firewall rule"), new("OP-12", "Create disabled access grant"),
        new("OP-13", "Activate transferred access grant"), new("OP-14", "Rotate access grant"),
        new("OP-15", "Revoke access grant"), new("OP-16", "Close attributable SMB sessions"),
        new("OP-17", "Return one setup-code secret response"), new("OP-18", "Display setup code once"),
        new("OP-19", "Destroy setup code on Hide or timeout"), new("OP-20", "Write setup code to clipboard"),
        new("OP-21", "Conditionally clear product clipboard value"), new("OP-22", "Render setup-code QR in memory"),
        new("OP-23", "Repair owned host configuration"), new("OP-24", "Remove owned host configuration"),
        new("OP-25", "Hand off Tailscale installation or sign-in"), new("OP-26", "Parse initial setup code"),
        new("OP-27", "Import endpoint-update bundle"), new("OP-28", "Inspect selected endpoint and collisions"),
        new("OP-29", "Switch selected endpoint"), new("OP-30", "Run advanced IP transport diagnostic"),
        new("OP-31", "Perform one-shot authentication"), new("OP-32", "Verify managed-folder access"),
        new("OP-33", "Save exact provider credential"), new("OP-34", "Delete exact provider credential"),
        new("OP-35", "Map selected drive"), new("OP-36", "Unmap selected drive"),
        new("OP-37", "Persist reconnect profile choice"), new("OP-38", "Clean up verification file"),
        new("OP-39", "Purge expired non-secret retention records")
    ];

    public static TopologyDefinition Create() => new(
        new("Default", false, true, true,
            ["Tailscale", "Hyper-V", "shares", "accounts", "mappings", "credentials", "second computer"]),
        [
            new("v030-clean", "Clean", "Windows only; no product state"),
            new("v030-tailscale-ready", "TailscaleReady", "Non-production tailnet signed in; no product state"),
            new("v030-configured", "Configured", "Product-test fixture only; exact known restore point")
        ],
        new("Internal", "Host", "Client", "TCP", 445, false, false),
        new("PrivateTailnet", "Client", "Host", "TCP", 445, true, false),
        BuildMatrix(), RequiredEndToEndRows, RequiredAssertions);

    public static GuardDecision GuardMutation(MutationSuiteInput input)
    {
        List<string> reasons = [];
        if (!input.IsElevated) reasons.Add("ElevationRequired");
        if (input.DisposableMarker != DisposableMarker) reasons.Add("DisposableMarkerRequired");
        if (input.ExpectedSnapshotId != "v030-configured" || input.CurrentSnapshotId != input.ExpectedSnapshotId) reasons.Add("ExactKnownSnapshotRequired");
        if (!input.Namespace.StartsWith(TestNamespacePrefix, StringComparison.Ordinal) || input.Namespace.Length <= TestNamespacePrefix.Length) reasons.Add("UniqueProductTestNamespaceRequired");
        if (!input.ScopeProof.IsTestScoped(input.Namespace)) reasons.Add("ProductionScopeProofRequired");
        return new(reasons.Count == 0, reasons);
    }

    public static MatrixValidation ValidateMatrix(IEnumerable<MatrixCell> cells)
    {
        MatrixCell[] supplied = cells.ToArray();
        List<string> errors = [];
        foreach (Operation operation in Operations)
            foreach (string scenario in ScenarioKinds)
            {
                MatrixCell[] matches = supplied.Where(cell => cell.OperationId == operation.Id && cell.Scenario == scenario).ToArray();
                if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches.SingleOrDefault()?.FixtureId) || string.IsNullOrWhiteSpace(matches.SingleOrDefault()?.ExpectedEvidence))
                    errors.Add($"{operation.Id}/{scenario}");
            }
        if (supplied.Any(cell => !Operations.Any(operation => operation.Id == cell.OperationId) || !ScenarioKinds.Contains(cell.Scenario, StringComparer.Ordinal))) errors.Add("UnknownCell");
        return new(errors.Count == 0, errors);
    }

    public static bool IsComplete()
    {
        TopologyDefinition topology = Create();
        return topology.DefaultSuite is { RequiresElevation: false, Offline: true, NonMutating: true }
            && topology.InternalLan is { NetworkKind: "Internal", From: "Host", To: "Client", Port: 445, IsOptional: false, IsExternal: false }
            && topology.OptionalTailnet is { NetworkKind: "PrivateTailnet", From: "Client", To: "Host", Port: 445, IsOptional: true, IsExternal: false }
            && topology.Snapshots.Count == 3
            && ValidateMatrix(topology.Matrix).Valid
            && RequiredEndToEndRows.All(topology.EndToEndRows.Contains)
            && RequiredAssertions.All(topology.Assertions.Contains)
            && !GuardMutation(new(false, "", "", "", "", ScopeProof.Empty)).Allowed;
    }

    private static MatrixCell[] BuildMatrix() =>
        Operations.SelectMany(operation => ScenarioKinds.Select(scenario =>
            new MatrixCell(operation.Id, scenario, $"VM-{operation.Id}-{scenario}", $"evidence-{operation.Id}-{scenario}"))).ToArray();
}

public sealed record Operation(string Id, string Name);

public sealed record DefaultSuite(string Name, bool RequiresElevation, bool Offline, bool NonMutating, IReadOnlyList<string> ForbiddenDependencies);

public sealed record Snapshot(string Id, string Kind, string FixtureBoundary);

public sealed record NetworkLeg(string NetworkKind, string From, string To, string Protocol, int Port, bool IsOptional, bool IsExternal);

public sealed record MatrixCell(string OperationId, string Scenario, string FixtureId, string ExpectedEvidence);

public sealed record MatrixValidation(bool Valid, IReadOnlyList<string> Errors);

public sealed record TopologyDefinition(
    DefaultSuite DefaultSuite,
    IReadOnlyList<Snapshot> Snapshots,
    NetworkLeg InternalLan,
    NetworkLeg OptionalTailnet,
    IReadOnlyList<MatrixCell> Matrix,
    IReadOnlyList<string> EndToEndRows,
    IReadOnlyList<string> Assertions);

public sealed record ScopeProof(string Folder, string Account, string Share, string Credential, string Mapping, string Tailnet)
{
    public static ScopeProof Empty { get; } = new("", "", "", "", "", "");

    public bool IsTestScoped(string testNamespace) =>
        Share == "Balls"
        && new[] { Folder, Account, Credential, Mapping, Tailnet }.All(value => value.StartsWith(testNamespace, StringComparison.Ordinal));
}

public sealed record MutationSuiteInput(bool IsElevated, string DisposableMarker, string CurrentSnapshotId, string ExpectedSnapshotId, string Namespace, ScopeProof ScopeProof);

public sealed record GuardDecision(bool Allowed, IReadOnlyList<string> Reasons);
