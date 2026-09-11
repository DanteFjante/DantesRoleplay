using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DantesRoleplay.ApplicationActivation;

// Coordinator review proposal. These interfaces are deliberately unregistered and unimplemented.
// They extend the activation owner's history; they do not constitute another active registry.
public sealed record ApplicationCandidateReference(
    ApplicationIdentifier ApplicationId, string CandidateId, int Revision, string ContentFingerprint);

public sealed record ApplicationCandidateLookup(string CandidateId, int Revision);

public sealed record ApplicationCandidateDocumentInput(
    string LogicalIdentity, string SourceId, string RelativePath, string MediaType, string Text);

/// <summary>Exact dependency identity resolved by the owning service, including absent dependencies as invalid evidence.</summary>
public sealed record ApplicationCandidateDependency(string DefinitionId, int Revision, string ContentFingerprint);

public sealed record ApplicationCandidateDocument(ActivatedApplicationDocument Document, byte[] RetainedBytes);

/// <summary>
/// CandidateId is a 32-character lowercase operation-derived identity. ExpectedCandidateRevision=0
/// creates it; positive appends to that exact latest candidate revision. Null active fingerprint
/// explicitly expects no active generation. The candidate never changes the active pointer.
/// Documents are replacements in the pinned base generation; v1 proposes no implicit deletion.
/// Origin is runtime or catalog-sync; catalog-sync requires an explicit export/compare receipt.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationCandidateWriteRequest(
    [property: JsonRequired] string CandidateId,
    [property: JsonRequired] int ExpectedCandidateRevision,
    [property: JsonRequired] string? ExpectedActiveFingerprint,
    string Origin, string? SynchronizationEvidenceReference, string NewImplementationReason,
    IReadOnlyList<ApplicationCandidateDocumentInput> Documents);

/// <summary>
/// Immutable readback from existing retained document identity/evidence, pinned to an application
/// revision and expected active generation. GrantReference records authoring provenance only.
/// EffectiveDocuments includes inherited base winners plus replacements; no mixed resolution.
/// Snapshot instances passed to validators are materialized by the owner, never trusted from JSON.
/// </summary>
[JsonConverter(typeof(RejectApplicationCandidateSnapshotJsonConverter))]
public sealed record ApplicationCandidateSnapshot(
    ApplicationCandidateReference Candidate, int ApplicationRevision, string ApplicationFingerprint,
    string? ExpectedActiveFingerprint, string Origin, string? SynchronizationEvidenceReference,
    string NewImplementationReason, string AuthorGrantReference, string SourceOperationId,
    IReadOnlyList<ApplicationCandidateDocument> EffectiveDocuments,
    IReadOnlyList<ApplicationCandidateDependency> Dependencies);

public sealed class RejectApplicationCandidateSnapshotJsonConverter : JsonConverter<ApplicationCandidateSnapshot>
{
    public override ApplicationCandidateSnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Candidate snapshots must be materialized from retained evidence by the activation owner.");
    public override void Write(Utf8JsonWriter writer, ApplicationCandidateSnapshot value, JsonSerializerOptions options) =>
        throw new JsonException("Internal candidate snapshots are not a transport result; return bounded inspection data.");
}

[JsonConverter(typeof(ApplicationCandidateCheckStatusJsonConverter))]
public enum ApplicationCandidateCheckStatus { Valid, Invalid, Unavailable }

public sealed class ApplicationCandidateCheckStatusJsonConverter()
    : JsonStringEnumConverter<ApplicationCandidateCheckStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record ApplicationCandidateDiagnostic(string Code, string Target, string Message);

/// <summary>
/// Valid requires a durable evidence reference and exact fingerprint/dependency pins. Syntax-only
/// preparation is insufficient: contracts, services/effects and bounded samples must be validated.
/// No missing dependency may be represented as an empty successful check.
/// EvidenceReference must resolve the exact preparation policy version; historical v1 activation
/// metadata is not evidence that the candidate passed a subsequently tightened preparation policy.
/// </summary>
public sealed record ApplicationCandidateCheckResult(
    ApplicationCandidateCheckStatus Status, string CandidateFingerprint, string DependencyGraphFingerprint,
    string? EvidenceReference, IReadOnlyList<ApplicationCandidateDiagnostic> Diagnostics);

public sealed record ApplicationDefinitionAlternative(
    string DefinitionId, int Revision, string ContentFingerprint, string Reason);

/// <summary>
/// Discovery supplies alternatives, not equivalence or publication approval. Valid additionally
/// requires the candidate-bound review of those alternatives and NewImplementationReason.
/// DiscoveryFingerprint pins the accepted InteractionManualContextPacket.ResultFingerprint;
/// neither its SelectedAction nor its ReuseDecision can substitute for this review.
/// </summary>
public sealed record ApplicationCandidateReuseResult(
    ApplicationCandidateCheckStatus Status, string CandidateFingerprint, string DiscoveryFingerprint,
    string? EvidenceReference, IReadOnlyList<ApplicationDefinitionAlternative> Alternatives,
    IReadOnlyList<ApplicationCandidateDiagnostic> Diagnostics);

public interface IApplicationCandidatePreparation
{
    Task<ApplicationCandidateCheckResult> ValidateAsync(InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate, CancellationToken cancellationToken = default);
}

public interface IApplicationCandidateReuseReview
{
    Task<ApplicationCandidateReuseResult> ReviewAsync(InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate, CancellationToken cancellationToken = default);
}

public sealed record ApplicationCandidateActivationRequest(
    ApplicationCandidateReference Candidate, string ValidationOperationId);

public interface IApplicationAuthoringService
{
    // Each mutation returns the existing committed receipt envelope for that specific mutation.
    // Retaining a draft/validation is not activation. Inspect resolves the source operation receipt
    // to candidate/validation/disposition data in a bounded completed result, preserving the common
    // envelope's rule that committed results have no dataJson. Failed validation is inspectable.
    Task<InteractionInvocationResult> WriteCandidateAsync(InteractionInvocationHost host,
        ApplicationCandidateWriteRequest request, CancellationToken cancellationToken = default);
    Task<InteractionInvocationResult> InspectAsync(InteractionInvocationHost host,
        ApplicationCandidateLookup candidate, CancellationToken cancellationToken = default);
    Task<InteractionInvocationResult> ValidateAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken = default);
    Task<InteractionInvocationResult> ActivateAsync(InteractionInvocationHost host,
        ApplicationCandidateActivationRequest request, CancellationToken cancellationToken = default);
    // Recovery selects host.ApplicationRevision.ApplicationId and copies a retained generation
    // into a fresh inert candidate with a fresh expectation.
    // It must subsequently pass normal validation/activation and never undoes committed state data.
    Task<InteractionInvocationResult> RecoverAsync(InteractionInvocationHost host, int activationRevision,
        string? expectedActiveFingerprint, CancellationToken cancellationToken = default);
}

public static class ApplicationAuthoringLimits
{
    // Entire canonical request/evidence JSON also obeys foundation 64 KiB/depth 32, duplicate-key
    // rejection. Large retained generations use existing 10 MiB/document, 256 MiB/generation limits;
    // v1 inline authored text is intentionally smaller and has no upload/transport bypass.
    public const int DocumentsPerWrite = 16;
    public const int Dependencies = 64;
    public const int Alternatives = 16;
    public const int Diagnostics = 16;
    public const int DiagnosticCharacters = 500;
    public const int ReasonCharacters = 2000;
}
