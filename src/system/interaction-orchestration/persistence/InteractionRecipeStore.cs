using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class InteractionRecipeStore(DantesRoleplayDbContext db) : IInteractionRecipeStore
{
    public async Task<InteractionRecipeWriteResult?> GetReviewReplayAsync(
        InteractionRecipeReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = RequireToken(request.RequestToken);
        var reviewer = RequireIdentifier(request.ReviewerPrincipalReference, nameof(request.ReviewerPrincipalReference));
        var reason = RequireBounded(request.Reason, 1000, "INVALID_RECIPE_REVIEW_REASON", nameof(request.Reason));
        var fingerprint = ReviewFingerprint(request, reviewer, reason);
        var prior = await db.InteractionRecipeRevisions.AsNoTracking().SingleOrDefaultAsync(row =>
            row.ReviewerPrincipalReference == reviewer && row.RequestToken == token, cancellationToken);
        if (prior is null) return null;
        if (prior.RequestFingerprint != fingerprint)
            return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_REVIEW_TOKEN_CONFLICT");
        var recipe = await db.InteractionRecipes.AsNoTracking().SingleAsync(row => row.Id == prior.RecipeId, cancellationToken);
        return new(InteractionRecipeWriteDisposition.Replayed,
            new(prior.RecipeId, prior.Version, recipe.TemplateFingerprint), "RECIPE_REVIEW_REPLAYED");
    }

    public async Task<IReadOnlyList<InteractionRecipeProjection>> ListAsync(
        ApplicationIdentifier applicationId,
        InteractionRecipeStatus status,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (limit is < 1 or > 50)
            throw new InteractionContractException("INVALID_RECIPE_LIMIT", "The recipe list limit is outside the closed range.");
        var rows = await db.InteractionRecipes.AsNoTracking().Include(value => value.Revisions)
            .Include(value => value.Evidence).Where(value => value.ApplicationId == applicationId.Value)
            .OrderBy(value => value.Id).ToArrayAsync(cancellationToken);
        return rows.Select(Project).Where(value => value.Status == status).Take(limit).ToArray();
    }

    public async Task<InteractionRecipeWriteResult> MarkStaleAsync(
        InteractionRecipeStaleDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var recipe = await db.InteractionRecipes.Include(row => row.Revisions).SingleOrDefaultAsync(row =>
            row.Id == draft.Recipe.Id && row.TemplateFingerprint == draft.Recipe.TemplateFingerprint, cancellationToken);
        if (recipe is null) return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_NOT_FOUND");
        var current = recipe.Revisions.OrderByDescending(row => row.Version).First();
        if (current.Status is "stale" or "retired")
            return new(InteractionRecipeWriteDisposition.Replayed,
                new(recipe.Id, current.Version, recipe.TemplateFingerprint), "RECIPE_STATUS_TERMINAL");
        if (current.Version != draft.Recipe.Version)
            return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_VERSION_CONFLICT");
        var reason = RequireBounded(draft.Reason, 1000, "INVALID_RECIPE_STALE_REASON", nameof(draft.Reason));
        var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/interaction-recipe-stale/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                recipe.Id, current.Version, draft.CurrentApplicationRevision.Revision,
                draft.CurrentApplicationRevision.Fingerprint, draft.CurrentEffectiveSetFingerprint, reason
            })));
        recipe.Revisions.Add(new InteractionRecipeRevision
        {
            RecipeId = recipe.Id,
            Version = current.Version + 1,
            Status = "stale",
            ApplicationRevision = draft.CurrentApplicationRevision.Revision,
            ApplicationFingerprint = RequireHash(draft.CurrentApplicationRevision.Fingerprint, "applicationFingerprint"),
            EffectiveSetFingerprint = RequireHash(draft.CurrentEffectiveSetFingerprint, "effectiveSetFingerprint"),
            ResolutionFingerprint = RequireHash(string.IsNullOrEmpty(draft.CurrentResolutionFingerprint)
                ? draft.CurrentEffectiveSetFingerprint : draft.CurrentResolutionFingerprint,
                "resolutionFingerprint"),
            ReviewerPrincipalReference = "",
            Reason = reason,
            RequestToken = "stale:" + fingerprint[..32].ToLowerInvariant(),
            RequestFingerprint = fingerprint,
            CreatedAtUtc = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(InteractionRecipeWriteDisposition.Created,
                new(recipe.Id, current.Version + 1, recipe.TemplateFingerprint), "RECIPE_MARKED_STALE");
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_STALE_CONFLICT");
        }
    }

    public async Task<InteractionRecipeWriteResult> AppendUseEvidenceAsync(
        InteractionRecipeUseEvidenceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var recipe = await db.InteractionRecipes.Include(row => row.Revisions).SingleOrDefaultAsync(row =>
            row.Id == draft.Recipe.Id && row.TemplateFingerprint == draft.Recipe.TemplateFingerprint, cancellationToken);
        if (recipe is null) return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_NOT_FOUND");
        var current = recipe.Revisions.OrderByDescending(row => row.Version).First();
        if (current.Version != draft.Recipe.Version)
            return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_VERSION_CONFLICT");
        var kind = draft.Successful ? "use-success" : "use-failure";
        var intentText = string.IsNullOrWhiteSpace(draft.IntentText) ? "" : NormalizeIntent(draft.IntentText);
        var performance = RequireReplayPerformance(draft.ReplayPerformance);
        var existing = await db.InteractionRecipeEvidence.AsNoTracking().SingleOrDefaultAsync(row =>
            row.RecipeId == recipe.Id && row.ExecutionReceiptId == draft.ExecutionReceiptId && row.Kind == kind,
            cancellationToken);
        if (existing is not null)
            return existing.ResolutionReceiptId == draft.ResolutionReceiptId
                && existing.IntentFingerprint == draft.IntentFingerprint
                && existing.IntentText == intentText
                && SamePerformance(existing, performance)
                ? new(InteractionRecipeWriteDisposition.Replayed, draft.Recipe, "RECIPE_USE_REPLAYED")
                : new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_USE_CONFLICT");
        db.InteractionRecipeEvidence.Add(new InteractionRecipeEvidence
        {
            RecipeId = recipe.Id,
            ExecutionReceiptId = InteractionReceiptIds.Require(draft.ExecutionReceiptId, nameof(draft.ExecutionReceiptId)),
            ResolutionReceiptId = InteractionReceiptIds.Require(draft.ResolutionReceiptId, nameof(draft.ResolutionReceiptId)),
            Kind = kind,
            IntentText = intentText,
            IntentFingerprint = RequireHash(draft.IntentFingerprint, nameof(draft.IntentFingerprint)),
            RoleProfile = RequireIdentifier(draft.RoleProfile, nameof(draft.RoleProfile)),
            ReplayBaselineAiCalls = performance?.BaselineAiCalls ?? 0,
            ReplayActualAiCalls = performance?.ActualAiCalls ?? 0,
            ReplaySavedAiCalls = performance?.SavedAiCalls ?? 0,
            ReplayElapsedMilliseconds = performance?.ElapsedMilliseconds ?? 0,
            ReplayChoiceResolutionMilliseconds = performance?.ChoiceResolutionMilliseconds ?? 0,
            ReplayProposalMilliseconds = performance?.ProposalMilliseconds ?? 0,
            ReplayExecutionMilliseconds = performance?.ExecutionMilliseconds ?? 0,
            ReplayPromptTokens = performance?.PromptTokens ?? 0,
            ReplayOutputTokens = performance?.OutputTokens ?? 0,
            ReplayFallbackReason = performance?.FallbackReason ?? "none",
            CreatedAtUtc = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(InteractionRecipeWriteDisposition.Created, draft.Recipe, "RECIPE_USE_RECORDED");
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_USE_CONFLICT");
        }
    }

    public async Task<InteractionRecipeWriteResult> AppendCandidateAsync(
        InteractionRecipeCandidateDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var intent = NormalizeIntent(draft.IntentText);
        var effective = RequireHash(draft.EffectiveSetFingerprint, nameof(draft.EffectiveSetFingerprint));
        var resolution = RequireHash(string.IsNullOrEmpty(draft.ResolutionFingerprint)
            ? effective : draft.ResolutionFingerprint, nameof(draft.ResolutionFingerprint));
        var intentFingerprint = RequireHash(draft.IntentFingerprint, nameof(draft.IntentFingerprint));
        var recipeId = InteractionRecipeIds.Create(draft.ApplicationRevision.ApplicationId, draft.Template.Fingerprint);
        var existingEvidence = await db.InteractionRecipeEvidence.AsNoTracking().SingleOrDefaultAsync(row =>
            row.RecipeId == recipeId && row.ExecutionReceiptId == draft.ExecutionReceiptId && row.Kind == "derived",
            cancellationToken);
        if (existingEvidence is not null)
            return await EvidenceReplayAsync(existingEvidence, draft, recipeId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var recipe = await db.InteractionRecipes.Include(row => row.Revisions).SingleOrDefaultAsync(row =>
                row.ApplicationId == draft.ApplicationRevision.ApplicationId.Value
                && row.TemplateFingerprint == draft.Template.Fingerprint, cancellationToken);
            var created = recipe is null;
            if (recipe is null)
            {
                recipe = new InteractionRecipe
                {
                    Id = recipeId,
                    ApplicationId = draft.ApplicationRevision.ApplicationId.Value,
                    TemplateFingerprint = draft.Template.Fingerprint,
                    TemplateJson = draft.Template.CanonicalJson,
                    CreatedAtUtc = DateTime.UtcNow
                };
                recipe.Revisions.Add(new InteractionRecipeRevision
                {
                    RecipeId = recipeId,
                    Version = 1,
                    Status = "candidate",
                    ApplicationRevision = draft.ApplicationRevision.Revision,
                    ApplicationFingerprint = draft.ApplicationRevision.Fingerprint,
                    EffectiveSetFingerprint = effective,
                    ResolutionFingerprint = resolution,
                    ReviewerPrincipalReference = "",
                    Reason = "Learned from an explicitly opted-in successful execution.",
                    RequestToken = CandidateToken(draft.ExecutionReceiptId),
                    RequestFingerprint = CandidateFingerprint(draft),
                    CreatedAtUtc = DateTime.UtcNow
                });
                db.InteractionRecipes.Add(recipe);
            }
            else if (recipe.Id != recipeId || recipe.TemplateJson != draft.Template.CanonicalJson)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_TEMPLATE_CONFLICT");
            }
            else if (recipe.Revisions.OrderByDescending(row => row.Version).First().Status is "stale" or "retired")
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_STATUS_TERMINAL");
            }

            recipe.Evidence.Add(CandidateEvidence(draft, recipeId, intent, intentFingerprint));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var current = recipe.Revisions.OrderByDescending(row => row.Version).First();
            var reference = new InteractionRecipeReference(recipeId, current.Version, recipe.TemplateFingerprint);
            return new(InteractionRecipeWriteDisposition.Created,
                reference, created ? "RECIPE_CANDIDATE_CREATED" : "RECIPE_EVIDENCE_APPENDED");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            existingEvidence = await db.InteractionRecipeEvidence.AsNoTracking().SingleOrDefaultAsync(row =>
                row.RecipeId == recipeId && row.ExecutionReceiptId == draft.ExecutionReceiptId && row.Kind == "derived",
                cancellationToken);
            if (existingEvidence is not null)
                return await EvidenceReplayAsync(existingEvidence, draft, recipeId, cancellationToken);
            var concurrentRecipe = await db.InteractionRecipes.Include(row => row.Revisions).SingleOrDefaultAsync(row =>
                row.ApplicationId == draft.ApplicationRevision.ApplicationId.Value
                && row.TemplateFingerprint == draft.Template.Fingerprint, cancellationToken);
            if (concurrentRecipe is not null
                && concurrentRecipe.Revisions.OrderByDescending(row => row.Version).First().Status is not ("stale" or "retired"))
            {
                db.InteractionRecipeEvidence.Add(CandidateEvidence(draft, concurrentRecipe.Id, intent, intentFingerprint));
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    var current = concurrentRecipe.Revisions.OrderByDescending(row => row.Version).First();
                    return new(InteractionRecipeWriteDisposition.Created,
                        new(concurrentRecipe.Id, current.Version, concurrentRecipe.TemplateFingerprint),
                        "RECIPE_EVIDENCE_APPENDED");
                }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    existingEvidence = await db.InteractionRecipeEvidence.AsNoTracking().SingleOrDefaultAsync(row =>
                        row.RecipeId == recipeId && row.ExecutionReceiptId == draft.ExecutionReceiptId
                        && row.Kind == "derived", cancellationToken);
                    if (existingEvidence is not null)
                        return await EvidenceReplayAsync(existingEvidence, draft, recipeId, cancellationToken);
                }
            }
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<InteractionRecipeProjection?> GetAsync(
        ApplicationIdentifier applicationId,
        string recipeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        recipeId = InteractionRecipeIds.Require(recipeId);
        var row = await db.InteractionRecipes.AsNoTracking()
            .Include(value => value.Revisions).Include(value => value.Evidence)
            .SingleOrDefaultAsync(value => value.ApplicationId == applicationId.Value && value.Id == recipeId, cancellationToken);
        return row is null ? null : Project(row);
    }

    public async Task<IReadOnlyList<InteractionRecipeProjection>> SearchAsync(
        ApplicationIdentifier applicationId,
        string query,
        InteractionRecipeStatus? status = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var normalized = NormalizeQuery(query);
        if (limit is < 1 or > 50)
            throw new InteractionContractException("INVALID_RECIPE_LIMIT", "The recipe search limit is outside the closed range.");
        var candidates = await db.InteractionRecipes.AsNoTracking()
            .Include(value => value.Revisions).Include(value => value.Evidence)
            .Where(value => value.ApplicationId == applicationId.Value)
            .ToArrayAsync(cancellationToken);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return candidates.Select(row => new
            {
                Row = row,
                Projection = Project(row),
                Exact = string.Equals(row.Id, normalized, StringComparison.OrdinalIgnoreCase),
                Score = words.Count(word => SearchText(row).Contains(word, StringComparison.OrdinalIgnoreCase))
            })
            .Where(value => (status is null || value.Projection.Status == status)
                && (value.Exact || value.Score > 0))
            .OrderByDescending(value => value.Exact)
            .ThenByDescending(value => value.Score)
            .ThenBy(value => value.Row.Id, StringComparer.Ordinal)
            .Take(limit).Select(value => value.Projection).ToArray();
    }

    public async Task<InteractionRecipeSearchPage> SearchPageAsync(
        ApplicationIdentifier applicationId,
        string query,
        InteractionRecipeStatus? status,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var normalized = NormalizeQuery(query);
        if (offset is < 0 or > 10_000 || limit is < 1 or > 50)
            throw new InteractionContractException("INVALID_RECIPE_PAGE", "The recipe page is outside the closed range.");
        var rows = await db.InteractionRecipes.AsNoTracking().Include(value => value.Revisions)
            .Include(value => value.Evidence).Where(value => value.ApplicationId == applicationId.Value)
            .ToArrayAsync(cancellationToken);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ranked = rows.Select(row => new
            {
                Row = row,
                Projection = Project(row),
                Exact = string.Equals(row.Id, normalized, StringComparison.OrdinalIgnoreCase),
                Score = words.Count(word => SearchText(row).Contains(word, StringComparison.OrdinalIgnoreCase))
            })
            .Where(value => (status is null || value.Projection.Status == status)
                && (value.Exact || value.Score > 0))
            .OrderByDescending(value => value.Exact).ThenByDescending(value => value.Score)
            .ThenBy(value => value.Row.Id, StringComparer.Ordinal).ToArray();
        return new(ranked.Skip(offset).Take(limit).Select(value => value.Projection).ToArray(), ranked.Length);
    }

    public async Task<InteractionRecipeWriteResult> ReviewAsync(
        InteractionRecipeReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = RequireToken(request.RequestToken);
        var recipeId = InteractionRecipeIds.Require(request.RecipeId);
        if (!recipeId.StartsWith(request.ApplicationId.Value + ".recipe.", StringComparison.Ordinal))
            throw new InteractionContractException("RECIPE_APPLICATION_MISMATCH", "The recipe does not belong to the application.");
        if (request.ExpectedVersion < 1)
            throw new InteractionContractException("INVALID_RECIPE_VERSION", "The expected recipe version must be positive.");
        var nextStatus = request.Decision switch
        {
            "verify" => InteractionRecipeStatus.Verified,
            "retire" => InteractionRecipeStatus.Retired,
            _ => throw new InteractionContractException("INVALID_RECIPE_REVIEW_DECISION", "The recipe review decision is not supported.")
        };
        var reviewer = RequireIdentifier(request.ReviewerPrincipalReference, nameof(request.ReviewerPrincipalReference));
        var reason = RequireBounded(request.Reason, 1000, "INVALID_RECIPE_REVIEW_REASON", nameof(request.Reason));
        var fingerprint = ReviewFingerprint(request, reviewer, reason);
        var replay = await GetReviewReplayAsync(request, cancellationToken);
        if (replay is not null) return replay;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var recipe = await db.InteractionRecipes.Include(row => row.Revisions).SingleOrDefaultAsync(row =>
                row.ApplicationId == request.ApplicationId.Value && row.Id == recipeId, cancellationToken);
            if (recipe is null)
                return await Rollback(new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_NOT_FOUND"));
            var current = recipe.Revisions.OrderByDescending(row => row.Version).First();
            if (current.Version != request.ExpectedVersion)
                return await Rollback(new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_VERSION_CONFLICT"));
            var currentStatus = InteractionRecipeStatusNames.Parse(current.Status);
            if (currentStatus is InteractionRecipeStatus.Stale or InteractionRecipeStatus.Retired)
                return await Rollback(new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_STATUS_TERMINAL"));
            if (nextStatus == InteractionRecipeStatus.Verified && currentStatus != InteractionRecipeStatus.Candidate)
                return await Rollback(new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_REVIEW_ALREADY_DECIDED"));
            recipe.Revisions.Add(new InteractionRecipeRevision
            {
                RecipeId = recipe.Id,
                Version = current.Version + 1,
                Status = InteractionRecipeStatusNames.Get(nextStatus),
                ApplicationRevision = current.ApplicationRevision,
                ApplicationFingerprint = current.ApplicationFingerprint,
                EffectiveSetFingerprint = current.EffectiveSetFingerprint,
                ResolutionFingerprint = current.ResolutionFingerprint,
                ReviewerPrincipalReference = reviewer,
                Reason = reason,
                RequestToken = token,
                RequestFingerprint = fingerprint,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(InteractionRecipeWriteDisposition.Created,
                new(recipe.Id, current.Version + 1, recipe.TemplateFingerprint), "RECIPE_REVIEW_APPLIED");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_REVIEW_CONFLICT");
        }

        async Task<InteractionRecipeWriteResult> Rollback(InteractionRecipeWriteResult result)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return result;
        }
    }

    private static InteractionRecipeProjection Project(InteractionRecipe row)
    {
        var revision = row.Revisions.OrderByDescending(value => value.Version).First();
        var template = InteractionRecipeTemplate.Parse(row.TemplateJson, ApplicationIdentifier.Parse(row.ApplicationId));
        return new(new(row.Id, revision.Version, row.TemplateFingerprint), ApplicationIdentifier.Parse(row.ApplicationId),
            InteractionRecipeStatusNames.Parse(revision.Status), template, row.Evidence.Count,
            row.Evidence.OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.ExecutionReceiptId, StringComparer.Ordinal)
                .Select(value => value.IntentFingerprint).ToArray(), row.CreatedAtUtc, revision.CreatedAtUtc,
            revision.ApplicationRevision, revision.ApplicationFingerprint, revision.EffectiveSetFingerprint,
            row.Evidence.OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.ExecutionReceiptId, StringComparer.Ordinal)
                .Select(value => new InteractionRecipeEvidenceReference(value.ResolutionReceiptId,
                    value.ExecutionReceiptId, value.Kind, value.IntentFingerprint, value.CreatedAtUtc,
                    value.IntentText, Performance(value))).ToArray(),
            revision.ResolutionFingerprint);
    }

    private static InteractionRecipeReplayPerformance? RequireReplayPerformance(
        InteractionRecipeReplayPerformance? value)
    {
        if (value is null) return null;
        if (value.BaselineAiCalls is < 1 or > InteractionContractLimits.ProposalSteps
            || value.ActualAiCalls is < 0 or > 1
            || value.SavedAiCalls != Math.Max(0, value.BaselineAiCalls - value.ActualAiCalls)
            || value.ElapsedMilliseconds < 0 || value.ChoiceResolutionMilliseconds < 0
            || value.ProposalMilliseconds < 0 || value.ExecutionMilliseconds < 0
            || value.PromptTokens < 0 || value.OutputTokens < 0
            || value.FallbackReason is not ("none" or "missing-roles" or "missing-inputs"
                or "missing-roles-and-inputs")
            || (value.ActualAiCalls == 0) != (value.FallbackReason == "none"))
            throw new InteractionContractException("INVALID_RECIPE_REPLAY_PERFORMANCE",
                "Recipe replay performance is outside its closed bounds.");
        return value;
    }

    private static InteractionRecipeReplayPerformance? Performance(InteractionRecipeEvidence value) =>
        value.ReplayBaselineAiCalls == 0 ? null : new(
            value.ReplayBaselineAiCalls, value.ReplayActualAiCalls, value.ReplaySavedAiCalls,
            value.ReplayElapsedMilliseconds, value.ReplayChoiceResolutionMilliseconds,
            value.ReplayProposalMilliseconds, value.ReplayExecutionMilliseconds,
            value.ReplayPromptTokens, value.ReplayOutputTokens, value.ReplayFallbackReason);

    private static bool SamePerformance(
        InteractionRecipeEvidence stored,
        InteractionRecipeReplayPerformance? expected) => Performance(stored) == expected;

    private async Task<InteractionRecipeWriteResult> EvidenceReplayAsync(
        InteractionRecipeEvidence row,
        InteractionRecipeCandidateDraft draft,
        string recipeId,
        CancellationToken cancellationToken)
    {
        var same = row.ResolutionReceiptId == draft.ResolutionReceiptId
            && row.IntentFingerprint == draft.IntentFingerprint
            && row.RoleProfile == draft.RoleProfile;
        var version = same
            ? await db.InteractionRecipeRevisions.AsNoTracking().Where(value => value.RecipeId == recipeId)
                .MaxAsync(value => value.Version, cancellationToken)
            : 0;
        return same
            ? new(InteractionRecipeWriteDisposition.Replayed,
                new(recipeId, version, draft.Template.Fingerprint), "RECIPE_LEARNING_REPLAYED")
            : new(InteractionRecipeWriteDisposition.Conflict, null, "RECIPE_EVIDENCE_CONFLICT");
    }

    private static string NormalizeIntent(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (normalized.Length is 0 or > InteractionRecipeProtocol.MaximumStoredIntentText || normalized.Any(char.IsControl))
            throw new InteractionContractException("RECIPE_INTENT_UNSAFE", "The learning intent is not safe bounded text.");
        return normalized;
    }

    private static InteractionRecipeEvidence CandidateEvidence(
        InteractionRecipeCandidateDraft draft,
        string recipeId,
        string intent,
        string intentFingerprint) => new()
    {
        RecipeId = recipeId,
        ExecutionReceiptId = InteractionReceiptIds.Require(draft.ExecutionReceiptId, nameof(draft.ExecutionReceiptId)),
        ResolutionReceiptId = InteractionReceiptIds.Require(draft.ResolutionReceiptId, nameof(draft.ResolutionReceiptId)),
        Kind = "derived",
        IntentText = intent,
        IntentFingerprint = intentFingerprint,
        RoleProfile = RequireIdentifier(draft.RoleProfile, nameof(draft.RoleProfile)),
        CreatedAtUtc = DateTime.UtcNow
    };

    private static string NormalizeQuery(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (normalized.Length is 0 or > InteractionRetrievalLimits.MaximumQueryLength || normalized.Any(char.IsControl))
            throw new InteractionContractException("INVALID_RECIPE_QUERY", "The recipe query is invalid.");
        return normalized;
    }

    private static string SearchText(InteractionRecipe row) => string.Join(' ',
        row.Id, row.TemplateJson, string.Join(' ', row.Evidence.Select(value => value.IntentText)));

    private static string CandidateToken(string executionReceiptId) => "learn:" + executionReceiptId[(executionReceiptId.LastIndexOf('.') + 1)..];
    private static string CandidateFingerprint(InteractionRecipeCandidateDraft draft) => InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/interaction-recipe-candidate/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            applicationId = draft.ApplicationRevision.ApplicationId.Value,
            draft.Template.Fingerprint,
            draft.ExecutionReceiptId,
            draft.ResolutionReceiptId,
            draft.IntentFingerprint
        })));

    private static string ReviewFingerprint(InteractionRecipeReviewRequest request, string reviewer, string reason) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/interaction-recipe-review/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                request.ApplicationId.Value,
                request.RecipeId,
                request.ExpectedVersion,
                request.Decision,
                reason,
                reviewer
            })));

    private static string RequireHash(string value, string name)
    {
        if (value is not { Length: 64 } || value.Any(character => !(char.IsAsciiDigit(character) || character is >= 'A' and <= 'F')))
            throw new InteractionContractException("INVALID_SHA256", $"{name} must be an uppercase SHA-256 value.");
        return value;
    }

    private static string RequireIdentifier(string value, string name) =>
        RequireBounded(value, InteractionContractLimits.Identifier, "INVALID_IDENTIFIER", name);

    private static string RequireToken(string value)
    {
        var token = RequireBounded(value, InteractionContractLimits.IdempotencyKey,
            "INVALID_IDEMPOTENCY_KEY", nameof(value));
        if (token.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-')))
            throw new InteractionContractException("INVALID_IDEMPOTENCY_KEY", "The request token contains unsupported characters.");
        return token;
    }

    private static string RequireBounded(string value, int maximum, string code, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new InteractionContractException(code, $"{name} is required and may contain at most {maximum} characters.");
        return value.Trim();
    }
}
