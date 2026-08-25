using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Sources;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class InteractionRecipeReviewService(
    IInteractionRecipeStore store,
    IInteractionRecipeProvenanceReader provenance,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IActiveCatalogFeatureSnapshotProvider snapshots) : IInteractionRecipeReviewService
{
    public async Task<InteractionRecipeWriteResult> ReviewAsync(
        InteractionRecipeReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var replay = await store.GetReviewReplayAsync(request, cancellationToken);
        if (replay is not null) return replay;
        if (request.Decision == "retire") return await store.ReviewAsync(request, cancellationToken);
        if (request.Decision != "verify")
            throw new InteractionContractException("INVALID_RECIPE_REVIEW_DECISION", "The recipe review decision is not supported.");
        var recipe = await store.GetAsync(request.ApplicationId, request.RecipeId, cancellationToken);
        if (recipe is null) return Conflict("RECIPE_NOT_FOUND");
        if (recipe.Reference.Version != request.ExpectedVersion || recipe.Status != InteractionRecipeStatus.Candidate)
            return Conflict("RECIPE_VERSION_CONFLICT");
        var application = applications.Get(request.ApplicationId);
        var activation = activations.Current(request.ApplicationId);
        if (application is null || application.Revision != recipe.ApplicationRevision
            || application.Fingerprint != recipe.ApplicationFingerprint
            || activation is null || activation.ApplicationRevision != application.Revision
            || activation.ApplicationFingerprint != application.Fingerprint
            || activation.ActivationFingerprint != recipe.EffectiveSetFingerprint)
        {
            if (application is not null && activation is not null)
                await store.MarkStaleAsync(new(recipe.Reference, application,
                    activation.ActivationFingerprint, "Application or activation authority changed before review."), cancellationToken);
            return Conflict("RECIPE_AUTHORITY_STALE");
        }
        if (!snapshots.TryGetSnapshot(request.ApplicationId, out var snapshot))
            return Conflict("RECIPE_CATALOG_UNAVAILABLE");
        foreach (var step in recipe.Template.Steps)
        {
            var record = snapshot.Documents.SingleOrDefault(value => value.Trust == SourceTrust.Trusted
                && value.Record.Kind == "mechanic" && value.Record.Status == "active"
                && value.Record.QualifiedId == step.QualifiedId
                && value.Record.Version == step.ContractVersion
                && value.Record.ContentFingerprint == step.ContractFingerprint)?.Record;
            if (record is null)
            {
                await store.MarkStaleAsync(new(recipe.Reference, application,
                    activation.ActivationFingerprint, "A referenced mechanic contract changed before review."), cancellationToken);
                return Conflict("RECIPE_CONTRACT_STALE");
            }
            if (!RolesMatch(record.ContentJson, step.RoleSlots))
                return Conflict("RECIPE_TEMPLATE_UNSAFE");
        }
        var evidence = await provenance.ValidateAsync(recipe, cancellationToken);
        if (!evidence.Valid) return Conflict(evidence.Code);
        return await store.ReviewAsync(request, cancellationToken);
    }

    private static bool RolesMatch(string contentJson, IReadOnlyList<string> slots)
    {
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (!document.RootElement.TryGetProperty("requirements", out var value)
                || value.ValueKind != JsonValueKind.String) return false;
            var requirements = MechanicRequirements.Parse(value.GetString()!);
            if (requirements.Event is not null || requirements.ProjectionProblems().Count > 0
                || requirements.CompositionProblems().Count > 0) return false;
            var supplied = slots.ToHashSet(StringComparer.Ordinal);
            return supplied.All(requirements.Roles.ContainsKey)
                && requirements.Roles.Where(item => !item.Value.Optional).All(item => supplied.Contains(item.Key));
        }
        catch { return false; }
    }

    private static InteractionRecipeWriteResult Conflict(string code) =>
        new(InteractionRecipeWriteDisposition.Conflict, null, code);
}
