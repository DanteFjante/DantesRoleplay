using System.Text.Json;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Interactions;

public enum InteractionExecutionReceiptDisposition
{
    Succeeded,
    Failed,
    Partial,
    Skipped,
    Stale,
    Unauthorized,
    Cancelled,
    TimedOut
}

public enum InteractionExecutionStepDisposition
{
    Succeeded,
    Replayed,
    Failed,
    Skipped
}

public enum InteractionReceiptWriteDisposition
{
    Appended,
    Replay,
    Conflict
}

public sealed record InteractionResolutionReceiptDraft
{
    public InteractionResolutionReceiptDraft(
        AuthorizedInteractionEnvelope envelope,
        InteractionResolutionResult result,
        string? queryFingerprint = null)
    {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        QueryFingerprint = queryFingerprint is null ? null : InteractionGuard.UpperSha256(queryFingerprint, nameof(queryFingerprint));
        if (result.Status == InteractionResolutionStatus.Resolved && result.Proposal is null)
            throw new InteractionContractException("RESOLUTION_PROPOSAL_REQUIRED", "A resolved receipt requires its proposal.");
        if (result.Status != InteractionResolutionStatus.Resolved && result.Proposal is not null)
            throw new InteractionContractException("NON_RESOLUTION_PROPOSAL_FORBIDDEN", "A non-resolution receipt cannot contain a proposal.");
    }

    public AuthorizedInteractionEnvelope Envelope { get; }
    public InteractionResolutionResult Result { get; }
    public string? QueryFingerprint { get; }
}

public sealed record InteractionExecutionStepReceiptDraft
{
    public InteractionExecutionStepReceiptDraft(
        int ordinal,
        string proposalStepId,
        InteractionExecutionStepDisposition disposition,
        string? operationId = null)
    {
        if (ordinal < 1 || ordinal > InteractionContractLimits.ProposalSteps)
            throw new InteractionContractException("INVALID_EXECUTION_STEP_ORDINAL", "The execution step ordinal is outside the closed limit.");
        if (!Enum.IsDefined(disposition))
            throw new InteractionContractException("INVALID_EXECUTION_STEP_DISPOSITION", "The execution step disposition is not supported.");
        Ordinal = ordinal;
        ProposalStepId = InteractionGuard.Identifier(proposalStepId, nameof(proposalStepId));
        Disposition = disposition;
        OperationId = operationId is null ? null : InteractionGuard.Bounded(operationId, 40, "INVALID_OPERATION_ID", nameof(operationId));
    }

    public int Ordinal { get; }
    public string ProposalStepId { get; }
    public InteractionExecutionStepDisposition Disposition { get; }
    public string? OperationId { get; }
}

public sealed record InteractionExecutionReceiptDraft
{
    public InteractionExecutionReceiptDraft(
        InteractionExecutionConsentReference consent,
        string executionRequestFingerprint,
        InteractionExecutionReceiptDisposition disposition,
        string safeSummary,
        IEnumerable<string> evidence,
        IEnumerable<InteractionExecutionStepReceiptDraft> steps)
    {
        Consent = consent ?? throw new ArgumentNullException(nameof(consent));
        ExecutionRequestFingerprint = InteractionGuard.UpperSha256(executionRequestFingerprint, nameof(executionRequestFingerprint));
        if (!Enum.IsDefined(disposition))
            throw new InteractionContractException("INVALID_EXECUTION_RECEIPT_DISPOSITION", "The execution receipt disposition is not supported.");
        Disposition = disposition;
        SafeSummary = InteractionReceiptSafety.SafeSummary(safeSummary);
        Evidence = InteractionReceiptSafety.Evidence(evidence);
        var values = steps?.ToArray() ?? throw new ArgumentNullException(nameof(steps));
        if (values.Length > InteractionContractLimits.ProposalSteps || values.Select(x => x.Ordinal).Distinct().Count() != values.Length ||
            values.Select(x => x.ProposalStepId).Distinct(StringComparer.Ordinal).Count() != values.Length ||
            !values.Select(x => x.Ordinal).Order().SequenceEqual(Enumerable.Range(1, values.Length)))
            throw new InteractionContractException("INVALID_EXECUTION_RECEIPT_STEPS", "Execution receipt steps must be distinct, ordered, and contiguous.");
        Steps = Array.AsReadOnly(values);
    }

    public InteractionExecutionConsentReference Consent { get; }
    public string ExecutionRequestFingerprint { get; }
    public InteractionExecutionReceiptDisposition Disposition { get; }
    public string SafeSummary { get; }
    public IReadOnlyList<string> Evidence { get; }
    public IReadOnlyList<InteractionExecutionStepReceiptDraft> Steps { get; }
}

public sealed record InteractionReceiptProjection(
    string Id,
    string Kind,
    string PrincipalReference,
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string IdempotencyKey,
    string RequestFingerprint,
    string Status,
    string Code,
    string? ProposalFingerprint,
    string SafeSummary,
    IReadOnlyList<string> Evidence,
    DateTime CreatedAtUtc,
    string? ResolutionReceiptId = null,
    IReadOnlyList<InteractionExecutionStepReceiptProjection>? Steps = null,
    InteractionRecipeReference? RecipeReference = null);

public sealed record InteractionExecutionStepReceiptProjection(
    int Ordinal,
    string ProposalStepId,
    string Disposition,
    string? OperationId);

