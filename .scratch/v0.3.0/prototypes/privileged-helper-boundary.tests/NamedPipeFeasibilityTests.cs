using BallsServer.SecurityPrototype;

namespace BallsServer.SecurityPrototype.Tests;

public sealed class NamedPipeFeasibilityTests
{
    [Fact]
    public async Task Same_user_ephemeral_pipe_confirms_both_process_ids_and_one_exchange()
    {
        PipeFeasibilityResult result = await EphemeralNamedPipeProbe.RunAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.ServerCreatedBeforeClientLaunch);
        Assert.True(result.ServerObservedClientProcessId);
        Assert.True(result.ClientObservedServerProcessId);
        Assert.Equal(1, result.RequestCount);
        Assert.Equal(1, result.ResponseCount);
        Assert.True(result.CleanupCompleted);
    }
}
