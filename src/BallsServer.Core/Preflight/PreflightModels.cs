using System.Collections.ObjectModel;

namespace BallsServer.Core.Preflight;

public enum PreflightCheckId
{
    Administrator,
    WindowsVersion,
    Storage,
    NetworkProfile,
    Firewall,
    Tailscale,
    Smb,
    FolderPermissions,
}

public enum PreflightCheckStatus
{
    Ready,
    Warning,
    ActionRequired,
    Unknown,
}

public enum PreflightOverallStatus
{
    Ready,
    ReadyWithWarnings,
    NotReady,
    Indeterminate,
}

public enum PreflightAggregateId
{
    Computer,
    ManagedFolder,
    LocalAccess,
    TailscaleAccess,
}

public enum AdministratorInformationAvailability
{
    Available,
    Unavailable,
}

public enum HostingState
{
    NotConfigured,
}

public sealed record PreflightEvidence(string Label, string Value);

public sealed record PreflightCheckResult(
    PreflightCheckId Id,
    string Title,
    PreflightCheckStatus Status,
    string ReasonCode,
    string Summary,
    IReadOnlyList<PreflightEvidence> Evidence)
{
    public static PreflightCheckResult Create(
        PreflightCheckId id,
        string title,
        PreflightCheckStatus status,
        string reasonCode,
        string summary,
        params PreflightEvidence[] evidence) =>
        new(id, title, status, reasonCode, summary, Array.AsReadOnly(evidence));

    public static PreflightCheckResult Unknown(
        PreflightCheckId id,
        string title,
        string reasonCode,
        string summary) =>
        Create(id, title, PreflightCheckStatus.Unknown, reasonCode, summary);
}

public sealed record PreflightRequest(string TargetPath);

public sealed record PreflightProgress(
    PreflightCheckId CheckId,
    string CheckTitle,
    int Position,
    int Total);

public sealed record PreflightAggregateResult(
    PreflightAggregateId Id,
    string Title,
    PreflightOverallStatus Status,
    string Summary,
    string? EvaluatedFolderPath,
    IReadOnlyList<PreflightCheckResult> Prerequisites)
{
    public bool IsReady => Status is PreflightOverallStatus.Ready or PreflightOverallStatus.ReadyWithWarnings;
}

public sealed record AdministratorInformation(
    AdministratorInformationAvailability Availability,
    string Summary,
    string ReasonCode,
    IReadOnlyList<PreflightEvidence> Evidence);

public sealed record HostingStateResult(
    HostingState State,
    string Title,
    string Summary)
{
    public static HostingStateResult NotConfigured { get; } = new(
        HostingState.NotConfigured,
        "Hosting state",
        "Balls Server hosting is not configured. Host Files preflight does not create a managed share and does not verify a client connection.");
}

public sealed record PreflightReport(
    string TargetPath,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<PreflightCheckResult> Checks,
    IReadOnlyList<PreflightCheckResult> Prerequisites,
    PreflightAggregateResult Computer,
    PreflightAggregateResult ManagedFolder,
    PreflightAggregateResult LocalAccess,
    PreflightAggregateResult TailscaleAccess,
    AdministratorInformation AdministratorInformation,
    HostingStateResult HostingState);

public sealed class PreflightPolicy
{
    public static PreflightPolicy HostDefault { get; } = new(
        minimumWindowsBuild: 26100,
        supportedEditionIds:
        [
            "Professional",
            "ProfessionalN",
            "ProfessionalEducation",
            "ProfessionalWorkstation",
        ],
        requiredFileSystem: "NTFS",
        minimumFreeBytes: 10L * 1024 * 1024 * 1024);

    public PreflightPolicy(
        int minimumWindowsBuild,
        IEnumerable<string> supportedEditionIds,
        string requiredFileSystem,
        long minimumFreeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumWindowsBuild);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredFileSystem);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumFreeBytes);

        var editions = supportedEditionIds
            .Where(static edition => !string.IsNullOrWhiteSpace(edition))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (editions.Length == 0)
        {
            throw new ArgumentException("At least one supported Windows edition is required.", nameof(supportedEditionIds));
        }

        MinimumWindowsBuild = minimumWindowsBuild;
        SupportedEditionIds = Array.AsReadOnly(editions);
        RequiredFileSystem = requiredFileSystem;
        MinimumFreeBytes = minimumFreeBytes;
    }

    public int MinimumWindowsBuild { get; }

    public IReadOnlyList<string> SupportedEditionIds { get; }

    public string RequiredFileSystem { get; }

    public long MinimumFreeBytes { get; }

    public bool SupportsEdition(string editionId) =>
        SupportedEditionIds.Contains(editionId, StringComparer.OrdinalIgnoreCase);
}

public sealed class ProbeResult<T>
    where T : notnull
{
    internal ProbeResult(T? value, bool hasValue, string? errorCode, string? errorMessage)
    {
        Value = value;
        HasValue = hasValue;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool HasValue { get; }

    public T? Value { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

}

public static class ProbeResult
{
    public static ProbeResult<T> Observed<T>(T value)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ProbeResult<T>(value, true, null, null);
    }

    public static ProbeResult<T> Unavailable<T>(string errorCode, string errorMessage)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ProbeResult<T>(default, false, errorCode, errorMessage);
    }
}
