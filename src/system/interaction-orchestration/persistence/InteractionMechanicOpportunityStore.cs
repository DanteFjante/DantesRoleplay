using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class InteractionMechanicOpportunityStore(DantesRoleplayDbContext db)
    : IInteractionMechanicOpportunityStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<InteractionMechanicOpportunityWriteResult> AppendAsync(
        InteractionMechanicOpportunityDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var normalized = Normalize(draft);
        var proposalJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(Payload(normalized), Json));
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionMechanicOpportunityProtocol.ProposalFingerprintDomain, proposalJson);
        var existing = await db.InteractionMechanicOpportunities.AsNoTracking()
            .SingleOrDefaultAsync(row => row.RecipeId == normalized.SourceRecipe.Id, cancellationToken);
        if (existing is not null)
            return existing.ProposalFingerprint == fingerprint && existing.ProposalJson == proposalJson
                ? new(InteractionMechanicOpportunityWriteDisposition.Replayed, Project(existing),
                    "MECHANIC_OPPORTUNITY_REPLAYED")
                : new(InteractionMechanicOpportunityWriteDisposition.Conflict, null,
                    "MECHANIC_OPPORTUNITY_CONFLICT");

        var recipe = await db.InteractionRecipes.AsNoTracking().Include(row => row.Revisions)
            .SingleOrDefaultAsync(row =>
            row.Id == normalized.SourceRecipe.Id
            && row.ApplicationId == normalized.ApplicationId.Value
            && row.TemplateFingerprint == normalized.SourceRecipe.TemplateFingerprint, cancellationToken);
        var current = recipe?.Revisions.OrderByDescending(value => value.Version).FirstOrDefault();
        if (recipe is null || current is null || current.Version != normalized.SourceRecipe.Version
            || current.Status != "verified")
            return new(InteractionMechanicOpportunityWriteDisposition.Conflict, null,
                "MECHANIC_OPPORTUNITY_RECIPE_NOT_FOUND");
        var executionIds = normalized.SupportingReceipts.Select(value => value.ExecutionReceiptId).ToArray();
        var supported = await db.InteractionRecipeEvidence.AsNoTracking().CountAsync(value =>
            value.RecipeId == normalized.SourceRecipe.Id && value.Kind == "use-success"
            && executionIds.Contains(value.ExecutionReceiptId), cancellationToken);
        if (supported != executionIds.Length)
            return new(InteractionMechanicOpportunityWriteDisposition.Conflict, null,
                "MECHANIC_OPPORTUNITY_EVIDENCE_NOT_FOUND");

        var row = new InteractionMechanicOpportunity
        {
            RecipeId = normalized.SourceRecipe.Id,
            RecipeVersion = normalized.SourceRecipe.Version,
            RecipeTemplateFingerprint = normalized.SourceRecipe.TemplateFingerprint,
            ApplicationId = normalized.ApplicationId.Value,
            ProposalFingerprint = fingerprint,
            ProposalJson = proposalJson,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.InteractionMechanicOpportunities.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(InteractionMechanicOpportunityWriteDisposition.Created, Project(row),
                "MECHANIC_OPPORTUNITY_CREATED");
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.InteractionMechanicOpportunities.AsNoTracking()
                .SingleOrDefaultAsync(value => value.RecipeId == normalized.SourceRecipe.Id, cancellationToken);
            return existing is not null && existing.ProposalFingerprint == fingerprint && existing.ProposalJson == proposalJson
                ? new(InteractionMechanicOpportunityWriteDisposition.Replayed, Project(existing),
                    "MECHANIC_OPPORTUNITY_REPLAYED")
                : new(InteractionMechanicOpportunityWriteDisposition.Conflict, null,
                    "MECHANIC_OPPORTUNITY_CONFLICT");
        }
    }

    public async Task<InteractionMechanicOpportunityProjection?> GetAsync(
        ApplicationIdentifier applicationId,
        string sourceRecipeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        sourceRecipeId = InteractionRecipeIds.Require(sourceRecipeId);
        var row = await db.InteractionMechanicOpportunities.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.RecipeId == sourceRecipeId, cancellationToken);
        return row is null ? null : Project(row);
    }

    public async Task<IReadOnlyList<InteractionMechanicOpportunityProjection>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (limit is < 1 or > 50)
            throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_LIMIT",
                "The mechanic-opportunity limit is outside the closed range.");
        var rows = await db.InteractionMechanicOpportunities.AsNoTracking()
            .Where(value => value.ApplicationId == applicationId.Value)
            .OrderByDescending(value => value.CreatedAtUtc).ThenBy(value => value.RecipeId)
            .Take(limit).ToArrayAsync(cancellationToken);
        return rows.Select(Project).ToArray();
    }

    private static InteractionMechanicOpportunityProjection Project(InteractionMechanicOpportunity row)
    {
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionMechanicOpportunityProtocol.ProposalFingerprintDomain,
            InteractionCanonicalJson.CanonicalizeObject(row.ProposalJson));
        if (fingerprint != row.ProposalFingerprint)
            throw new InvalidOperationException("The stored mechanic-opportunity fingerprint is invalid.");
        var value = JsonSerializer.Deserialize<StoredPayload>(row.ProposalJson, Json)
            ?? throw new InvalidOperationException("The stored mechanic-opportunity proposal is invalid.");
        var source = new InteractionRecipeReference(value.RecipeId, value.RecipeVersion,
            value.RecipeTemplateFingerprint);
        return new(row.ProposalFingerprint, ApplicationIdentifier.Parse(value.ApplicationId), source,
            value.ApplicationRevision, value.ApplicationFingerprint, value.EffectiveSetFingerprint,
            value.RepeatedIntent, value.SupportingReceipts, value.ProposedRoles, value.ProposedInputSchemaJson,
            value.ExactChildDependencies, value.IntendedEffectsAndOwnership, value.SuggestedMatchPhrases,
            value.EstimatedCallReduction, value.PossibleOverlap, value.MechanicPreferenceReason, row.CreatedAtUtc);
    }

    private static StoredPayload Payload(InteractionMechanicOpportunityDraft value) => new(
        value.ApplicationId.Value, value.SourceRecipe.Id, value.SourceRecipe.Version,
        value.SourceRecipe.TemplateFingerprint, value.ApplicationRevision, value.ApplicationFingerprint,
        value.EffectiveSetFingerprint, value.RepeatedIntent, value.SupportingReceipts, value.ProposedRoles,
        value.ProposedInputSchemaJson, value.ExactChildDependencies, value.IntendedEffectsAndOwnership,
        value.SuggestedMatchPhrases, value.EstimatedCallReduction, value.PossibleOverlap,
        value.MechanicPreferenceReason);

    private static InteractionMechanicOpportunityDraft Normalize(InteractionMechanicOpportunityDraft value)
    {
        if (!value.SourceRecipe.Id.StartsWith(value.ApplicationId.Value + ".recipe.", StringComparison.Ordinal))
            throw new InteractionContractException("MECHANIC_OPPORTUNITY_APPLICATION_MISMATCH",
                "The source recipe does not belong to the proposal application.");
        var repeatedIntent = BoundedText(value.RepeatedIntent, 500, "INVALID_MECHANIC_OPPORTUNITY_INTENT");
        var applicationFingerprint = Hash(value.ApplicationFingerprint, nameof(value.ApplicationFingerprint));
        var effectiveSetFingerprint = Hash(value.EffectiveSetFingerprint, nameof(value.EffectiveSetFingerprint));
        if (value.ApplicationRevision < 1)
            throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_REVISION",
                "The proposal application revision must be positive.");
        if (value.SupportingReceipts.Count is < InteractionMechanicOpportunityProtocol.SuccessfulUseThreshold
            or > InteractionMechanicOpportunityProtocol.MaximumSupportingReceipts
            || value.SupportingReceipts.Select(item => item.ExecutionReceiptId).Distinct(StringComparer.Ordinal).Count()
                != value.SupportingReceipts.Count)
            throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_EVIDENCE",
                "The proposal requires bounded distinct successful-use receipts.");
        foreach (var receipt in value.SupportingReceipts)
        {
            InteractionReceiptIds.Require(receipt.ResolutionReceiptId, nameof(receipt.ResolutionReceiptId));
            InteractionReceiptIds.Require(receipt.ExecutionReceiptId, nameof(receipt.ExecutionReceiptId));
            Hash(receipt.IntentFingerprint, nameof(receipt.IntentFingerprint));
        }
        if (value.ExactChildDependencies.Count is < 1 or > InteractionContractLimits.ProposalSteps
            || value.ExactChildDependencies.Select(child => child.StepId).Distinct(StringComparer.Ordinal).Count()
                != value.ExactChildDependencies.Count)
            throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_CHILDREN",
                "The proposal requires a bounded exact child graph.");
        foreach (var child in value.ExactChildDependencies)
        {
            if (child.Version < 1 || string.IsNullOrWhiteSpace(child.QualifiedId))
                throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_CHILDREN",
                    "Every proposed child requires exact current identity.");
            Hash(child.Fingerprint, nameof(child.Fingerprint));
        }
        var seenSteps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in value.ExactChildDependencies)
        {
            if (child.DependsOn.Any(dependency => !seenSteps.Contains(dependency)))
                throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_CHILDREN",
                    "Proposed child dependencies must name earlier exact children.");
            seenSteps.Add(child.StepId);
        }
        var childIds = value.ExactChildDependencies.Select(child => child.QualifiedId)
            .ToHashSet(StringComparer.Ordinal);
        if (value.ProposedRoles.Count > InteractionContractLimits.RoleHints
            || value.IntendedEffectsAndOwnership.Count != value.ExactChildDependencies.Count
            || !value.IntendedEffectsAndOwnership.Select(item => item.ChildQualifiedId)
                .ToHashSet(StringComparer.Ordinal).SetEquals(childIds)
            || value.SuggestedMatchPhrases.Count is < 1 or > 8
            || value.SuggestedMatchPhrases.Any(phrase => string.IsNullOrWhiteSpace(phrase) || phrase.Length > 200)
            || value.PossibleOverlap.Count > InteractionMechanicOpportunityProtocol.MaximumOverlapCandidates)
            throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_SHAPE",
                "The mechanic-opportunity proposal is outside its closed bounds.");
        foreach (var overlap in value.PossibleOverlap)
            Hash(overlap.Fingerprint, nameof(overlap.Fingerprint));
        var inputSchema = InteractionCanonicalJson.CanonicalizeObject(value.ProposedInputSchemaJson);
        var reason = BoundedText(value.MechanicPreferenceReason, 1500,
            "INVALID_MECHANIC_OPPORTUNITY_REASON");
        var efficiency = value.EstimatedCallReduction;
        if (efficiency.ObservedSuccessfulUses != value.SupportingReceipts.Count
            || efficiency.BaselineChildCallsPerUse != value.ExactChildDependencies.Count
            || efficiency.ExpectedMechanicCallsPerUse != 1
            || efficiency.GrossCallsSavedPerUse != Math.Max(0, value.ExactChildDependencies.Count - 1)
            || efficiency.GrossCallsSavedAcrossObservedUses
                != efficiency.GrossCallsSavedPerUse * efficiency.ObservedSuccessfulUses
            || efficiency.RecipeToolCallsPerUse != 1 || efficiency.IncrementalToolCallsSavedVersusRecipe != 0)
            throw new InteractionContractException("INVALID_MECHANIC_OPPORTUNITY_ESTIMATE",
                "The mechanic-opportunity efficiency estimate is inconsistent.");
        return value with
        {
            ApplicationFingerprint = applicationFingerprint,
            EffectiveSetFingerprint = effectiveSetFingerprint,
            RepeatedIntent = repeatedIntent,
            ProposedInputSchemaJson = inputSchema,
            SuggestedMatchPhrases = value.SuggestedMatchPhrases.Select(phrase => phrase.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MechanicPreferenceReason = reason
        };
    }

    private static string Hash(string value, string name)
    {
        if (value is not { Length: 64 }
            || value.Any(character => !(char.IsAsciiDigit(character) || character is >= 'A' and <= 'F')))
            throw new InteractionContractException("INVALID_SHA256", $"{name} must be uppercase SHA-256.");
        return value;
    }

    private static string BoundedText(string value, int maximum, string code)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Normalize(System.Text.NormalizationForm.FormKC).Trim();
        if (normalized.Length is 0 || normalized.Length > maximum || normalized.Any(char.IsControl))
            throw new InteractionContractException(code, "The mechanic-opportunity text is outside its safe bounds.");
        return normalized;
    }

    private sealed record StoredPayload(
        string ApplicationId,
        string RecipeId,
        int RecipeVersion,
        string RecipeTemplateFingerprint,
        int ApplicationRevision,
        string ApplicationFingerprint,
        string EffectiveSetFingerprint,
        string RepeatedIntent,
        IReadOnlyList<InteractionMechanicOpportunityReceiptEvidence> SupportingReceipts,
        IReadOnlyList<InteractionMechanicOpportunityRole> ProposedRoles,
        string ProposedInputSchemaJson,
        IReadOnlyList<InteractionMechanicOpportunityChild> ExactChildDependencies,
        IReadOnlyList<InteractionMechanicOpportunityEffectOwnership> IntendedEffectsAndOwnership,
        IReadOnlyList<string> SuggestedMatchPhrases,
        InteractionMechanicOpportunityEfficiencyEstimate EstimatedCallReduction,
        IReadOnlyList<InteractionMechanicOpportunityOverlap> PossibleOverlap,
        string MechanicPreferenceReason);
}
