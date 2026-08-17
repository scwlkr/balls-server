using BallsServer.DisposableVmTopology;

namespace BallsServer.DisposableVmTopology.Tests;

public sealed class TopologyContractTests
{
    [Fact]
    public void Published_contract_is_complete()
    {
        Assert.True(TopologyContract.IsComplete());
    }

    [Fact]
    public void Default_suite_is_unelevated_offline_non_mutating_and_has_no_environment_dependencies()
    {
        DefaultSuite suite = TopologyContract.Create().DefaultSuite;

        Assert.False(suite.RequiresElevation);
        Assert.True(suite.Offline);
        Assert.True(suite.NonMutating);
        Assert.Equal(["Tailscale", "Hyper-V", "shares", "accounts", "mappings", "credentials", "second computer"], suite.ForbiddenDependencies);
    }

    [Theory]
    [InlineData(false, "BallsServer.DisposableVm", "v030-configured", "v030-configured", "BallsServer.Test.run-01", "ElevationRequired")]
    [InlineData(true, "missing", "v030-configured", "v030-configured", "BallsServer.Test.run-01", "DisposableMarkerRequired")]
    [InlineData(true, "BallsServer.DisposableVm", "v030-clean", "v030-configured", "BallsServer.Test.run-01", "ExactKnownSnapshotRequired")]
    [InlineData(true, "BallsServer.DisposableVm", "v030-configured", "v030-configured", "production", "UniqueProductTestNamespaceRequired")]
    public void Mutation_guard_denies_missing_required_boundary(bool elevated, string marker, string currentSnapshot, string expectedSnapshot, string testNamespace, string reason)
    {
        GuardDecision decision = TopologyContract.GuardMutation(new(elevated, marker, currentSnapshot, expectedSnapshot, testNamespace, TestScope(testNamespace)));

        Assert.False(decision.Allowed);
        Assert.Contains(reason, decision.Reasons);
    }

    [Fact]
    public void Mutation_guard_denies_any_scope_that_is_not_proven_product_test_only()
    {
        GuardDecision decision = TopologyContract.GuardMutation(new(true, "BallsServer.DisposableVm", "v030-configured", "v030-configured", "BallsServer.Test.run-01", new("BallsServer.Test.run-01.folder", "BallsServer.Test.run-01.account", "OtherShare", "BallsServer.Test.run-01.credential", "BallsServer.Test.run-01.mapping", "BallsServer.Test.run-01.tailnet")));

        Assert.False(decision.Allowed);
        Assert.Contains("ProductionScopeProofRequired", decision.Reasons);
    }

    [Fact]
    public void Mutation_guard_allows_the_fixed_Balls_share_only_when_every_other_scope_proof_is_test_namespaced()
    {
        const string testNamespace = "BallsServer.Test.run-01";
        ScopeProof scope = new($"{testNamespace}.folder", $"{testNamespace}.account", "Balls", $"{testNamespace}.credential", $"{testNamespace}.mapping", $"{testNamespace}.tailnet");

        GuardDecision decision = TopologyContract.GuardMutation(new(true, "BallsServer.DisposableVm", "v030-configured", "v030-configured", testNamespace, scope));

        Assert.True(decision.Allowed);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void Fully_bounded_future_mutation_plan_is_only_eligible_and_does_not_execute_a_vm()
    {
        const string testNamespace = "BallsServer.Test.run-01";
        GuardDecision decision = TopologyContract.GuardMutation(new(true, "BallsServer.DisposableVm", "v030-configured", "v030-configured", testNamespace, TestScope(testNamespace)));

        Assert.True(decision.Allowed);
        Assert.Empty(decision.Reasons);
    }

    [Fact]
    public void Two_vm_topology_is_internal_and_optional_tailnet_is_only_client_to_host_tcp_445()
    {
        TopologyDefinition topology = TopologyContract.Create();

        Assert.Equal(new("Internal", "Host", "Client", "TCP", 445, false, false), topology.InternalLan);
        Assert.Equal(new("PrivateTailnet", "Client", "Host", "TCP", 445, true, false), topology.OptionalTailnet);
    }

    [Fact]
    public void Snapshot_manifest_has_clean_tailscale_ready_and_configured_restore_points()
    {
        Assert.Equal(["v030-clean", "v030-tailscale-ready", "v030-configured"], TopologyContract.Create().Snapshots.Select(snapshot => snapshot.Id));
    }

    [Fact]
    public void Matrix_has_one_nonblank_cell_for_every_prior_operation_and_required_scenario()
    {
        TopologyDefinition topology = TopologyContract.Create();
        MatrixValidation validation = TopologyContract.ValidateMatrix(topology.Matrix);

        Assert.True(validation.Valid);
        Assert.Equal(39 * 11, topology.Matrix.Count);
    }

    [Fact]
    public void Matrix_schema_rejects_a_blank_required_cell()
    {
        MatrixCell[] cells = TopologyContract.Create().Matrix.Where(cell => cell.OperationId != "OP-09" || cell.Scenario != "rollback").ToArray();
        MatrixValidation validation = TopologyContract.ValidateMatrix(cells);

        Assert.False(validation.Valid);
        Assert.Contains("OP-09/rollback", validation.Errors);
    }

    [Fact]
    public void Matrix_schema_rejects_unknown_operation_or_scenario()
    {
        MatrixCell[] cells = [.. TopologyContract.Create().Matrix, new("OP-99", "surprise", "fixture", "evidence")];
        MatrixValidation validation = TopologyContract.ValidateMatrix(cells);

        Assert.False(validation.Valid);
        Assert.Contains("UnknownCell", validation.Errors);
    }

    [Fact]
    public void End_to_end_rows_and_safety_assertions_are_complete()
    {
        TopologyDefinition topology = TopologyContract.Create();

        Assert.All(TopologyContract.RequiredEndToEndRows, row => Assert.Contains(row, topology.EndToEndRows));
        Assert.All(TopologyContract.RequiredAssertions, assertion => Assert.Contains(assertion, topology.Assertions));
    }

    private static ScopeProof TestScope(string testNamespace) => new(
        $"{testNamespace}.folder", $"{testNamespace}.account", "Balls",
        $"{testNamespace}.credential", $"{testNamespace}.mapping", $"{testNamespace}.tailnet");
}
