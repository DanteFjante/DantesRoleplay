using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.Retrieval;
using System.Security.Cryptography;
using System.Text;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class VerifiedInteractionRecipeResolver(
    IInteractionRecipeStore recipes,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IInteractionProposalVerifier verifier,
    ITextEmbeddingProvider? embeddings = null,
    IInteractionDerivedVectorIndex? vectors = null) : IVerifiedInteractionRecipeResolver
{
    public async Task<VerifiedInteractionRecipeResolution?> ResolveAsync(
        AuthorizedInteractionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var current = await CurrentMatch(envelope, cancellationToken);
        if (current is null) return null;
        var recipe = current.Recipe;
        var snapshot = current.Snapshot;
        if (recipe.Template.Steps.Any(step => step.InputBindings.Count > 0)) return null;

        var inspected = new List<InteractionInspectedFeature>();
        var draftSteps = new List<InteractionPlannerDraftStep>();
        foreach (var step in recipe.Template.Steps)
        {
            var record = snapshot.Documents.SingleOrDefault(document => document.Trust == SourceTrust.Trusted
                && document.Record.Kind == (step.Kind == InteractionPlanStepKind.Query
                    ? ApplicationQueryContract.CatalogKind : "mechanic") && document.Record.Status == "active"
                && document.Record.QualifiedId == step.QualifiedId
                && document.Record.Version == step.ContractVersion
                && document.Record.ContentFingerprint == step.ContractFingerprint)?.Record;
            if (record is null)
            {
                await MarkStale(recipe, envelope, "A referenced mechanic contract changed.", cancellationToken);
                return null;
            }
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var slot in step.RoleSlots)
            {
                if (!envelope.Intent.RoleHints.TryGetValue(slot, out var entityReference)) return null;
                bindings.Add(slot, entityReference);
            }
            var reference = InteractionFeatureReference.Create(envelope.Host.ApplicationRevision.ApplicationId,
                InteractionRetrievalLane.TrustedFeature, snapshot.Manifest.Fingerprint, record);
            inspected.Add(new(InteractionFeatureHit.Create(reference, record, null, null, exact: true), record.ContentJson));
            draftSteps.Add(new(step.StepId, step.Kind, step.QualifiedId,
                step.ContractVersion, step.ContractFingerprint, step.DependsOn, bindings, "{}"));
        }
        var result = verifier.Verify(new(envelope, inspected.AsReadOnly(),
            new InteractionPlannerProposalCommand(draftSteps.AsReadOnly())));
        return result.Status == InteractionResolutionStatus.Resolved && result.Proposal is not null
            ? new(result.Proposal, recipe.Reference) : null;
    }

    public async Task<VerifiedInteractionRecipeGuidance?> GuideAsync(
        AuthorizedInteractionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var current = await CurrentMatch(envelope, cancellationToken);
        if (current is null) return null;
        var required = current.Recipe.Template.Steps.SelectMany(step => step.RoleSlots)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (required.All(envelope.Intent.RoleHints.ContainsKey)) return null;
        return new(current.Recipe.Reference, current.Recipe.Template.Steps);
    }

    private async Task<CurrentRecipe?> CurrentMatch(
        AuthorizedInteractionEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var found = await recipes.SearchAsync(envelope.Host.ApplicationRevision.ApplicationId,
            envelope.Intent.IntentText, InteractionRecipeStatus.Verified, 2, cancellationToken);
        if (found.Count == 0)
            found = await VectorMatches(envelope, cancellationToken);
        if (found.Count != 1) return null;
        var recipe = found[0];
        if (recipe.ApplicationRevision != envelope.Host.ApplicationRevision.Revision
            || recipe.ApplicationFingerprint != envelope.Host.ApplicationRevision.Fingerprint
            || recipe.EffectiveSetFingerprint != envelope.Host.EffectiveSetFingerprint)
        {
            await MarkStale(recipe, envelope, "Application or activation authority changed.", cancellationToken);
            return null;
        }
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot))
            return null;
        foreach (var step in recipe.Template.Steps)
        {
            var record = snapshot.Documents.SingleOrDefault(document => document.Trust == SourceTrust.Trusted
                && document.Record.Kind == (step.Kind == InteractionPlanStepKind.Query
                    ? ApplicationQueryContract.CatalogKind : "mechanic") && document.Record.Status == "active"
                && document.Record.QualifiedId == step.QualifiedId
                && document.Record.Version == step.ContractVersion
                && document.Record.ContentFingerprint == step.ContractFingerprint)?.Record;
            if (record is not null) continue;
            await MarkStale(recipe, envelope, "A referenced mechanic contract changed.", cancellationToken);
            return null;
        }
        return new(recipe, snapshot);
    }

    private async Task<IReadOnlyList<InteractionRecipeProjection>> VectorMatches(
        AuthorizedInteractionEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (embeddings is null || vectors is null
            || !snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot)) return [];
        try
        {
            var candidates = await recipes.ListAsync(envelope.Host.ApplicationRevision.ApplicationId,
                InteractionRecipeStatus.Verified, 50, cancellationToken);
            if (candidates.Count == 0) return [];
            var safe = new List<(InteractionRecipeProjection Recipe, InteractionFeatureReference Reference, string Text)>();
            foreach (var recipe in candidates)
            {
                var text = new List<string>();
                var current = true;
                foreach (var step in recipe.Template.Steps)
                {
                    var record = snapshot.Documents.SingleOrDefault(value => value.Trust == SourceTrust.Trusted
                        && value.Record.Kind == (step.Kind == InteractionPlanStepKind.Query
                            ? ApplicationQueryContract.CatalogKind : "mechanic") && value.Record.Status == "active"
                        && value.Record.QualifiedId == step.QualifiedId
                        && value.Record.Version == step.ContractVersion
                        && value.Record.ContentFingerprint == step.ContractFingerprint)?.Record;
                    if (record is null) { current = false; break; }
                    text.Add(record.Name);
                    text.Add(record.Description);
                    text.AddRange(step.RoleSlots);
                }
                if (!current) continue;
                var catalogFingerprint = RecipeGenerationFingerprint(envelope, candidates);
                safe.Add((recipe, new InteractionFeatureReference(envelope.Host.ApplicationRevision.ApplicationId,
                    InteractionRetrievalLane.TrustedRecipe, catalogFingerprint, "recipe", recipe.Reference.Id,
                    recipe.Reference.Version, recipe.Reference.TemplateFingerprint), string.Join(' ', text)));
            }
            if (safe.Count == 0) return [];
            var status = await embeddings.CheckAsync(cancellationToken);
            if (!status.Ready || status.Identity is null) return [];
            var generationFingerprint = safe[0].Reference.CatalogFingerprint;
            var generation = new InteractionRetrievalGeneration(
                InteractionRetrievalFingerprint.GenerationKey(envelope.Host.ApplicationRevision.ApplicationId,
                    InteractionRetrievalLane.TrustedRecipe, generationFingerprint, status.Identity),
                envelope.Host.ApplicationRevision.ApplicationId, InteractionRetrievalLane.TrustedRecipe,
                generationFingerprint, InteractionRetrievalFingerprint.FormatVersion, status.Identity);
            var documents = new List<InteractionVectorDocument>();
            foreach (var batch in safe.Chunk(32))
            {
                var result = await embeddings.EmbedAsync(batch.Select(value => value.Text).ToArray(), cancellationToken);
                if (!result.Ok || result.Identity != status.Identity || result.Vectors.Count != batch.Length) return [];
                for (var index = 0; index < batch.Length; index++)
                    documents.Add(InteractionVectorDocument.Create(batch[index].Reference,
                        batch[index].Text, result.Vectors[index]));
            }
            await vectors.ReplaceAsync(generation, documents, cancellationToken);
            var query = await embeddings.EmbedAsync([envelope.Intent.IntentText], cancellationToken);
            if (!query.Ok || query.Identity != status.Identity || query.Vectors.Count != 1) return [];
            var hits = await vectors.SearchAsync(generation, query.Vectors[0], 2, cancellationToken);
            var map = safe.ToDictionary(value => value.Recipe.Reference.Id, value => value.Recipe, StringComparer.Ordinal);
            return hits.Where(value => map.ContainsKey(value.QualifiedId)).Select(value => map[value.QualifiedId]).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return []; }
    }

    private static string RecipeGenerationFingerprint(
        AuthorizedInteractionEnvelope envelope,
        IReadOnlyList<InteractionRecipeProjection> recipes)
    {
        var value = string.Join('\n', envelope.Host.EffectiveSetFingerprint,
            envelope.Host.ResolutionFingerprint,
            recipes.OrderBy(item => item.Reference.Id, StringComparer.Ordinal)
                .Select(item => $"{item.Reference.Id}:{item.Reference.Version}:{item.Reference.TemplateFingerprint}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private async Task MarkStale(
        InteractionRecipeProjection recipe,
        AuthorizedInteractionEnvelope envelope,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await recipes.MarkStaleAsync(new(recipe.Reference, envelope.Host.ApplicationRevision,
                envelope.Host.EffectiveSetFingerprint, reason,
                envelope.Host.ResolutionFingerprint), cancellationToken);
        }
        catch
        {
            // A failed diagnostic transition cannot make a stale recipe executable.
        }
    }

    private sealed record CurrentRecipe(
        InteractionRecipeProjection Recipe,
        ActiveCatalogFeatureSnapshot Snapshot);
}
