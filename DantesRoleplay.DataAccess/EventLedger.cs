using DantesRoleplay.Events;
using Microsoft.EntityFrameworkCore;
namespace DantesRoleplay.DataAccess;
public sealed class EventLedger(DantesRoleplayDbContext db) : IEventLedger
{
    private readonly DantesRoleplayDbContext _db = db;
    public async Task WriteAcceptedAsync(IReadOnlyList<ProposedEvent> proposals, string correlationId, CancellationToken cancellationToken = default)
    {
        foreach (var proposal in proposals.OrderBy(x => x.Ordinal))
        {
            var type = await _db.EventTypes.FirstAsync(x => x.Id == proposal.Type && x.Status == EventTypeStatus.Active, cancellationToken);
            var row = new EventRecord { Id = Guid.NewGuid().ToString("n"), TypeId = proposal.Type, TypeVersion = type.CurrentVersion, Scope = proposal.Scope, PayloadJson = proposal.PayloadJson, Timestamp = DateTime.UtcNow, CorrelationId = correlationId, Depth = 0, Sequence = proposal.Ordinal, RootOperationId = "" };
            foreach (var (entityId, ordinal) in proposal.EntityIds.Select((id, index) => (id, index))) row.Entities.Add(new EventEntity { EventId = row.Id, EntityId = entityId, Ordinal = ordinal });
            _db.Events.Add(row);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }
    public async Task AttachRootOperationAsync(string correlationId, string operationId, CancellationToken cancellationToken = default) { await _db.Events.Where(e => e.CorrelationId == correlationId && e.RootOperationId == "").ExecuteUpdateAsync(s => s.SetProperty(e => e.RootOperationId, operationId), cancellationToken); }
    public async Task<IReadOnlyList<EventSummary>> FindAsync(string? id = null, string? correlationId = null, string? type = null, string? entityId = null, int limit = 50, CancellationToken cancellationToken = default)
    { var q = _db.Events.AsNoTracking().Include(e => e.Entities).AsQueryable(); if (!string.IsNullOrWhiteSpace(id)) q=q.Where(e=>e.Id==id); if(!string.IsNullOrWhiteSpace(correlationId)) q=q.Where(e=>e.CorrelationId==correlationId); if(!string.IsNullOrWhiteSpace(type)) q=q.Where(e=>e.TypeId==type); if(!string.IsNullOrWhiteSpace(entityId)) q=q.Where(e=>e.Entities.Any(x=>x.EntityId==entityId)); return await q.OrderBy(e=>e.Timestamp).ThenBy(e=>e.Sequence).Take(Math.Clamp(limit,1,200)).Select(e=>new EventSummary(e.Id,e.TypeId,e.TypeVersion,e.Timestamp,e.CorrelationId,e.Sequence,e.RootOperationId,e.Entities.OrderBy(x=>x.Ordinal).Select(x=>x.EntityId).ToList())).ToListAsync(cancellationToken); }
}
