namespace BallsServer.ManagedResourceSafety;

public enum FolderPathKind
{
    Local,
    Remote,
    Device,
}

public enum FolderRefusal
{
    None,
    Missing,
    Remote,
    Device,
    DriveRoot,
    ProtectedSystem,
    NonFixedVolume,
    NonNtfs,
    RootReparse,
    AncestorReparse,
    UnresolvedDescendantReparse,
    IdentityDrift,
    Unknown,
}

public sealed record FolderIdentity(
    string VolumeId,
    string FileId,
    string CanonicalPath,
    string DescriptorFingerprint);

public sealed record DescendantLink(
    string RelativePath,
    string CanonicalLinkPath,
    string? CanonicalTargetPath,
    bool TargetEvidenceComplete,
    bool ReportedTargetContained,
    string? StableLinkObjectId,
    string? StableTargetObjectId,
    string? TargetVolumeId);

public sealed record DescendantRootIdentity(string CanonicalFinalPath, string VolumeId);

public sealed record CanonicalPathIdentity(string CanonicalFinalPath, string VolumeId, string StableFileId);

public sealed record FolderObservation(
    string? RequestedPath,
    FolderPathKind PathKind,
    bool Exists,
    bool IsDirectory,
    bool IsDriveRoot,
    bool IsProtectedSystemLocation,
    bool IsFixedVolume,
    string? FileSystem,
    bool RootIsReparsePoint,
    bool AncestorIsReparsePoint,
    bool DescendantScanComplete,
    FolderIdentity? Identity,
    IReadOnlyList<DescendantLink> DescendantLinks);

public sealed record FolderValidation(
    bool Accepted,
    FolderRefusal Refusal,
    string Guidance,
    FolderIdentity? RetainedIdentity,
    IReadOnlyList<DescendantLink> RetainedDescendantLinks,
    IReadOnlyList<string> Mutations);

public sealed record FolderUseValidation(
    bool Accepted,
    FolderRefusal Refusal,
    string Guidance,
    IReadOnlyList<string> Mutations);

public static class ManagedFolderPolicy
{
    private const string AdministratorGuidance =
        "Administrator action: inspect the exact folder and volume without taking ownership, following links, or changing descendants; then select a proven local NTFS directory and re-run observation.";

    public static FolderValidation Validate(FolderObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        FolderRefusal refusal = observation switch
        {
            { Exists: false } or { IsDirectory: false } or { Identity: null } => FolderRefusal.Missing,
            { PathKind: FolderPathKind.Remote } => FolderRefusal.Remote,
            { PathKind: FolderPathKind.Device } => FolderRefusal.Device,
            { PathKind: not FolderPathKind.Local } => FolderRefusal.Unknown,
            { IsDriveRoot: true } => FolderRefusal.DriveRoot,
            { IsProtectedSystemLocation: true } => FolderRefusal.ProtectedSystem,
            { IsFixedVolume: false } => FolderRefusal.NonFixedVolume,
            { FileSystem: not null } when !string.Equals(observation.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase) => FolderRefusal.NonNtfs,
            { FileSystem: null } => FolderRefusal.Unknown,
            { RootIsReparsePoint: true } => FolderRefusal.RootReparse,
            { AncestorIsReparsePoint: true } => FolderRefusal.AncestorReparse,
            { DescendantScanComplete: false } => FolderRefusal.UnresolvedDescendantReparse,
            _ when string.IsNullOrWhiteSpace(observation.Identity!.VolumeId) ||
                string.IsNullOrWhiteSpace(observation.Identity.FileId) ||
                string.IsNullOrWhiteSpace(observation.Identity.DescriptorFingerprint) => FolderRefusal.Unknown,
            _ when !CanonicalWindowsPath.TrySegments(observation.Identity!.CanonicalPath, out _) => FolderRefusal.Unknown,
            _ when observation.DescendantLinks.Any(link => IsUnsafeLink(observation.Identity!, link)) => FolderRefusal.UnresolvedDescendantReparse,
            _ when HasDuplicateLinkEvidence(observation.DescendantLinks) => FolderRefusal.UnresolvedDescendantReparse,
            _ when !HasCanonicalLinkOrder(observation.DescendantLinks) => FolderRefusal.UnresolvedDescendantReparse,
            _ => FolderRefusal.None,
        };

        return refusal == FolderRefusal.None
            ? new(true, refusal, string.Empty, observation.Identity, observation.DescendantLinks.ToArray(), [])
            : new(false, refusal, AdministratorGuidance, null, [], []);
    }

