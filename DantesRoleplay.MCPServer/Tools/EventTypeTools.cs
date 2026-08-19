using DantesRoleplay.Events;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Protocol handlers for declaring event types only. They do not emit or route events.</summary>
public sealed class EventTypeTools
{
    public async Task<ToolEnvelope> FindAsync(IEventTypeStore store, IOperationLog log, string? id, int? version, string? query, string? category, string? scope, bool includeInactive, int? limit, CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "find_event_types", async () =>
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                var item = await store.GetAsync(id, version, cancellationToken);
                if (item is not null) return ToolOutcome.OkAbout(id, item, $"Read event type {id} v{item.Version}.", $"{VerbSurface.CommitCall("event-type", id, true)} — dry run a revision first.");
                return ToolOutcome.Fail("UNKNOWN_EVENT_TYPE", $"There is no event type '{id}' at the requested version.", "query(kind: \"event-types\")", $"Event type '{id}' not found.");
            }
            var items = await store.FindAsync(query, category, scope, includeInactive, limit ?? 50, cancellationToken);
            return ToolOutcome.Ok(new { EventTypes = items }, $"Found {items.Count} event type(s).", items.Count == 0 ? VerbSurface.CommitCall("event-type", true) : $"query(kind: \"event-types\", id: \"{items[0].Id}\")");
        });

    public async Task<ToolEnvelope> WriteAsync(IEventTypeStore store, IOperationLog log, string id, string category, string name, string schema, string description, string scope, string? status, string changeNote, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "write_event_type", intent, id, proceduresUsed, async () =>
        {
            if (id.StartsWith("world.", StringComparison.Ordinal)) return ToolOutcome.Fail("RESERVED_EVENT_TYPE", "world.* event types are kernel structural definitions and can only arrive from the catalog.", "query(kind: \"event-types\", category: \"world\")", $"Rejected reserved event type '{id}'.");
            if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<EventTypeStatus>(status, true, out _)) return ToolOutcome.Fail("INVALID_STATUS", $"'{status}' is not a status.", VerbSurface.CommitCall("event-type", id), $"Rejected '{id}': bad status.");
            var request = new WriteEventTypeRequest { Id = id, Category = category, Name = name, Description = description, PayloadSchema = schema, Scope = scope, Status = string.IsNullOrWhiteSpace(status) ? null : Enum.Parse<EventTypeStatus>(status, true), ChangeNote = changeNote };
            var checks = await store.CheckAsync(request, cancellationToken);
            if (dryRun) return ToolOutcome.Ok(new { Checks = checks, CanWrite = checks.All(x => x.Passed || !x.Blocking) }, $"Dry run for event type '{id}'.", VerbSurface.CommitCall("event-type", id));
            var failed = checks.FirstOrDefault(x => x.Blocking && !x.Passed); if (failed is not null) return ToolOutcome.Fail("INVALID_EVENT_TYPE", failed.Detail, VerbSurface.CommitCall("event-type", id, true), $"Rejected event type '{id}'.");
            var result = await store.WriteAsync(request, cancellationToken); return ToolOutcome.OkAbout(id, result.EventType, result.Created ? $"Created event type '{id}'." : $"Created version {result.EventType.Version} of event type '{id}'.", "This only registers the type; event emission and subscriptions are not active yet.");
        }, consumesReadEvidence: !dryRun);
}
