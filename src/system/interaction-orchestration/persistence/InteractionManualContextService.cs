using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Interactions;

/// <summary>
/// Host-facing, read-only context derivation. Registration and wire contracts remain integration-owned.
/// This service never publishes associations, selects a write, or substitutes for execution validation.
/// </summary>
public sealed class InteractionManualContextService(
    IProcedureStore procedures,
    IInteractionFeatureRetriever features,
    IStandingGrantPolicy grants,
    IStandingGrantTargetResolver targets,
    IApplicationDefinitionChangeReader changes,
    IReadOnlyCollection<string> permittedGlobalCategories,
    IInteractionRecipeStore? recipes = null) : IInteractionManualContextService
{
    private const int MaximumSources = 128;
    private readonly string[] _globalCategories = CopyCategories(permittedGlobalCategories);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<InteractionRetrievalLane>(JsonNamingPolicy.CamelCase) }
    };

    public Task<InteractionInvocationResult> DiscoverAsync(InteractionManualContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DiscoverAsync(request.Host, request.Intent, request.KnownInputJson, request.MaximumCharacters,
            request.ExpectedResolutionFingerprint, cancellationToken);
    }

    public async Task<InteractionInvocationResult> DiscoverAsync(InteractionInvocationHost host, string intent,
        string knownInputJson = "{}", int maximumCharacters = 16_000, string? expectedResolutionFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(host);
            if (host.Profile != InteractionExecutionProfile.ReadOnly)
                return InteractionInvocationResult.Unavailable("MANUAL_PROFILE_UNSUPPORTED", "Manual discovery requires a read-only invocation.");
            if (host.Budget.DeadlineUtc <= DateTime.UtcNow)
                return InteractionInvocationResult.Cancelled("INVOCATION_DEADLINE_EXCEEDED", "The discovery deadline elapsed.");
            if (!host.Budget.TryConsumeOperation())
                return InteractionInvocationResult.Failed("INVOCATION_BUDGET_EXHAUSTED", "The discovery operation budget is exhausted.");
            if (features is not InteractionFeatureRetriever retriever || procedures is not ProcedureStore manualStore)
                return InteractionInvocationResult.Unavailable("MANUAL_ADAPTER_UNAVAILABLE", "The authorized discovery owners are unavailable.");
            if (maximumCharacters is < 4000 or > 24_000)
                return InteractionInvocationResult.Failed("MANUAL_CONTEXT_LIMIT", "The context size must be between 4000 and 24000 characters.");
            if (expectedResolutionFingerprint is not null && (expectedResolutionFingerprint.Length != 64
                || expectedResolutionFingerprint.Any(value => !char.IsAsciiHexDigitUpper(value))))
                return InteractionInvocationResult.Failed("MANUAL_EVIDENCE_INVALID", "Expected resolution evidence must be an uppercase SHA-256 fingerprint.");
            var search = new InteractionFeatureSearchInput(intent, 8);
            var canonicalInput = InteractionCanonicalJson.CanonicalizeObject(knownInputJson);
            if (canonicalInput.Length > 2000)
                return InteractionInvocationResult.Failed("MANUAL_INPUT_LIMIT", "Known discovery input exceeds 2000 characters.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) deadline.Cancel();
            else if (remaining.TotalMilliseconds <= int.MaxValue) deadline.CancelAfter(remaining);
            var token = deadline.Token;
            var change = changes.CurrentChange(host.ApplicationRevision.ApplicationId);
            var featureResult = await retriever.SearchAuthorizedAsync(new(host.ApplicationRevision.ApplicationId,
                InteractionRetrievalLane.TrustedFeature), search,
                (reference, cancellation) => CanReadAsync(host, Selection(reference), cancellation), token);
            var selectedTargets = new Dictionary<string, StandingGrantDefinitionReference>(StringComparer.Ordinal);
            var candidates = new JsonArray();
            var sections = new List<ManualCandidate>();
            var sourceEvidence = new List<string>();
            var globalSources = new Dictionary<string, string>(StringComparer.Ordinal);
            var exact = new List<string>();
            var bounded = false;
            var globalManual = await manualStore.ReadOperationalManualAsync(_globalCategories, MaximumSources + 1, token);
            bounded = globalManual.Count > MaximumSources;
            foreach (var detail in globalManual.Take(MaximumSources))
            {
                var sourceFingerprint = ProcedureManualSections.Fingerprint(detail);
                globalSources.Add(detail.Id, sourceFingerprint);
                sourceEvidence.Add(sourceFingerprint);
                var isExact = PhraseMatches(search.Query, detail.Id, detail.Matches);
                if (isExact) exact.Add(detail.Id);
                AddSections(detail.Id, detail.Version, sourceFingerprint, detail.Governs, detail.Instructions,
                    detail.Constraints, detail.Name + " " + detail.Description + " " + detail.Matches, isExact,
                    string.IsNullOrEmpty(detail.SourceHash) ? null : detail.SourceHash,
                    ProcedureManualSections.Hash(detail.Matches));
            }
            foreach (var hit in featureResult.Hits)
            {
                selectedTargets[hit.Reference.QualifiedId] = Selection(hit.Reference);
                using var document = JsonDocument.Parse(hit.ContractJson);
                var root = document.RootElement;
                candidates.Add(JsonSerializer.SerializeToNode(new
                {
                    reference = InteractionManualTargetReference.From(hit.Reference), hit.Name, reason = hit.Exact ? "exact-authored-match" : "retrieval-candidate",
                    prerequisites = Text(root, "requirements"),
                    missingInputs = MissingInputs(root, canonicalInput),
                    inputValidation = "required-at-execution"
                }, JsonOptions));
                if (hit.Exact) exact.Add(hit.Reference.QualifiedId);
                if (hit.Reference.Kind == "procedure")
                    AddSections(hit.Reference.QualifiedId, hit.Reference.Version, hit.Reference.ContentFingerprint,
                        Text(root, "governs"), Text(root, "instructions"), Text(root, "constraints"),
                        hit.Name + " " + hit.Description, hit.Exact, null, ProcedureManualSections.Hash(Text(root, "matches")));
            }
            var reusable = new JsonArray();
            if (recipes is InteractionRecipeStore recipeStore)
            {
                foreach (var recipe in await recipeStore.SearchAuthorizedAsync(host.ApplicationRevision.ApplicationId,
                    search.Query, async (candidate, cancellation) =>
                    {
                        foreach (var step in candidate.Template.Steps)
                            if (!await CanReadAsync(host, Selection(step), cancellation)) return false;
                        return true;
                    }, 4, token))
                {
                    // A verified recipe remains merely a candidate until every exact step is revalidated.
                    foreach (var step in recipe.Template.Steps) selectedTargets[step.QualifiedId] = Selection(step);
                    reusable.Add(JsonSerializer.SerializeToNode(new
                    {
                        reference = recipe.Reference, compatible = true,
                        resolution = "validate-bindings-and-steps",
                        steps = recipe.Template.Steps.Select(step => new
                        { step.QualifiedId, step.ContractVersion, step.ContractFingerprint, step.InputBindings })
                    }, JsonOptions));
                }
            }
            token.ThrowIfCancellationRequested();
            var currentChange = changes.CurrentChange(host.ApplicationRevision.ApplicationId);
            if (currentChange?.Fingerprint != change?.Fingerprint || currentChange?.Revision != change?.Revision)
                return InteractionInvocationResult.Failed("MANUAL_DEFINITION_CHANGED", "The active definition changed during discovery; resolve again.");
            // Recheck global rows before emitting a complete packet; a concurrent revision must not
            // mix one procedure's old instructions with a newer association or lifecycle.
            foreach (var source in globalSources)
            {
                var current = await procedures.GetAsync(source.Key, cancellationToken: token);
                if (current is null || ProcedureManualSections.Fingerprint(current) != source.Value)
                    return InteractionInvocationResult.Failed("MANUAL_DEFINITION_CHANGED", "A manual source changed during discovery; resolve again.");
            }
            foreach (var selected in selectedTargets.Values)
                if (!await CanReadAsync(host, selected, token))
                    return InteractionInvocationResult.Failed("MANUAL_AUTHORITY_CHANGED", "Discovery authority changed; resolve again.");
            token.ThrowIfCancellationRequested();
            currentChange = changes.CurrentChange(host.ApplicationRevision.ApplicationId);
            if (currentChange?.Fingerprint != change?.Fingerprint || currentChange?.Revision != change?.Revision)
                return InteractionInvocationResult.Failed("MANUAL_DEFINITION_CHANGED", "The active definition changed during discovery; resolve again.");
            var selectedSections = sections.OrderByDescending(value => value.Score)
                .ThenBy(value => value.Section.Reference, StringComparer.Ordinal).Take(8).ToList();
            bounded |= sections.Count > selectedSections.Count;
            var fingerprint = ProcedureManualSections.Hash(JsonSerializer.Serialize(new
            {
                format = ProcedureManualSections.Format, host.Principal.PrincipalId, host.GrantReference,
                applicationId = host.ApplicationRevision.ApplicationId.Value,
                intent = search.Query, canonicalInput,
                sources = sourceEvidence.Order(StringComparer.Ordinal),
                candidates, reusable, sections = selectedSections.Select(value => new
                { value.SourceFingerprint, value.Section.Reference, value.Section.ContentFingerprint })
            }, JsonOptions));
            var packet = new JsonObject
            {
                ["resolution"] = expectedResolutionFingerprint is not null && expectedResolutionFingerprint != fingerprint
                    ? "refresh-required" : exact.Distinct().Count() > 1 ? "ambiguous" : "unresolved",
                ["resolutionFingerprint"] = fingerprint,
                ["resultFingerprint"] = new string('0', 64),
                ["intent"] = search.Query,
                ["knownInputs"] = JsonNode.Parse(canonicalInput),
                ["candidates"] = candidates,
                ["reusableTasks"] = reusable,
                ["selectedAction"] = null,
                ["manualSections"] = JsonSerializer.SerializeToNode(selectedSections.Select(value => new
                {
                    value.ProcedureId, value.Version, value.SourceFingerprint, value.StoredSourceHash,
                    value.AssociationFingerprint, value.Governs,
                    section = value.Section
                }), JsonOptions),
                ["reuseDecision"] = "Inspect existing action, parameter binding, revision or composition before authoring; equivalence is not established by retrieval.",
                ["publication"] = "unavailable: standing author-and-activate grants are not integrated",
                ["retrievalMode"] = featureResult.Mode.ToString(),
                ["retrievalAvailability"] = featureResult.AvailabilityCode,
                ["bounded"] = bounded,
                ["nextSteps"] = new JsonArray("Inspect a candidate's exact callable contract and full manual by ID and revision.",
                    "Supply missing inputs and resolve ambiguity; revalidate scope, grants and exact revisions at execution.")
            };
            // Bound serialized content, including JSON escaping; never slice JSON or claim omitted
            // constraints have been validated. Exact source reads remain the recovery path.
            var manual = (JsonArray)packet["manualSections"]!;
            while (packet.ToJsonString().Length > maximumCharacters && manual.Count > 0)
            { manual.RemoveAt(manual.Count - 1); packet["bounded"] = true; }
            while (packet.ToJsonString().Length > maximumCharacters && reusable.Count > 0)
            { reusable.RemoveAt(reusable.Count - 1); packet["bounded"] = true; }
            while (packet.ToJsonString().Length > maximumCharacters && candidates.Count > 0)
            { candidates.RemoveAt(candidates.Count - 1); packet["bounded"] = true; }
            var resultFingerprint = ProcedureManualSections.Hash(InteractionCanonicalJson.CanonicalizeObject(packet.ToJsonString()));
            packet["resultFingerprint"] = resultFingerprint;
            var json = packet.ToJsonString();
            if (json.Length > maximumCharacters)
                return InteractionInvocationResult.Failed("MANUAL_CONTEXT_LIMIT", "Use a smaller known input to fit the context budget.");
            token.ThrowIfCancellationRequested();
            return InteractionInvocationResult.CompletedComputation(json, "manual.context." + resultFingerprint.ToLowerInvariant());

            void AddSections(string id, int version, string sourceFingerprint, string governs,
                string instructions, string constraints, string metadata, bool isExact, string? storedSourceHash,
                string associationFingerprint)
            {
                if (instructions.Length + constraints.Length > 64_000) { bounded = true; return; }
                foreach (var section in ProcedureManualSections.Derive(id, instructions, constraints))
                {
                    var score = isExact ? 1000 : Score(search.Query,
                        metadata + " " + governs + " " + section.ParentContext + " " + section.Text);
                    if (score > 0) sections.Add(new(id, version, sourceFingerprint, storedSourceHash,
                        associationFingerprint, governs, section, score));
                }
            }
        }
        catch (OperationCanceledException)
        { return InteractionInvocationResult.Cancelled("INVOCATION_CANCELLED", "Manual discovery was cancelled."); }
        catch (InteractionContractException exception)
        { return InteractionInvocationResult.Failed(exception.Code, "The discovery request is invalid."); }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        { return InteractionInvocationResult.Failed("MANUAL_CONTEXT_INVALID", "The discovery input or source is invalid."); }
        catch
        { return InteractionInvocationResult.Unavailable("MANUAL_CONTEXT_UNAVAILABLE", "An authoritative discovery dependency is unavailable; retry exact manual inspection."); }
    }

    private async Task<bool> CanReadAsync(InteractionInvocationHost host, StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolution = await targets.ResolveAsync(host, selection, cancellationToken);
            var target = resolution.Target;
            if (resolution.Status != StandingGrantTargetResolutionStatus.Available || target is null
                || target.DefinitionId != selection.DefinitionId || target.Kind != selection.Kind
                || target.Revision != selection.Revision || target.ContentFingerprint != selection.ContentFingerprint
                || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId) return false;
            var decision = await grants.EvaluateAsync(host,
                new(StandingGrantCapability.Read, StandingGrantScope.Application, [target], []), cancellationToken);
            var grant = decision.Grant;
            return decision.Allowed && grant is not null && grant.GrantReference == host.GrantReference
                && grant.PrincipalReference == host.Principal.PrincipalId && grant.ApplicationId == host.ApplicationRevision.ApplicationId
                && grant.Scope == StandingGrantScope.Application && grant.StateSpaceId is null
                && grant.Capabilities.Contains(StandingGrantCapability.Read) && !grant.Revoked && grant.ExpiresAtUtc > DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; } // Unsupported/denied targets are indistinguishable from absent targets.
    }

    private static StandingGrantDefinitionReference Selection(InteractionFeatureReference reference) =>
        new(reference.QualifiedId, reference.Kind, reference.Version, reference.ContentFingerprint);
    private static StandingGrantDefinitionReference Selection(InteractionRecipeTemplateStep step) =>
        new(step.QualifiedId, step.Kind == InteractionPlanStepKind.Query ? "query" : "mechanic", step.ContractVersion, step.ContractFingerprint);

    private static string[] CopyCategories(IReadOnlyCollection<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (categories.Count > 16 || categories.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Global manual categories require a bounded explicit host allow-list.");
        return categories.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static bool PhraseMatches(string query, string id, string matches) => id == query
        || matches.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(value => Normalize(value) == Normalize(query));
    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    private static int Score(string query, string text) => query.Split([' ', '.', '-', ','], StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    private static string[] MissingInputs(JsonElement root, string input)
    {
        using var known = JsonDocument.Parse(input);
        if (!root.TryGetProperty("inputSchema", out var schema) || schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array) return [];
        return required.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!).Where(value => !known.RootElement.TryGetProperty(value, out _)).Take(32).ToArray();
    }
    private sealed record ManualCandidate(string ProcedureId, int Version, string SourceFingerprint,
        string? StoredSourceHash, string AssociationFingerprint, string Governs,
        ProcedureManualSections.Section Section, int Score);
}