    public static FolderUseValidation ValidateAtUse(FolderValidation retained, FolderObservation current)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(current);

        FolderValidation currentValidation = Validate(current);
        bool sameIdentity = IsCanonicalSuccess(retained) &&
            IsCanonicalSuccess(currentValidation) &&
            retained.RetainedIdentity == currentValidation.RetainedIdentity;
        bool sameLinks = sameIdentity &&
            retained.RetainedDescendantLinks.SequenceEqual(currentValidation.RetainedDescendantLinks);

        return sameIdentity && sameLinks
            ? new(true, FolderRefusal.None, string.Empty, [])
            : new(false, FolderRefusal.IdentityDrift,
                "Administrator action: the retained folder, volume, descriptor, or descendant-link identity changed; apply nothing and re-run the complete read-only validation.", []);
    }

    private static bool IsUnsafeLink(FolderIdentity root, DescendantLink link)
    {
        bool targetContained = link.CanonicalTargetPath is not null &&
            CanonicalWindowsPath.IsContained(root.CanonicalPath, link.CanonicalTargetPath) &&
            string.Equals(root.VolumeId, link.TargetVolumeId, StringComparison.Ordinal);
        return !link.TargetEvidenceComplete ||
            !targetContained ||
            link.ReportedTargetContained != targetContained ||
            !CanonicalWindowsPath.IsExactDescendantWithRelativePath(root.CanonicalPath, link.CanonicalLinkPath, link.RelativePath) ||
            string.IsNullOrWhiteSpace(link.StableLinkObjectId) ||
            string.IsNullOrWhiteSpace(link.StableTargetObjectId) ||
            string.Equals(link.StableLinkObjectId, link.StableTargetObjectId, StringComparison.Ordinal);
    }

    private static bool IsCanonicalSuccess(FolderValidation result) =>
        result is
        {
            Accepted: true,
            Refusal: FolderRefusal.None,
            Guidance.Length: 0,
            RetainedIdentity: not null,
            RetainedDescendantLinks: not null,
            Mutations.Count: 0,
        } &&
        !string.IsNullOrWhiteSpace(result.RetainedIdentity.VolumeId) &&
        !string.IsNullOrWhiteSpace(result.RetainedIdentity.FileId) &&
        !string.IsNullOrWhiteSpace(result.RetainedIdentity.DescriptorFingerprint) &&
        CanonicalWindowsPath.TrySegments(result.RetainedIdentity.CanonicalPath, out _) &&
        result.RetainedDescendantLinks.All(link => !IsUnsafeLink(result.RetainedIdentity, link)) &&
        !HasDuplicateLinkEvidence(result.RetainedDescendantLinks) &&
        HasCanonicalLinkOrder(result.RetainedDescendantLinks);

    private static bool HasDuplicateLinkEvidence(IReadOnlyList<DescendantLink> links) =>
        links.GroupBy(link => link.RelativePath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1) ||
        links.GroupBy(link => link.CanonicalLinkPath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1) ||
        links.GroupBy(link => link.StableLinkObjectId, StringComparer.Ordinal).Any(group => group.Count() != 1);

    private static bool HasCanonicalLinkOrder(IReadOnlyList<DescendantLink> links) =>
        links.SequenceEqual(links.OrderBy(link => link.RelativePath, StringComparer.Ordinal));
}

public sealed record TreeEntry(
    string Path,
    bool IsReparsePoint,
    CanonicalPathIdentity? CanonicalTarget,
    bool CanonicalizationComplete,
    string? StableLinkObjectId = null);

public interface IDescendantTree
{
    IReadOnlyList<TreeEntry> Enumerate(string directory);
}

public static class DescendantLinkDiscovery
{
    public static IReadOnlyList<DescendantLink> Discover(DescendantRootIdentity root, IDescendantTree tree)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(root.CanonicalFinalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root.VolumeId);
        ArgumentNullException.ThrowIfNull(tree);

        List<DescendantLink> links = [];
        Stack<string> pending = new();
        pending.Push(root.CanonicalFinalPath);

