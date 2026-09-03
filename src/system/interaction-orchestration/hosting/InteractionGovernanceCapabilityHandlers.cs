using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>Descriptor-backed operator views over interaction learning and planning evidence.</summary>
public sealed class InteractionGovernanceReadCapabilityHandler(
    string capabilityId,
    IInteractionRecipeStore recipes,
    IInteractionMechanicOpportunityStore opportunities,
    IInteractionEnvelopeFactory envelopes,
    IInteractionTaskContextMaterializer contextPacks) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = capabilityId switch
    {
        SystemCapabilityIds.InteractionRecipes => new(
            capabilityId, 1, "interaction-orchestration",
            "Inspect application-scoped learned recipes, their supporting receipts, and evidence-derived replay performance.",
            SystemCapabilityMode.Read, GovernanceSchemas.RecipeReadInput, GovernanceSchemas.RecipeReadOutput,
            ["procedure.system.inspect"], PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false),
        SystemCapabilityIds.MechanicOpportunities => new(
            capabilityId, 1, "interaction-orchestration",
            "Inspect inert mechanic-opportunity proposals, supporting receipts, estimated call reduction, exact dependencies, and overlap candidates.",
            SystemCapabilityMode.Read, GovernanceSchemas.OpportunityReadInput, GovernanceSchemas.OpportunityReadOutput,
            ["procedure.system.inspect"], PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false),
        SystemCapabilityIds.InteractionContextPack => new(
            capabilityId, 1, "interaction-orchestration",
            "Materialize and inspect the same bounded, authorized, revision-bound task-context pack supplied to an interaction planner.",
            SystemCapabilityMode.Read, GovernanceSchemas.ContextReadInput, GovernanceSchemas.ContextReadOutput,
            ["procedure.system.inspect"], PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false),
        _ => throw new InvalidOperationException("Unknown interaction governance read capability.")
    };

    public Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        CancellationToken cancellationToken = default) =>
        ReadAsync(input, new(TrustedPrincipalContext.Unauthenticated("CONTEXT_REQUIRED"), "invalid", "invalid"),
            cancellationToken);

    public async Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var application = ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!);
            if (capabilityId == SystemCapabilityIds.InteractionRecipes)
                return SystemCapabilityHandlerResult.Success(await ReadRecipes(application, input, cancellationToken));
            if (capabilityId == SystemCapabilityIds.MechanicOpportunities)
            {
                var limit = input.TryGetProperty("limit", out var limitValue) ? limitValue.GetInt32() : 20;
                var values = await opportunities.ListAsync(application, limit, cancellationToken);
                if (input.TryGetProperty("proposalFingerprint", out var fingerprint))
                    values = values.Where(value => value.ProposalFingerprint == fingerprint.GetString()).ToArray();
                return SystemCapabilityHandlerResult.Success(GovernanceSchemas.Opportunities(values));
            }

            if (!context.Principal.Verified)
                return SystemCapabilityHandlerResult.Failure("INTERACTION_CONTEXT_PRINCIPAL_REQUIRED",
                    "A verified private operator principal is required to inspect a task context.",
                    "Retry through an authenticated private operator route.");
            var stateSpaceId = input.GetProperty("stateSpaceId").GetString()!;
            var sessionContextId = input.GetProperty("sessionContextId").GetString()!;
            var role = input.TryGetProperty("role", out var roleValue) ? roleValue.GetString()! : "outer";
            var intentText = input.GetProperty("intentText").GetString()!;
            var intentJson = JsonSerializer.Serialize(new
            {
                idempotencyKey = "context-inspect." + GovernanceSchemas.Hash(
                    application.Value + "\n" + stateSpaceId + "\n" + sessionContextId + "\n" + intentText)[..32]
                    .ToLowerInvariant(),
                intentText,
                maximumPlanSteps = 16,
                plannerPreference = "local"
            });
            var envelope = envelopes.Create(context.Principal, application, stateSpaceId, sessionContextId,
                intentJson, role switch
                {
                    "inner" => InteractionAiRole.Inner,
                    "outer" => InteractionAiRole.Outer,
                    "direct" => InteractionAiRole.Direct,
                    _ => throw new InteractionContractException("INVALID_AI_ROLE", "The context-pack role is invalid.")
                });
            var request = new InteractionAuthorizationRequest(context.Principal, application, stateSpaceId,
                InteractionCapability.Plan, "system.interaction-context-pack");
            var pack = await contextPacks.MaterializeAsync(envelope, request, cancellationToken);
            return SystemCapabilityHandlerResult.Success(GovernanceSchemas.Context(pack));
        }
        catch (Exception exception) when (exception is InteractionContractException or InteractionTaskContextException
            or ArgumentException or JsonException)
        {
            return SystemCapabilityHandlerResult.Failure("INTERACTION_GOVERNANCE_READ_INVALID",
                GovernanceSchemas.Safe(exception.Message),
                "Use the current capability schema and an authorized application and state-space scope.");
        }
    }

    private async Task<JsonElement> ReadRecipes(
        ApplicationIdentifier application,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        InteractionRecipeStatus? status = input.TryGetProperty("status", out var statusValue)
            ? InteractionRecipeStatusNames.Parse(statusValue.GetString()!) : null;
        var limit = input.TryGetProperty("limit", out var limitValue) ? limitValue.GetInt32() : 20;
        IReadOnlyList<InteractionRecipeProjection> values;
        if (input.TryGetProperty("recipeId", out var id))
        {
            var value = await recipes.GetAsync(application, id.GetString()!, cancellationToken);
            values = value is null || status is not null && value.Status != status ? [] : [value];
        }
        else if (input.TryGetProperty("query", out var query))
            values = (await recipes.SearchPageAsync(application, query.GetString()!, status, 0, limit,
                cancellationToken)).Items;
        else if (status is not null)
            values = await recipes.ListAsync(application, status.Value, limit, cancellationToken);
        else
        {
            var collected = new List<InteractionRecipeProjection>();
            foreach (var recipeStatus in Enum.GetValues<InteractionRecipeStatus>())
                collected.AddRange(await recipes.ListAsync(application, recipeStatus, limit, cancellationToken));
            values = collected.OrderByDescending(value => value.RevisedAtUtc)
                .ThenBy(value => value.Reference.Id, StringComparer.Ordinal).Take(limit).ToArray();
        }
        return GovernanceSchemas.Recipes(values);
    }
}

