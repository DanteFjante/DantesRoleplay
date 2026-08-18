using System.ComponentModel;
using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The write side of the three-verb MCP surface. Payloads are deliberately JSON objects so each
/// commit kind can retain the shape of its existing implementation handler.
/// </summary>
[McpServerToolType]
public sealed class CommitTool
{
    private static readonly string[] ValidKinds =
        ["procedure", "component", "effects", "mechanic", "action"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [McpServerTool(Name = "commit")]
    [Description(
        "Change the system through one of the closed kinds: procedure, component, effects, " +
        "mechanic, or action. Payload must be a JSON object matching the selected kind. Use " +
        "dryRun: true first for procedure, mechanic, and effects.")]
    public async Task<ToolEnvelope> CommitAsync(
        IProcedureStore procedures,
        IWorldStore world,
        IEffectApplier effects,
        IMechanicStore mechanics,
        IActionRunner actions,
        IOperationLog log,
        [Description("Closed kind: procedure, component, effects, mechanic, or action.")]
        string kind,
        [Description("JSON object containing the selected kind's existing tool arguments.")]
        string payload,
        [Description("What you were trying to achieve, in your own words.")] string intent = "",
        [Description("Procedure ids consulted before this commit.")] string[]? proceduresUsed = null,
        [Description("Validate without changing state where supported.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!ValidKinds.Contains(normalizedKind, StringComparer.Ordinal))
        {
            return await ToolRunner.RunAsync(
                log,
                "commit",
                intent,
                $"commit:{normalizedKind}",
                proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail(
                    "UNKNOWN_KIND",
                    $"Unknown commit kind '{kind}'. Valid kinds: {string.Join(", ", ValidKinds)}.",
                    "query(kind: \"capabilities\")",
                    $"Rejected commit kind '{kind}'.")),
                consumesReadEvidence: !dryRun);
        }

        if (dryRun && (normalizedKind == "component" || normalizedKind == "action"))
        {
            return await ToolRunner.RunAsync(
                log,
                "commit",
                intent,
                $"commit:{normalizedKind}",
                proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail(
                    "NOT_SUPPORTED",
                    $"Dry run is not supported for commit kind '{normalizedKind}'.",
                    $"commit(kind: \"{normalizedKind}\", payload: {PayloadArgument(normalizedKind)})",
                    $"Rejected unsupported dry run for '{normalizedKind}'.")),
                consumesReadEvidence: false);
        }

        if (!TryReadObject(payload, out var document, out var parseError))
        {
            return await ToolRunner.RunAsync(
                log,
                "commit",
                intent,
                $"commit:{normalizedKind}",
                proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail(
                    "INVALID_PAYLOAD",
                    parseError!,
                    $"commit(kind: \"{normalizedKind}\", payload: {PayloadArgument(normalizedKind)})",
                    $"Rejected invalid payload for '{normalizedKind}'.")),
                consumesReadEvidence: !dryRun);
        }

        var parsedPayload = document!;
        using (parsedPayload)
        {
            using var dispatch = ToolRunner.EnterProtocol("commit", normalizedKind, !dryRun);

            return normalizedKind switch
            {
                "procedure" => await CommitProcedureAsync(
                    procedures, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "component" => await CommitComponentAsync(
                    world, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                "effects" => await CommitEffectsAsync(
                    effects, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "mechanic" => await CommitMechanicAsync(
                    mechanics, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "action" => await CommitActionAsync(
                    actions, parsedPayload.RootElement, proceduresUsed, cancellationToken),
                _ => throw new InvalidOperationException($"Unhandled commit kind '{kind}'.")
            };
        }
    }

    private static async Task<ToolEnvelope> CommitProcedureAsync(
        IProcedureStore procedures,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out ProcedurePayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Id) ||
            string.IsNullOrWhiteSpace(value.Category) ||
            string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.Description) ||
            string.IsNullOrWhiteSpace(value.Instructions))
        {
            return await InvalidPayloadEnvelope(
                log,
                "procedure",
                error ?? "Procedure payload requires id, category, name, description, and instructions.",
                intent,
                proceduresUsed,
                dryRun);
        }

        return await new ProcedureTools().WriteProcedureAsync(
            procedures,
            log,
            value.Id,
            value.Category,
            value.Name,
            value.Description,
            value.Instructions,
            value.Governs ?? string.Empty,
            value.Constraints ?? string.Empty,
            value.Status,
            value.ChangeNote ?? string.Empty,
            intent,
            proceduresUsed,
            dryRun,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitComponentAsync(
        IWorldStore world,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out ComponentPayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Id) ||
            string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.Description))
        {
            return await InvalidPayloadEnvelope(
                log,
                "component",
                error ?? "Component payload requires id, name, and description.",
                intent,
                proceduresUsed,
                dryRun: false);
        }

        return await new WorldTools().DefineComponentAsync(
            world,
            log,
            value.Id,
            value.Name,
            value.Description,
            value.Schema ?? string.Empty,
            intent,
            proceduresUsed,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitEffectsAsync(
        IEffectApplier applier,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out EffectsPayload? value, out var error) || value?.Effects is null)
        {
            return await InvalidPayloadEnvelope(
                log,
                "effects",
                error ?? "Effects payload requires an effects array.",
                intent,
                proceduresUsed,
                dryRun);
        }

        return await new WorldTools().ApplyEffectsAsync(
            applier,
            log,
            value.Effects,
            intent,
            proceduresUsed,
            dryRun,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitMechanicAsync(
        IMechanicStore mechanics,
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out MechanicPayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Id) ||
            string.IsNullOrWhiteSpace(value.Category) ||
            string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.Source))
        {
            return await InvalidPayloadEnvelope(
                log,
                "mechanic",
                error ?? "Mechanic payload requires id, category, name, and source.",
                intent,
                proceduresUsed,
                dryRun);
        }

