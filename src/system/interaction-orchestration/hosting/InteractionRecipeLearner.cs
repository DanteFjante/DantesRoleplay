namespace DantesRoleplay.Interactions;

internal sealed class InteractionRecipeLearner(IInteractionRecipeStore store) : IInteractionRecipeLearner
{
    public async Task RecordUseAsync(InteractionRecipeUseEvidenceDraft draft, CancellationToken cancellationToken = default) =>
        _ = await store.AppendUseEvidenceAsync(draft, cancellationToken);

    public async Task<InteractionRecipeLearningResult> LearnAsync(
        InteractionRecipeLearningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var receipt = request.ExecutionReceipt;
        if (receipt.Kind != "execution" || receipt.Status != "succeeded" || receipt.ResolutionReceiptId is null
            || receipt.Steps is null || receipt.Steps.Count != request.Proposal.Steps.Count
            || receipt.Steps.Any(step => step.Disposition is not ("succeeded" or "replayed")))
            return NotCreated("LEARNING_EXECUTION_INELIGIBLE", "Only a completely successful interaction can be learned.");

        InteractionRecipeTemplate template;
        try
        {
            template = InteractionRecipeTemplate.FromProposal(
                request.Envelope.Host.ApplicationRevision.ApplicationId, request.Proposal);
        }
        catch (InteractionContractException exception)
        {
            return NotCreated(exception.Code, exception.Message);
        }

        var intentFingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/interaction-recipe-intent-evidence/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                intentText = request.Envelope.Intent.IntentText.Normalize(System.Text.NormalizationForm.FormKC).Trim()
            })));
        try
        {
            var result = await store.AppendCandidateAsync(new(
                request.Envelope.Host.ApplicationRevision,
                request.Envelope.Host.EffectiveSetFingerprint,
                template,
                receipt.ResolutionReceiptId,
                receipt.Id,
                request.Envelope.Intent.IntentText,
                intentFingerprint,
                request.Envelope.Host.RoleProfile.StableKey), cancellationToken);
            return result.Disposition switch
            {
                InteractionRecipeWriteDisposition.Created => new(InteractionRecipeLearningDisposition.Created,
                    result.Code, result.Code == "RECIPE_CANDIDATE_CREATED"
                        ? "A reviewable route candidate was created."
                        : "Additional successful evidence was recorded for the route.", result.Recipe),
                InteractionRecipeWriteDisposition.Replayed => new(InteractionRecipeLearningDisposition.Replayed,
                    result.Code, "The route-learning evidence was already recorded.", result.Recipe),
                _ => new(InteractionRecipeLearningDisposition.Conflict, result.Code,
                    "The completed interaction was not learned because its evidence conflicted.")
            };
        }
        catch (InteractionContractException exception)
        {
            return NotCreated(exception.Code, exception.Message);
        }
    }

    private static InteractionRecipeLearningResult NotCreated(string code, string summary) => new(
        InteractionRecipeLearningDisposition.NotCreated, code,
        summary.Length <= 1000 ? summary : summary[..1000]);
}
