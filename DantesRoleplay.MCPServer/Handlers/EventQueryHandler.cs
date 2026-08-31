using System.Globalization;
using DantesRoleplay.Events;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// Reads the structural event ledger.
///
/// Read-only, and there is no write counterpart anywhere on the MCP surface. Events are produced by
/// committing a world change and by nothing else — an event that could be written directly would be
/// a claim about a change that never happened.
/// </summary>
public sealed class EventQueryHandler
{
    public async Task<ToolEnvelope> FindAsync(
        IEventLedger ledger,
        IOperationLog log,
        string? id,
        string? correlationId,
        string? causationId,
        string? rootOperationId,
        string? type,
        string? entityId,
        int? afterSequence,
        string? from,
        string? to,
        int? limit,
        CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "find_events", async () =>
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                var one = await ledger.GetAsync(id, cancellationToken);

                return one is not null
                    ? ToolOutcome.OkAbout(
                        id,
                        one,
                        $"Read event {id} ({one.TypeId} v{one.TypeVersion}).",
                        $"query(kind: \"events\", correlationId: \"{one.CorrelationId}\") — everything else that "
                        + "committed with it.")
                    : ToolOutcome.Fail(
                        "UNKNOWN_EVENT",
                        $"There is no event '{id}'. The ledger is append-only, so an id that is not "
                        + "here was never accepted.",
                        "query(kind: \"events\")",
                        $"Event '{id}' not found.");
            }

            if (!TryParseInstant(from, out var fromUtc, out var problem)
                || !TryParseInstant(to, out var toUtc, out problem))
            {
                return ToolOutcome.Fail(
                    "INVALID_TIME",
                    problem,
                    "query(kind: \"events\", from: \"2026-08-19T00:00:00Z\")",
                    "Rejected an unparseable event time filter.");
            }

            var events = await ledger.FindAsync(
                correlationId,
                causationId,
                rootOperationId,
                type,
                entityId,
                afterSequence,
                fromUtc,
                toUtc,
                limit ?? 50,
                cancellationToken);

            if (events.Count == 0)
            {
                return ToolOutcome.Ok(
                    new { Events = events },
                    "No events matched.",
                    "query(kind: \"events\") — the whole ledger, newest last.",
                    "An empty ledger is normal until a world change commits; registering a "
                    + "subscription does not produce one.");
            }

            var last = events[^1];

            return ToolOutcome.Ok(
                new { Events = events },
                $"Found {events.Count} event(s).",
                $"query(kind: \"events\", id: \"{events[0].Id}\") — one in full, payload included.",
                $"query(kind: \"events\", correlationId: \"{last.CorrelationId}\", afterSequence: {last.Sequence}) "
                + "— the next page of that chain.");
        });

    /// <summary>
    /// Parses a bound as UTC, or explains why it could not.
    ///
    /// Taken as a string rather than a date so a malformed value comes back as a named failure with
    /// an example, instead of a protocol-level binding error the caller cannot act on. A value with
    /// no zone is read as UTC, because every timestamp in this system already is.
    /// </summary>
    private static bool TryParseInstant(string? value, out DateTime? parsed, out string problem)
    {
        parsed = null;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var instant))
        {
            parsed = instant;
            return true;
        }

        problem = $"'{value}' is not a time. Use an ISO-8601 instant, e.g. \"2026-08-19T14:30:00Z\".";
        return false;
    }
}
