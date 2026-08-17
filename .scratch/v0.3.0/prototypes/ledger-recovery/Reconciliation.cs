using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BallsServer.LedgerRecovery;

public enum LiveEvidenceKind { Exact, Drifted, Absent, Foreign, Multiple, Unknown, AccessDenied, PolicyOverride }
public enum ReconciliationClass { OwnedConformant, OwnedDrifted, Missing, UnmanagedConflict, Ambiguous, Unknown }
public enum OwnershipProvenance { Protected, Unowned, Invalid }
public sealed record ReconciliationResult(ReconciliationClass Classification, OwnershipProvenance Provenance);

public sealed record LiveResourceEvidence(
    LiveEvidenceKind Kind,
    string? StableId,
    string? CanonicalFingerprint,
    string? ContextBinding,
    bool ManagedClaim = false,
    string? FriendlyName = null);

public sealed record ProtectedOwnershipRecord(
    ResourceKind Kind,
    HostOperation OwningOperation,
    string ProductHostId,
    string StableId,
    string CanonicalFingerprint,
    string OwnershipMarker,
    long Revision,
    string ContextBinding,
    string ProofHash)
{
    public static ProtectedOwnershipRecord Create(ResourceRecord resource, string productHostId, long revision)
    {
        ProtectedOwnershipRecord proof = new(resource.Kind, resource.OwningOperation, productHostId, resource.StableId,
            resource.CanonicalFingerprint, resource.OwnershipMarker, revision, resource.ContextBinding, "");
        return proof with { ProofHash = ComputeHash(proof) };
    }

    public static string ComputeHash(ProtectedOwnershipRecord proof)
    {
        string value = string.Join('|', (int)proof.Kind, (int)proof.OwningOperation, proof.ProductHostId, proof.StableId,
            proof.CanonicalFingerprint, proof.OwnershipMarker, proof.Revision.ToString(CultureInfo.InvariantCulture), proof.ContextBinding);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";
    }
}

public sealed record ReconciliationInput(
    ResourceRecord Desired,
    ProtectedOwnershipRecord? Ownership,
    LiveResourceEvidence Live,
    string ProductHostId,
    long ExpectedRevision);

public static class ReconciliationEngine
{
    public static ReconciliationResult Reconcile(ReconciliationInput? input)
    {
        if (input?.Desired is null || input.Live is null || !ValidDesired(input.Desired) || !Enum.IsDefined(input.Live.Kind) ||
            !CanonicalLedgerValue.IsHostId(input.ProductHostId) || input.ExpectedRevision < 0 ||
            (input.Live.Kind == LiveEvidenceKind.Absent &&
             (input.Live.StableId is not null || input.Live.CanonicalFingerprint is not null || input.Live.ContextBinding is not null)) ||
            (input.Live.Kind is LiveEvidenceKind.Exact or LiveEvidenceKind.Drifted or LiveEvidenceKind.Foreign &&
             (!CanonicalLedgerValue.IsResourceId(input.Live.StableId) || !CanonicalLedgerValue.IsHash(input.Live.CanonicalFingerprint) ||
              !CanonicalLedgerValue.IsContext(input.Live.ContextBinding))))
            return new(ReconciliationClass.Unknown, OwnershipProvenance.Invalid);

        OwnershipProvenance provenance = input.Ownership is null ? OwnershipProvenance.Unowned : OwnershipProvenance.Protected;
        if (input.Live.Kind is LiveEvidenceKind.Unknown or LiveEvidenceKind.AccessDenied or LiveEvidenceKind.PolicyOverride)
            return new(ReconciliationClass.Unknown, provenance);
        if (input.Live.Kind == LiveEvidenceKind.Multiple) return new(ReconciliationClass.Ambiguous, provenance);
        if (input.Ownership is null) return new(input.Live.Kind == LiveEvidenceKind.Absent ? ReconciliationClass.Missing : ReconciliationClass.UnmanagedConflict, provenance);
        if (!ValidProof(input)) return new(ReconciliationClass.Unknown, OwnershipProvenance.Invalid);
        if (input.Live.Kind == LiveEvidenceKind.Absent) return new(ReconciliationClass.Missing, OwnershipProvenance.Protected);

        bool identity = input.Live.StableId == input.Ownership.StableId && input.Live.ContextBinding == input.Ownership.ContextBinding;
        if (!identity || input.Live.Kind == LiveEvidenceKind.Foreign) return new(ReconciliationClass.UnmanagedConflict, OwnershipProvenance.Protected);
        return new(input.Live.Kind == LiveEvidenceKind.Exact && input.Live.CanonicalFingerprint == input.Ownership.CanonicalFingerprint
            ? ReconciliationClass.OwnedConformant : ReconciliationClass.OwnedDrifted, OwnershipProvenance.Protected);
    }

