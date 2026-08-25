using DantesRoleplay.ApplicationExecution;

namespace DantesRoleplay.Interactions;

public static class InteractionExecutionProtocol
{
    public const string RequestFingerprintDomain = "dantes-roleplay/interaction-execution-request/v1";
    public const string StepFingerprintDomain = "dantes-roleplay/interaction-execution-step/v1";
}

public sealed record InteractionExecutionRequest
{
    public InteractionExecutionRequest(
        string resolutionReceiptId,
        string proposalFingerprint,
        string idempotencyKey,
        InteractionPlannerProposalCommand proposal,
        bool stopOnFailure = true,
        bool learn = false,
        InteractionIntent? learningIntent = null)
    {
        ResolutionReceiptId = InteractionReceiptIds.Require(resolutionReceiptId, nameof(resolutionReceiptId));
        ProposalFingerprint = InteractionGuard.UpperSha256(proposalFingerprint, nameof(proposalFingerprint));
        IdempotencyKey = InteractionGuard.IdempotencyKey(idempotencyKey);
        Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
        if (!stopOnFailure)
            throw new InteractionContractException("STOP_ON_FAILURE_REQUIRED", "The initial executor requires stopOnFailure true.");
        StopOnFailure = true;
        if (learn && learningIntent is null)
            throw new InteractionContractException("LEARNING_INTENT_REQUIRED", "learningIntent is required when route learning is requested.");
        if (!learn && learningIntent is not null)
            throw new InteractionContractException("LEARNING_INTENT_FORBIDDEN", "learningIntent is allowed only when route learning is requested.");
        Learn = learn;
        LearningIntent = learningIntent;
    }

    public string ResolutionReceiptId { get; }
    public string ProposalFingerprint { get; }
    public string IdempotencyKey { get; }
    public InteractionPlannerProposalCommand Proposal { get; }
    public bool StopOnFailure { get; }
    public bool Learn { get; }
    public InteractionIntent? LearningIntent { get; }
}

public sealed record InteractionExecutionOutcome(
    InteractionExecutionReceiptDisposition Disposition,
    string Code,
    string SafeSummary,
    IReadOnlyList<ApplicationActionExecutionResult> ActionResults,
    InteractionReceiptWriteResult? Receipt,
    string ExecutionRequestFingerprint,
    InteractionRecipeLearningResult? Learning = null)
{
    public bool Successful => Disposition == InteractionExecutionReceiptDisposition.Succeeded
        && Receipt?.Receipt is not null;
}

public interface IInteractionExecutionCoordinator
{
    Task<InteractionExecutionOutcome> ExecuteAsync(
        InteractionExecutionRequest request,
        InteractionAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken = default);
}