public sealed class InteractionRecipeReviewCapabilityHandler(
    IInteractionRecipeStore recipes,
    IInteractionRecipeReviewService reviews) : ISystemWriteCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.InteractionRecipeReview, 1, "interaction-orchestration",
        "Verify a current candidate recipe or retire a candidate, verified, or stale recipe. Retirement is the explicit rejection route for an unaccepted candidate.",
        SystemCapabilityMode.Write, GovernanceSchemas.RecipeReviewInput, GovernanceSchemas.RecipeReviewOutput,
        ["procedure.system.create-feature"], PrivateOperatorCapability.Modify,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, true, true);

    public async Task<SystemCapabilityWritePreflight> PreflightAsync(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var command = GovernanceSchemas.Review(input, "preview");
            var recipe = await recipes.GetAsync(command.ApplicationId, command.RecipeId, cancellationToken);
            if (recipe is null || recipe.Reference.Version != command.ExpectedVersion)
                return SystemCapabilityWritePreflight.Failure("RECIPE_VERSION_CONFLICT",
                    "The recipe is missing or its version changed.",
                    "Inspect current recipe evidence and retry with its exact version.");
            if (command.Decision == "verify" && recipe.Status != InteractionRecipeStatus.Candidate)
                return SystemCapabilityWritePreflight.Failure("RECIPE_REVIEW_STATE_INVALID",
                    "Only a current candidate recipe can be verified.",
                    "Inspect the recipe's current lifecycle status.");
            if (command.Decision == "retire" && recipe.Status == InteractionRecipeStatus.Retired)
                return SystemCapabilityWritePreflight.Failure("RECIPE_REVIEW_STATE_INVALID",
                    "The recipe is already retired.", "Inspect the current recipe projection.");
            return SystemCapabilityWritePreflight.Ready(GovernanceSchemas.Hash(input.GetRawText()),
                command.Decision == "verify"
                    ? "Verify this exact candidate route after reviewing its receipts and current mechanic contracts."
                    : "Retire this exact route so it is no longer eligible for replay.",
                [$"application:{command.ApplicationId.Value}", $"interaction-recipe:{command.RecipeId}@{command.ExpectedVersion}"]);
        }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException or JsonException)
        {
            return SystemCapabilityWritePreflight.Failure("INTERACTION_RECIPE_REVIEW_INVALID",
                GovernanceSchemas.Safe(exception.Message), "Use the current recipe-review schema.");
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
        JsonElement input,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var command = GovernanceSchemas.Review(input, context.RequestToken) with
            {
                ReviewerPrincipalReference = context.Invocation.Principal.PrincipalId
            };
            var result = await reviews.ReviewAsync(command, cancellationToken);
            if (result.Disposition == InteractionRecipeWriteDisposition.Conflict || result.Recipe is null)
                return SystemCapabilityWriteHandlerResult.Failure(result.Code,
                    "The recipe review conflicted with current evidence.",
                    "Inspect the current recipe projection and retry with its exact version.");
            var data = GovernanceSchemas.ReviewResult(command.Decision, result);
            return SystemCapabilityWriteHandlerResult.Success(data,
                "operation.interaction-recipe-review." + Guid.NewGuid().ToString("N"),
                GovernanceSchemas.Hash(data.GetRawText()));
        }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException or JsonException)
        {
            return SystemCapabilityWriteHandlerResult.Failure("INTERACTION_RECIPE_REVIEW_INVALID",
                GovernanceSchemas.Safe(exception.Message), "Use the current recipe-review schema.");
        }
    }
}

