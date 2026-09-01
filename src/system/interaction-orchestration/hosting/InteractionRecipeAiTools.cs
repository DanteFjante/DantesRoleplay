using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>
/// Finds verified interaction recipes and lets the selected local provider solve each bounded
/// recipe step through the same direct tool surface. No MCP transport is involved.
/// </summary>
public sealed class InteractionRecipeAiToolSource(
    IInteractionRecipeStore recipes,
    IAiService? ai = null) : ISystemAiToolSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context) =>
    [
        new DelegateTool("interaction_recipes_find",
            "Find current application interaction recipes by intent text and status.",
            """{"type":"object","additionalProperties":false,"required":["applicationId","query"],"properties":{"applicationId":{"type":"string"},"query":{"type":"string"},"status":{"enum":["candidate","verified","stale","retired"]},"limit":{"type":"integer","minimum":1,"maximum":50}}}""",
            (call, token) => FindAsync(call, token)),
        new DelegateTool("interaction_recipe_run",
            "Find one verified multi-step recipe and recursively ask the selected local AI to solve each dependency-ordered task using direct system tools.",
            """{"type":"object","additionalProperties":false,"required":["applicationId","query"],"properties":{"applicationId":{"type":"string"},"query":{"type":"string"},"roleBindings":{"type":"object","additionalProperties":{"type":"string"}}}}""",
            (call, token) => RunAsync(context, call, token))
    ];

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
        try
        {
            var agent = ai;
            if (agent is null)
                return AiToolResult.Failure("AI_SERVICE_UNAVAILABLE",
                    "No local AI service is configured for recursive recipe execution.");
            var application = ApplicationIdentifier.Parse(Required(call, "applicationId"));
            var query = Required(call, "query");
            var found = await recipes.SearchAsync(application, query, InteractionRecipeStatus.Verified, 2,
                cancellationToken);
            if (found.Count != 1)
                return AiToolResult.Failure(found.Count == 0 ? "INTERACTION_RECIPE_NOT_FOUND" : "INTERACTION_RECIPE_AMBIGUOUS",
                    found.Count == 0
                        ? "No verified recipe matched this task."
                        : "More than one verified recipe matched this task; provide a more specific query.");

            var recipe = found[0];
            var bindings = Bindings(call);
            var results = new Dictionary<string, AiResponse>(StringComparer.Ordinal);
            var childTools = source.AuthorizedTools()
                .Where(value => value.Definition.Name != "interaction_recipe_run").ToArray();
            foreach (var step in recipe.Template.Steps)
            {
                var missing = step.RoleSlots.Where(value => !bindings.ContainsKey(value)).ToArray();
                if (missing.Length != 0)
                    return AiToolResult.Failure("INTERACTION_RECIPE_BINDINGS_REQUIRED",
                        $"Recipe step '{step.StepId}' requires role bindings: {string.Join(", ", missing)}.");
                var prompt = StepPrompt(recipe, step, bindings, results);
                var child = await agent.SendAgentRequestAsync(source.Profile, new(
                    source.Request.Provider,
                    source.Request.Model,
                    [new(AiMessageRole.User, prompt)],
                    AiRequestKind.Task,
                    source.Request.Reasoning,
                    MaximumToolRounds: source.Request.MaximumToolRounds,
                    MaximumOutputTokens: source.Request.MaximumOutputTokens), childTools, cancellationToken);
                results.Add(step.StepId, child);
                if (!child.Ok)
                    return AiToolResult.Failure(child.ErrorCode.Length == 0 ? "INTERACTION_RECIPE_STEP_FAILED" : child.ErrorCode,
                        $"Recipe step '{step.StepId}' failed: {child.ErrorMessage}");
            }

            return AiToolResult.Success(JsonSerializer.Serialize(new
            {
                recipe = recipe.Reference,
                steps = recipe.Template.Steps.Select(step => new
                {
                    step.StepId,
                    step.QualifiedId,
                    text = results[step.StepId].Text,
                    structuredData = results[step.StepId].StructuredData,
                    toolCalls = results[step.StepId].ToolCalls
                })
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

    private static string StepPrompt(
        InteractionRecipeProjection recipe,
        InteractionRecipeTemplateStep step,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlyDictionary<string, AiResponse> results)
    {
        var prompt = new StringBuilder()
            .Append("Execute one bounded task from verified recipe '").Append(recipe.Reference.Id).AppendLine("'.")
            .Append("Task: use exact contract '").Append(step.QualifiedId).Append("' version ")
            .Append(step.ContractVersion).Append(" fingerprint ").Append(step.ContractFingerprint).AppendLine(".");
        if (step.RoleSlots.Count != 0)
            prompt.Append("Role bindings: ").AppendLine(JsonSerializer.Serialize(
                step.RoleSlots.ToDictionary(value => value, value => bindings[value], StringComparer.Ordinal)));
        if (step.DependsOn.Count != 0)
        {
            prompt.AppendLine("Authoritative results from prerequisite tasks:");
            foreach (var dependency in step.DependsOn)
            {
                var text = results[dependency].Text;
                prompt.Append(dependency).Append(": ")
                    .AppendLine(text.Length <= 4_000 ? text : text[..4_000]);
            }
        }
        prompt.AppendLine("Use the supplied direct tools when system state must be read or changed. Report the exact result of this task only.");
        return prompt.ToString();
    }

    private static IReadOnlyDictionary<string, string> Bindings(AiToolInvocation call)
    {
        if (!call.Arguments.TryGetProperty("roleBindings", out var value))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return value.EnumerateObject().ToDictionary(property => property.Name,
            property => property.Value.GetString()!, StringComparer.Ordinal);
    }

    private static string Required(AiToolInvocation call, string name) =>
        call.Arguments.GetProperty(name).GetString()!;

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