        while (pending.TryPop(out string? directory))
        {
            foreach (TreeEntry entry in tree.Enumerate(directory))
            {
                if (entry.IsReparsePoint)
                {
                    CanonicalPathIdentity? target = entry.CanonicalTarget;
                    bool resolved = entry.CanonicalizationComplete &&
                        target is not null &&
                        !string.IsNullOrWhiteSpace(target.StableFileId);
                    bool contained = resolved &&
                        string.Equals(root.VolumeId, target!.VolumeId, StringComparison.Ordinal) &&
                        CanonicalWindowsPath.IsContained(root.CanonicalFinalPath, target.CanonicalFinalPath);
                    links.Add(new(
                        Path.GetRelativePath(root.CanonicalFinalPath, entry.Path).Replace('\\', '/'),
                        entry.Path,
                        target?.CanonicalFinalPath,
                        resolved,
                        contained,
                        entry.StableLinkObjectId,
                        target?.StableFileId,
                        target?.VolumeId));
                    continue;
                }

                pending.Push(entry.Path);
            }
        }

        return links.OrderBy(link => link.RelativePath, StringComparer.Ordinal).ToArray();
    }

}

internal static class CanonicalWindowsPath
{
    public static bool IsContained(string rootPath, string targetPath)
    {
        if (!TrySegments(rootPath, out string[] rootSegments) ||
            !TrySegments(targetPath, out string[] targetSegments) ||
            targetSegments.Length < rootSegments.Length)
        {
            return false;
        }

        for (int index = 0; index < rootSegments.Length; index++)
        {
            if (!string.Equals(rootSegments[index], targetSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsExactDescendantWithRelativePath(string rootPath, string childPath, string relativePath)
    {
        if (!TrySegments(rootPath, out string[] rootSegments) ||
            !TrySegments(childPath, out string[] childSegments) ||
            childSegments.Length <= rootSegments.Length ||
            !IsContained(rootPath, childPath) ||
            !TryRelativeSegments(relativePath, out string[] relativeSegments))
        {
            return false;
        }

        return relativeSegments.SequenceEqual(childSegments.Skip(rootSegments.Length), StringComparer.OrdinalIgnoreCase);
    }

    public static bool TrySegments(string path, out string[] segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("\\?\\", StringComparison.Ordinal) ||
            path.StartsWith("\\.\\", StringComparison.Ordinal) ||
            path.StartsWith("\\??\\", StringComparison.Ordinal))
        {
            return false;
        }

        string normalized = path.Replace('/', '\\');
        if (normalized.Length < 3 ||
            !char.IsAsciiLetter(normalized[0]) ||
            normalized[1] != ':' ||
            normalized[2] != '\\' ||
            normalized.Contains("\\\\", StringComparison.Ordinal) ||
            (normalized.Length > 3 && normalized.EndsWith('\\')))
        {
            return false;
        }

        string[] candidate = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (candidate.Length == 0 || candidate.Any(IsUnsafeSegment))
        {
            return false;
        }

        if (candidate[0].Length != 2 || candidate[0][1] != ':' || !char.IsAsciiLetter(candidate[0][0]) ||
            candidate.Skip(1).Any(segment => segment.Contains(':', StringComparison.Ordinal)))
        {
            return false;
        }

        segments = candidate;
        return true;
    }

    private static bool TryRelativeSegments(string path, out string[] segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('\\') ||
            path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains("//", StringComparison.Ordinal) ||
            path.EndsWith('/') ||
            path != path.Trim())
        {
            return false;
        }

        string[] candidate = path.Split('/');
        if (candidate.Length == 0 || candidate.Any(IsUnsafeSegment) || candidate.Any(segment => segment.Contains(':', StringComparison.Ordinal)))
        {
            return false;
        }

        segments = candidate;
        return true;
    }

    private static bool IsUnsafeSegment(string segment)
    {
        if (segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            return true;
        }

        string stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 &&
             (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             stem[3] is >= '1' and <= '9');
    }
}

public sealed class DisposableFolderFixture : IDisposable
{
    private bool disposed;

    private DisposableFolderFixture(string path) => Path = path;

    public string Path { get; }

    public static DisposableFolderFixture Create()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BallsServer.Test.{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new(path);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
