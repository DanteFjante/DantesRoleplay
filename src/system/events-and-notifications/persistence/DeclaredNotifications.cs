using DantesRoleplay.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <param name="Code">Empty when every notice was accepted; a stable failure code otherwise.</param>
internal sealed record NotificationBatch(
    IReadOnlyList<Notification> Rows,
    string Code = "",
    string Reason = "")
{
    public bool Ok => Code.Length == 0;
}

/// <summary>
/// Turns the notices a reaction raised into rows, or refuses the whole root change.
///
/// Refusing is the right severity for the same reason it is for a declared event: a notice is a
/// statement to a person, carrying the authority of having come out of a committed change. One
/// with no subject, or pointing at a creature that does not exist, is a rule that is confused
/// about the world, and telling somebody anyway is worse than telling them nothing.
/// </summary>
internal static class DeclaredNotifications
{
    private const string Failure = "SUBSCRIBER_INVALID_NOTIFICATION";

    public static async Task<NotificationBatch> BuildAsync(
        DantesRoleplayDbContext db,
        IReadOnlyList<DeclaredNotification> declared,
        string subscriptionId,
        string executionId,
        string correlationId,
        string eventId,
        int firstOrdinal,
        CancellationToken cancellationToken)
    {
        if (declared.Count == 0)
        {
            return new NotificationBatch([]);
        }

        var rows = new List<Notification>(declared.Count);
        var timestamp = DateTime.UtcNow;

        for (var index = 0; index < declared.Count; index++)
        {
            var candidate = declared[index];
            var where = $"Subscription '{subscriptionId}' raised notification {index}";

            var topic = (candidate.Topic ?? string.Empty).Trim();
            var subject = (candidate.Subject ?? string.Empty).Trim();

            if (topic.Length == 0)
            {
                return Reject($"{where} with no topic. A topic is how a reader finds it again.");
            }

            if (subject.Length == 0)
            {
                return Reject(
                    $"{where} with no subject. A notice has to be readable in a list without "
                    + "opening it, which is what the subject is for.");
            }

            var row = new Notification
            {
                Id = Guid.NewGuid().ToString("n"),
                Topic = topic,
                Subject = subject,
                Body = (candidate.Body ?? string.Empty).Trim(),
                CorrelationId = correlationId,
                EventId = eventId,
                ExecutionId = executionId,
                RootOperationId = correlationId,
                Ordinal = firstOrdinal + index,
                CreatedAt = timestamp,
                State = NotificationState.Unread
            };

            var ordinal = 0;

            foreach (var raw in candidate.EntityIds ?? [])
            {
                var entityId = (raw ?? string.Empty).Trim();

                if (entityId.Length == 0)
                {
                    continue;
                }

                var exists = await db.Entities.AsNoTracking()
                    .AnyAsync(e => e.Id == entityId && e.DeletedAt == null, cancellationToken);

                if (!exists)
                {
                    return Reject(
                        $"{where} about entity '{entityId}', which does not exist or has been "
                        + "deleted. A notice nobody can find by the thing it concerns is a notice "
                        + "nobody will find.");
                }

                if (row.Entities.Any(link => string.Equals(link.EntityId, entityId, StringComparison.Ordinal)))
                {
                    continue;
                }

                row.Entities.Add(new NotificationEntity
                {
                    NotificationId = row.Id,
                    EntityId = entityId,
                    Ordinal = ordinal++
                });
            }

            rows.Add(row);
        }

        return new NotificationBatch(rows);

        static NotificationBatch Reject(string reason) => new([], Failure, reason);
    }
}
