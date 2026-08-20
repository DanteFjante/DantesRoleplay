using DantesRoleplay.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Reads notifications and moves their delivery state.
///
/// There is deliberately no write path for content here. A notice's text and links come from one
/// place — a reaction that ran and committed with its whole chain — and this class cannot produce
/// one. Administrative calls move a notice through its lifecycle; they cannot make one up, and
/// they cannot edit one that exists.
/// </summary>
public sealed class NotificationStore(DantesRoleplayDbContext db) : INotificationStore
{
    /// <summary>Highest number of rows one read returns, whatever the caller asks for.</summary>
    public const int MaxLimit = 200;

    private readonly DantesRoleplayDbContext _db = db;

    public async Task<IReadOnlyList<NotificationView>> FindAsync(
        string? id = null,
        NotificationState? state = null,
        string? topic = null,
        string? entityId = null,
        string? correlationId = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Notifications.AsNoTracking().Include(n => n.Entities).AsQueryable();

        if (!string.IsNullOrWhiteSpace(id))
        {
            query = query.Where(n => n.Id == id.Trim());
        }

        if (state is not null)
        {
            query = query.Where(n => n.State == state);
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            query = query.Where(n => n.Topic == topic.Trim());
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(n => n.CorrelationId == correlationId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            // Through the join index, never by matching the body. Text search would find notices
            // that merely mention a name and miss every one that does not spell it out.
            var wanted = entityId.Trim();
            query = query.Where(n => n.Entities.Any(link => link.EntityId == wanted));
        }

        if (from is not null)
        {
            query = query.Where(n => n.CreatedAt >= from);
        }

        // Exclusive, so two adjacent windows neither overlap nor skip.
        if (to is not null)
        {
            query = query.Where(n => n.CreatedAt < to);
        }

        // Newest first: a reader wants what has just happened. Ordinal breaks a tie within one
        // change, and the id after that, so the order is total rather than merely mostly stable.
        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Ordinal)
            .Take(Clamp(limit))
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Ordinal)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(View)
            .ToList();
    }

    public async Task<NotificationResult> SetStateAsync(
        string id,
        NotificationState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var row = await _db.Notifications
            .Include(n => n.Entities)
            .FirstOrDefaultAsync(n => n.Id == id.Trim(), cancellationToken);

        if (row is null)
        {
            return new NotificationResult(
                null,
                $"No notification '{id.Trim()}'. Read the list first with query(kind: \"notifications\").");
        }

        // Idempotent, and silently so. A client retrying a call it is unsure about should not have
        // to find out which way the first attempt went.
        if (row.State == state)
        {
            return new NotificationResult(View(row));
        }

        // One-way. "I have dealt with this" must not be something a later mistake quietly undoes.
        if (row.State == NotificationState.Archived)
        {
            return new NotificationResult(
                null,
                $"Notification '{row.Id}' is archived, and archiving is one-way in this release. "
                + "Nothing is lost — it is still readable with "
                + "query(kind: \"notifications\", state: \"archived\").");
        }

        var now = DateTime.UtcNow;

        switch (state)
        {
            case NotificationState.Unread:
                // Cleared, not kept. A notice marked unread is one somebody means to come back to,
                // and leaving a read timestamp on it would say the opposite.
                row.ReadAt = null;
                break;

            case NotificationState.Read:
                // Set once. The interesting fact is when it was FIRST read.
                row.ReadAt ??= now;
                break;

            case NotificationState.Archived:
                row.ArchivedAt = now;
                break;
        }

        row.State = state;

        await _db.SaveChangesAsync(cancellationToken);

        return new NotificationResult(View(row));
    }

    private static int Clamp(int limit) => limit <= 0 ? 50 : Math.Min(limit, MaxLimit);

    private static NotificationView View(Notification row) =>
        new(row.Id,
            row.Topic,
            row.Subject,
            row.Body,
            row.CorrelationId,
            row.EventId,
            row.ExecutionId,
            row.RootOperationId,
            row.Ordinal,
            row.CreatedAt,
            row.State,
            row.ReadAt,
            row.ArchivedAt,
            row.Entities.OrderBy(link => link.Ordinal).Select(link => link.EntityId).ToList());
}
