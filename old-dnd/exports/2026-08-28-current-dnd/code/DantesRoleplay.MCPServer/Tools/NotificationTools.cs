using System.Globalization;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// Reads notifications and moves their delivery state.
///
/// Two operations and no third. Nothing here creates a notice or edits one: content and links come
/// from a reaction that committed with its whole chain, and an administrative call that could also
/// rewrite the text would turn evidence into a draft.
/// </summary>
public sealed class NotificationTools
{
    public async Task<ToolEnvelope> FindAsync(
        INotificationStore notifications,
        IOperationLog log,
        string? id,
        string? state,
        string? topic,
        string? entityId,
        string? correlationId,
        string? from,
        string? to,
        int? limit,
        CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "find_notifications", async () =>
        {
            if (!TryParseState(state, out var parsedState, out var stateProblem))
            {
                return ToolOutcome.Fail("INVALID_STATE", stateProblem,
                    "query(kind: \"notifications\", state: \"unread\")",
                    $"Rejected notification state '{state}'.");
            }

            if (!TryParseInstant(from, out var fromUtc, out var timeProblem)
                || !TryParseInstant(to, out var toUtc, out timeProblem))
            {
                return ToolOutcome.Fail("INVALID_TIME", timeProblem,
                    "query(kind: \"notifications\", from: \"2026-08-19T00:00:00Z\")",
                    "Rejected an unparseable notification time filter.");
            }

            var found = await notifications.FindAsync(
                id, parsedState, topic, entityId, correlationId, fromUtc, toUtc, limit ?? 50, cancellationToken);

            if (found.Count == 0)
            {
                return ToolOutcome.Ok(
                    new { Notifications = found },
                    "No notifications matched.",
                    "query(kind: \"notifications\") — everything, newest first.",
                    "Nothing here is normal until a reaction that raises one commits. Registering a "
                    + "subscription raises nothing by itself.");
            }

            var first = found[0];

            return ToolOutcome.Ok(
                new { Notifications = found },
                $"Found {found.Count} notification(s).",
                VerbSurface.CommitCall("notification", first.Id) + " — mark one read.",
                $"query(kind: \"events\", correlationId: \"{first.CorrelationId}\") — the chain that "
                + "produced it, if you want to know why you were told.");
        });

    public async Task<ToolEnvelope> SetStateAsync(
        INotificationStore notifications,
        IOperationLog log,
        string? id,
        string? state,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "set_notification_state", intent, id ?? string.Empty, proceduresUsed, async () =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return ToolOutcome.Fail("MISSING_ID",
                    "A notification state change names exactly one notice.",
                    VerbSurface.CommitCall("notification"),
                    "Rejected a notification state change with no id.");
            }

            if (!TryParseState(state, out var parsedState, out var problem) || parsedState is null)
            {
                return ToolOutcome.Fail("INVALID_STATE",
                    parsedState is null && problem.Length == 0
                        ? "A state is required: unread, read, or archived."
                        : problem,
                    VerbSurface.CommitCall("notification", id),
                    $"Rejected notification state '{state}'.");
            }

            if (dryRun)
            {
                // Reads the row to say what WOULD happen, and writes nothing. The interesting part
                // of this dry run is whether the notice exists and whether it is already archived.
                var existing = await notifications.FindAsync(id: id, limit: 1, cancellationToken: cancellationToken);

                if (existing.Count == 0)
                {
                    return ToolOutcome.Fail("UNKNOWN_NOTIFICATION",
                        $"There is no notification '{id}'.",
                        "query(kind: \"notifications\")",
                        $"Notification '{id}' not found on a dry run.");
                }

                var current = existing[0];

                return ToolOutcome.OkAbout(
                    current.Id,
                    new { current.Id, From = current.State, To = parsedState, WouldChange = current.State != parsedState },
                    current.State == parsedState
                        ? $"Notification {current.Id} is already {Name(parsedState.Value)}; committing changes nothing."
                        : $"Notification {current.Id} would go from {Name(current.State)} to {Name(parsedState.Value)}.",
                    VerbSurface.CommitCall("notification", current.Id));
            }

            var result = await notifications.SetStateAsync(id, parsedState.Value, cancellationToken);

            if (!result.Ok)
            {
                return ToolOutcome.Fail("NOTIFICATION_REFUSED", result.Problem,
                    "query(kind: \"notifications\")",
                    $"Refused a state change on '{id}': {result.Problem}");
            }

            var notice = result.Notification!;

            return ToolOutcome.OkAbout(
                notice.Id,
                notice,
                $"Notification {notice.Id} is now {Name(notice.State)}.",
                "query(kind: \"notifications\", state: \"unread\") — what is still waiting.");
        }, consumesReadEvidence: !dryRun);

    private static string Name(NotificationState state) => state.ToString().ToLowerInvariant();

    private static bool TryParseState(string? value, out NotificationState? parsed, out string problem)
    {
        parsed = null;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<NotificationState>(value.Trim(), ignoreCase: true, out var state))
        {
            parsed = state;
            return true;
        }

        problem = $"'{value}' is not a notification state. Use unread, read, or archived.";
        return false;
    }

    /// <summary>
    /// Same reasoning as the event ledger's: a bound arrives as a string so a malformed one comes
    /// back as a named failure with an example, rather than as a binding error nobody can act on.
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
