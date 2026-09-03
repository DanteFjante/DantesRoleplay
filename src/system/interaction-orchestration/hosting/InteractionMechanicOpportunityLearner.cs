using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Sources;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class InteractionMechanicOpportunityLearner(
    IInteractionRecipeStore recipes,
    IInteractionMechanicOpportunityStore opportunities,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IActiveCatalogFeatureSnapshotProvider snapshots) : IInteractionMechanicOpportunityLearner
{
    public async Task<InteractionMechanicOpportunityWriteResult?> ObserveAsync(
        InteractionRecipeReference recipeReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipeReference);
        var applicationId = ApplicationFrom(recipeReference.Id);
        var existing = await opportunities.GetAsync(applicationId, recipeReference.Id, cancellationToken);
        if (existing is not null)
            return new(InteractionMechanicOpportunityWriteDisposition.Replayed, existing,
                "MECHANIC_OPPORTUNITY_REPLAYED");

        var recipe = await recipes.GetAsync(applicationId, recipeReference.Id, cancellationToken);
        if (recipe is null || recipe.Status != InteractionRecipeStatus.Verified
            || recipe.Reference != recipeReference)
            return null;

        var application = applications.Get(applicationId);
        var activation = activations.Current(applicationId);
        if (application is null || activation is null
            || application.Revision != recipe.ApplicationRevision
            || application.Fingerprint != recipe.ApplicationFingerprint
            || activation.ApplicationRevision != application.Revision
            || activation.ApplicationFingerprint != application.Fingerprint
            || activation.ActivationFingerprint != recipe.EffectiveSetFingerprint
            || !snapshots.TryGetSnapshot(applicationId, out var snapshot))
            return null;

        var successful = SuccessfulIntentGroup(recipe);
        if (successful is null) return null;

        var records = new List<CatalogRecordDefinition>();
        var requirements = new List<MechanicRequirements>();
        foreach (var step in recipe.Template.Steps)
        {
            var record = snapshot.Documents.SingleOrDefault(value => value.Trust == SourceTrust.Trusted
                && value.Record.Kind == "mechanic" && value.Record.Status == "active"
                && value.Record.QualifiedId == step.QualifiedId
                && value.Record.Version == step.ContractVersion
                && value.Record.ContentFingerprint == step.ContractFingerprint)?.Record;
            if (record is null || !TryRequirements(record, out var parsed)) return null;
            records.Add(record);
            requirements.Add(parsed);
        }

        var evidence = successful.Value.Evidence
            .Take(InteractionMechanicOpportunityProtocol.MaximumSupportingReceipts)
            .Select(value => new InteractionMechanicOpportunityReceiptEvidence(value.ResolutionReceiptId,
                value.ExecutionReceiptId, value.IntentFingerprint, value.CreatedAtUtc)).ToArray();
        var children = recipe.Template.Steps.Select(step => new InteractionMechanicOpportunityChild(
            step.StepId, step.QualifiedId, step.ContractVersion, step.ContractFingerprint,
            step.DependsOn, step.RoleSlots)).ToArray();
        var roles = recipe.Template.Steps.SelectMany(step => step.RoleSlots.Select(role => (role, step.StepId)))
            .GroupBy(value => value.role, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new InteractionMechanicOpportunityRole(group.Key,
                group.Select(value => value.StepId).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray())).ToArray();
        var effects = records.Select((record, index) => new InteractionMechanicOpportunityEffectOwnership(
            record.QualifiedId,
            requirements[index].EffectComponentIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "The child mechanic retains ownership of this effect family; the proposed mechanic only composes it."))
            .ToArray();
        var inputSchema = ProposedInputSchema(recipe.Template.Steps, requirements);
        var phrases = successful.Value.Evidence.Select(value => value.IntentText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Append(successful.Value.Intent)
            .Select(NormalizeIntent).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var overlap = Overlap(snapshot, applicationId, successful.Value.Intent, recipe.Template.Steps).ToArray();
        var saved = Math.Max(0, children.Length - 1);
        var efficiency = new InteractionMechanicOpportunityEfficiencyEstimate(evidence.Length, children.Length, 1,
            saved, saved * evidence.Length, 1, 0);
        var reason = children.Length == 1
            ? "A registered mechanic would turn the repeated reviewed intent into a discoverable typed capability with stable schemas and catalog governance. Deterministic recipe replay already gives the AI a one-call route, so this proposal claims no additional AI tool-call reduction."
            : "A registered composite mechanic would make the repeated reviewed intent a discoverable typed capability and execute the exact child graph atomically. Deterministic recipe replay already gives the AI a one-call route, so the mechanic's advantage is atomic catalog-owned composition and stable schemas rather than an additional AI tool-call reduction.";
        var draft = new InteractionMechanicOpportunityDraft(applicationId, recipe.Reference,
            recipe.ApplicationRevision, recipe.ApplicationFingerprint, recipe.EffectiveSetFingerprint,
            successful.Value.Intent, evidence, roles, inputSchema, children, effects, phrases, efficiency,
            overlap, reason);
        return await opportunities.AppendAsync(draft, cancellationToken);
    }

    private static (string Intent, IReadOnlyList<InteractionRecipeEvidenceReference> Evidence)? SuccessfulIntentGroup(
        InteractionRecipeProjection recipe)
    {
        var provenance = recipe.Provenance ?? [];
        var fallback = provenance.FirstOrDefault(value => value.Kind == "derived"
            && !string.IsNullOrWhiteSpace(value.IntentText))?.IntentText;
        if (string.IsNullOrWhiteSpace(fallback)) return null;
        var uses = provenance.Where(value => value.Kind == "use-success")
            .Select(value => (Evidence: value,
                Intent: NormalizeIntent(string.IsNullOrWhiteSpace(value.IntentText) ? fallback : value.IntentText)))
            .GroupBy(value => value.Intent, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Intent = group.First().Intent,
                Evidence = (IReadOnlyList<InteractionRecipeEvidenceReference>)group.Select(value => value.Evidence)
                    .GroupBy(value => value.ExecutionReceiptId, StringComparer.Ordinal).Select(value => value.First())
                    .OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.ExecutionReceiptId, StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(value => value.Evidence.Count >= InteractionMechanicOpportunityProtocol.SuccessfulUseThreshold)
            .OrderByDescending(value => value.Evidence.Count).ThenBy(value => value.Intent, StringComparer.Ordinal)
            .FirstOrDefault();
        return uses is null ? null : (uses.Intent, uses.Evidence);
    }

    private static string ProposedInputSchema(
        IReadOnlyList<InteractionRecipeTemplateStep> steps,
        IReadOnlyList<MechanicRequirements> requirements)
    {
        using var emptyDocument = JsonDocument.Parse("{\"type\":\"object\",\"additionalProperties\":false}");
        var properties = steps.Select((step, index) => new
            {
                step.StepId,
                Schema = requirements[index].InputSchema?.Clone() ?? emptyDocument.RootElement.Clone()
            })
            .ToDictionary(value => value.StepId, value => value.Schema, StringComparer.Ordinal);
        return InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "stepInputs" },
            properties = new
            {
                stepInputs = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = steps.Select(step => step.StepId).ToArray(),
                    properties
                }
            }
        }));
    }

    private static IEnumerable<InteractionMechanicOpportunityOverlap> Overlap(
        ActiveCatalogFeatureSnapshot snapshot,
        ApplicationIdentifier applicationId,
        string intent,
        IReadOnlyList<InteractionRecipeTemplateStep> steps)
    {
        var intentTokens = Tokens(intent);
        var childIds = steps.Select(step => Unqualify(applicationId, step.QualifiedId))
            .ToHashSet(StringComparer.Ordinal);
        return snapshot.Documents.Where(value => value.Trust == SourceTrust.Trusted
                && value.Record.Kind == "mechanic" && value.Record.Status == "active")
            .Select(value =>
            {
                var record = value.Record;
                var exactGraph = TryRequirements(record, out var parsed)
                    && parsed.Children.Count > 0
                    && parsed.Children.Values.Select(child => child.MechanicId)
                        .ToHashSet(StringComparer.Ordinal).SetEquals(childIds);
                var recordTokens = Tokens(string.Join(' ', record.Name, record.Description,
                    string.Join(' ', record.MatchPhrases)));
                var union = intentTokens.Union(recordTokens).Count();
                var similarity = union == 0 ? 0 : (double)intentTokens.Intersect(recordTokens).Count() / union;
                return new { record, exactGraph, similarity };
            })
            .Where(value => value.exactGraph || value.similarity >= 0.20)
            .OrderByDescending(value => value.exactGraph).ThenByDescending(value => value.similarity)
            .ThenBy(value => value.record.QualifiedId, StringComparer.Ordinal)
            .Take(InteractionMechanicOpportunityProtocol.MaximumOverlapCandidates)
            .Select(value => new InteractionMechanicOpportunityOverlap(value.record.QualifiedId,
                value.record.Version, value.record.ContentFingerprint,
                value.exactGraph ? 1 : Math.Round(value.similarity, 4),
                value.exactGraph ? "Equivalent declared child graph." : "Lexically similar intent, description, or match phrases."));
    }

    private static bool TryRequirements(CatalogRecordDefinition record, out MechanicRequirements requirements)
    {
        try
        {
            using var document = JsonDocument.Parse(record.ContentJson);
            if (!document.RootElement.TryGetProperty("requirements", out var value))
            {
                requirements = new();
                return false;
            }
            requirements = value.ValueKind switch
            {
                JsonValueKind.String => MechanicRequirements.Parse(value.GetString()!),
                JsonValueKind.Object => MechanicRequirements.Parse(value.GetRawText()),
                _ => new MechanicRequirements()
            };
            return value.ValueKind is JsonValueKind.String or JsonValueKind.Object
                && requirements.CompositionProblems().Count == 0;
        }
        catch
        {
            requirements = new();
            return false;
        }
    }

    private static HashSet<string> Tokens(string value) => value.Normalize(System.Text.NormalizationForm.FormKC)
        .ToLowerInvariant().Split([' ', '\t', '\r', '\n', '.', ',', ':', ';', '-', '_', '/', '\\', '(', ')'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(token => token.Length >= 3 && token is not ("the" or "and" or "with" or "from" or "into" or "for"))
        .ToHashSet(StringComparer.Ordinal);

    private static string NormalizeIntent(string value) =>
        value.Normalize(System.Text.NormalizationForm.FormKC).Trim();

    private static string Unqualify(ApplicationIdentifier applicationId, string qualifiedId) =>
        qualifiedId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
            ? qualifiedId[(applicationId.Value.Length + 1)..]
            : qualifiedId;

    private static ApplicationIdentifier ApplicationFrom(string recipeId)
    {
        var separator = recipeId.LastIndexOf(".recipe.", StringComparison.Ordinal);
        return ApplicationIdentifier.Parse(recipeId[..separator]);
    }
}
