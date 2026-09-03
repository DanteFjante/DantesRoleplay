using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>
/// Finds verified interaction recipes, compiles current recipes without model calls, and executes
/// the resulting exact proposal through the common interaction authority path. A single read-only
/// AI pass is allowed only when required role choices are genuinely unresolved.
/// </summary>
public sealed class InteractionRecipeAiToolSource(
    IInteractionRecipeStore recipes,
    IAiService? ai = null,
    IInteractionGateway? interactions = null,
    IApplicationRegistry? applications = null,
    IActiveCatalogFeatureSnapshotProvider? snapshots = null,
    IInteractionRecipeLearner? recipeLearner = null,
    IInteractionMechanicOpportunityStore? mechanicOpportunities = null) : ISystemAiToolSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string ReplayIntentFingerprintDomain = "dantes-roleplay/interaction-recipe-replay-intent/v1";

    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context) =>
    [
        new DelegateTool("interaction_recipes_find",
            "Find current application interaction recipes by intent text and status.",
            """{"type":"object","additionalProperties":false,"required":["applicationId","query"],"properties":{"applicationId":{"type":"string"},"query":{"type":"string"},"status":{"enum":["candidate","verified","stale","retired"]},"limit":{"type":"integer","minimum":1,"maximum":50}}}""",
            (call, token) => FindAsync(call, token)),
        new DelegateTool("interaction_recipe_run",
            "Compile one current verified recipe into an exact proposal and execute its dependency-ordered steps. Known roles and per-step inputs are bound deterministically; only missing role choices may invoke one read-only AI pass. Trusted host confirmation is required before execution.",
            """{"type":"object","additionalProperties":false,"required":["applicationId","query"],"properties":{"applicationId":{"type":"string"},"query":{"type":"string"},"roleBindings":{"type":"object","maxProperties":32,"additionalProperties":{"type":"string"}},"stepInputs":{"type":"object","maxProperties":16,"additionalProperties":{"type":"object"}}}}""",
            (call, token) => RunAsync(context, call, token)),
        new DelegateTool("interaction_mechanic_opportunities_list",
            "List inert review proposals created from repeated successful verified recipe use. Proposals never choose a mechanic ID and cannot activate or write catalog content.",
            """{"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":50}}}""",
            (call, token) => ListMechanicOpportunitiesAsync(context, call, token))
    ];

    private async Task<AiToolResult> ListMechanicOpportunitiesAsync(
        SystemAiToolSourceContext source,
        AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        try
        {
            if (mechanicOpportunities is null)
                return AiToolResult.Failure("MECHANIC_OPPORTUNITY_LEARNING_UNAVAILABLE",
                    "Mechanic-opportunity learning is not configured in this host.");
            var application = ApplicationIdentifier.Parse(Required(call, "applicationId"));
            if (!source.Invocation.Principal.Verified || source.Invocation.ApplicationId != application
                || string.IsNullOrWhiteSpace(source.Invocation.StateSpaceId))
                return AiToolResult.Failure("MECHANIC_OPPORTUNITY_CONTEXT_REQUIRED",
                    "Mechanic-opportunity proposals require an authorized application and state-space context.");
            var limit = call.Arguments.TryGetProperty("limit", out var limitValue) ? limitValue.GetInt32() : 20;
            var proposals = await mechanicOpportunities.ListAsync(application, limit, cancellationToken);
            return AiToolResult.Success(JsonSerializer.Serialize(proposals, Json));
        }
        catch (InteractionContractException exception)
        {
            return AiToolResult.Failure(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return AiToolResult.Failure("MECHANIC_OPPORTUNITY_INPUT_INVALID", exception.Message);
        }
    }

    private async Task<AiToolResult> FindAsync(
        AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = call.Arguments.TryGetProperty("status", out var statusValue)
                ? InteractionRecipeStatusNames.Parse(statusValue.GetString()!)
                : (InteractionRecipeStatus?)null;
            var limit = call.Arguments.TryGetProperty("limit", out var limitValue) ? limitValue.GetInt32() : 20;
            var found = await recipes.SearchAsync(
                ApplicationIdentifier.Parse(Required(call, "applicationId")),
                Required(call, "query"), status, limit, cancellationToken);
            return AiToolResult.Success(JsonSerializer.Serialize(found, Json));
        }
        catch (InteractionContractException exception)
        {
            return AiToolResult.Failure(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return AiToolResult.Failure("INTERACTION_RECIPE_INPUT_INVALID", exception.Message);
        }
    }

    private async Task<AiToolResult> RunAsync(
        SystemAiToolSourceContext source,
        AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        try
        {
            if (interactions is null || applications is null || snapshots is null)
                return AiToolResult.Failure("INTERACTION_RECIPE_REPLAY_UNAVAILABLE",
                    "Deterministic recipe replay is not configured in this host.");
            var application = ApplicationIdentifier.Parse(Required(call, "applicationId"));
            if (source.Invocation.ApplicationId != application
                || string.IsNullOrWhiteSpace(source.Invocation.StateSpaceId)
                || !source.Invocation.Principal.Verified)
                return AiToolResult.Failure("INTERACTION_RECIPE_CONTEXT_REQUIRED",
                    "Recipe replay requires an authorized application and state-space context.");
            var query = Required(call, "query");
            var found = await recipes.SearchAsync(application, query, InteractionRecipeStatus.Verified, 2,
                cancellationToken);
            if (found.Count != 1)
                return AiToolResult.Failure(found.Count == 0 ? "INTERACTION_RECIPE_NOT_FOUND" : "INTERACTION_RECIPE_AMBIGUOUS",
                    found.Count == 0
                        ? "No verified recipe matched this task."
                        : "More than one verified recipe matched this task; provide a more specific query.");

            var recipe = found[0];
            await RequireCurrentAsync(recipe, source, cancellationToken);
            var bindings = Bindings(call, recipe);
            var inputs = Inputs(call, recipe);
            var choiceWatch = Stopwatch.StartNew();
            var choice = await ResolveMissingBindingsAsync(source, recipe, bindings, cancellationToken);
            choiceWatch.Stop();
            if (!choice.Ok)
                return AiToolResult.Failure(choice.ErrorCode, choice.ErrorMessage);
            bindings = choice.Bindings;

            var proposal = Compile(recipe, bindings, inputs);
            var proposalJson = ProposalJson(proposal);
            var intentFingerprint = InteractionCanonicalJson.Fingerprint(
                ReplayIntentFingerprintDomain,
                InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                {
                    recipe = recipe.Reference,
                    query,
                    roleBindings = bindings,
                    stepInputs = inputs
                })));
            var identity = Hash(source.Invocation.CorrelationId + "\n" + call.CallId + "\n" + intentFingerprint)
                [..32].ToLowerInvariant();
            var intentJson = JsonSerializer.Serialize(new
            {
                idempotencyKey = "recipe-plan." + identity,
                intentText = query,
                maximumPlanSteps = recipe.Template.Steps.Count,
                roleHints = bindings,
                plannerPreference = "local"
            });

            var planningWatch = Stopwatch.StartNew();
            var plan = await interactions.PlanAsync(source.Invocation.Principal, application,
                source.Invocation.StateSpaceId, "recipe-session." + identity, intentJson,
                proposalJson, role: InteractionAiRole.Direct, cancellationToken: cancellationToken);
            planningWatch.Stop();
            if (plan.Status != InteractionResolutionStatus.Resolved || plan.ProposalFingerprint is null
                || plan.Receipt.Receipt is null)
                return AiToolResult.Failure(plan.Code,
                    string.IsNullOrWhiteSpace(plan.SafeSummary)
                        ? "The compiled recipe proposal was not accepted." : plan.SafeSummary);

            if (source.ToolApproval is null || !await source.ToolApproval.ConfirmAsync(new(
                    "interaction_recipe_run",
                    $"Execute verified recipe '{recipe.Reference.Id}' with {recipe.Template.Steps.Count} exact dependency-ordered steps.",
                    call.Arguments.Clone()), cancellationToken))
                return AiToolResult.Failure("AI_TOOL_CONFIRMATION_REQUIRED",
                    "Trusted host confirmation is required before the compiled recipe can execute.");

            var executionJson = JsonSerializer.Serialize(new
            {
                resolutionReceiptId = plan.Receipt.Receipt.Id,
                proposalFingerprint = plan.ProposalFingerprint,
                idempotencyKey = "recipe-execute." + identity,
                proposal = JsonSerializer.Deserialize<JsonElement>(proposalJson),
                stopOnFailure = true,
                learn = false
            });
            var executionWatch = Stopwatch.StartNew();
            var execution = await interactions.ExecuteAsync(source.Invocation.Principal, application,
                source.Invocation.StateSpaceId, executionJson, cancellationToken);
            executionWatch.Stop();
            total.Stop();
            var performance = new InteractionRecipeReplayPerformance(
                recipe.Template.Steps.Count,
                choice.AiCalls,
                Math.Max(0, recipe.Template.Steps.Count - choice.AiCalls),
                Milliseconds(total),
                Milliseconds(choiceWatch),
                Milliseconds(planningWatch),
                Milliseconds(executionWatch),
                choice.PromptTokens,
                choice.OutputTokens);
            if (execution.Receipt?.Receipt is not null)
            {
                try
                {
                    var evidence = new InteractionRecipeUseEvidenceDraft(recipe.Reference,
                        plan.Receipt.Receipt.Id, execution.Receipt.Receipt.Id, execution.Successful,
                        intentFingerprint, InteractionRoleProfile.Direct.StableKey, query, performance);
                    if (recipeLearner is null)
                        await recipes.AppendUseEvidenceAsync(evidence, CancellationToken.None);
                    else
                        await recipeLearner.RecordUseAsync(evidence, CancellationToken.None);
                }
                catch
                {
                    // Replay metrics and action truth are already in the interaction receipts.
                    // Diagnostic recipe-use evidence must never rewrite either.
                }
            }
            if (!execution.Successful)
                return AiToolResult.Failure(execution.Code,
                    string.IsNullOrWhiteSpace(execution.SafeSummary)
                        ? "The compiled recipe execution did not complete." : execution.SafeSummary);

            return AiToolResult.Success(JsonSerializer.Serialize(new
            {
                recipe = recipe.Reference,
                proposal = JsonSerializer.Deserialize<JsonElement>(proposalJson),
                execution = new
                {
                    disposition = execution.Disposition.ToString().ToLowerInvariant(),
                    execution.Code,
                    execution.SafeSummary,
                    receipt = execution.Receipt?.Receipt,
                    steps = execution.ActionResults
                },
                efficiency = new
                {
                    baselineAiCalls = performance.BaselineAiCalls,
                    actualAiCalls = performance.ActualAiCalls,
                    savedAiCalls = performance.SavedAiCalls,
                    elapsedMilliseconds = performance.ElapsedMilliseconds,
                    choiceResolutionMilliseconds = performance.ChoiceResolutionMilliseconds,
                    proposalMilliseconds = performance.ProposalMilliseconds,
                    executionMilliseconds = performance.ExecutionMilliseconds,
                    promptTokens = performance.PromptTokens,
                    outputTokens = performance.OutputTokens,
                    totalTokens = performance.PromptTokens + performance.OutputTokens
                }
            }, Json));
        }
        catch (InteractionContractException exception)
        {
            return AiToolResult.Failure(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return AiToolResult.Failure("INTERACTION_RECIPE_INPUT_INVALID", exception.Message);
        }
    }

    private static int Milliseconds(Stopwatch value) =>
        (int)Math.Min(int.MaxValue, value.ElapsedMilliseconds);

    private async Task<BindingResolution> ResolveMissingBindingsAsync(
        SystemAiToolSourceContext source,
        InteractionRecipeProjection recipe,
        IReadOnlyDictionary<string, string> known,
        CancellationToken cancellationToken)
    {
        var required = recipe.Template.Steps.SelectMany(step => step.RoleSlots)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missing = required.Where(value => !known.ContainsKey(value)).ToArray();
        if (missing.Length == 0) return BindingResolution.Success(known, 0, 0, 0);
        if (ai is null)
            return BindingResolution.Failure("AI_SERVICE_UNAVAILABLE",
                $"Recipe replay needs role bindings: {string.Join(", ", missing)}.");
        var readTools = source.AuthorizedTools().Where(tool =>
                tool.Definition.Name != "interaction_recipe_run"
                && (tool.Definition.Name.StartsWith("read_", StringComparison.Ordinal)
                    || tool.Definition.Description.StartsWith("Read ", StringComparison.Ordinal)
                    || tool.Definition.Name == "interaction_recipes_find"))
            .ToArray();
        var properties = missing.ToDictionary(value => value,
            _ => (object)new { type = "string", minLength = 1, maxLength = 200 },
            StringComparer.Ordinal);
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "roleBindings" },
            properties = new
            {
                roleBindings = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = missing,
                    properties
                }
            }
        });
        var prompt = new StringBuilder()
            .Append("Resolve only the missing entity references for current verified recipe '")
            .Append(recipe.Reference.Id).AppendLine("'.")
            .Append("Missing roles: ").AppendLine(string.Join(", ", missing))
            .Append("Known roles: ").AppendLine(JsonSerializer.Serialize(known))
            .AppendLine("Use read-only tools when necessary. Return only the required structured roleBindings. Do not execute or propose any action.")
            .ToString();
        var response = await ai.SendAgentRequestAsync(source.Profile, new(
            source.Request.Provider,
            source.Request.Model,
            [new(AiMessageRole.User, prompt)],
            AiRequestKind.RecipeExecution,
            source.Request.Reasoning,
            schema,
            MaximumToolRounds: source.Request.MaximumToolRounds,
            MaximumOutputTokens: Math.Min(source.Request.MaximumOutputTokens, 1_024)),
            readTools, cancellationToken);
        if (!response.Ok || response.StructuredData is null)
            return BindingResolution.Failure(
                string.IsNullOrEmpty(response.ErrorCode) ? "INTERACTION_RECIPE_BINDING_RESOLUTION_FAILED" : response.ErrorCode,
                string.IsNullOrEmpty(response.ErrorMessage) ? "Missing recipe roles could not be resolved." : response.ErrorMessage,
                response.PromptTokens, response.OutputTokens);
        var resolved = new Dictionary<string, string>(known, StringComparer.Ordinal);
        var root = response.StructuredData.Value;
        if (!root.TryGetProperty("roleBindings", out var values) || values.ValueKind != JsonValueKind.Object)
            return BindingResolution.Failure("INTERACTION_RECIPE_BINDING_RESOLUTION_FAILED",
                "The binding resolver returned an invalid structured result.",
                response.PromptTokens, response.OutputTokens);
        foreach (var role in missing)
        {
            if (!values.TryGetProperty(role, out var value) || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
                return BindingResolution.Failure("INTERACTION_RECIPE_BINDING_RESOLUTION_FAILED",
                    $"The binding resolver did not resolve role '{role}'.",
                    response.PromptTokens, response.OutputTokens);
            resolved.Add(role, value.GetString()!);
        }
        return BindingResolution.Success(resolved, 1, response.PromptTokens, response.OutputTokens);
    }

    private async Task RequireCurrentAsync(
        InteractionRecipeProjection recipe,
        SystemAiToolSourceContext source,
        CancellationToken cancellationToken)
    {
        var current = applications!.Get(recipe.ApplicationId);
        if (current is null || !snapshots!.TryGetSnapshot(recipe.ApplicationId, out var snapshot))
            throw new InteractionContractException("INTERACTION_RECIPE_CURRENT_AUTHORITY_UNAVAILABLE",
                "The current application recipe authority is unavailable.");
        var resolution = snapshot.Resolution?.Fingerprint ?? snapshot.Manifest.Fingerprint;
        var stale = recipe.ApplicationRevision != current.Revision
            || recipe.ApplicationFingerprint != current.Fingerprint
            || recipe.EffectiveSetFingerprint != snapshot.Manifest.Fingerprint
            || recipe.ResolutionFingerprint != resolution
            || (!string.IsNullOrEmpty(source.Invocation.ResolutionFingerprint)
                && source.Invocation.ResolutionFingerprint != resolution)
            || recipe.Template.Steps.Any(step => snapshot.Documents.SingleOrDefault(document =>
                document.Trust == SourceTrust.Trusted
                && document.Record.Kind == "mechanic"
                && document.Record.Status == "active"
                && document.Record.QualifiedId == step.QualifiedId
                && document.Record.Version == step.ContractVersion
                && document.Record.ContentFingerprint == step.ContractFingerprint) is null);
        if (!stale) return;
        try
        {
            await recipes.MarkStaleAsync(new(recipe.Reference, current,
                snapshot.Manifest.Fingerprint, "A verified replay dependency is stale.", resolution),
                cancellationToken);
        }
        catch
        {
            // Failure to record the diagnostic transition cannot make the recipe executable.
        }
        throw new InteractionContractException("INTERACTION_RECIPE_STALE",
            "The verified recipe no longer matches the current application or mechanic contracts.");
    }

    private static InteractionPlannerProposalCommand Compile(
        InteractionRecipeProjection recipe,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlyDictionary<string, string> inputs) => new(recipe.Template.Steps.Select(step =>
        new InteractionPlannerDraftStep(step.StepId, InteractionPlanStepKind.Action,
            step.QualifiedId, step.ContractVersion, step.ContractFingerprint, step.DependsOn,
            step.RoleSlots.ToDictionary(role => role, role => bindings[role], StringComparer.Ordinal),
            inputs.TryGetValue(step.StepId, out var input) ? input : "{}", [])).ToArray());

    private static string ProposalJson(InteractionPlannerProposalCommand proposal) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            command = "propose",
            steps = proposal.Steps.Select(step => new
            {
                stepId = step.StepId,
                kind = "action",
                qualifiedId = step.QualifiedId,
                version = step.Version,
                fingerprint = step.Fingerprint,
                dependsOn = step.DependsOn,
                roleBindings = step.RoleBindings,
                input = JsonSerializer.Deserialize<JsonElement>(step.InputJson),
                resultBindings = Array.Empty<object>()
            })
        }));

    private static IReadOnlyDictionary<string, string> Bindings(
        AiToolInvocation call,
        InteractionRecipeProjection recipe)
    {
        var allowed = recipe.Template.Steps.SelectMany(step => step.RoleSlots)
            .ToHashSet(StringComparer.Ordinal);
        if (!call.Arguments.TryGetProperty("roleBindings", out var value))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InteractionContractException("INTERACTION_RECIPE_INPUT_INVALID",
                "roleBindings must be an object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.Value.GetString())
                || !result.TryAdd(property.Name, property.Value.GetString()!))
                throw new InteractionContractException("INTERACTION_RECIPE_BINDING_INVALID",
                    "A role binding is unknown, duplicated, or invalid.");
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> Inputs(
        AiToolInvocation call,
        InteractionRecipeProjection recipe)
    {
        if (!call.Arguments.TryGetProperty("stepInputs", out var value))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InteractionContractException("INTERACTION_RECIPE_INPUT_INVALID",
                "stepInputs must be an object.");
        var allowed = recipe.Template.Steps.Select(step => step.StepId).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.Object
                || !result.TryAdd(property.Name,
                    InteractionCanonicalJson.CanonicalizeObject(property.Value.GetRawText())))
                throw new InteractionContractException("INTERACTION_RECIPE_STEP_INPUT_INVALID",
                    "A per-step input is unknown, duplicated, or invalid.");
        }
        return result;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Required(AiToolInvocation call, string name) =>
        call.Arguments.GetProperty(name).GetString()!;

    private sealed record BindingResolution(
        bool Ok,
        IReadOnlyDictionary<string, string> Bindings,
        int AiCalls,
        int PromptTokens,
        int OutputTokens,
        string ErrorCode,
        string ErrorMessage)
    {
        public static BindingResolution Success(IReadOnlyDictionary<string, string> bindings,
            int calls, int promptTokens, int outputTokens) =>
            new(true, bindings, calls, promptTokens, outputTokens, "", "");

        public static BindingResolution Failure(string code, string message,
            int promptTokens = 0, int outputTokens = 0) =>
            new(false, new Dictionary<string, string>(StringComparer.Ordinal), 1,
                promptTokens, outputTokens, code, message);
    }

    private sealed class DelegateTool(
        string name,
        string description,
        string schema,
        Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> invoke) : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(name, description, schema);
        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default) => invoke(invocation, cancellationToken);
    }
}
