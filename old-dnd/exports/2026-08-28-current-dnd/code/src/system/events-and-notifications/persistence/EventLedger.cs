using DantesRoleplay.Events;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// The structural event ledger.
///
/// Writes happen inside the caller's transaction — this class never opens or commits one. That is
/// deliberate: an event says a world change committed, so if the change rolls back the event must
/// go with it, and the only way to guarantee that is to be enrolled in the same transaction rather
/// than to manage a second one.
/// </summary>
public sealed class EventLedger(DantesRoleplayDbContext db) : IEventLedger
{
    /// <summary>A listing is for orientation. Anything larger is a chain read, and that takes filters.</summary>
    private const int MaxLimit = 200;

    private readonly DantesRoleplayDbContext _db = db;

    public async Task<IReadOnlyList<EventDetail>> WriteAcceptedAsync(
        IReadOnlyList<ProposedEvent> proposals,
        string rootOperationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootOperationId);

        if (proposals.Count == 0)
        {
            return [];
        }

        // Sequence continues from whatever this correlation already holds rather than restarting at
        // the proposal's ordinal. A reaction's effects are a second batch in the same chain, and two
        // batches both numbering from zero would make "the fourth thing that happened" ambiguous.
        var sequence = (await _db.Events
            .Where(e => e.CorrelationId == rootOperationId)
            .MaxAsync(e => (int?)e.Sequence, cancellationToken) ?? -1) + 1;

        // One timestamp for the whole batch. These rows are one atomic world change, and stamping
        // them individually would suggest an ordering that the clock cannot actually resolve —
        // Sequence is what orders them, and it is exact.
        var timestamp = DateTime.UtcNow;

        // The versions in force right now, read once. A per-proposal lookup would be a query per
        // effect, and every proposal in a batch is accepted against the same registry state.
        var written = new List<EventRecord>(proposals.Count);
        var wanted = proposals.Select(p => p.Type).Distinct(StringComparer.Ordinal).ToList();

        var versions = await _db.EventTypes
            .AsNoTracking()
            .Where(t => wanted.Contains(t.Id) && t.Status == EventTypeStatus.Active)
            .Select(t => new { t.Id, t.CurrentVersion })
            .ToDictionaryAsync(t => t.Id, t => t.CurrentVersion, StringComparer.Ordinal, cancellationToken);

        foreach (var proposal in proposals.OrderBy(p => p.Ordinal))
        {
            if (!versions.TryGetValue(proposal.Type, out var version))
            {
                // A structural type is missing or deprecated. Throwing inside the caller's
                // transaction rolls the world change back with it, which is the right answer: an
                // accepted change that cannot be recorded is not an accepted change.
                throw new InvalidOperationException(
                    $"Event type '{proposal.Type}' is not registered and active, so the world change "
                    + "it describes cannot be recorded. Import the catalog's event types.");
            }

            var row = new EventRecord
            {
                Id = Guid.NewGuid().ToString("n"),
                TypeId = proposal.Type,
                TypeVersion = version,
                Scope = proposal.Scope,
                PayloadJson = proposal.PayloadJson,
                Timestamp = timestamp,

                // One value in both columns. See the note on EventRecord.RootOperationId.
                CorrelationId = rootOperationId,
                RootOperationId = rootOperationId,

                // From the proposal, not assumed. A root world change proposes at depth 0 with no
                // causation; a reaction's children name the event they answer and sit one deeper.
                CausationId = proposal.CausationId,
                Depth = proposal.Depth,
                Sequence = sequence++,

                // Empty for a structural event: nothing declared it, it followed from the change.
                ProducerExecutionId = proposal.ProducerExecutionId
            };

            var ordinal = 0;

            foreach (var entityId in proposal.EntityIds)
            {
                row.Entities.Add(new EventEntity
                {
                    EventId = row.Id,
                    EntityId = entityId,
                    Ordinal = ordinal++
                });
            }

            _db.Events.Add(row);
            written.Add(row);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return written
            .Select(row => new EventDetail(
                row.Id, row.TypeId, row.TypeVersion, row.Scope, row.PayloadJson, row.Timestamp,
                row.CorrelationId, row.CausationId, row.Depth, row.Sequence, row.RootOperationId,
                row.Entities.OrderBy(x => x.Ordinal).Select(x => x.EntityId).ToList(),
                row.ProducerExecutionId))
            .ToList();
    }

