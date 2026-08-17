using BallsServer.ManagedResourceSafety;

namespace BallsServer.ManagedResourceSafety.Tests;

public sealed class ManagedFolderSafetyTests
{
    public static TheoryData<FolderObservation, FolderRefusal> InvalidCandidates => new()
    {
        { ValidFolder() with { Exists = false }, FolderRefusal.Missing },
        { ValidFolder() with { IsDirectory = false }, FolderRefusal.Missing },
        { ValidFolder() with { PathKind = FolderPathKind.Remote }, FolderRefusal.Remote },
        { ValidFolder() with { PathKind = FolderPathKind.Device }, FolderRefusal.Device },
        { ValidFolder() with { IsDriveRoot = true }, FolderRefusal.DriveRoot },
        { ValidFolder() with { IsProtectedSystemLocation = true }, FolderRefusal.ProtectedSystem },
        { ValidFolder() with { IsFixedVolume = false }, FolderRefusal.NonFixedVolume },
        { ValidFolder() with { FileSystem = "ReFS" }, FolderRefusal.NonNtfs },
        { ValidFolder() with { FileSystem = null }, FolderRefusal.Unknown },
        { ValidFolder() with { RootIsReparsePoint = true }, FolderRefusal.RootReparse },
        { ValidFolder() with { AncestorIsReparsePoint = true }, FolderRefusal.AncestorReparse },
        { ValidFolder() with { DescendantScanComplete = false }, FolderRefusal.UnresolvedDescendantReparse },
    };

    [Theory]
    [MemberData(nameof(InvalidCandidates))]
    public void Invalid_folder_candidates_fail_closed(FolderObservation observation, FolderRefusal expected)
    {
        FolderValidation result = ManagedFolderPolicy.Validate(observation);

        Assert.False(result.Accepted);
        Assert.Equal(expected, result.Refusal);
        Assert.Empty(result.Mutations);
        Assert.Contains("administrator", result.Guidance, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<DescendantLink> UnsafeDescendantLinks => new()
    {
        { new("nested/link", "C:\\fixture\\nested\\link", null, false, false, "link-object-1", null, "volume-1") },
        { new("nested/link", "C:\\fixture\\nested\\link", "D:\\outside", true, false, "link-object-1", "target-object-1", "volume-2") },
        { new("nested/link", "C:\\fixture\\nested\\link", "C:\\fixture\\target", true, true, null, "target-object-1", "volume-1") },
    };

    [Theory]
    [MemberData(nameof(UnsafeDescendantLinks))]
    public void Unresolved_or_uncontained_descendant_links_refuse(DescendantLink link)
    {
        FolderObservation observation = ValidFolder() with { DescendantLinks = [link] };

        FolderValidation result = ManagedFolderPolicy.Validate(observation);

        Assert.Equal(FolderRefusal.UnresolvedDescendantReparse, result.Refusal);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void Safe_candidate_retains_folder_volume_descriptor_and_descendant_identities()
    {
        FolderObservation observation = ValidFolder() with
        {
            DescendantLinks = [new("nested/link", "C:\\fixture\\nested\\link", "C:\\fixture\\target", true, true, "link-object-1", "target-object-1", "volume-1")],
        };

        FolderValidation result = ManagedFolderPolicy.Validate(observation);

        Assert.True(result.Accepted);
        Assert.Equal(observation.Identity, result.RetainedIdentity);
        Assert.Equal(observation.DescendantLinks, result.RetainedDescendantLinks);
        Assert.Empty(result.Mutations);
    }

    public static TheoryData<FolderObservation> DriftedObservations => new()
    {
        { ValidFolder() with { Identity = ValidFolder().Identity! with { VolumeId = "volume-2" } } },
        { ValidFolder() with { Identity = ValidFolder().Identity! with { FileId = "file-2" } } },
        { ValidFolder() with { Identity = ValidFolder().Identity! with { CanonicalPath = "C:\\fixture\\renamed" } } },
        { ValidFolder() with { Identity = ValidFolder().Identity! with { DescriptorFingerprint = "descriptor-2" } } },
        { ValidFolder() with { DescendantLinks = [new("nested/link", "C:\\fixture\\nested\\link", "C:\\fixture\\target", true, true, "link-object-2", "target-object-1", "volume-1")] } },
    };

    [Theory]
    [MemberData(nameof(DriftedObservations))]
    public void Identity_or_descendant_drift_refuses_time_of_use(FolderObservation current)
    {
        FolderValidation retained = ManagedFolderPolicy.Validate(ValidFolder());

        FolderUseValidation result = ManagedFolderPolicy.ValidateAtUse(retained, current);

        Assert.False(result.Accepted);
        Assert.Equal(FolderRefusal.IdentityDrift, result.Refusal);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void Descendant_discovery_walks_nested_entries_without_following_links()
    {
        FakeTree tree = new(
            new TreeEntry("C:\\root\\a", false, null, false),
            new TreeEntry("C:\\root\\a\\link", true, new("C:\\root\\target", "volume-1", "target-object-1"), true, "link-object-1"),
            new TreeEntry("C:\\root\\b", false, null, false),
            new TreeEntry("C:\\root\\b\\link", true, new("C:\\root\\target-2", "volume-1", "target-object-2"), true, "link-object-2"));

        IReadOnlyList<DescendantLink> links = DescendantLinkDiscovery.Discover(new("C:\\root", "volume-1"), tree);

        Assert.Equal(2, links.Count);
        Assert.Equal(["a/link", "b/link"], links.Select(link => link.RelativePath));
        Assert.Equal(3, tree.DirectoriesEnumerated);
        Assert.DoesNotContain("C:\\root\\a\\link", tree.EnumeratedDirectories);
    }

    [Fact]
    public void Unique_disposable_fixture_is_removed_deterministically()
    {
        string? path;
        using (DisposableFolderFixture fixture = DisposableFolderFixture.Create())
        {
            path = fixture.Path;
            Assert.StartsWith(System.IO.Path.GetTempPath(), path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("BallsServer.Test.", System.IO.Path.GetFileName(path), StringComparison.Ordinal);
            File.WriteAllText(System.IO.Path.Combine(path, "owned.txt"), "non-secret");
        }

        Assert.False(Directory.Exists(path));
    }

    private static FolderObservation ValidFolder() => new(
        RequestedPath: "C:\\fixture",
        PathKind: FolderPathKind.Local,
        Exists: true,
        IsDirectory: true,
        IsDriveRoot: false,
        IsProtectedSystemLocation: false,
        IsFixedVolume: true,
        FileSystem: "NTFS",
        RootIsReparsePoint: false,
        AncestorIsReparsePoint: false,
        DescendantScanComplete: true,
        Identity: new("volume-1", "file-1", "C:\\fixture", "descriptor-1"),
        DescendantLinks: []);

    private sealed class FakeTree(params TreeEntry[] entries) : IDescendantTree
    {
        public int DirectoriesEnumerated { get; private set; }

        public List<string> EnumeratedDirectories { get; } = [];

        public IReadOnlyList<TreeEntry> Enumerate(string directory)
        {
            DirectoriesEnumerated++;
            EnumeratedDirectories.Add(directory);
            return entries.Where(entry =>
            {
                string? parent = System.IO.Path.GetDirectoryName(entry.Path);
                return string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        }
    }
}
