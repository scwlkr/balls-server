namespace BallsServer.ClientLifecycle;

public enum ClientAction { Check, Connect, Reconnect, Switch }
public enum RecoveryCategory { None, InvalidCredential, LockedAccount, PathUnavailable, ObservationFailed, Collision, CleanupFailed, HostRevoked, Refused }
public enum AuthenticationResult { Success, InvalidCredential, LockedAccount, PathUnavailable }

public sealed record Endpoint(string HostId, string GrantId, long CredentialRevision, string ServerName, string ShareName, string QualifiedSamAccount)
{
    public string MappingUnc => $"\\\\{ServerName}\\{ShareName}";
    public string ProviderTarget => ServerName;
    public bool IsExact => !string.IsNullOrWhiteSpace(ServerName) && !ServerName.Contains('*') && !ServerName.Contains('\\') &&
        !System.Net.IPAddress.TryParse(ServerName, out _) && ShareName == "Balls" && QualifiedSamAccount.Contains('\\');
}

public sealed record SetupCode(int SchemaVersion, Endpoint Endpoint)
{
    public const int CurrentSchemaVersion = 1;
    public bool IsValid => SchemaVersion == CurrentSchemaVersion && Endpoint.IsExact;
}

public sealed record EndpointUpdate(int SchemaVersion, Endpoint Endpoint)
{
    public bool IsBoundTo(ClientState state) => SchemaVersion == SetupCode.CurrentSchemaVersion && Endpoint.IsExact &&
        Endpoint.HostId == state.HostId && Endpoint.GrantId == state.GrantId && Endpoint.CredentialRevision == state.CredentialRevision;
}

public sealed record ClientState(
    string HostId,
    string GrantId,
    long CredentialRevision,
    Endpoint? SelectedEndpoint = null,
    string? ProductRecordedCredentialTarget = null,
    char? ProductRecordedLetter = null,
    bool SaveSelected = false,
    bool ReconnectSelected = false,
    bool HostRevoked = false);

public sealed record Inspection(bool EndpointAvailable, bool MappingCollision, bool DriveLetterUsed, bool CredentialCollision, bool OpenFile = false, bool ObservationComplete = true);
public sealed record ActionPlan(bool Allowed, int AuthenticationAttempts, IReadOnlyList<string> Steps, RecoveryCategory Category, string PublicMessage, string? CandidateMappingUnc = null, string? CandidateProviderTarget = null);
public sealed record CleanupPlan(bool Success, bool DeleteCredential, bool Unmap, RecoveryCategory Category);
public sealed record VerificationPlan(string TemporaryFileName, string? PrivateLeftoverPath, RecoveryCategory Category)
{
    public string PublicMessage => Category == RecoveryCategory.CleanupFailed ? "Access verification needs recovery." : "Access verification completed.";
}
public sealed record VmHarnessRequest(bool IsDevelopmentHost, bool IsAdministrator, bool DisposableVmMarker, string? SnapshotId, string? TestNamespace);
public sealed record VmHarnessResult(bool Allowed, string PublicMessage);

public static class ClientLifecyclePlanner
{
    private static readonly string[] InspectionSteps = ["InspectEndpoint", "InspectMapping", "InspectDriveLetter", "InspectCredential"];