    public async Task<EventDetail?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var row = await _db.Events
            .AsNoTracking()
            .Include(e => e.Entities)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return row is null
            ? null
            : new EventDetail(
                row.Id,
                row.TypeId,
                row.TypeVersion,
                row.Scope,
                row.PayloadJson,
                row.Timestamp,
                row.CorrelationId,
                row.CausationId,
                row.Depth,
                row.Sequence,
                row.RootOperationId,
                row.Entities.OrderBy(x => x.Ordinal).Select(x => x.EntityId).ToList(),
                row.ProducerExecutionId);
    }

    public async Task<EventHistoryPage> ListRecentAsync(
        EventHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        var rows = _db.Events.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.TypeId))
        {
            rows = rows.Where(e => e.TypeId == query.TypeId);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            rows = rows.Where(e => e.Entities.Any(x => x.EntityId == query.EntityId));
        }

        if (!string.IsNullOrWhiteSpace(query.RootOperationId))
        {
            rows = rows.Where(e => e.RootOperationId == query.RootOperationId);
        }

        if (query.Before is { } before)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(before.Id);
            rows = rows.Where(e =>
                e.Timestamp < before.Timestamp ||
                (e.Timestamp == before.Timestamp && e.Sequence < before.Sequence) ||
                (e.Timestamp == before.Timestamp && e.Sequence == before.Sequence &&
                    string.Compare(e.Id, before.Id) < 0));
        }

        var page = await rows
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Sequence)
            .ThenByDescending(e => e.Id)
            .Take(limit + 1)
            .Select(e => new EventSummary(
                e.Id,
                e.TypeId,
                e.TypeVersion,
                e.Scope,
                e.Timestamp,
                e.CorrelationId,
                e.CausationId,
                e.Depth,
                e.Sequence,
                e.RootOperationId,
                e.Entities.OrderBy(x => x.Ordinal).Select(x => x.EntityId).ToList()))
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var events = hasMore ? page.Take(limit).ToList() : page;
        var last = events.LastOrDefault();
        return new EventHistoryPage(
            events,
            hasMore && last is not null
                ? new EventHistoryCursor(last.Timestamp, last.Sequence, last.Id)
                : null);
    }

    public async Task<IReadOnlyList<EventSummary>> FindAsync(
        string? correlationId = null,
        string? causationId = null,
        string? rootOperationId = null,
        string? type = null,
        string? entityId = null,
        int? afterSequence = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var rows = _db.Events.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            rows = rows.Where(e => e.CorrelationId == correlationId);
        }

        if (!string.IsNullOrWhiteSpace(causationId))
        {
            rows = rows.Where(e => e.CausationId == causationId);
        }

        if (!string.IsNullOrWhiteSpace(rootOperationId))
        {
            rows = rows.Where(e => e.RootOperationId == rootOperationId);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            rows = rows.Where(e => e.TypeId == type);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            // Through the join rows, so this is an index seek rather than a scan of every payload
            // in the ledger looking for an id inside the JSON.
            rows = rows.Where(e => e.Entities.Any(x => x.EntityId == entityId));
        }

        if (afterSequence is { } sequence)
        {
            rows = rows.Where(e => e.Sequence > sequence);
        }

        if (from is { } lower)
        {
            rows = rows.Where(e => e.Timestamp >= lower);
        }

        if (to is { } upper)
        {
            rows = rows.Where(e => e.Timestamp < upper);
        }

        // Id last, so the order is total. A batch shares one timestamp and two correlations can
        // share a sequence, and a page boundary that depends on which row the database happened to
        // return first is a page boundary that loses rows.
        return await rows
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Sequence)
            .ThenBy(e => e.Id)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .Select(e => new EventSummary(
                e.Id,
                e.TypeId,
                e.TypeVersion,
                e.Scope,
                e.Timestamp,
                e.CorrelationId,
                e.CausationId,
                e.Depth,
                e.Sequence,
                e.RootOperationId,
                e.Entities.OrderBy(x => x.Ordinal).Select(x => x.EntityId).ToList()))
            .ToListAsync(cancellationToken);
    }
}
