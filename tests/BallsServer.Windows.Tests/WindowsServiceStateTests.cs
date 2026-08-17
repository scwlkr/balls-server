using BallsServer.Core.Preflight;
using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class WindowsServiceStateTests
{
    [Theory]
    [InlineData(0u, WindowsServiceState.Unknown)]
    [InlineData(1u, WindowsServiceState.Stopped)]
    [InlineData(2u, WindowsServiceState.StartPending)]
    [InlineData(3u, WindowsServiceState.StopPending)]
    [InlineData(4u, WindowsServiceState.Running)]
    [InlineData(5u, WindowsServiceState.ContinuePending)]
    [InlineData(6u, WindowsServiceState.PausePending)]
    [InlineData(7u, WindowsServiceState.Paused)]
    [InlineData(8u, WindowsServiceState.Unknown)]
    [InlineData(uint.MaxValue, WindowsServiceState.Unknown)]
    public void MapStateMapsDocumentedNativeStatesAndFailsClosedForOtherValues(
        uint nativeState,
        WindowsServiceState expected)
    {
        Assert.Equal(expected, NativeWindowsServiceStatusSource.MapState(nativeState));
    }

    [Theory]
    [InlineData("Spooler")]
    [InlineData("")]
    public void QueryRefusesServicesOutsideTheTwoItemAllowList(string serviceName)
    {
        var source = new NativeWindowsServiceStatusSource();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => source.Query(serviceName));

        Assert.Equal("serviceName", exception.ParamName);
    }
}