public sealed record InteractionReceiptWriteResult(
    InteractionReceiptWriteDisposition Disposition,
    InteractionReceiptProjection? Receipt,
    string Code)
{
    public static InteractionReceiptWriteResult Appended(InteractionReceiptProjection receipt) => new(InteractionReceiptWriteDisposition.Appended, receipt, "INTERACTION_RECEIPT_APPENDED");
    public static InteractionReceiptWriteResult Replay(InteractionReceiptProjection receipt) => new(InteractionReceiptWriteDisposition.Replay, receipt, "INTERACTION_RECEIPT_REPLAY");
    public static InteractionReceiptWriteResult Conflict() => new(InteractionReceiptWriteDisposition.Conflict, null, "INTERACTION_RECEIPT_IDEMPOTENCY_CONFLICT");
}

public interface IInteractionReceiptStore
{
    Task<InteractionReceiptWriteResult> AppendResolutionAsync(InteractionResolutionReceiptDraft draft, CancellationToken cancellationToken = default);
    Task<InteractionReceiptWriteResult> AppendExecutionAsync(InteractionExecutionReceiptDraft draft, CancellationToken cancellationToken = default);
    Task<InteractionReceiptProjection?> GetAsync(InteractionAuthorizationRequest authorizationRequest, string receiptId, CancellationToken cancellationToken = default);
}

public sealed record InteractionResolutionExecutionAuthority(
    string ResolutionReceiptId,
    string PrincipalReference,
    ApplicationIdentifier ApplicationId,
    int ApplicationRevision,
    string ApplicationFingerprint,
    string StateSpaceId,
    string SessionContextId,
    string StateRevision,
    string EffectiveSetFingerprint,
    string RoleProfile,
    string? ConversationId,
    string? ParentDelegationId,
    string AuthorizationEvidenceReference,
    string ResolutionIdempotencyKey,
    string EnvelopeFingerprint,
    string Status,
    string ProposalFingerprint,
    InteractionRecipeReference? RecipeReference = null);

/// <summary>Freshly authorized internal evidence needed to validate, but never reconstruct, a proposal body.</summary>
public interface IInteractionExecutionAuthorityStore
{
    Task<InteractionResolutionExecutionAuthority?> GetAsync(
        InteractionAuthorizationRequest authorizationRequest,
        string resolutionReceiptId,
        CancellationToken cancellationToken = default);
}

public sealed class InteractionResolutionReceipt
{
    public required string Id { get; set; }
    public required string PrincipalReference { get; set; }
    public required string ApplicationId { get; set; }
    public int ApplicationRevision { get; set; }
    public required string ApplicationFingerprint { get; set; }
    public required string StateSpaceId { get; set; }
    public required string SessionContextId { get; set; }
    public required string StateRevision { get; set; }
    public required string EffectiveSetFingerprint { get; set; }
    public required string RoleProfile { get; set; }
    public string? ConversationId { get; set; }
    public string? ParentDelegationId { get; set; }
    public required string AuthorizationEvidenceReference { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string EnvelopeFingerprint { get; set; }
    public string? QueryFingerprint { get; set; }
    public required string Status { get; set; }
    public required string Code { get; set; }
    public string? ProposalFingerprint { get; set; }
    public required string SafeSummary { get; set; }
    public required string EvidenceJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? RecipeId { get; set; }
    public int? RecipeVersion { get; set; }
    public string? RecipeTemplateFingerprint { get; set; }
}

public sealed class InteractionExecutionReceipt
{
    public required string Id { get; set; }
    public required string ResolutionReceiptId { get; set; }
    public required string PrincipalReference { get; set; }
    public required string ApplicationId { get; set; }
    public required string StateSpaceId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string ExecutionRequestFingerprint { get; set; }
    public required string ProposalFingerprint { get; set; }
    public required string Disposition { get; set; }
    public required string SafeSummary { get; set; }
    public required string EvidenceJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<InteractionExecutionReceiptStep> Steps { get; } = new List<InteractionExecutionReceiptStep>();
}

public sealed class InteractionExecutionReceiptStep
{
    public required string ExecutionReceiptId { get; set; }
    public int Ordinal { get; set; }
    public required string ProposalStepId { get; set; }
    public required string Disposition { get; set; }
    public string? OperationId { get; set; }
    public InteractionExecutionReceipt? ExecutionReceipt { get; set; }
}

public static class InteractionReceiptSafety
{
    public static string SafeSummary(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == 0 ? string.Empty : InteractionGuard.Bounded(value, InteractionContractLimits.SafeEvidenceText, "INVALID_SAFE_SUMMARY", nameof(value));
    }

    public static IReadOnlyList<string> Evidence(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = values.Select(value => InteractionGuard.Bounded(value, InteractionContractLimits.SafeEvidenceText, "INVALID_SAFE_EVIDENCE", nameof(values))).ToArray();
        if (result.Length > InteractionContractLimits.EvidenceItems)
            throw new InteractionContractException("INVALID_SAFE_EVIDENCE", "The evidence collection exceeds the closed limit.");
        return Array.AsReadOnly(result);
    }

    public static string SerializeEvidence(IEnumerable<string> values) => InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(Evidence(values)));

    public static IReadOnlyList<string> DeserializeEvidence(string json)
    {
        var canonical = InteractionCanonicalJson.Canonicalize(json);
        using var document = JsonDocument.Parse(canonical);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
            throw new InvalidOperationException("Stored interaction receipt evidence is invalid.");
        return Evidence(document.RootElement.EnumerateArray().Select(value => value.GetString()!));
    }
}

public static class InteractionReceiptIds
{
    public static string New() => "interaction-receipt." + Guid.NewGuid().ToString("n");

    public static string Require(string value, string parameter)
    {
        value = InteractionGuard.Identifier(value, parameter);
        if (value.Length != 52 || !value.StartsWith("interaction-receipt.", StringComparison.Ordinal) ||
            value[20..].Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw new InteractionContractException("INVALID_RECEIPT_ID", "The interaction receipt id is invalid.", parameter);
        return value;
    }
}
