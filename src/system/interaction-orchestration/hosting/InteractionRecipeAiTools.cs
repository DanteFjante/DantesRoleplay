using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            "Compile one current verified recipe into an exact proposal and execute its dependency-ordered steps. Known roles and declared input parameters are bound deterministically; one read-only AI pass may fill only explicitly missing choices. Trusted host confirmation is required before execution.",
            """{"type":"object","additionalProperties":false,"required":["applicationId","query"],"properties":{"applicationId":{"type":"string"},"query":{"type":"string"},"roleBindings":{"type":"object","maxProperties":32,"additionalProperties":{"type":"string"}},"inputBindings":{"type":"object","maxProperties":512},"stepInputs":{"type":"object","maxProperties":16,"additionalProperties":{"type":"object"}}}}""",
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
            var inputBindings = InputBindings(call, recipe);
            var legacyInputs = LegacyInputs(call, recipe);
            var choiceWatch = Stopwatch.StartNew();
            var choice = await ResolveMissingChoicesAsync(source, recipe, bindings, inputBindings, cancellationToken);
            choiceWatch.Stop();
            if (!choice.Ok)
                return AiToolResult.Failure(choice.ErrorCode, choice.ErrorMessage);
            bindings = choice.Bindings;
            inputBindings = choice.Inputs;

            var proposal = Compile(recipe, bindings, inputBindings, legacyInputs);
            var proposalJson = ProposalJson(proposal);
            var intentFingerprint = InteractionCanonicalJson.Fingerprint(
                ReplayIntentFingerprintDomain,
                InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                {
                    recipe = recipe.Reference,
                    query,
                    roleBindings = bindings,
                    inputBindings,
                    stepInputs = legacyInputs
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
                choice.OutputTokens,
                choice.FallbackReason);
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
                    totalTokens = performance.PromptTokens + performance.OutputTokens,
                    aiFallbackReason = performance.FallbackReason
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

    private async Task<BindingResolution> ResolveMissingChoicesAsync(
        SystemAiToolSourceContext source,
        InteractionRecipeProjection recipe,
        IReadOnlyDictionary<string, string> known,
        IReadOnlyDictionary<string, JsonElement> knownInputs,
        CancellationToken cancellationToken)
    {
        var required = recipe.Template.Steps.SelectMany(step => step.RoleSlots)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missing = required.Where(value => !known.ContainsKey(value)).ToArray();
        var requiredInputs = recipe.Template.Steps.SelectMany(step => step.InputBindings)
            .Select(value => value.Parameter).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missingInputs = requiredInputs.Where(value => !knownInputs.ContainsKey(value)).ToArray();
        if (missing.Length == 0 && missingInputs.Length == 0)
            return BindingResolution.Success(known, knownInputs, 0, 0, 0, "none");
        if (ai is null)
            return BindingResolution.Failure("INTERACTION_RECIPE_CHOICES_REQUIRED",
                $"Recipe replay needs {MissingSummary(missing, missingInputs)}.");
        var readTools = source.AuthorizedTools().Where(tool =>
                tool.Definition.Name != "interaction_recipe_run"
                && (tool.Definition.Name.StartsWith("read_", StringComparison.Ordinal)
                    || tool.Definition.Description.StartsWith("Read ", StringComparison.Ordinal)
                    || tool.Definition.Name == "interaction_recipes_find"))
            .ToArray();
        var roleProperties = missing.ToDictionary(value => value,
            _ => (object)new { type = "string", minLength = 1, maxLength = 200 },
            StringComparer.Ordinal);
        var inputProperties = missingInputs.ToDictionary(value => value,
            _ => (object)new { }, StringComparer.Ordinal);
        var rootProperties = new Dictionary<string, object>(StringComparer.Ordinal);
        var requiredGroups = new List<string>();
        if (missing.Length > 0)
        {
            rootProperties.Add("roleBindings", new
            {
                type = "object",
                additionalProperties = false,
                required = missing,
                properties = roleProperties
            });
            requiredGroups.Add("roleBindings");
        }
        if (missingInputs.Length > 0)
        {
            rootProperties.Add("inputBindings", new
            {
                type = "object",
                additionalProperties = false,
                required = missingInputs,
                properties = inputProperties
            });
            requiredGroups.Add("inputBindings");
        }
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = requiredGroups,
            properties = rootProperties
        });
        var prompt = new StringBuilder()
            .Append("Resolve only the missing choices for current verified recipe '")
            .Append(recipe.Reference.Id).AppendLine("'.")
            .Append("Missing roles: ").AppendLine(string.Join(", ", missing))
            .Append("Missing input parameters: ").AppendLine(string.Join(", ", missingInputs))
            .Append("Known roles: ").AppendLine(JsonSerializer.Serialize(known))
            .Append("Known input parameters: ").AppendLine(JsonSerializer.Serialize(knownInputs))
            .AppendLine("Use read-only tools when necessary. Return only the missing structured choices. Do not execute or propose any action.")
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
        var resolvedInputs = knownInputs.ToDictionary(value => value.Key, value => value.Value.Clone(), StringComparer.Ordinal);
        var root = response.StructuredData.Value;
        if (missing.Length > 0 && (!root.TryGetProperty("roleBindings", out var roleValues)
                || roleValues.ValueKind != JsonValueKind.Object))
            return BindingResolution.Failure("INTERACTION_RECIPE_BINDING_RESOLUTION_FAILED",
                "The binding resolver returned an invalid structured result.",
                response.PromptTokens, response.OutputTokens);
        if (missing.Length > 0)
        {
            roleValues = root.GetProperty("roleBindings");
            foreach (var role in missing)
            {
                if (!roleValues.TryGetProperty(role, out var value) || value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(value.GetString()))
                    return BindingResolution.Failure("INTERACTION_RECIPE_BINDING_RESOLUTION_FAILED",
                        $"The binding resolver did not resolve role '{role}'.",
                        response.PromptTokens, response.OutputTokens);
                resolved.Add(role, value.GetString()!);
            }
        }
        if (missingInputs.Length > 0)
        {
            if (!root.TryGetProperty("inputBindings", out var inputValues) || inputValues.ValueKind != JsonValueKind.Object)
                return BindingResolution.Failure("INTERACTION_RECIPE_BINDING_RESOLUTION_FAILED",
                    "The binding resolver returned invalid input parameters.",
                    response.PromptTokens, response.OutputTokens);
            foreach (var parameter in missingInputs)
            {
                if (!inputValues.TryGetProperty(parameter, out var value))
                    return BindingResolution.Failure("INTERACTION_RECIPE_BINDING_RESOLUTION_FAILED",
                        $"The binding resolver did not resolve input parameter '{parameter}'.",
                        response.PromptTokens, response.OutputTokens);
                resolvedInputs.Add(parameter, value.Clone());
            }
        }
        return BindingResolution.Success(resolved, resolvedInputs, 1, response.PromptTokens,
            response.OutputTokens, FallbackReason(missing, missingInputs));
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
                && document.Record.Kind == (step.Kind == InteractionPlanStepKind.Query
                    ? ApplicationQueryContract.CatalogKind : "mechanic")
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
        IReadOnlyDictionary<string, JsonElement> inputs,
        IReadOnlyDictionary<string, string> legacyInputs) => new(recipe.Template.Steps.Select(step =>
        new InteractionPlannerDraftStep(step.StepId, step.Kind,
            step.QualifiedId, step.ContractVersion, step.ContractFingerprint, step.DependsOn,
            step.RoleSlots.ToDictionary(role => role, role => bindings[role], StringComparer.Ordinal),
            step.InputBindings.Count > 0 ? BoundInput(step, inputs)
                : legacyInputs.TryGetValue(step.StepId, out var input) ? input : "{}",
            step.ResultBindings)).ToArray());

    private static string ProposalJson(InteractionPlannerProposalCommand proposal) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            command = "propose",
            steps = proposal.Steps.Select(step => new
            {
                stepId = step.StepId,
                kind = step.Kind == InteractionPlanStepKind.Query ? "query" : "action",
                qualifiedId = step.QualifiedId,
                version = step.Version,
                fingerprint = step.Fingerprint,
                dependsOn = step.DependsOn,
                roleBindings = step.RoleBindings,
                input = JsonSerializer.Deserialize<JsonElement>(step.InputJson),
                resultBindings = (step.ResultBindings ?? []).Select(binding => new
                {
                    fromStepId = binding.FromStepId,
                    fromPointer = binding.FromPointer,
                    toRole = binding.ToRole,
                    toInputPointer = binding.ToInputPointer
                })
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

    private static IReadOnlyDictionary<string, JsonElement> InputBindings(
        AiToolInvocation call,
        InteractionRecipeProjection recipe)
    {
        if (!call.Arguments.TryGetProperty("inputBindings", out var value))
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InteractionContractException("INTERACTION_RECIPE_INPUT_INVALID",
                "inputBindings must be an object.");
        var allowed = recipe.Template.Steps.SelectMany(step => step.InputBindings)
            .Select(binding => binding.Parameter).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !result.TryAdd(property.Name, property.Value.Clone()))
                throw new InteractionContractException("INTERACTION_RECIPE_STEP_INPUT_INVALID",
                    "A recipe input parameter is unknown, duplicated, or invalid.");
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> LegacyInputs(
        AiToolInvocation call,
        InteractionRecipeProjection recipe)
    {
        if (!call.Arguments.TryGetProperty("stepInputs", out var value))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        if (recipe.Template.Steps.Any(step => step.InputBindings.Count > 0))
            throw new InteractionContractException("INTERACTION_RECIPE_STEP_INPUT_INVALID",
                "Parameterized recipes require declared inputBindings instead of raw stepInputs.");
        if (value.ValueKind != JsonValueKind.Object)
            throw new InteractionContractException("INTERACTION_RECIPE_INPUT_INVALID", "stepInputs must be an object.");
        var allowed = recipe.Template.Steps.Select(step => step.StepId).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.Object
                || !result.TryAdd(property.Name, InteractionCanonicalJson.CanonicalizeObject(property.Value.GetRawText())))
                throw new InteractionContractException("INTERACTION_RECIPE_STEP_INPUT_INVALID",
                    "A per-step input is unknown, duplicated, or invalid.");
        }
        return result;
    }

    private static string BoundInput(
        InteractionRecipeTemplateStep step,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var root = new JsonObject();
        foreach (var binding in step.InputBindings)
            SetInput(root, binding.ToInputPointer, JsonNode.Parse(values[binding.Parameter].GetRawText()));
        return InteractionCanonicalJson.CanonicalizeObject(root.ToJsonString());
    }

    private static void SetInput(JsonObject root, string pointer, JsonNode? value)
    {
        var tokens = pointer.Split('/').Skip(1).Select(token => token
            .Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        JsonObject current = root;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (current[tokens[index]] is not JsonObject child)
            {
                child = new JsonObject();
                current[tokens[index]] = child;
            }
            current = child;
        }
        current[tokens[^1]] = value;
    }

    private static string MissingSummary(IReadOnlyList<string> roles, IReadOnlyList<string> inputs)
    {
        var parts = new List<string>();
        if (roles.Count > 0) parts.Add("role bindings: " + string.Join(", ", roles));
        if (inputs.Count > 0) parts.Add("input parameters: " + string.Join(", ", inputs));
        return string.Join("; ", parts);
    }

    private static string FallbackReason(IReadOnlyList<string> roles, IReadOnlyList<string> inputs) =>
        (roles.Count > 0, inputs.Count > 0) switch
        {
            (true, true) => "missing-roles-and-inputs",
            (true, false) => "missing-roles",
            (false, true) => "missing-inputs",
            _ => "none"
        };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Required(AiToolInvocation call, string name) =>
        call.Arguments.GetProperty(name).GetString()!;

    private sealed record BindingResolution(
        bool Ok,
        IReadOnlyDictionary<string, string> Bindings,
        IReadOnlyDictionary<string, JsonElement> Inputs,
        int AiCalls,
        int PromptTokens,
        int OutputTokens,
        string ErrorCode,
        string ErrorMessage,
        string FallbackReason)
    {
        public static BindingResolution Success(IReadOnlyDictionary<string, string> bindings,
            IReadOnlyDictionary<string, JsonElement> inputs, int calls, int promptTokens, int outputTokens,
            string fallbackReason) =>
            new(true, bindings, inputs, calls, promptTokens, outputTokens, "", "", fallbackReason);

        public static BindingResolution Failure(string code, string message,
            int promptTokens = 0, int outputTokens = 0) =>
            new(false, new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, JsonElement>(StringComparer.Ordinal), 1,
                promptTokens, outputTokens, code, message, "resolution-failed");
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