    private static bool ValidDesired(ResourceRecord desired) => Enum.IsDefined(desired.Kind) && Enum.IsDefined(desired.OwningOperation) &&
        CanonicalLedgerValue.OperationMatches(desired.Kind, desired.OwningOperation) && CanonicalLedgerValue.IsResourceId(desired.StableId) &&
        CanonicalLedgerValue.IsHash(desired.CanonicalFingerprint) && CanonicalLedgerValue.IsMarker(desired.OwnershipMarker) &&
        CanonicalLedgerValue.IsContext(desired.ContextBinding);

    private static bool ValidProof(ReconciliationInput input)
    {
        ProtectedOwnershipRecord proof = input.Ownership!;
        ResourceRecord desired = input.Desired;
        return Enum.IsDefined(proof.Kind) && Enum.IsDefined(proof.OwningOperation) && Enum.IsDefined(desired.Kind) && Enum.IsDefined(desired.OwningOperation) &&
            CanonicalLedgerValue.OperationMatches(proof.Kind, proof.OwningOperation) && CanonicalLedgerValue.OperationMatches(desired.Kind, desired.OwningOperation) &&
            CanonicalLedgerValue.IsHostId(proof.ProductHostId) && CanonicalLedgerValue.IsResourceId(proof.StableId) &&
            CanonicalLedgerValue.IsHash(proof.CanonicalFingerprint) && CanonicalLedgerValue.IsMarker(proof.OwnershipMarker) &&
            CanonicalLedgerValue.IsContext(proof.ContextBinding) && CanonicalLedgerValue.IsHash(proof.ProofHash) && proof.Kind == desired.Kind &&
            proof.OwningOperation == desired.OwningOperation && proof.ProductHostId == input.ProductHostId &&
            proof.StableId == desired.StableId && proof.CanonicalFingerprint == desired.CanonicalFingerprint &&
            proof.OwnershipMarker == desired.OwnershipMarker && proof.Revision == input.ExpectedRevision &&
            proof.ContextBinding == desired.ContextBinding && proof.ProofHash == ProtectedOwnershipRecord.ComputeHash(proof);
    }
}

public sealed record AppliedPrimitive(
    string OperationId,
    string PrimitiveId,
    ResourceKind Kind,
    string StableId,
    string PostconditionFingerprint,
    string ContextBinding,
    long Revision,
    bool CreatedByCurrentTransaction);

public sealed record RollbackRequest(
    AppliedPrimitive Primitive,
    string ActiveOperationId,
    long Revision,
    string ContextBinding,
    bool CreationRecordValid,
    bool DependenciesUnchanged,
    bool InCurrentUse,
    bool PolicyBlocked,
    bool ObservationComplete,
    LiveResourceEvidence Live);

