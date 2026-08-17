using BallsServer.Windows;

namespace BallsServer.Windows.Tests;

public sealed class WindowsStorageProbeTests
{
    [Fact]
    public void FindNearestExistingAncestorReturnsAnExistingInputUnchanged()
    {
        var existingDirectory = Path.GetFullPath(AppContext.BaseDirectory);

        var result = WindowsStorageProbe.FindNearestExistingAncestor(existingDirectory);

        Assert.Equal(existingDirectory.TrimEnd(Path.DirectorySeparatorChar), result.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FindNearestExistingAncestorWalksUpFromMissingDescendants()
    {
        var existingDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var missingPath = Path.Combine(
            existingDirectory,
            $"missing-{Guid.NewGuid():N}",
            "child",
            "folder");

        var result = WindowsStorageProbe.FindNearestExistingAncestor(missingPath);

        Assert.Equal(existingDirectory.TrimEnd(Path.DirectorySeparatorChar), result.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FindNearestExistingAncestorRejectsAnEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => WindowsStorageProbe.FindNearestExistingAncestor(" "));
    }

    [Fact]
    public void FindVolumeRootReturnsTheActualContainingVolumeForAnExistingPath()
    {
        var existingDirectory = Path.GetFullPath(AppContext.BaseDirectory);

        var result = WindowsStorageProbe.FindVolumeRoot(existingDirectory);

        Assert.True(Path.IsPathFullyQualified(result));
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), result, StringComparison.Ordinal);
        Assert.True(Directory.Exists(result));
    }

    [Theory]
    [InlineData(0u, DriveType.Unknown)]
    [InlineData(1u, DriveType.NoRootDirectory)]
    [InlineData(2u, DriveType.Removable)]
    [InlineData(3u, DriveType.Fixed)]
    [InlineData(4u, DriveType.Network)]
    [InlineData(5u, DriveType.CDRom)]
    [InlineData(6u, DriveType.Ram)]
    [InlineData(99u, DriveType.Unknown)]
    public void MapDriveTypeMapsNativeValues(uint nativeValue, DriveType expected)
    {
        Assert.Equal(expected, WindowsStorageProbe.MapDriveType(nativeValue));
    }

    [Theory]
    [InlineData(@"\\server\share\folder", true)]
    [InlineData(@"\\?\UNC\server\share\folder", true)]
    [InlineData(@"C:\folder", false)]
    [InlineData(@"\\?\C:\folder", false)]
    [InlineData(@"\\.\C:\folder", false)]
    public void IsUncPathDistinguishesNetworkAndLocalWindowsPaths(string path, bool expected)
    {
        Assert.Equal(expected, WindowsStorageProbe.IsUncPath(path));
    }
}