        return await new MechanicTools().WriteMechanicAsync(
            mechanics,
            log,
            value.Id,
            value.Category,
            value.Name,
            value.Description ?? string.Empty,
            value.Matches ?? string.Empty,
            value.Requirements ?? "{}",
            value.Source,
            value.Scope ?? string.Empty,
            value.Status,
            value.ChangeNote ?? string.Empty,
            intent,
            proceduresUsed,
            dryRun,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitActionAsync(
        IActionRunner actions,
        JsonElement payload,
        string[]? proceduresUsed,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out ActionPayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Intent))
        {
            return ToolEnvelope.Failure(
                "INVALID_PAYLOAD",
                error ?? "Action payload requires intent.",
                "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"...\\\"}\")");
        }

        return await new ActionTools().RunActionAsync(
            actions,
            value.Intent,
            value.RoleEntityIds,
            value.Input ?? "{}",
            value.Scope,
            value.Seed,
            proceduresUsed,
            cancellationToken);
    }

    private static Task<ToolEnvelope> InvalidPayloadEnvelope(
        IOperationLog log,
        string kind,
        string why,
        string intent,
        string[]? proceduresUsed,
        bool dryRun) =>
        ToolRunner.RunAsync(
            log,
            "commit",
            intent,
            $"commit:{kind}",
            proceduresUsed,
            () => Task.FromResult(ToolOutcome.Fail(
                "INVALID_PAYLOAD",
                why,
                $"commit(kind: \"{kind}\", payload: {PayloadArgument(kind)})",
                $"Rejected invalid payload for '{kind}'.")),
            consumesReadEvidence: !dryRun);

    private static bool TryReadObject(
        string payload,
        out JsonDocument? document,
        out string? error)
    {
        document = null;
        error = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "Payload is required and must be a JSON object.";
            return false;
        }

        try
        {
            document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Payload must be a JSON object, not an array, string, or scalar.";
                document.Dispose();
                document = null;
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Payload is not valid JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryDeserialize<T>(
        JsonElement payload,
        out T? value,
        out string? error)
        where T : class
    {
        try
        {
            value = payload.Deserialize<T>(JsonOptions);
            error = value is null ? "Payload could not be read as the selected object shape." : null;
            return value is not null;
        }
        catch (JsonException ex)
        {
            value = null;
            error = $"Payload does not match the selected kind: {ex.Message}";
            return false;
        }
    }

    private static string ExpectedPayload(string kind) =>
        kind switch
        {
            "procedure" => "{\"id\":\"...\",\"category\":\"...\",\"name\":\"...\",\"description\":\"...\",\"instructions\":\"...\"}",
            "component" => "{\"id\":\"...\",\"name\":\"...\",\"description\":\"...\",\"schema\":\"{}\"}",
            "effects" => "{\"effects\":[{\"type\":\"entity.create\",\"entityId\":\"...\",\"name\":\"...\"}]}",
            "mechanic" => "{\"id\":\"...\",\"category\":\"...\",\"name\":\"...\",\"source\":\"...\"}",
            "action" => "{\"intent\":\"...\",\"roleEntityIds\":{},\"input\":\"{}\"}",
            _ => "{}"
        };

    private static string PayloadArgument(string kind) =>
        JsonSerializer.Serialize(ExpectedPayload(kind));

    private sealed class ProcedurePayload
    {
        public string? Id { get; init; }
        public string? Category { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Instructions { get; init; }
        public string? Governs { get; init; }
        public string? Constraints { get; init; }
        public string? Status { get; init; }
        public string? ChangeNote { get; init; }
    }

    private sealed class ComponentPayload
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Schema { get; init; }
    }

    private sealed class EffectsPayload
    {
        public Effect[]? Effects { get; init; }
    }

    private sealed class MechanicPayload
    {
        public string? Id { get; init; }
        public string? Category { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Matches { get; init; }
        public string? Requirements { get; init; }
        public string? Source { get; init; }
        public string? Scope { get; init; }
        public string? Status { get; init; }
        public string? ChangeNote { get; init; }
    }

    private sealed class ActionPayload
    {
        public string? Intent { get; init; }
        public Dictionary<string, string>? RoleEntityIds { get; init; }
        public string? Input { get; init; }
        public string? Scope { get; init; }
        public long? Seed { get; init; }
    }
}
