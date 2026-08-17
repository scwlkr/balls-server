using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

internal sealed class StubPowerShellJsonSource(string json) : IPowerShellJsonSource
{
    public List<PowerShellQuery> Queries { get; } = [];

    public ValueTask<string> QueryAsync(PowerShellQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Queries.Add(query);
        return ValueTask.FromResult(json);
    }
}

internal sealed class ThrowingPowerShellJsonSource(Exception exception) : IPowerShellJsonSource
{
    public List<PowerShellQuery> Queries { get; } = [];

    public ValueTask<string> QueryAsync(PowerShellQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        return ValueTask.FromException<string>(exception);
    }
}

internal sealed class StubWindowsServiceStatusSource(WindowsServiceStatus status)
    : IWindowsServiceStatusSource
{
    public List<string> ServiceNames { get; } = [];

    public WindowsServiceStatus Query(string serviceName)
    {
        ServiceNames.Add(serviceName);
        return status;
    }
}

internal sealed class StubTailscaleStatusSource(TailscaleStatus status) : ITailscaleStatusSource
{
    public int CallCount { get; private set; }

    public ValueTask<TailscaleStatus> QueryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return ValueTask.FromResult(status);
    }
}

internal sealed class StubFolderAccessEvaluator : IFolderAccessEvaluator
{
    private readonly Func<string, FolderPermissionObservation> _evaluate;

    internal StubFolderAccessEvaluator(Func<string, FolderPermissionObservation> evaluate)
    {
        _evaluate = evaluate;
    }

    public string? TargetPath { get; private set; }

    public FolderPermissionObservation Evaluate(string targetPath)
    {
        TargetPath = targetPath;
        return _evaluate(targetPath);
    }
}