internal static class GovernanceSchemas
{
    private const string HashSchema = "{\"type\":\"string\",\"minLength\":64,\"maxLength\":64,\"pattern\":\"^[0-9A-F]{64}$\"}";
    public const string RecipeReadInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"recipeId":{"type":"string","minLength":41,"maxLength":102},"query":{"type":"string","minLength":1,"maxLength":500},"status":{"enum":["candidate","verified","stale","retired"]},"limit":{"type":"integer","minimum":1,"maximum":50}}}
    """;
    public static readonly string RecipeReadOutput = """
    {"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["id","version","templateFingerprint","applicationId","status","steps","evidence","performance","createdAtUtc","revisedAtUtc"],"properties":{"id":{"type":"string","minLength":41,"maxLength":102},"version":{"type":"integer","minimum":1},"templateFingerprint":__HASH__,"applicationId":{"type":"string","minLength":1,"maxLength":63},"status":{"enum":["candidate","verified","stale","retired"]},"steps":{"type":"array","minItems":1,"maxItems":16,"items":{"type":"object"}},"evidence":{"type":"array","maxItems":64,"items":{"type":"object"}},"performance":{"type":"object","additionalProperties":false,"required":["measurement","observedUses","successfulUses","failedUses","measuredReplays","childStepsPerReplay","baselineAiCalls","actualAiCalls","savedAiCalls","elapsedMilliseconds","promptTokens","outputTokens"],"properties":{"measurement":{"enum":["no-replay-metrics","persisted-replay-metrics"]},"observedUses":{"type":"integer","minimum":0},"successfulUses":{"type":"integer","minimum":0},"failedUses":{"type":"integer","minimum":0},"measuredReplays":{"type":"integer","minimum":0},"childStepsPerReplay":{"type":"integer","minimum":1,"maximum":16},"baselineAiCalls":{"type":"integer","minimum":0},"actualAiCalls":{"type":"integer","minimum":0},"savedAiCalls":{"type":"integer","minimum":0},"elapsedMilliseconds":{"type":"integer","minimum":0},"promptTokens":{"type":"integer","minimum":0},"outputTokens":{"type":"integer","minimum":0}}},"createdAtUtc":{"type":"string","minLength":1,"maxLength":64},"revisedAtUtc":{"type":"string","minLength":1,"maxLength":64}}}}}}
    """.Replace("__HASH__", HashSchema, StringComparison.Ordinal);
    public static readonly string OpportunityReadInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"proposalFingerprint":__HASH__,"limit":{"type":"integer","minimum":1,"maximum":50}}}
    """.Replace("__HASH__", HashSchema, StringComparison.Ordinal);
    public const string OpportunityReadOutput = """
    {"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","maxItems":50,"items":{"type":"object"}}}}
    """;
    public const string ContextReadInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","stateSpaceId","sessionContextId","intentText"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"sessionContextId":{"type":"string","minLength":1,"maxLength":200},"intentText":{"type":"string","minLength":1,"maxLength":500},"role":{"enum":["inner","outer","direct"]}}}
    """;
    public static readonly string ContextReadOutput = """
    {"type":"object","additionalProperties":false,"required":["profile","fingerprint","sourceReferences","evidence"],"properties":{"profile":{"type":"string","minLength":1,"maxLength":100},"fingerprint":__HASH__,"sourceReferences":{"type":"array","maxItems":64,"items":{"type":"string","minLength":1,"maxLength":1000}},"evidence":{"type":"object"}}}
    """.Replace("__HASH__", HashSchema, StringComparison.Ordinal);
    public const string RecipeReviewInput = """
    {"type":"object","additionalProperties":false,"required":["applicationId","recipeId","expectedVersion","decision","reason","idempotencyKey"],"properties":{"applicationId":{"type":"string","minLength":1,"maxLength":63},"recipeId":{"type":"string","minLength":41,"maxLength":102},"expectedVersion":{"type":"integer","minimum":1},"decision":{"enum":["verify","retire"]},"reason":{"type":"string","minLength":1,"maxLength":1000},"idempotencyKey":{"type":"string","minLength":1,"maxLength":128}}}
    """;
    public static readonly string RecipeReviewOutput = """
    {"type":"object","additionalProperties":false,"required":["outcome","code","recipe"],"properties":{"outcome":{"enum":["verified","retired"]},"code":{"type":"string","minLength":1,"maxLength":100},"recipe":{"type":"object","additionalProperties":false,"required":["id","version","templateFingerprint"],"properties":{"id":{"type":"string","minLength":41,"maxLength":102},"version":{"type":"integer","minimum":1},"templateFingerprint":__HASH__}}}}
    """.Replace("__HASH__", HashSchema, StringComparison.Ordinal);

    public static JsonElement Recipes(IReadOnlyList<InteractionRecipeProjection> values) => Element(new
    {
        items = values.Select(value =>
        {
            var allEvidence = value.Provenance ?? [];
            var successful = allEvidence.Count(item => item.Kind == "use-success");
            var failed = allEvidence.Count(item => item.Kind == "use-failure");
            var measurements = allEvidence.Where(item => item.ReplayPerformance is not null)
                .Select(item => item.ReplayPerformance!).ToArray();
            var evidence = allEvidence.OrderByDescending(item => item.CreatedAtUtc).Take(64).ToArray();
            return new
            {
                id = value.Reference.Id,
                version = value.Reference.Version,
                templateFingerprint = value.Reference.TemplateFingerprint,
                applicationId = value.ApplicationId.Value,
                status = InteractionRecipeStatusNames.Get(value.Status),
                steps = value.Template.Steps,
                evidence,
                performance = new
                {
                    measurement = measurements.Length == 0 ? "no-replay-metrics" : "persisted-replay-metrics",
                    observedUses = successful + failed,
                    successfulUses = successful,
                    failedUses = failed,
                    measuredReplays = measurements.Length,
                    childStepsPerReplay = value.Template.Steps.Count,
                    baselineAiCalls = measurements.Sum(item => (long)item.BaselineAiCalls),
                    actualAiCalls = measurements.Sum(item => (long)item.ActualAiCalls),
                    savedAiCalls = measurements.Sum(item => (long)item.SavedAiCalls),
                    elapsedMilliseconds = measurements.Sum(item => (long)item.ElapsedMilliseconds),
                    promptTokens = measurements.Sum(item => (long)item.PromptTokens),
                    outputTokens = measurements.Sum(item => (long)item.OutputTokens)
                },
                value.CreatedAtUtc,
                value.RevisedAtUtc
            };
        }).ToArray()
    });

    public static JsonElement Opportunities(IReadOnlyList<InteractionMechanicOpportunityProjection> values) =>
        Element(new
        {
            items = values.Select(value => new
            {
                value.ProposalFingerprint,
                applicationId = value.ApplicationId.Value,
                value.SourceRecipe,
                value.ApplicationRevision,
                value.ApplicationFingerprint,
                value.EffectiveSetFingerprint,
                value.RepeatedIntent,
                value.SupportingReceipts,
                value.ProposedRoles,
                proposedInputSchema = JsonSerializer.Deserialize<JsonElement>(value.ProposedInputSchemaJson),
                value.ExactChildDependencies,
                value.IntendedEffectsAndOwnership,
                value.SuggestedMatchPhrases,
                value.EstimatedCallReduction,
                value.PossibleOverlap,
                value.MechanicPreferenceReason,
                value.CreatedAtUtc
            }).ToArray()
        });

    public static JsonElement Context(InteractionTaskContextPack pack) => Element(new
    {
        pack.Profile,
        pack.Fingerprint,
        pack.SourceReferences,
        evidence = JsonSerializer.Deserialize<JsonElement>(pack.Json)
    });

    public static InteractionRecipeReviewRequest Review(JsonElement input, string requestToken) => new(
        requestToken,
        ApplicationIdentifier.Parse(input.GetProperty("applicationId").GetString()!),
        input.GetProperty("recipeId").GetString()!,
        input.GetProperty("expectedVersion").GetInt32(),
        input.GetProperty("decision").GetString()!,
        input.GetProperty("reason").GetString()!,
        "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    public static JsonElement ReviewResult(string decision, InteractionRecipeWriteResult result) => Element(new
    {
        outcome = decision == "verify" ? "verified" : "retired",
        code = result.Code,
        recipe = new
        {
            id = result.Recipe!.Id,
            version = result.Recipe.Version,
            templateFingerprint = result.Recipe.TemplateFingerprint
        }
    });

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "The governance request was rejected."
        : value.Length <= 300 ? value : value[..300];
    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
