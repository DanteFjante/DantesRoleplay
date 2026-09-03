using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Categories;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Effects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class InteractionMechanicSandboxService(
    DantesRoleplayDbContext db,
    IInteractionMechanicOpportunityStore opportunities,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IMechanicStore mechanics,
    IMechanicEngine engine,
    IBoundedJsonSchemaValidator schemas) : IInteractionMechanicSandboxService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<InteractionMechanicSandboxValidation> ValidateAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        InteractionMechanicSandboxCandidate candidate,
        string? excludedDraftId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        stateSpaceId = Identifier(stateSpaceId, nameof(stateSpaceId));
        candidate = NormalizeCandidate(candidate);
        var catalogChecks = new List<InteractionMechanicSandboxValidationCheck>();
        MechanicRequirements? requirements = null;
        try
        {
            requirements = MechanicRequirements.Parse(candidate.RequirementsJson);
            Add(catalogChecks, "requirements-parse", true, true, "Requirements are valid JSON.");
            var problems = requirements.CompositionProblems().Concat(requirements.ProjectionProblems())
                .Concat(requirements.EventProblems()).ToArray();
            Add(catalogChecks, "requirements-contract", problems.Length == 0, true,
                problems.Length == 0 ? "Requirements use the closed mechanic contract." : string.Join(' ', problems));
        }
        catch (JsonException exception)
        {
            Add(catalogChecks, "requirements-parse", false, true,
                Bounded(exception.Message, 500));
        }
        Add(catalogChecks, "source-present", candidate.Source.Length > 0, true,
            candidate.Source.Length > 0 ? $"Candidate source contains {candidate.Source.Length} characters." : "JavaScript source is required.");
        Add(catalogChecks, "match-phrases", candidate.MatchPhrases.Count > 0, true,
            candidate.MatchPhrases.Count > 0 ? $"Candidate declares {candidate.MatchPhrases.Count} match phrase(s)." : "At least one match phrase is required.");
        Add(catalogChecks, "category-path", CategoryPath.TryValidate(candidate.Category, out var categoryProblem), true,
            string.IsNullOrEmpty(categoryProblem) ? "The category path is valid." : categoryProblem);
        var allowlistValid = candidate.EffectAllowlist.EffectTypes.All(EffectType.All.Contains)
            && candidate.EffectAllowlist.EffectTypes.Distinct(StringComparer.Ordinal).Count()
                == candidate.EffectAllowlist.EffectTypes.Count
            && candidate.EffectAllowlist.ComponentIds.Distinct(StringComparer.Ordinal).Count()
                == candidate.EffectAllowlist.ComponentIds.Count;
        Add(catalogChecks, "effect-allowlist", allowlistValid, true,
            allowlistValid ? "Effect types and component IDs use a closed declared allowlist." : "The effect allowlist is invalid or duplicated.");
        if (requirements is not null)
        {
            var ownershipMatches = requirements.EffectComponentIds.ToHashSet(StringComparer.Ordinal)
                .SetEquals(candidate.EffectAllowlist.ComponentIds);
            Add(catalogChecks, "effect-ownership", ownershipMatches, true,
                ownershipMatches ? "Declared effect components exactly match the sandbox allowlist."
                    : "Requirements effectComponentIds must exactly match the sandbox component allowlist.");
            if (requirements.InputSchema is JsonElement inputSchema)
            {
                var compiled = schemas.Compile(inputSchema.GetRawText());
                Add(catalogChecks, "input-schema", compiled.IsAccepted, true,
                    compiled.IsAccepted ? "The input schema is accepted by the bounded profile."
                        : compiled.Diagnostics.FirstOrDefault()?.Message ?? "The input schema is invalid.");
            }
        }

        var syntheticId = applicationId.Value + ".sandbox-candidate";
        try
        {
            var storeChecks = await mechanics.CheckAsync(new WriteMechanicRequest
            {
                Id = syntheticId,
                Category = candidate.Category,
                Name = candidate.Name,
                Description = candidate.Description,
                Matches = string.Join('\n', candidate.MatchPhrases),
                Requirements = candidate.RequirementsJson,
                Source = candidate.Source,
                Scope = applicationId.Value,
                Status = MechanicStatus.Draft,
                CreatedBy = "sandbox",
                ChangeNote = "Inert sandbox validation only."
            }, cancellationToken);
            catalogChecks.AddRange(storeChecks.Where(value => value.Name != "id-format"
                    && value.Name != "create-or-revise" && value.Name != "no-near-duplicate")
                .Select(value => new InteractionMechanicSandboxValidationCheck(
                    "catalog:" + value.Name, value.Passed, value.Blocking, Bounded(value.Detail, 1000))));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            Add(catalogChecks, "catalog-check", false, true, Bounded(exception.Message, 500));
        }

        var antiSprawl = await AntiSprawlAsync(applicationId, candidate, syntheticId, excludedDraftId, cancellationToken);
        var scenarioResults = new List<InteractionMechanicSandboxScenarioResult>();
        if (catalogChecks.All(value => value.Passed || !value.Blocking))
        {
            foreach (var scenario in candidate.Scenarios)
                scenarioResults.Add(await ReplayAsync(stateSpaceId, candidate, scenario, cancellationToken));
        }
        else
        {
            scenarioResults.AddRange(candidate.Scenarios.Select(value => new InteractionMechanicSandboxScenarioResult(
                value.Name, false, false, 0, 0, "", "Scenario was not run because catalog validation failed.", [])));
        }
        var passed = catalogChecks.All(value => value.Passed || !value.Blocking)
            && antiSprawl.All(value => value.Passed || !value.Blocking)
            && scenarioResults.Count == candidate.Scenarios.Count && scenarioResults.All(value => value.Passed);
        return new(passed, catalogChecks.ToArray(), antiSprawl, scenarioResults.ToArray(), DateTime.UtcNow);
    }

    public async Task<InteractionMechanicSandboxDraftProjection> CreateOrReviseAsync(
        InteractionMechanicSandboxDraftCommand command,
        InteractionMechanicSandboxWriteAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateAuthority(authority);
        var candidate = NormalizeCandidate(command.Candidate);
        var opportunity = (await opportunities.ListAsync(command.ApplicationId, 50, cancellationToken))
            .SingleOrDefault(value => value.ProposalFingerprint == command.OpportunityProposalFingerprint)
            ?? throw Conflict("MECHANIC_SANDBOX_OPPORTUNITY_NOT_FOUND",
                "The reviewed mechanic opportunity is unavailable or stale.");
        if (opportunity.ApplicationId != command.ApplicationId)
            throw Conflict("MECHANIC_SANDBOX_SCOPE_MISMATCH", "The opportunity belongs to another application.");
        var validation = await ValidateAsync(command.ApplicationId, command.StateSpaceId, candidate,
            command.DraftId, cancellationToken);
        var candidateJson = CandidateJson(candidate);
        var candidateFingerprint = Fingerprint("dantes-roleplay/mechanic-sandbox-candidate/v1", candidateJson);
        var requestFingerprint = Fingerprint("dantes-roleplay/mechanic-sandbox-write/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                applicationId = command.ApplicationId.Value,
                command.StateSpaceId,
                command.OpportunityProposalFingerprint,
                command.DraftId,
                command.ExpectedRevision,
                candidateFingerprint
            })));
        var replay = await db.InteractionMechanicSandboxDraftRevisions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ApplicationId == command.ApplicationId.Value
                && value.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (replay.RequestFingerprint != requestFingerprint)
                throw Conflict("MECHANIC_SANDBOX_IDEMPOTENCY_CONFLICT",
                    "The idempotency key is already bound to different draft content.");
            var replayDraft = await db.InteractionMechanicSandboxDrafts.AsNoTracking()
                .Include(value => value.Revisions)
                .SingleAsync(value => value.ApplicationId == command.ApplicationId.Value
                    && value.Id == replay.DraftId, cancellationToken);
            return Project(replayDraft);
        }

        var now = DateTime.UtcNow;
        InteractionMechanicSandboxDraft draft;
        int revision;
        if (command.DraftId is null)
        {
            if (command.ExpectedRevision is not null)
                throw Conflict("MECHANIC_SANDBOX_REVISION_INVALID", "A new draft cannot declare an expected revision.");
            var expired = await db.InteractionMechanicSandboxDrafts.Where(value =>
                    value.ApplicationId == command.ApplicationId.Value
                    && (value.Status == "draft" || value.Status == "validated")
                    && value.ExpiresAtUtc <= now)
                .ToArrayAsync(cancellationToken);
            foreach (var value in expired) value.Status = "expired";
            if (expired.Length > 0) await db.SaveChangesAsync(cancellationToken);
            var usedSlots = (await db.InteractionMechanicSandboxDrafts.Where(value =>
                    value.ApplicationId == command.ApplicationId.Value
                    && (value.Status == "draft" || value.Status == "validated"))
                .Select(value => value.QuotaSlot).ToArrayAsync(cancellationToken)).ToHashSet();
            var quotaSlot = Enumerable.Range(1, InteractionMechanicSandboxProtocol.MaximumActiveDraftsPerApplication)
                .FirstOrDefault(value => !usedSlots.Contains(value));
            if (quotaSlot == 0)
                throw Conflict("MECHANIC_SANDBOX_QUOTA_EXCEEDED",
                    "The application already has the maximum number of active mechanic drafts.");
            draft = new()
            {
                Id = "mechanic-sandbox-draft." + Guid.NewGuid().ToString("N"),
                ApplicationId = command.ApplicationId.Value,
                StateSpaceId = Identifier(command.StateSpaceId, nameof(command.StateSpaceId)),
                OpportunityProposalFingerprint = Hash(command.OpportunityProposalFingerprint),
                Status = validation.Passed ? "validated" : "draft",
                QuotaSlot = quotaSlot,
                CurrentRevision = 1,
                CreatedAtUtc = now,
                RevisedAtUtc = now,
                ExpiresAtUtc = now.Add(InteractionMechanicSandboxProtocol.DraftLifetime),
                ReviewPrincipalReference = authority.PrincipalReference,
                ReviewAuthorizationEvidence = authority.AuthorizationEvidenceReference
            };
            db.InteractionMechanicSandboxDrafts.Add(draft);
            revision = 1;
        }
        else
        {
            draft = await db.InteractionMechanicSandboxDrafts.Include(value => value.Revisions)
                .SingleOrDefaultAsync(value => value.ApplicationId == command.ApplicationId.Value
                    && value.Id == command.DraftId, cancellationToken)
                ?? throw Conflict("MECHANIC_SANDBOX_DRAFT_NOT_FOUND", "The mechanic sandbox draft was not found.");
            if (draft.ExpiresAtUtc <= now || draft.Status is "approved-for-export" or "expired")
                throw Conflict("MECHANIC_SANDBOX_DRAFT_TERMINAL", "The mechanic sandbox draft cannot be revised.");
            if (command.ExpectedRevision != draft.CurrentRevision)
                throw Conflict("MECHANIC_SANDBOX_REVISION_CONFLICT", "The mechanic sandbox draft revision changed.");
            if (draft.OpportunityProposalFingerprint != command.OpportunityProposalFingerprint
                || draft.StateSpaceId != command.StateSpaceId)
                throw Conflict("MECHANIC_SANDBOX_SCOPE_MISMATCH", "The draft scope or opportunity changed.");
            if (draft.CurrentRevision >= InteractionMechanicSandboxProtocol.MaximumRevisionsPerDraft)
                throw Conflict("MECHANIC_SANDBOX_REVISION_QUOTA_EXCEEDED",
                    "The mechanic sandbox draft reached its revision limit.");
            revision = draft.CurrentRevision + 1;
            draft.CurrentRevision = revision;
            draft.RevisedAtUtc = now;
            draft.Status = validation.Passed ? "validated" : "draft";
        }
        draft.Revisions.Add(new()
        {
            DraftId = draft.Id,
            ApplicationId = command.ApplicationId.Value,
            Revision = revision,
            CandidateFingerprint = candidateFingerprint,
            CandidateJson = candidateJson,
            ValidationJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(validation, Json)),
            IdempotencyKey = Identifier(command.IdempotencyKey, nameof(command.IdempotencyKey), 128),
            RequestFingerprint = requestFingerprint,
            OperationId = authority.OperationId,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return Project(draft);
    }

    public async Task<InteractionMechanicSandboxDraftProjection?> GetAsync(
        ApplicationIdentifier applicationId,
        string draftId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.InteractionMechanicSandboxDrafts.AsNoTracking().Include(value => value.Revisions)
            .SingleOrDefaultAsync(value => value.ApplicationId == applicationId.Value && value.Id == draftId,
                cancellationToken);
        return row is null ? null : Project(row);
    }

    public async Task<IReadOnlyList<InteractionMechanicSandboxDraftProjection>> ListAsync(
        ApplicationIdentifier applicationId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 50) throw Conflict("MECHANIC_SANDBOX_LIMIT_INVALID", "The draft limit is invalid.");
        var rows = await db.InteractionMechanicSandboxDrafts.AsNoTracking().Include(value => value.Revisions)
            .Where(value => value.ApplicationId == applicationId.Value)
            .OrderByDescending(value => value.RevisedAtUtc).ThenBy(value => value.Id).Take(limit)
            .ToArrayAsync(cancellationToken);
        return rows.Select(Project).ToArray();
    }

    public async Task<(InteractionMechanicSandboxDraftProjection Draft, InteractionMechanicSandboxExportPackage Export)> PromoteAsync(
        InteractionMechanicSandboxPromotionCommand command,
        InteractionMechanicSandboxWriteAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateAuthority(authority);
        var draft = await db.InteractionMechanicSandboxDrafts.Include(value => value.Revisions)
            .SingleOrDefaultAsync(value => value.ApplicationId == command.ApplicationId.Value
                && value.Id == command.DraftId, cancellationToken)
            ?? throw Conflict("MECHANIC_SANDBOX_DRAFT_NOT_FOUND", "The mechanic sandbox draft was not found.");
        if (draft.PromotionIdempotencyKey == command.IdempotencyKey)
        {
            if (draft.PromotionRequestFingerprint != PromotionFingerprint(command))
                throw Conflict("MECHANIC_SANDBOX_IDEMPOTENCY_CONFLICT",
                    "The promotion idempotency key is bound to another request.");
            var replay = Project(draft);
            return (replay, Export(replay));
        }
        if (draft.ExpiresAtUtc <= DateTime.UtcNow || draft.Status != "validated")
            throw Conflict("MECHANIC_SANDBOX_PROMOTION_BLOCKED",
                "Only a current fully validated draft may be approved for export.");
        if (draft.CurrentRevision != command.ExpectedRevision || draft.StateSpaceId != command.StateSpaceId)
            throw Conflict("MECHANIC_SANDBOX_REVISION_CONFLICT", "The mechanic sandbox draft revision or scope changed.");
        var projection = Project(draft);
        var currentValidation = await ValidateAsync(command.ApplicationId, command.StateSpaceId,
            projection.Candidate, draft.Id, cancellationToken);
        if (!currentValidation.Passed)
            throw Conflict("MECHANIC_SANDBOX_PROMOTION_BLOCKED",
                "Catalog, anti-sprawl, and scenario validation must all pass before promotion.");
        draft.Revisions.Single(value => value.Revision == draft.CurrentRevision).ValidationJson =
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(currentValidation, Json));
        draft.Status = "approved-for-export";
        draft.PromotionPrincipalReference = authority.PrincipalReference;
        draft.PromotionAuthorizationEvidence = authority.AuthorizationEvidenceReference;
        draft.PromotedAtUtc = DateTime.UtcNow;
        draft.PromotionIdempotencyKey = Identifier(command.IdempotencyKey, nameof(command.IdempotencyKey), 128);
        draft.PromotionRequestFingerprint = PromotionFingerprint(command);
        draft.PromotionOperationId = authority.OperationId;
        await db.SaveChangesAsync(cancellationToken);
        projection = Project(draft);
        return (projection, Export(projection));
    }

    private async Task<IReadOnlyList<InteractionMechanicSandboxValidationCheck>> AntiSprawlAsync(
        ApplicationIdentifier applicationId,
        InteractionMechanicSandboxCandidate candidate,
        string syntheticId,
        string? excludedDraftId,
        CancellationToken cancellationToken)
    {
        if (!snapshots.TryGetSnapshot(applicationId, out var snapshot))
            return [new("anti-sprawl:catalog", false, true, "The active application catalog is unavailable.")];
        try
        {
            var authored = snapshot.Documents.Where(value => value.Trust == SourceTrust.Trusted
                    && value.Record.Kind == "mechanic" && value.Record.Status == "active")
                .Select(value => ToMechanicFile(value.Record)).Where(value => value is not null)
                .Select(value => CatalogAntiSprawlMechanic.Create(value!, value!.Id)).ToList();
            var currentDrafts = await db.InteractionMechanicSandboxDrafts.AsNoTracking()
                .Include(value => value.Revisions)
                .Where(value => value.ApplicationId == applicationId.Value && value.Id != excludedDraftId
                    && (value.Status == "draft" || value.Status == "validated")
                    && value.ExpiresAtUtc > DateTime.UtcNow)
                .ToArrayAsync(cancellationToken);
            foreach (var current in currentDrafts)
            {
                var revision = current.Revisions.Single(value => value.Revision == current.CurrentRevision);
                var existing = JsonSerializer.Deserialize<InteractionMechanicSandboxCandidate>(revision.CandidateJson, Json)!;
                var existingFile = new MechanicFile(current.Id, existing.Category, existing.Name,
                    existing.Description, string.Join('\n', existing.MatchPhrases), existing.RequirementsJson,
                    existing.Source, applicationId.Value, MechanicStatus.Draft, "sandbox", "Inert sandbox candidate.");
                authored.Add(CatalogAntiSprawlMechanic.Create(existingFile, current.Id));
            }
            var draftFile = new MechanicFile(syntheticId, candidate.Category, candidate.Name,
                candidate.Description, string.Join('\n', candidate.MatchPhrases), candidate.RequirementsJson,
                candidate.Source, applicationId.Value, MechanicStatus.Draft, "sandbox", "Inert sandbox candidate.");
            authored.Add(CatalogAntiSprawlMechanic.Create(draftFile, syntheticId));
            var findings = CatalogAntiSprawlAnalyzer.Analyze(authored, []).Findings
                .Where(value => value.Left.QualifiedId == syntheticId || value.Right.QualifiedId == syntheticId)
                .ToArray();
            if (findings.Length == 0)
                return [new("anti-sprawl:no-overlap", true, true, "No deterministic or high-confidence fuzzy overlap was found.")];
            return findings.Select(value => new InteractionMechanicSandboxValidationCheck(
                "anti-sprawl:" + value.Code.ToLowerInvariant(),
                false,
                value.Classification != "fuzzy",
                Bounded(value.Summary, 1000))).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return [new("anti-sprawl:validation", false, true, Bounded(exception.Message, 500))];
        }
    }

    private async Task<InteractionMechanicSandboxScenarioResult> ReplayAsync(
        string stateSpaceId,
        InteractionMechanicSandboxCandidate candidate,
        InteractionMechanicSandboxScenario scenario,
        CancellationToken cancellationToken)
    {
        try
        {
            var projection = JsonSerializer.Deserialize<MechanicProjection>(scenario.ProjectionJson, Json)
                ?? throw new JsonException("The captured projection is missing.");
            projection = projection with { StateSpaceId = stateSpaceId, Execution = null };
            var result = await engine.RunAsync(candidate.Source, projection, Limits(candidate.Limits), cancellationToken);
            var effects = result.Output.Effects;
            var allowlisted = effects.All(value => candidate.EffectAllowlist.EffectTypes.Contains(value.Type, StringComparer.Ordinal)
                && (!value.Type.StartsWith("component.", StringComparison.Ordinal)
                    || candidate.EffectAllowlist.ComponentIds.Contains(value.DefinitionId, StringComparer.Ordinal)));
            var expected = scenario.Expected;
            var actualTypes = effects.Select(value => value.Type).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var actualComponents = effects.Where(value => value.Type.StartsWith("component.", StringComparison.Ordinal))
                .Select(value => value.DefinitionId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var passed = result.Ok == expected.Successful && allowlisted
                && effects.Count >= expected.MinimumEffects && effects.Count <= expected.MaximumEffects
                && actualTypes.SetEquals(expected.EffectTypes) && actualComponents.SetEquals(expected.ComponentIds)
                && (string.IsNullOrEmpty(expected.NarrationContains)
                    || result.Output.Narration.Contains(expected.NarrationContains, StringComparison.OrdinalIgnoreCase));
            var summary = !allowlisted ? "The candidate emitted an effect outside its declared allowlist."
                : passed ? "The captured scenario replay matched every focused expectation."
                : result.Ok ? "The sandbox ran, but its output did not match the focused expectation."
                : Bounded(result.Error, 500);
            return new(scenario.Name, passed, result.Ok, effects.Count, result.ElapsedMilliseconds,
                result.LimitHit, summary, effects.Select(value => new InteractionMechanicSandboxEffectPreview(
                    value.Type, value.EntityId, value.DefinitionId, value.ToEntityId, value.Kind,
                    value.Slot, value.Name, value.Data)).ToArray());
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new(scenario.Name, false, false, 0, 0, "", Bounded(exception.Message, 500), []);
        }
    }

    private static InteractionMechanicSandboxDraftProjection Project(InteractionMechanicSandboxDraft row)
    {
        var revision = row.Revisions.OrderByDescending(value => value.Revision).First();
        var candidate = JsonSerializer.Deserialize<InteractionMechanicSandboxCandidate>(revision.CandidateJson, Json)!;
        var storedValidation = JsonSerializer.Deserialize<InteractionMechanicSandboxValidation>(revision.ValidationJson, Json)!;
        var validation = storedValidation with
        {
            ScenarioResults = storedValidation.ScenarioResults.Select(value => value with
            {
                EffectPreviews = value.EffectPreviews ?? []
            }).ToArray()
        };
        var status = row.ExpiresAtUtc <= DateTime.UtcNow && row.Status is "draft" or "validated"
            ? "expired" : row.Status;
        return new(row.Id, ApplicationIdentifier.Parse(row.ApplicationId), row.StateSpaceId,
            row.OpportunityProposalFingerprint, row.CurrentRevision, revision.CandidateFingerprint, status,
            row.CreatedAtUtc, row.RevisedAtUtc, row.ExpiresAtUtc, candidate, validation,
            row.ReviewPrincipalReference, row.ReviewAuthorizationEvidence,
            string.IsNullOrEmpty(row.PromotionPrincipalReference) ? null : row.PromotionPrincipalReference,
            string.IsNullOrEmpty(row.PromotionAuthorizationEvidence) ? null : row.PromotionAuthorizationEvidence,
            row.PromotedAtUtc);
    }

    private static InteractionMechanicSandboxExportPackage Export(InteractionMechanicSandboxDraftProjection value) =>
        new(value.DraftId, value.Revision, value.CandidateFingerprint, value.OpportunityProposalFingerprint,
            value.Candidate, value.Validation, PermanentIdRequired: true,
            FilesystemWritePerformed: false, Activated: false);

    private static InteractionMechanicSandboxCandidate NormalizeCandidate(InteractionMechanicSandboxCandidate value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var name = Text(value.Name, 200, nameof(value.Name));
        var category = Text(value.Category, 200, nameof(value.Category));
        var description = Text(value.Description, 2_000, nameof(value.Description));
        var phrases = value.MatchPhrases.Select(item => Text(item, 200, "matchPhrase"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (phrases.Length is < 1 or > 8 || value.Source.Length is < 1 or > InteractionMechanicSandboxProtocol.MaximumSourceLength
            || value.Scenarios.Count is < 1 or > InteractionMechanicSandboxProtocol.MaximumScenarios)
            throw Conflict("MECHANIC_SANDBOX_CANDIDATE_INVALID", "The candidate is outside the sandbox bounds.");
        var requirements = InteractionCanonicalJson.CanonicalizeObject(value.RequirementsJson);
        var allowlist = new InteractionMechanicSandboxEffectAllowlist(
            value.EffectAllowlist.EffectTypes.Order(StringComparer.Ordinal).ToArray(),
            value.EffectAllowlist.ComponentIds.Order(StringComparer.Ordinal).ToArray());
        var limits = value.Limits;
        if (limits.MaxStatements is < 1 or > 50_000 || limits.TimeoutMilliseconds is < 1 or > 1_000
            || limits.MemoryBytes is < 1 or > 4 * 1024 * 1024 || limits.MaxRecursionDepth is < 1 or > 32
            || limits.MaxEffects is < 0 or > 50 || limits.MaxEvents is < 0 or > 10
            || limits.MaxNotifications is < 0 or > 10 || limits.MaxLogLines is < 0 or > 50)
            throw Conflict("MECHANIC_SANDBOX_LIMITS_INVALID", "Candidate execution limits exceed the sandbox ceiling.");
        var scenarios = value.Scenarios.Select(item => NormalizeScenario(item, limits)).ToArray();
        return value with { Name = name, Category = category, Description = description,
            MatchPhrases = phrases, RequirementsJson = requirements, Source = value.Source.Trim(),
            EffectAllowlist = allowlist, Scenarios = scenarios };
    }

    private static InteractionMechanicSandboxScenario NormalizeScenario(
        InteractionMechanicSandboxScenario value,
        InteractionMechanicSandboxLimits limits)
    {
        var name = Text(value.Name, 100, nameof(value.Name));
        if (!value.Expected.Successful || value.ProjectionJson.Length is < 2 or > 65_536
            || value.Expected.MinimumEffects < 0
            || value.Expected.MaximumEffects < value.Expected.MinimumEffects
            || value.Expected.MaximumEffects > limits.MaxEffects)
            throw Conflict("MECHANIC_SANDBOX_SCENARIO_INVALID", "A captured test scenario is outside the closed bounds.");
        var projection = InteractionCanonicalJson.CanonicalizeObject(value.ProjectionJson);
        return value with { Name = name, ProjectionJson = projection };
    }

    private static MechanicFile? ToMechanicFile(CatalogRecordDefinition record)
    {
        using var document = JsonDocument.Parse(record.ContentJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("requirements", out var requirements)
            || !root.TryGetProperty("source", out var source)) return null;
        var requirementsText = requirements.ValueKind == JsonValueKind.String
            ? requirements.GetString()! : requirements.GetRawText();
        var sourceText = source.ValueKind == JsonValueKind.String ? source.GetString()! : "";
        var category = String(root, "category", "application.sandbox");
        var scope = String(root, "scope", "");
        return new(record.QualifiedId, category, record.Name, record.Description,
            string.Join('\n', record.MatchPhrases), requirementsText, sourceText, scope,
            record.Status == "active" ? MechanicStatus.Active : MechanicStatus.Draft);
    }

    private static string String(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : fallback;

    private static ExecutionLimits Limits(InteractionMechanicSandboxLimits value) => new()
    {
        MaxStatements = value.MaxStatements,
        Timeout = TimeSpan.FromMilliseconds(value.TimeoutMilliseconds),
        MemoryBytes = value.MemoryBytes,
        MaxRecursionDepth = value.MaxRecursionDepth,
        MaxEffects = value.MaxEffects,
        MaxEvents = value.MaxEvents,
        MaxNotifications = value.MaxNotifications,
        MaxLogLines = value.MaxLogLines
    };

    private static string CandidateJson(InteractionMechanicSandboxCandidate value) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(value, Json));

    private static string PromotionFingerprint(InteractionMechanicSandboxPromotionCommand value) =>
        Fingerprint("dantes-roleplay/mechanic-sandbox-promotion/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                applicationId = value.ApplicationId.Value,
                value.StateSpaceId,
                value.DraftId,
                value.ExpectedRevision
            })));

    private static string Fingerprint(string domain, string canonical) =>
        InteractionCanonicalJson.Fingerprint(domain, canonical);

    private static void ValidateAuthority(InteractionMechanicSandboxWriteAuthority value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!DantesRoleplay.Authorization.TrustedPrincipalContext.IsValidPrincipalId(value.PrincipalReference))
            throw Conflict("MECHANIC_SANDBOX_AUTHORIZATION_REQUIRED", "A verified principal is required.");
        Identifier(value.AuthorizationEvidenceReference, nameof(value.AuthorizationEvidenceReference));
        Identifier(value.RequestToken, nameof(value.RequestToken), 128);
        Identifier(value.OperationId, nameof(value.OperationId), 200);
        Text(value.Intent, 2_000, nameof(value.Intent));
    }

    private static void Add(List<InteractionMechanicSandboxValidationCheck> target,
        string name, bool passed, bool blocking, string summary) =>
        target.Add(new(name, passed, blocking, Bounded(summary, 1000)));

    private static string Identifier(string value, string name, int maximum = 200)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximum
            || value.Any(char.IsControl))
            throw Conflict("MECHANIC_SANDBOX_IDENTIFIER_INVALID", $"{name} is invalid.");
        return value;
    }

    private static string Text(string value, int maximum, string name)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (normalized.Length < 1 || normalized.Length > maximum || normalized.Any(character => char.IsControl(character)
                && character is not ('\r' or '\n' or '\t')))
            throw Conflict("MECHANIC_SANDBOX_TEXT_INVALID", $"{name} is outside its safe bounds.");
        return normalized;
    }

    private static string Hash(string value)
    {
        if (value is not { Length: 64 } || value.Any(character => !(char.IsAsciiDigit(character)
                || character is >= 'A' and <= 'F')))
            throw Conflict("MECHANIC_SANDBOX_FINGERPRINT_INVALID", "A required fingerprint is invalid.");
        return value;
    }

    private static string Bounded(string value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? "Validation failed without a safe detail."
            : value.Length <= maximum ? value : value[..maximum];

    private static InteractionContractException Conflict(string code, string message) => new(code, message);
}
