using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

internal sealed class InteractionGateway(
    IInteractionFeatureRetriever features,
    IInteractionEnvelopeFactory envelopes,
    IInteractionPlanner planner,
    IInteractionProposalVerifier verifier,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IInteractionReceiptStore receipts,
    IInteractionExecutionCoordinator execution) : IInteractionGateway
{
    public Task<InteractionFeatureSearchResult> SearchFeaturesAsync(
        ApplicationIdentifier applicationId,
        string? query,
        string? qualifiedId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var hasId = !string.IsNullOrWhiteSpace(qualifiedId);
        if (hasQuery == hasId)
            throw new InteractionContractException("FEATURE_LOOKUP_SELECTOR_REQUIRED",
                "Specify exactly one of query or qualifiedId.");
        return features.SearchAsync(
            new(applicationId, InteractionRetrievalLane.TrustedFeature),
            new(hasId ? qualifiedId! : query!, limit),
            cancellationToken);
    }

    public async Task<InteractionPlanGatewayResult> PlanAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string sessionContextId,
        string intentJson,
        string? submittedProposalJson = null,
        string? conversationId = null,
        InteractionAiRole role = InteractionAiRole.Outer,
        string? parentDelegationId = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = envelopes.Create(principal, applicationId, stateSpaceId, sessionContextId,
            intentJson, role, conversationId, parentDelegationId);
        var authorization = new InteractionAuthorizationRequest(principal, applicationId, stateSpaceId,
            InteractionCapability.Plan, "system.interaction-plan");

        if (string.IsNullOrWhiteSpace(submittedProposalJson))
        {
            var plannerKind = envelope.Intent.PlannerPreference == InteractionPlannerPreference.Remote
                ? InteractionPlannerKind.Remote
                : InteractionPlannerKind.Local;
            var outcome = await planner.PlanAsync(envelope, authorization, plannerKind, cancellationToken);
            return Project(outcome.Result, outcome.Receipt, outcome.TraceFingerprint);
        }

        var command = InteractionPlannerCommand.Parse(submittedProposalJson) as InteractionPlannerProposalCommand
            ?? throw new InteractionContractException("PROPOSAL_COMMAND_REQUIRED",
                "The submitted proposal must be a closed propose command.");
        var result = verifier.Verify(new(envelope, InspectCurrent(envelope, command), command));
        var trace = InteractionCanonicalJson.Fingerprint("dantes-roleplay/interaction-submitted-proposal/v1",
            InteractionCanonicalJson.CanonicalizeObject(submittedProposalJson));
        var receipt = await receipts.AppendResolutionAsync(new(envelope, result, trace), CancellationToken.None);
        return Project(result, receipt, trace);
    }

    public Task<InteractionReceiptProjection?> GetReceiptAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string receiptId,
        CancellationToken cancellationToken = default) =>
        receipts.GetAsync(new(principal, applicationId, stateSpaceId,
            InteractionCapability.ReadReceipt, "system.interaction-receipt"), receiptId, cancellationToken);

    public Task<InteractionExecutionOutcome> ExecuteAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string executionRequestJson,
        CancellationToken cancellationToken = default)
    {
        var request = ParseExecution(executionRequestJson);
        return execution.ExecuteAsync(request,
            new(principal, applicationId, stateSpaceId, InteractionCapability.Execute,
                "system.interaction-execute"), cancellationToken);
    }

    private static InteractionExecutionRequest ParseExecution(string json)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        ExactProperties(root, "resolutionReceiptId", "proposalFingerprint", "idempotencyKey", "proposal", "stopOnFailure", "learn", "learningIntent");
        if (!root.TryGetProperty("proposal", out var proposal) || proposal.ValueKind != JsonValueKind.Object)
            throw new InteractionContractException("EXECUTION_PROPOSAL_REQUIRED", "Execution requires the full inert proposal.");
        if (root.TryGetProperty("stopOnFailure", out var stop)
            && (stop.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !stop.GetBoolean()))
            throw new InteractionContractException("STOP_ON_FAILURE_REQUIRED", "The initial executor requires stopOnFailure true.");
        var command = InteractionPlannerCommand.Parse(proposal.GetRawText()) as InteractionPlannerProposalCommand
            ?? throw new InteractionContractException("PROPOSAL_COMMAND_REQUIRED", "Execution requires a closed propose command.");
        var learn = root.TryGetProperty("learn", out var learnValue)
            ? learnValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? learnValue.GetBoolean()
                : throw new InteractionContractException("INVALID_EXECUTION_REQUEST", "Execution request field 'learn' must be boolean.")
            : false;
        InteractionIntent? learningIntent = null;
        if (root.TryGetProperty("learningIntent", out var intentValue))
        {
            if (intentValue.ValueKind != JsonValueKind.Object)
                throw new InteractionContractException("INVALID_EXECUTION_REQUEST", "Execution request field 'learningIntent' must be an object.");
            learningIntent = InteractionIntent.Parse(intentValue.GetRawText());
        }
        return new(
            RequiredString(root, "resolutionReceiptId"),
            RequiredString(root, "proposalFingerprint"),
            RequiredString(root, "idempotencyKey"),
            command,
            stopOnFailure: true,
            learn,
            learningIntent);
    }

    private static void ExactProperties(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InteractionContractException("INVALID_EXECUTION_REQUEST", "Execution request must be an object.");
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        var unexpected = element.EnumerateObject().Select(property => property.Name)
            .FirstOrDefault(name => !set.Contains(name));
        if (unexpected is not null)
            throw new InteractionContractException("INVALID_EXECUTION_REQUEST",
                $"Execution request field '{unexpected}' is not supported.");
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InteractionContractException("INVALID_EXECUTION_REQUEST",
                $"Execution request field '{property}' is required.");
        return value.GetString()!;
    }

    private static InteractionPlanGatewayResult Project(
        InteractionResolutionResult result,
        InteractionReceiptWriteResult receipt,
        string traceFingerprint) =>
        new(result.Status, result.Code, result.SafeSummary, result.Evidence,
            result.Proposal?.Fingerprint, result.Proposal is null ? null : ToProjection(result.Proposal),
            receipt, traceFingerprint, result.RecipeReference);

    private static InteractionProposalProjection ToProjection(InteractionProposal proposal) =>
        new("propose", Array.AsReadOnly(proposal.Steps.Select(step => new InteractionProposalStepProjection(
            step.StepId, step.Kind.ToString().ToLowerInvariant(), step.Contract.QualifiedKey,
            step.Contract.Version, step.Contract.Fingerprint, step.DependsOn, step.RoleBindings,
            JsonSerializer.Deserialize<JsonElement>(step.InputJson))).ToArray()));

    private IReadOnlyList<InteractionInspectedFeature> InspectCurrent(
        AuthorizedInteractionEnvelope envelope,
        InteractionPlannerProposalCommand command)
    {
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot))
            return [];
        var inspected = new List<InteractionInspectedFeature>();
        foreach (var qualifiedId in command.Steps.Select(step => step.QualifiedId).Distinct(StringComparer.Ordinal))
        {
            var document = snapshot.Documents.SingleOrDefault(value =>
                value.Trust == SourceTrust.Trusted && value.Record.QualifiedId == qualifiedId);
            if (document is null) continue;
            var reference = InteractionFeatureReference.Create(envelope.Host.ApplicationRevision.ApplicationId,
                InteractionRetrievalLane.TrustedFeature, snapshot.Manifest.Fingerprint, document.Record);
            var hit = InteractionFeatureHit.Create(reference, document.Record, null, null, exact: true);
            inspected.Add(new(hit, document.Record.ContentJson));
        }
        return inspected.AsReadOnly();
    }
}
