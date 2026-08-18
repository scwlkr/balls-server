using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BallsServer.Windows;

public static partial class HostOwnershipPolicy
{
    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] ResourceOrder =
    [
        "Group",
        "Account",
        "Membership",
        "FolderAce",
        "Share",
        "FirewallRule",
    ];

    public static string EvaluateJson(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        try
        {
            var request = JsonSerializer.Deserialize<PolicyRequest>(input, InputOptions) ??
                throw new FormatException("The ownership policy input is incomplete.");
            return JsonSerializer.Serialize(Evaluate(request), OutputOptions);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new FormatException("The ownership policy input is malformed.", exception);
        }
    }

    private static PolicyResult Evaluate(PolicyRequest request)
    {
        if (request.Phase is not ("Preview" or "Execute") ||
            request.Operation is not ("Apply" or "StopSharing") ||
            !IsValidLedger(request.Ledger) ||
            !IsCompleteSnapshotStructure(request.Live))
        {
            return PolicyResult.Unknown();
        }

        if (!IsKnownSafe(request.Live!))
        {
            return PolicyResult.Refused();
        }

        var ledgerResources = request.Ledger!.Resources.ToDictionary(
            resource => resource.Kind,
            StringComparer.Ordinal);
        var liveResources = request.Live!.Resources.ToDictionary(
            resource => resource.Kind,
            StringComparer.Ordinal);
        foreach (var kind in ResourceOrder)
        {
            var owned = ledgerResources[kind];
            var live = liveResources[kind];
            if (live.State != "Present" ||
                !string.Equals(live.StableId, owned.StableId, StringComparison.Ordinal) ||
                !string.Equals(live.Fingerprint, owned.Fingerprint, StringComparison.Ordinal))
            {
                return PolicyResult.Refused();
            }
        }

        if (!MatchesFolderAndEndpoint(request.Ledger, request.Live))
        {
            return PolicyResult.Refused();
        }

        if (request.Operation == "Apply")
        {
            var verify = new OwnershipPrimitive(
                "VerifyEffectiveAccess",
                "Host",
                request.Ledger.ProductHostId,
                request.Ledger.DesiredStateFingerprint,
                "Started");
            var verifyPlan = new[] { verify };
            var verifyDigest = ComputePlanDigest(request.Operation, request.Ledger, request.Live, verifyPlan);
            if (request.Phase == "Execute" &&
                !string.Equals(request.ApprovedPlanDigest, verifyDigest, StringComparison.Ordinal))
            {
                return PolicyResult.Refused();
            }

            return new PolicyResult(
                "NoChanges",
                verifyDigest,
                verifyPlan);
        }

        var primitives = CreateStopPrimitives(ledgerResources);
        var digest = ComputePlanDigest(request.Operation, request.Ledger, request.Live, primitives);
        if (request.Phase == "Execute" &&
            !string.Equals(request.ApprovedPlanDigest, digest, StringComparison.Ordinal))
        {
            return PolicyResult.Refused();
        }

        return new PolicyResult(
            request.Phase == "Preview" ? "PreviewReady" : "Ready",
            digest,
            primitives);
    }

    private static bool IsValidLedger(OwnershipLedger? ledger)
    {
        if (ledger is null || ledger.SchemaVersion != 2 || ledger.Revision < 0 ||
            ledger.Status != "Committed" || string.IsNullOrEmpty(ledger.ProductHostId) ||
            !OpaqueIdPattern().IsMatch(ledger.ProductHostId) ||
            string.IsNullOrEmpty(ledger.DesiredStateFingerprint) ||
            !HashPattern().IsMatch(ledger.DesiredStateFingerprint) ||
            string.IsNullOrEmpty(ledger.ManagedFolderFingerprint) ||
            !HashPattern().IsMatch(ledger.ManagedFolderFingerprint) ||
            string.IsNullOrEmpty(ledger.UnrelatedAclFingerprint) ||
            !HashPattern().IsMatch(ledger.UnrelatedAclFingerprint) ||
            string.IsNullOrEmpty(ledger.EndpointFingerprint) ||
            !HashPattern().IsMatch(ledger.EndpointFingerprint) ||
            string.IsNullOrWhiteSpace(ledger.ManagedFolderStableId) ||
            ledger.Resources.Count != ResourceOrder.Length ||
            ledger.Resources.Select(resource => resource.Kind).Distinct(StringComparer.Ordinal).Count() !=
                ResourceOrder.Length)
        {
            return false;
        }

        var kinds = ledger.Resources.Select(resource => resource.Kind).ToHashSet(StringComparer.Ordinal);
        return ResourceOrder.All(kinds.Contains) && ledger.Resources.All(resource =>
            !string.IsNullOrWhiteSpace(resource.StableId) && HashPattern().IsMatch(resource.Fingerprint));
    }

    private static bool IsCompleteSnapshotStructure(LiveSnapshot? live) =>
        live is not null &&
        live.Complete &&
        live.Resources.Count == ResourceOrder.Length &&
        live.Resources.Select(resource => resource.Kind).Distinct(StringComparer.Ordinal).Count() ==
            ResourceOrder.Length &&
        ResourceOrder.All(kind => live.Resources.Any(resource => resource.Kind == kind)) &&
        live.Resources.All(resource =>
            !string.IsNullOrWhiteSpace(resource.StableId) &&
            !string.IsNullOrWhiteSpace(resource.Fingerprint));

    private static bool IsKnownSafe(LiveSnapshot live) =>
        live.ServerRunning &&
        live.Smb1Disabled &&
        live.Smb2Enabled &&
        live.MinimumDialect >= 768 &&
        live.MaximumDialect >= live.MinimumDialect &&
        live.SigningEnabled &&
        live.SigningRequired &&
        live.GuestDisabled &&
        live.AnonymousDisabled &&
        live.BlankPasswordsDisabled &&
        live.FirewallScopeSafe &&
        live.AuthenticatedEffectiveAccess &&
        live.DescendantReparseCount == 0 &&
        live.OtherShareCount == 0 &&
        live.ConflictingAceCount == 0;

    private static bool MatchesFolderAndEndpoint(OwnershipLedger ledger, LiveSnapshot live) =>
        string.Equals(ledger.ManagedFolderStableId, live.ManagedFolderStableId, StringComparison.Ordinal) &&
        string.Equals(ledger.ManagedFolderFingerprint, live.ManagedFolderFingerprint, StringComparison.Ordinal) &&
        string.Equals(ledger.UnrelatedAclFingerprint, live.UnrelatedAclFingerprint, StringComparison.Ordinal) &&
        string.Equals(ledger.EndpointFingerprint, live.EndpointFingerprint, StringComparison.Ordinal);

    private static IReadOnlyList<OwnershipPrimitive> CreateStopPrimitives(
        IReadOnlyDictionary<string, OwnedResource> resources) =>
    [
        Primitive("DisableAccount", resources["Account"]),
        Primitive("RemoveMembership", resources["Membership"]),
        Primitive("RemoveShare", resources["Share"]),
        Primitive("RemoveFirewallRule", resources["FirewallRule"]),
        Primitive("RemoveFolderAce", resources["FolderAce"]),
        Primitive("RemoveAccount", resources["Account"]),
        Primitive("RemoveGroup", resources["Group"]),
        new OwnershipPrimitive("MarkHostRemoved", "Ledger", string.Empty, string.Empty, "Terminal"),
    ];

    private static OwnershipPrimitive Primitive(string kind, OwnedResource resource) =>
        new(kind, resource.Kind, resource.StableId, resource.Fingerprint, "Started");

    private static string ComputePlanDigest(
        string operation,
        OwnershipLedger ledger,
        LiveSnapshot live,
        IReadOnlyList<OwnershipPrimitive> primitives)
    {
        var canonical = new StringBuilder()
            .Append(operation).Append('|')
            .Append(ledger.ProductHostId).Append('|')
            .Append(ledger.Revision).Append('|')
            .Append(ledger.DesiredStateFingerprint).Append('|')
            .Append(ledger.ManagedFolderStableId).Append('|')
            .Append(ledger.ManagedFolderFingerprint).Append('|')
            .Append(ledger.UnrelatedAclFingerprint).Append('|')
            .Append(live.EndpointFingerprint);
        foreach (var primitive in primitives)
        {
            canonical.Append('|').Append(primitive.Kind)
                .Append(':').Append(primitive.ResourceKind)
                .Append(':').Append(primitive.StableId)
                .Append(':').Append(primitive.Fingerprint)
                .Append(':').Append(primitive.JournalPhase);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    private sealed record PolicyRequest(
        string Phase,
        string Operation,
        string? ApprovedPlanDigest,
        OwnershipLedger? Ledger,
        LiveSnapshot? Live);

    private sealed record OwnershipLedger(
        int SchemaVersion,
        string ProductHostId,
        long Revision,
        string Status,
        string DesiredStateFingerprint,
        string ManagedFolderStableId,
        string ManagedFolderFingerprint,
        string UnrelatedAclFingerprint,
        string EndpointFingerprint,
        IReadOnlyList<OwnedResource> Resources,
        IReadOnlyList<string> AppliedPrimitives,
        string? StartedPrimitive);

    private sealed record OwnedResource(string Kind, string StableId, string Fingerprint);

    private sealed record LiveSnapshot(
        bool Complete,
        bool ServerRunning,
        bool Smb1Disabled,
        bool Smb2Enabled,
        int MinimumDialect,
        int MaximumDialect,
        bool SigningEnabled,
        bool SigningRequired,
        bool GuestDisabled,
        bool AnonymousDisabled,
        bool BlankPasswordsDisabled,
        bool FirewallScopeSafe,
        bool AuthenticatedEffectiveAccess,
        string ManagedFolderStableId,
        string ManagedFolderFingerprint,
        string UnrelatedAclFingerprint,
        string EndpointFingerprint,
        int DescendantReparseCount,
        int OtherShareCount,
        int ConflictingAceCount,
        IReadOnlyList<LiveResource> Resources);

    private sealed record LiveResource(string Kind, string State, string StableId, string Fingerprint);

    private sealed record OwnershipPrimitive(
        string Kind,
        string ResourceKind,
        string StableId,
        string Fingerprint,
        string JournalPhase);

    private sealed record PolicyResult(
        string Status,
        string? PlanDigest,
        IReadOnlyList<OwnershipPrimitive> Primitives)
    {
        public static PolicyResult Refused() => new("Refused", null, []);

        public static PolicyResult Unknown() => new("Unknown", null, []);
    }
}
