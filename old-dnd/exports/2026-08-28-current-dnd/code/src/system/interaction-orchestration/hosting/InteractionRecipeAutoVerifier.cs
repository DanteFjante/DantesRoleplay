using DantesRoleplay.Interactions;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class InteractionRecipeAutoVerifier(
    IInteractionRecipeAutoVerificationEvidenceReader evidence,
    IInteractionRecipeStore store,
    IInteractionRecipeReviewService reviews) : IInteractionRecipeAutoVerifier
{
    public async Task<InteractionRecipeLearningResult> VerifyAsync(
        InteractionRecipeAutoVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var eligibility = await evidence.ValidateAsync(request, cancellationToken);
        if (!eligibility.Eligible)
            return new(InteractionRecipeLearningDisposition.Created, eligibility.Code,
                "The route candidate remains inert and available for private review.", request.Candidate);

        var recipe = await store.GetAsync(request.ExecutionReceipt.ApplicationId,
            request.Candidate.Id, cancellationToken);
        if (recipe is null || recipe.Reference.TemplateFingerprint != request.Candidate.TemplateFingerprint)
            return Failed(request.Candidate, "The learned route candidate is unavailable.");
        if (recipe.Status == InteractionRecipeStatus.Verified)
            return new(InteractionRecipeLearningDisposition.Replayed, "RECIPE_AUTO_VERIFIED",
                "The outer-fallback route was already verified.", recipe.Reference);
        if (recipe.Status != InteractionRecipeStatus.Candidate)
            return Failed(recipe.Reference, "The learned route is no longer eligible for verification.");

        var suffix = request.ExecutionReceipt.Id[(request.ExecutionReceipt.Id.LastIndexOf('.') + 1)..];
        var result = await reviews.ReviewAsync(new(
            "auto:" + suffix,
            recipe.ApplicationId,
            recipe.Reference.Id,
            recipe.Reference.Version,
            "verify",
            InteractionRecipeProtocol.AutoVerificationReason,
            InteractionRecipeProtocol.AutoVerifierPrincipal), cancellationToken);
        return result.Disposition switch
        {
            InteractionRecipeWriteDisposition.Created => new(InteractionRecipeLearningDisposition.Created,
                "RECIPE_AUTO_VERIFIED", "The successful outer-fallback route was verified for later inner use.",
                result.Recipe),
            InteractionRecipeWriteDisposition.Replayed => new(InteractionRecipeLearningDisposition.Replayed,
                "RECIPE_AUTO_VERIFIED", "The successful outer-fallback route verification was replayed.",
                result.Recipe),
            _ => Failed(recipe.Reference, "The route candidate could not be automatically verified.")
        };
    }

    private static InteractionRecipeLearningResult Failed(InteractionRecipeReference recipe, string summary) =>
        new(InteractionRecipeLearningDisposition.Created, "RECIPE_AUTO_VERIFICATION_FAILED", summary, recipe);
}
