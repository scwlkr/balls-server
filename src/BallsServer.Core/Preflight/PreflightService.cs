namespace BallsServer.Core.Preflight;

public interface IPreflightCheck
{
    PreflightCheckId Id { get; }

    string Title { get; }

    int Order { get; }

    ValueTask<PreflightCheckResult> CheckAsync(PreflightContext context, CancellationToken cancellationToken);
}

public interface IPreflightService
{
    Task<PreflightReport> RunAsync(PreflightRequest request, CancellationToken cancellationToken = default);

    Task<PreflightReport> RunAsync(
        PreflightRequest request,
        IProgress<PreflightProgress> progress,
        CancellationToken cancellationToken = default);
}

public sealed record PreflightContext(string TargetPath, PreflightPolicy Policy);

public sealed class PreflightService : IPreflightService
{
    private readonly IPreflightCheck[] _checks;
    private readonly PreflightPolicy _policy;
    private readonly TimeProvider _timeProvider;

    public PreflightService(
        IEnumerable<IPreflightCheck> checks,
        PreflightPolicy policy,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(policy);

        _checks = checks.OrderBy(static check => check.Order).ToArray();
        _policy = policy;
        _timeProvider = timeProvider ?? TimeProvider.System;

        HostPreflightCatalog.Validate(_checks);
    }

