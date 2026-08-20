using System.ComponentModel;
using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The write side of the three-verb MCP surface. Payloads are deliberately JSON objects so each
/// commit kind can retain the shape of its existing implementation handler.
///
/// The kind list is <see cref="VerbSurface.CommitKinds"/> and nothing else; a guard test asserts
/// the switch below against it in both directions. Every rejection carries the expected payload
/// in full rather than a pointer to it, so a session that guessed wrong is corrected in the same
/// round trip instead of spending another one asking.
/// </summary>
[McpServerToolType]
public sealed class CommitTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [McpServerTool(Name = "commit")]
    [Description(
        "Change anything in this system. kind is one of: procedure, component, effects, mechanic, event-type, subscription, " +
        "action, notification. payload is a JSON object encoded as a string, shaped per kind — " +
        "query(kind: \"capabilities\") gives every shape exactly, and a rejection repeats the one " +
        "you needed. Where dryRun is supported, send dryRun: true first and read every check, then " +
        "commit the identical payload. This is the only path that changes state.")]
    public async Task<ToolEnvelope> CommitAsync(
        IProcedureStore procedures,
        IWorldStore world,
        IEffectApplier effects,
        IMechanicStore mechanics,
        IEventTypeStore eventTypes,
        ISubscriptionStore subscriptions,
        IActionRunner actions,
        IOperationLog log,
        INotificationStore notifications,
        [Description("Closed kind: procedure, component, effects, mechanic, event-type, subscription, action, or notification.")]
        string kind,
        [Description("JSON object containing the selected kind's existing tool arguments.")]
        string payload,
        [Description(
            "What you were trying to achieve, in your own words. For kind \"action\" the payload's "
            + "own intent is what gets recorded, because it is also what selects the rule.")]
        string intent = "",
        [Description("Procedure ids consulted before this commit.")] string[]? proceduresUsed = null,
        [Description("Validate without changing state where supported.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;
        var spec = VerbSurface.Commit(normalizedKind);

        if (spec is null)
        {
            return await ToolRunner.RunAsync(
                log,
                "commit",
                intent,
                $"commit:{normalizedKind}",
                proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail(
                    "UNKNOWN_KIND",
                    $"Unknown commit kind '{kind}'. Valid kinds: "
                    + $"{string.Join(", ", VerbSurface.CommitKindNames)}.",
                    "query(kind: \"capabilities\")",
                    $"Rejected commit kind '{kind}'.")),
                consumesReadEvidence: !dryRun);
        }

        if (dryRun && !spec.SupportsDryRun)
        {
            return await ToolRunner.RunAsync(
                log,
                "commit",
                intent,
                $"commit:{normalizedKind}",
                proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail(
                    "NOT_SUPPORTED",
                    $"Dry run is not supported for commit kind '{normalizedKind}'. "
                    + (normalizedKind == "action"
                        ? "An action dry-runs its own effects internally before applying them."
                        : "Send the commit itself; it validates before it writes."),
                    VerbSurface.CommitCall(normalizedKind),
                    $"Rejected unsupported dry run for '{normalizedKind}'.")),
                consumesReadEvidence: false);
        }

        if (!TryReadObject(payload, out var document, out var parseError))
        {
            return await InvalidPayloadEnvelope(
                log, normalizedKind, parseError!, intent, proceduresUsed, dryRun);
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
                "event-type" => await CommitEventTypeAsync(eventTypes, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "subscription" => await CommitSubscriptionAsync(subscriptions, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "action" => await CommitActionAsync(
                    actions, log, parsedPayload.RootElement, intent, proceduresUsed, cancellationToken),
                "notification" => await CommitNotificationAsync(
                    notifications, log, parsedPayload.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
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
        IOperationLog log,
        JsonElement payload,
        string intent,
        string[]? proceduresUsed,
        CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out ActionPayload? value, out var error) ||
            value is null ||
            string.IsNullOrWhiteSpace(value.Intent))
        {
            // Through ToolRunner like every other rejection: a failure nobody recorded is the one
            // thing history cannot show you afterwards (§P3).
            return await InvalidPayloadEnvelope(
                log,
                "action",
                error ?? "Action payload requires intent — what the actor is trying to do, in "
                    + "the player's words.",
                intent,
                proceduresUsed,
                dryRun: false);
        }

        return await new ActionTools().RunActionAsync(
            actions,
            // The payload's intent is the one that both selects the rule and lands in the audit.
            // Falling back means a caller who filled in only the top-level intent is not silently
            // ignored — but the two cannot both be recorded without a kernel change.
            string.IsNullOrWhiteSpace(value.Intent) ? intent : value.Intent,
            value.RoleEntityIds,
            value.Input ?? "{}",
            value.Scope,
            value.Seed,
            proceduresUsed,
            cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitEventTypeAsync(IEventTypeStore eventTypes, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out EventTypePayload? value, out var error) || value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Category) || string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Schema)) return await InvalidPayloadEnvelope(log, "event-type", error ?? "Event type payload requires id, category, name, and schema.", intent, proceduresUsed, dryRun);
        return await new EventTypeTools().WriteAsync(eventTypes, log, value.Id, value.Category, value.Name, value.Schema, value.Description ?? string.Empty, value.Scope ?? string.Empty, value.Status, value.ChangeNote ?? string.Empty, intent, proceduresUsed, dryRun, cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitSubscriptionAsync(ISubscriptionStore subscriptions, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out SubscriptionPayload? value, out var error) || value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Category) || string.IsNullOrWhiteSpace(value.EventTypeId) || string.IsNullOrWhiteSpace(value.EventMechanicId) || string.IsNullOrWhiteSpace(value.Mode) || !Enum.TryParse<SubscriptionMode>(value.Mode, true, out var mode)) return await InvalidPayloadEnvelope(log, "subscription", error ?? "Subscription payload requires id, category, eventTypeId, eventMechanicId, and mode (guard or reaction).", intent, proceduresUsed, dryRun);
        if (!string.IsNullOrWhiteSpace(value.Status) && !Enum.TryParse<SubscriptionStatus>(value.Status, true, out var _)) return await InvalidPayloadEnvelope(log, "subscription", "status must be draft, active, disabled, or archived.", intent, proceduresUsed, dryRun);
        return await new SubscriptionTools().WriteAsync(subscriptions, log, new WriteSubscriptionRequest { Id = value.Id, Category = value.Category, EventTypeId = value.EventTypeId, EventMechanicId = value.EventMechanicId, Mode = mode, Order = value.Order, FixedRoleEntityIdsJson = value.FixedRoleEntityIdsJson ?? "{}", TrackedEntityIdsJson = value.TrackedEntityIdsJson ?? "[]", PayloadEqualsJson = value.PayloadEqualsJson ?? "{}", MaxExecutionsPerChain = value.MaxExecutionsPerChain ?? 1, Scope = value.Scope ?? string.Empty, Status = string.IsNullOrWhiteSpace(value.Status) ? null : Enum.Parse<SubscriptionStatus>(value.Status, true), ChangeNote = value.ChangeNote ?? string.Empty }, intent, proceduresUsed, dryRun, cancellationToken);
    }

    /// <summary>
    /// The one commit that changes no content anywhere. It moves a notice's delivery state and can
    /// touch nothing else — a call able to edit what a notice says would turn evidence into a draft.
    /// </summary>
    private static async Task<ToolEnvelope> CommitNotificationAsync(INotificationStore notifications, IOperationLog log, JsonElement payload, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken)
    {
        if (!TryDeserialize(payload, out NotificationPayload? value, out var error) || value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.State)) return await InvalidPayloadEnvelope(log, "notification", error ?? "Notification payload requires id and state (unread, read, or archived).", intent, proceduresUsed, dryRun);
        return await new NotificationTools().SetStateAsync(notifications, log, value.Id, value.State, intent, proceduresUsed, dryRun, cancellationToken);
    }

    /// <summary>
    /// D4: the expected shape travels inside the rejection, not as a pointer to where the shape
    /// lives. A session that guessed wrong then has everything it needs in the same round trip.
    /// </summary>
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
                $"{why} Expected payload for kind '{kind}': "
                + $"{VerbSurface.Commit(kind)?.Payload ?? "{}"}.",
                VerbSurface.CommitCall(kind, dryRun),
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
    private sealed class EventTypePayload { public string? Id { get; init; } public string? Category { get; init; } public string? Name { get; init; } public string? Description { get; init; } public string? Schema { get; init; } public string? Scope { get; init; } public string? Status { get; init; } public string? ChangeNote { get; init; } }
    private sealed class NotificationPayload { public string? Id { get; init; } public string? State { get; init; } }

    private sealed class SubscriptionPayload { public string? Id { get; init; } public string? Category { get; init; } public string? EventTypeId { get; init; } public string? EventMechanicId { get; init; } public string? Mode { get; init; } public int Order { get; init; } public string? FixedRoleEntityIdsJson { get; init; } public string? TrackedEntityIdsJson { get; init; } public string? PayloadEqualsJson { get; init; } public int? MaxExecutionsPerChain { get; init; } public string? Scope { get; init; } public string? Status { get; init; } public string? ChangeNote { get; init; } }
}