public enum RollbackDisposition { RemoveExactCurrentTransactionObject, PreserveNoOwnership, RepairNeeded, Unknown }
public static class RollbackPolicy
{
    public static RollbackDisposition Evaluate(RollbackRequest? request)
    {
        if (request?.Primitive is null || request.Live is null || !Enum.IsDefined(request.Primitive.Kind) || !Enum.IsDefined(request.Live.Kind) ||
            !CanonicalLedgerValue.IsOperationId(request.ActiveOperationId) || !CanonicalLedgerValue.IsContext(request.ContextBinding) || request.Revision < 0)
            return RollbackDisposition.Unknown;
        AppliedPrimitive primitive = request.Primitive;
        if (!primitive.CreatedByCurrentTransaction) return RollbackDisposition.PreserveNoOwnership;
        if (!request.ObservationComplete || request.Live.Kind is LiveEvidenceKind.Unknown or LiveEvidenceKind.AccessDenied) return RollbackDisposition.Unknown;
        bool exact = request.CreationRecordValid && request.DependenciesUnchanged && !request.InCurrentUse && !request.PolicyBlocked &&
            primitive.OperationId == request.ActiveOperationId && primitive.Revision == request.Revision && primitive.ContextBinding == request.ContextBinding &&
            CanonicalLedgerValue.IsPrimitiveId(primitive.PrimitiveId) && CanonicalLedgerValue.IsResourceId(primitive.StableId) &&
            CanonicalLedgerValue.IsHash(primitive.PostconditionFingerprint) && request.Live.Kind == LiveEvidenceKind.Exact &&
            request.Live.StableId == primitive.StableId && request.Live.CanonicalFingerprint == primitive.PostconditionFingerprint &&
            request.Live.ContextBinding == primitive.ContextBinding;
        return exact ? RollbackDisposition.RemoveExactCurrentTransactionObject : RollbackDisposition.RepairNeeded;
    }
}

public enum RemovalPoint { Setup, Repair, ExplicitOwnedRemoval, RecoveryCleanup }
public static class RemovalPolicy
{
    public static bool NotFoundIsSuccess(RemovalPoint point) => Enum.IsDefined(point) && point is RemovalPoint.ExplicitOwnedRemoval or RemovalPoint.RecoveryCleanup;
}

public enum DesiredResourceState { Present, Absent }
public enum ConvergenceDisposition { VerifyNoChange, CreateOnceAfterAuthorization, PreserveUnownedAbsence, RemoveExactOwnedObject, RepairNeeded, Refused, Unknown }
public sealed record ConvergenceResult(ConvergenceDisposition Disposition, int MaximumCreates, bool AdoptsUnmanagedObject, bool ReportsOwnedRemoval);
public static class ConvergencePolicy
{
    public static ConvergenceResult Evaluate(ReconciliationInput input, DesiredResourceState desired)
    {
        ReconciliationResult reconciliation = ReconciliationEngine.Reconcile(input);
        if (reconciliation is null || !Enum.IsDefined(reconciliation.Classification) || !Enum.IsDefined(reconciliation.Provenance) || !Enum.IsDefined(desired))
            return new(ConvergenceDisposition.Unknown, 0, false, false);
        ConvergenceDisposition disposition = (reconciliation.Classification, desired, reconciliation.Provenance) switch
        {
            (ReconciliationClass.OwnedConformant, DesiredResourceState.Present, OwnershipProvenance.Protected) => ConvergenceDisposition.VerifyNoChange,
            (ReconciliationClass.OwnedConformant, DesiredResourceState.Absent, OwnershipProvenance.Protected) => ConvergenceDisposition.RemoveExactOwnedObject,
            (ReconciliationClass.Missing, DesiredResourceState.Present, OwnershipProvenance.Protected or OwnershipProvenance.Unowned) => ConvergenceDisposition.CreateOnceAfterAuthorization,
            (ReconciliationClass.Missing, DesiredResourceState.Absent, OwnershipProvenance.Unowned) => ConvergenceDisposition.PreserveUnownedAbsence,
            (ReconciliationClass.Missing, DesiredResourceState.Absent, OwnershipProvenance.Protected) => ConvergenceDisposition.RemoveExactOwnedObject,
            (ReconciliationClass.OwnedDrifted, _, _) => ConvergenceDisposition.RepairNeeded,
            (ReconciliationClass.UnmanagedConflict or ReconciliationClass.Ambiguous, _, _) => ConvergenceDisposition.Refused,
            _ => ConvergenceDisposition.Unknown,
        };
        return new(disposition, disposition == ConvergenceDisposition.CreateOnceAfterAuthorization ? 1 : 0, false,
            disposition == ConvergenceDisposition.RemoveExactOwnedObject);
    }
}
