using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Capabilities;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class SystemInnerWorkerApplicationToolFactory(
    ApplicationActionInvocationAdapter actions,
    IStandingGrantApplicationReadModelInvocationAdapter reads)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal IReadOnlyList<IAiTool> Create(SystemInnerWorkerResolvedProfile profile,
        IReadOnlyList<SystemInnerWorkerApplicationToolSelection> selections) =>
        Array.AsReadOnly(selections.Select(selection => (IAiTool)new Tool(
            Definition(selection.Binding.Kind, ApplicationCapabilityContractAdapter.Create(
                profile.Worker.InvocationHost.ApplicationRevision.ApplicationId, selection.Record,
                profile.Worker.InvocationHost.StateSpaceId)),
            (call, token) => InvokeAsync(profile, selection, call, token))).ToArray());

    internal static AiToolDefinition Definition(SystemInnerWorkerToolKind kind,
        CapabilityContractDescriptor contract)
    {
        if (kind is not (SystemInnerWorkerToolKind.ApplicationAction
            or SystemInnerWorkerToolKind.ApplicationQuery))
            throw new InteractionContractException("INNER_WORKER_APPLICATION_TOOL_KIND_INVALID",
                "The selected application tool kind is invalid.");
        var input = JsonNode.Parse(contract.Input.SchemaJson)
            ?? throw new InteractionContractException("INNER_WORKER_APPLICATION_TOOL_SCHEMA_INVALID",
                "The selected application tool input schema is unavailable.");
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("roles", "input"),
            ["properties"] = new JsonObject
            {
                ["roles"] = new JsonObject
                {
                    ["type"] = "object", ["maxProperties"] = 64,
                    ["additionalProperties"] = new JsonObject { ["type"] = "string" }
                },
                ["input"] = input.DeepClone()
            }
        };
        return new(ToolName(kind, contract.Id),
            kind == SystemInnerWorkerToolKind.ApplicationAction
                ? $"Execute exact active application action '{contract.Id}'."
                : $"Read exact active application query '{contract.Id}'.",
            InteractionCanonicalJson.CanonicalizeObject(schema.ToJsonString()));
    }

    private async Task<AiToolResult> InvokeAsync(SystemInnerWorkerResolvedProfile profile,
        SystemInnerWorkerApplicationToolSelection selection, AiToolInvocation call,
        CancellationToken cancellationToken)
    {
        var binding = profile.ToolBindings.SingleOrDefault(value =>
            value.Definition.Name == call.Name && value.Kind == selection.Binding.Kind);
        if (binding is null || binding != selection.Binding
            || selection.Record.Status != "active"
            || selection.Record.QualifiedId != binding.CapabilityVersion.ExactDefinitionId
            || selection.Record.Version != binding.CapabilityVersion.Version
            || selection.Record.ContentFingerprint != binding.CapabilityVersion.Fingerprint)
            return AiToolResult.Failure("INNER_WORKER_APPLICATION_TOOL_STALE",
                "The selected application tool no longer matches its retained binding.");
        try
        {
            var roles = Roles(call.Arguments.GetProperty("roles"));
            var input = InteractionCanonicalJson.CanonicalizeObject(
                call.Arguments.GetProperty("input").GetRawText());
            if (!profile.Worker.InvocationHost.Budget.TryTransferOperations(1, out var budget))
                return AiToolResult.Failure("INNER_AI_OPERATION_BUDGET_EXHAUSTED",
                    "The shared worker operation budget is exhausted.");
            var child = ChildHost(profile.Worker.InvocationHost, call.CallId, binding, budget!,
                binding.Kind == SystemInnerWorkerToolKind.ApplicationAction
                    ? InteractionExecutionProfile.Atomic : InteractionExecutionProfile.ReadOnly);
            InteractionInvocationResult result;
            if (binding.Kind == SystemInnerWorkerToolKind.ApplicationAction)
            {
                result = await actions.ExecuteWorkflowChildAsync(new(child,
                    binding.CapabilityVersion.ExactDefinitionId, binding.CapabilityVersion.Version,
                    binding.CapabilityVersion.Fingerprint, roles, input), cancellationToken);
            }
            else
            {
                var query = ApplicationQueryContract.Parse(selection.Record.ContentJson,
                    child.ApplicationRevision.ApplicationId);
                var expected = new InteractionQueryContractReference(query.Executor,
                    query.ProjectionQualifiedId, query.ProjectionVersion, query.ProjectionContentHash,
                    query.OutputSchemaHash, query.OutputSchemaJson, query.Exposure, query.Roles.Keys,
                    query.ObjectCollectionId);
                result = await reads.ReadAsync(new(child, query.Id, expected, roles, input), cancellationToken);
            }
            return AiToolResult.Success(JsonSerializer.Serialize(result, Json));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InteractionContractException)
        {
            return AiToolResult.Failure("INNER_WORKER_APPLICATION_TOOL_INPUT_INVALID",
                "The application tool input is invalid.");
        }
    }

    private static IReadOnlyDictionary<string, string> Roles(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException();
        return value.EnumerateObject().ToDictionary(property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()! : throw new JsonException(), StringComparer.Ordinal);
    }

    private static InteractionInvocationHost ChildHost(InteractionInvocationHost parent, string callId,
        SystemInnerWorkerToolBinding binding, InteractionInvocationBudget budget,
        InteractionExecutionProfile profile) => new(parent.Principal, parent.ApplicationRevision,
            parent.StateSpaceId!, parent.GrantReference,
            "inner-tool." + Hash(parent.CommandId + "\n" + callId + "\n" + binding.Kind + "\n"
                + binding.CapabilityVersion.ExactDefinitionId)[..32].ToLowerInvariant(),
            parent.StateRevision!, profile, budget, parent.CommandId);

    private static string ToolName(SystemInnerWorkerToolKind kind, string id)
    {
        var prefix = kind == SystemInnerWorkerToolKind.ApplicationAction ? "app_action_" : "app_query_";
        var safe = new string(id.Select(value => char.IsLetterOrDigit(value) || value is '_' or '-'
            ? value : '_').ToArray());
        var value = prefix + safe;
        return value.Length <= 64 ? value : value[..55] + "_" + Hash(id)[..8].ToLowerInvariant();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Tool(AiToolDefinition definition,
        Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> invoke) : IAiTool
    {
        public AiToolDefinition Definition { get; } = definition;
        public Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default) => invoke(invocation, cancellationToken);
    }
}