    public Task<PreflightReport> RunAsync(
        PreflightRequest request,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(request, progress: null, cancellationToken);

    public Task<PreflightReport> RunAsync(
        PreflightRequest request,
        IProgress<PreflightProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return RunCoreAsync(request, progress, cancellationToken);
    }

    private async Task<PreflightReport> RunCoreAsync(
        PreflightRequest request,
        IProgress<PreflightProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetPath);

        var targetPath = Path.GetFullPath(request.TargetPath);
        var context = new PreflightContext(targetPath, _policy);
        var startedAt = _timeProvider.GetUtcNow();
        var results = new List<PreflightCheckResult>(_checks.Length);

        for (var index = 0; index < _checks.Length; index++)
        {
            var check = _checks[index];
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PreflightProgress(check.Id, check.Title, index + 1, _checks.Length));

            try
            {
                var result = await check.CheckAsync(context, cancellationToken).ConfigureAwait(false);
                results.Add(result.Id == check.Id
                    ? result
                    : PreflightCheckResult.Unknown(
                        check.Id,
                        check.Title,
                        "invalid_check_result",
                        "This check returned an invalid result and could not be trusted."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                results.Add(PreflightCheckResult.Unknown(
                    check.Id,
                    check.Title,
                    "check_failed",
                    "Windows did not return enough information to complete this check."));
            }
        }

        var completedAt = _timeProvider.GetUtcNow();
        var frozenResults = Array.AsReadOnly(results.ToArray());
        var frozenPrerequisites = Array.AsReadOnly(
            results.Where(static result => result.Id != PreflightCheckId.Administrator).ToArray());
        var resultsById = frozenResults.ToDictionary(static result => result.Id);
        var computer = CreateAggregate(
            PreflightAggregateId.Computer,
            "Computer",
            evaluatedFolderPath: null,
            resultsById[PreflightCheckId.WindowsVersion],
            resultsById[PreflightCheckId.Firewall],
            resultsById[PreflightCheckId.Smb]);
        var managedFolder = CreateAggregate(
            PreflightAggregateId.ManagedFolder,
            "Managed folder",
            targetPath,
            resultsById[PreflightCheckId.Storage],
            resultsById[PreflightCheckId.FolderPermissions]);
        var localAccess = CreateAggregate(
            PreflightAggregateId.LocalAccess,
            "Local access",
            targetPath,
            [
                .. computer.Prerequisites,
                .. managedFolder.Prerequisites,
                resultsById[PreflightCheckId.NetworkProfile],
            ]);
        var tailscaleAccess = CreateAggregate(
            PreflightAggregateId.TailscaleAccess,
            "Tailscale access",
            targetPath,
            [
                .. computer.Prerequisites,
                .. managedFolder.Prerequisites,
                resultsById[PreflightCheckId.Tailscale],
            ]);

        return new PreflightReport(
            targetPath,
            startedAt,
            completedAt,
            frozenResults,
            frozenPrerequisites,
            computer,
            managedFolder,
            localAccess,
            tailscaleAccess,
            CreateAdministratorInformation(resultsById[PreflightCheckId.Administrator]),
            HostingStateResult.NotConfigured);
    }

    private static AdministratorInformation CreateAdministratorInformation(PreflightCheckResult result)
    {
        var availability = result.Status == PreflightCheckStatus.Unknown
            ? AdministratorInformationAvailability.Unavailable
            : AdministratorInformationAvailability.Available;
        var summary = availability == AdministratorInformationAvailability.Unavailable
            ? "Windows could not determine administrator information. Future Host Files setup may request approval, but this dashboard stays unelevated."
            : result.Summary;

        return new AdministratorInformation(
            availability,
            summary,
            result.ReasonCode,
            result.Evidence);
    }

    private static PreflightAggregateResult CreateAggregate(
        PreflightAggregateId id,
        string title,
        string? evaluatedFolderPath,
        params PreflightCheckResult[] prerequisites)
    {
        var frozenPrerequisites = Array.AsReadOnly(prerequisites);
        var status = Reduce(frozenPrerequisites);

        return new PreflightAggregateResult(
            id,
            title,
            status,
            DescribeAggregate(id, status),
            evaluatedFolderPath,
            frozenPrerequisites);
    }

    private static string DescribeAggregate(PreflightAggregateId id, PreflightOverallStatus status) =>
        (id, status) switch
        {
            (PreflightAggregateId.Computer, PreflightOverallStatus.Ready) =>
                "Windows, Windows Firewall, and SMB policy meet the Host Files prerequisites.",
            (PreflightAggregateId.Computer, PreflightOverallStatus.ReadyWithWarnings) =>
                "The computer meets the Host Files prerequisites with an advisory warning.",
            (PreflightAggregateId.Computer, PreflightOverallStatus.NotReady) =>
                "A shared computer prerequisite needs action before either access path can be ready.",
            (PreflightAggregateId.Computer, PreflightOverallStatus.Indeterminate) =>
                "Windows did not provide enough information to determine the shared computer prerequisites.",
            (PreflightAggregateId.ManagedFolder, PreflightOverallStatus.Ready) =>
                "The selected managed-folder candidate meets the storage and current-token access prerequisites.",
            (PreflightAggregateId.ManagedFolder, PreflightOverallStatus.ReadyWithWarnings) =>
                "The selected managed-folder candidate meets its prerequisites with an advisory warning.",
            (PreflightAggregateId.ManagedFolder, PreflightOverallStatus.NotReady) =>
                "The selected managed-folder candidate needs action before it can be used for either access path.",
            (PreflightAggregateId.ManagedFolder, PreflightOverallStatus.Indeterminate) =>
                "Windows did not provide enough information to determine managed-folder readiness.",
            (PreflightAggregateId.LocalAccess, PreflightOverallStatus.Ready) =>
                "The shared prerequisites and trusted local-network posture are ready for local access.",
            (PreflightAggregateId.LocalAccess, PreflightOverallStatus.ReadyWithWarnings) =>
                "Local access is ready with an advisory warning.",
            (PreflightAggregateId.LocalAccess, PreflightOverallStatus.NotReady) =>
                "A prerequisite for the local access path needs action.",
            (PreflightAggregateId.LocalAccess, PreflightOverallStatus.Indeterminate) =>
                "Windows did not provide enough information to determine local access-path readiness.",
            (PreflightAggregateId.TailscaleAccess, PreflightOverallStatus.Ready) =>
                "The shared prerequisites and Tailscale state are ready for Tailscale access.",
            (PreflightAggregateId.TailscaleAccess, PreflightOverallStatus.ReadyWithWarnings) =>
                "Tailscale access is ready with an advisory warning.",
            (PreflightAggregateId.TailscaleAccess, PreflightOverallStatus.NotReady) =>
                "A prerequisite for the Tailscale access path needs action.",
            (PreflightAggregateId.TailscaleAccess, PreflightOverallStatus.Indeterminate) =>
                "Windows did not provide enough information to determine Tailscale access-path readiness.",
            _ => "Readiness could not be determined.",
        };

    internal static PreflightOverallStatus Reduce(IEnumerable<PreflightCheckResult> results)
    {
        var statuses = results.Select(static result => result.Status).ToArray();

        if (statuses.Contains(PreflightCheckStatus.ActionRequired))
        {
            return PreflightOverallStatus.NotReady;
        }

        if (statuses.Contains(PreflightCheckStatus.Unknown))
        {
            return PreflightOverallStatus.Indeterminate;
        }

        return statuses.Contains(PreflightCheckStatus.Warning)
            ? PreflightOverallStatus.ReadyWithWarnings
            : PreflightOverallStatus.Ready;
    }
}
