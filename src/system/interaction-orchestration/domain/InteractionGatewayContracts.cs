using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using System.Text.Json;

namespace DantesRoleplay.Interactions;

/// <summary>Application-neutral host boundary used by MCP and private web adapters.</summary>
public interface IInteractionGateway
{
    Task<InteractionFeatureSearchResult> SearchFeaturesAsync(
        ApplicationIdentifier applicationId,
        string? query,
        string? qualifiedId,
        int limit = 10,
        string? namespaceId = null,
        CancellationToken cancellationToken = default);

    Task<InteractionPlanGatewayResult> PlanAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string sessionContextId,
        string intentJson,
        string? submittedProposalJson = null,
        string? conversationId = null,
        InteractionAiRole role = InteractionAiRole.Outer,
        string? parentDelegationId = null,
        CancellationToken cancellationToken = default);

    Task<InteractionReceiptProjection?> GetReceiptAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string receiptId,
        CancellationToken cancellationToken = default);

    Task<InteractionExecutionOutcome> ExecuteAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string executionRequestJson,
        CancellationToken cancellationToken = default);
}

public sealed record InteractionPlanGatewayResult(
    InteractionResolutionStatus Status,
    string Code,
    string SafeSummary,
    IReadOnlyList<string> Evidence,
    string? ProposalFingerprint,
    InteractionProposalProjection? Proposal,
    InteractionReceiptWriteResult Receipt,
    string TraceFingerprint,
    InteractionRecipeReference? RecipeReference = null);

public sealed record InteractionProposalProjection(
    string Command,
    IReadOnlyList<InteractionProposalStepProjection> Steps);

public sealed record InteractionProposalStepProjection(
    string StepId,
    string Kind,
    string QualifiedId,
    int Version,
    string Fingerprint,
    IReadOnlyList<string> DependsOn,
    IReadOnlyDictionary<string, string> RoleBindings,
    JsonElement Input,
    IReadOnlyList<InteractionResultBinding> ResultBindings);
