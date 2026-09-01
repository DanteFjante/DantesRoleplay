using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.SystemCapabilities;

public sealed class EcsLifecycleAiToolSource(IEcsLifecycleStore lifecycle) : ISystemAiToolSource
{
    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context) =>
    [
        new InspectComponentTypeTool(lifecycle),
        new ManageComponentTypeTool(lifecycle, context.ToolApproval),
        new ManageRelationshipKindTool(lifecycle, context.ToolApproval),
        new InspectEntityTool(lifecycle),
        new ManageEntityTool(lifecycle, context.ToolApproval)
    ];

    private abstract class Tool : IAiTool
    {
        protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        public abstract AiToolDefinition Definition { get; }
        public abstract Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default);

        protected static AiToolResult Result(object? value) =>
            AiToolResult.Success(JsonSerializer.Serialize(value, Json));

        protected static AiToolResult Failure(Exception exception) => exception switch
        {
            EcsLifecycleException lifecycle => AiToolResult.Failure(lifecycle.Code, lifecycle.Message),
            ArgumentException argument => AiToolResult.Failure("INVALID_ECS_LIFECYCLE_REQUEST", argument.Message),
            _ => AiToolResult.Failure("ECS_LIFECYCLE_FAILED", exception.Message)
        };
    }

    private sealed class ManageRelationshipKindTool(
        IEcsLifecycleStore lifecycle,
        IAiToolApprovalGate? approval) : Tool
    {
        public override AiToolDefinition Definition { get; } = new(
            "ecs_manage_relationship_kind",
            "Migrate every live relationship using one obsolete qualified kind to an application-owned or base-application-owned canonical kind. The migration is transactional, collision-safe, and requires trusted host approval.",
            """{"type":"object","additionalProperties":false,"required":["sourceQualifiedKind","targetQualifiedKind"],"properties":{"sourceQualifiedKind":{"type":"string","minLength":3,"maxLength":200},"targetQualifiedKind":{"type":"string","minLength":3,"maxLength":200}}}""");

        public override async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (approval is null || !await approval.ConfirmAsync(new(
                    Definition.Name, Definition.Description, invocation.Arguments.Clone()), cancellationToken))
                return AiToolResult.Failure("AI_TOOL_CONFIRMATION_REQUIRED",
                    "Trusted host confirmation is required for ECS lifecycle changes.");
            try
            {
                return Result(await lifecycle.MigrateRelationshipKindAsync(
                    RequiredString(invocation.Arguments, "sourceQualifiedKind"),
                    RequiredString(invocation.Arguments, "targetQualifiedKind"), cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure(exception);
            }
        }
    }

    private sealed class InspectComponentTypeTool(IEcsLifecycleStore lifecycle) : Tool
    {
        public override AiToolDefinition Definition { get; } = new(
            "ecs_inspect_component_type",
            "Inspect a component type, including disabled status and exact reference blockers.",
            """{"type":"object","additionalProperties":false,"required":["qualifiedTypeId"],"properties":{"qualifiedTypeId":{"type":"string","minLength":3,"maxLength":200}}}""");

        public override async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return Result(await lifecycle.GetComponentTypeAsync(
                    invocation.Arguments.GetProperty("qualifiedTypeId").GetString()!, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure(exception);
            }
        }
    }

    private sealed class ManageComponentTypeTool(
        IEcsLifecycleStore lifecycle,
        IAiToolApprovalGate? approval) : Tool
    {
        public override AiToolDefinition Definition { get; } = new(
            "ecs_manage_component_type",
            "Correct an unused component-type ID, migrate live components into an existing canonical type, enable or disable a type, or permanently delete a disabled unreferenced type. Migration validates every value against the target schema and can supply exact revision-bound replacement values. Immutable projection or trigger definitions block correction. Trusted host approval is required.",
            """{"type":"object","additionalProperties":false,"required":["action","qualifiedTypeId"],"properties":{"action":{"enum":["rename","migrate","enable","disable","delete"]},"qualifiedTypeId":{"type":"string","minLength":3,"maxLength":200},"correctedQualifiedTypeId":{"type":"string","minLength":3,"maxLength":200},"rewrittenValues":{"type":"array","maxItems":2000,"items":{"type":"object","additionalProperties":false,"required":["stateSpaceId","entityId","expectedRevision","value"],"properties":{"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"entityId":{"type":"string","minLength":1,"maxLength":200},"expectedRevision":{"type":"integer","minimum":1},"value":{}}}}}}""");

        public override async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (approval is null || !await approval.ConfirmAsync(new(
                    Definition.Name, Definition.Description, invocation.Arguments.Clone()), cancellationToken))
                return AiToolResult.Failure("AI_TOOL_CONFIRMATION_REQUIRED",
                    "Trusted host confirmation is required for ECS lifecycle changes.");
            try
            {
                var id = invocation.Arguments.GetProperty("qualifiedTypeId").GetString()!;
                var action = invocation.Arguments.GetProperty("action").GetString();
                return action switch
                {
                    "rename" => Result(await lifecycle.RenameComponentTypeAsync(id,
                        RequiredString(invocation.Arguments, "correctedQualifiedTypeId"), cancellationToken)),
                    "migrate" => Result(await lifecycle.MigrateComponentTypeAsync(id,
                        RequiredString(invocation.Arguments, "correctedQualifiedTypeId"),
                        MigrationValues(invocation.Arguments), cancellationToken)),
                    "enable" => Result(await lifecycle.SetComponentTypeEnabledAsync(id, true, cancellationToken)),
                    "disable" => Result(await lifecycle.SetComponentTypeEnabledAsync(id, false, cancellationToken)),
                    "delete" => Result(new { deleted = await lifecycle.DeleteComponentTypeAsync(id, cancellationToken) }),
                    _ => AiToolResult.Failure("INVALID_ECS_LIFECYCLE_ACTION", "Unknown component-type lifecycle action.")
                };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure(exception);
            }
        }
    }

    private sealed class InspectEntityTool(IEcsLifecycleStore lifecycle) : Tool
    {
        public override AiToolDefinition Definition { get; } = new(
            "ecs_inspect_entity",
            "Inspect an ECS entity, including disabled status and exact reference blockers.",
            """{"type":"object","additionalProperties":false,"required":["stateSpaceId","entityId"],"properties":{"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"entityId":{"type":"string","minLength":1,"maxLength":200}}}""");

        public override async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return Result(await lifecycle.GetEntityAsync(
                    invocation.Arguments.GetProperty("stateSpaceId").GetString()!,
                    invocation.Arguments.GetProperty("entityId").GetString()!, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure(exception);
            }
        }
    }

    private sealed class ManageEntityTool(
        IEcsLifecycleStore lifecycle,
        IAiToolApprovalGate? approval) : Tool
    {
        public override AiToolDefinition Definition { get; } = new(
            "ecs_manage_entity",
            "Edit an entity name, correct an unused entity ID, enable or disable an entity, or permanently delete a disabled unused entity. Trusted host approval is required.",
            """{"type":"object","additionalProperties":false,"required":["action","stateSpaceId","entityId"],"properties":{"action":{"enum":["update","enable","disable","delete"]},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"entityId":{"type":"string","minLength":1,"maxLength":200},"correctedEntityId":{"type":"string","minLength":1,"maxLength":200},"name":{"type":"string","minLength":1,"maxLength":400},"expectedRevision":{"type":"integer","minimum":1}}}""");

        public override async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (approval is null || !await approval.ConfirmAsync(new(
                    Definition.Name, Definition.Description, invocation.Arguments.Clone()), cancellationToken))
                return AiToolResult.Failure("AI_TOOL_CONFIRMATION_REQUIRED",
                    "Trusted host confirmation is required for ECS lifecycle changes.");
            try
            {
                var stateSpaceId = invocation.Arguments.GetProperty("stateSpaceId").GetString()!;
                var entityId = invocation.Arguments.GetProperty("entityId").GetString()!;
                var action = invocation.Arguments.GetProperty("action").GetString();
                return action switch
                {
                    "update" => Result(await lifecycle.UpdateEntityAsync(
                        stateSpaceId, entityId,
                        OptionalString(invocation.Arguments, "correctedEntityId") ?? entityId,
                        RequiredString(invocation.Arguments, "name"),
                        RequiredRevision(invocation.Arguments), cancellationToken)),
                    "enable" => Result(await lifecycle.SetEntityEnabledAsync(
                        stateSpaceId, entityId, true, RequiredRevision(invocation.Arguments), cancellationToken)),
                    "disable" => Result(await lifecycle.SetEntityEnabledAsync(
                        stateSpaceId, entityId, false, RequiredRevision(invocation.Arguments), cancellationToken)),
                    "delete" => Result(new
                    {
                        deleted = await lifecycle.DeleteEntityPermanentlyAsync(
                            stateSpaceId, entityId, cancellationToken)
                    }),
                    _ => AiToolResult.Failure("INVALID_ECS_LIFECYCLE_ACTION", "Unknown entity lifecycle action.")
                };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure(exception);
            }
        }
    }

    private static string RequiredString(JsonElement arguments, string name) =>
        OptionalString(arguments, name) ?? throw new ArgumentException($"'{name}' is required for this action.");

    private static string? OptionalString(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredRevision(JsonElement arguments) =>
        arguments.TryGetProperty("expectedRevision", out var value) && value.TryGetInt32(out var revision)
            ? revision
            : throw new ArgumentException("'expectedRevision' is required for this action.");

    private static IReadOnlyList<EcsComponentMigrationValue> MigrationValues(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("rewrittenValues", out var values)) return [];
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 2000)
            throw new ArgumentException("'rewrittenValues' must be a bounded array.");
        return values.EnumerateArray().Select(value => new EcsComponentMigrationValue(
            RequiredString(value, "stateSpaceId"),
            RequiredString(value, "entityId"),
            value.TryGetProperty("expectedRevision", out var revision) && revision.TryGetInt32(out var number)
                ? number : throw new ArgumentException("Each rewritten value needs expectedRevision."),
            value.TryGetProperty("value", out var data)
                ? data.GetRawText() : throw new ArgumentException("Each rewritten value needs value."))).ToArray();
    }
}