    public static ActionPlan Plan(ClientAction action, ClientState state, Inspection inspection, char? ownerLetter = null, EndpointUpdate? update = null)
    {
        if (state.HostRevoked) return Refused(RecoveryCategory.HostRevoked, InspectionSteps);
        Endpoint? endpoint = action == ClientAction.Switch ? ImportUpdate(state, update) : state.SelectedEndpoint;
        if (endpoint is null || !endpoint.IsExact) return Refused(RecoveryCategory.Refused, InspectionSteps);
        if (!inspection.ObservationComplete) return Refused(RecoveryCategory.ObservationFailed, InspectionSteps);
        if (!inspection.EndpointAvailable) return Refused(RecoveryCategory.PathUnavailable, InspectionSteps);
        if (inspection.MappingCollision || inspection.DriveLetterUsed || inspection.CredentialCollision || inspection.OpenFile) return Refused(RecoveryCategory.Collision, InspectionSteps);
        if (action is ClientAction.Connect or ClientAction.Reconnect or ClientAction.Switch && ownerLetter is null) return Refused(RecoveryCategory.Refused, InspectionSteps);
        if (action is ClientAction.Connect or ClientAction.Reconnect or ClientAction.Switch && (ownerLetter is < 'D' or > 'Z')) return Refused(RecoveryCategory.Refused, InspectionSteps);

        List<string> steps = [.. InspectionSteps];
        if (action == ClientAction.Switch) steps.Insert(0, "ImportEndpointUpdate");
        steps.Add("AuthenticateExactEndpoint");
        if (action is ClientAction.Connect or ClientAction.Reconnect or ClientAction.Switch) steps.Add("MapExactUncInProcess");
        return new(true, 1, steps, RecoveryCategory.None, "Connection check completed.", endpoint.MappingUnc, endpoint.ProviderTarget);
    }

    public static RecoveryCategory CategorizeAuthentication(AuthenticationResult result) => result switch
    {
        AuthenticationResult.InvalidCredential => RecoveryCategory.InvalidCredential,
        AuthenticationResult.LockedAccount => RecoveryCategory.LockedAccount,
        AuthenticationResult.PathUnavailable => RecoveryCategory.PathUnavailable,
        _ => RecoveryCategory.None
    };

    public static ClientState SelectSave(ClientState state, bool save) => state with { SaveSelected = save, ReconnectSelected = save };
    public static ClientState SelectReconnect(ClientState state, bool reconnect) => state with { ReconnectSelected = state.SaveSelected && reconnect };

    public static bool CanSaveCredential(ClientState state, string proposedProviderTarget) => state.SelectedEndpoint is { } endpoint && state.SaveSelected &&
        proposedProviderTarget == endpoint.ProviderTarget && endpoint.IsExact && (state.ProductRecordedCredentialTarget is null || state.ProductRecordedCredentialTarget == proposedProviderTarget);

    public static CleanupPlan PlanCleanup(ClientState state, bool credentialFound, bool mappingFound)
    {
        bool ownsCredential = state.ProductRecordedCredentialTarget == state.SelectedEndpoint?.ProviderTarget;
        bool ownsMapping = state.ProductRecordedLetter is not null && state.SelectedEndpoint is not null;
        return new((!credentialFound || ownsCredential) && (!mappingFound || ownsMapping), ownsCredential && credentialFound, ownsMapping && mappingFound,
            (!credentialFound || ownsCredential) && (!mappingFound || ownsMapping) ? RecoveryCategory.None : RecoveryCategory.Collision);
    }

    public static VerificationPlan PlanVerification(Endpoint endpoint, string operationId, bool cleanupSucceeded)
    {
        string file = $".ballsserver-verify-{operationId}.tmp";
        return cleanupSucceeded ? new(file, null, RecoveryCategory.None) : new(file, endpoint.MappingUnc + "\\" + file, RecoveryCategory.CleanupFailed);
    }

    public static VmHarnessResult GuardVmHarness(VmHarnessRequest request)
    {
        bool validNamespace = request.TestNamespace?.StartsWith("BallsServer.Test.", StringComparison.Ordinal) == true;
        bool allowed = !request.IsDevelopmentHost && request.IsAdministrator && request.DisposableVmMarker && !string.IsNullOrWhiteSpace(request.SnapshotId) && validNamespace;
        return new(allowed, allowed ? "Disposable VM harness is eligible." : "Disposable VM harness refused.");
    }

    private static Endpoint? ImportUpdate(ClientState state, EndpointUpdate? update) => update is not null && update.IsBoundTo(state) ? update.Endpoint : null;
    private static ActionPlan Refused(RecoveryCategory category, IEnumerable<string> steps) => new(false, 0, steps.ToArray(), category, "Connection check did not complete.");
}

public static class Program
{
    public static void Main() => Console.WriteLine("PASS: isolated client lifecycle model loaded; no credential, mapping, SMB, filesystem, or Windows mutation executed.");
}
