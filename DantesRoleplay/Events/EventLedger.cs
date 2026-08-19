namespace DantesRoleplay.Events;

/// <summary>An accepted structural event. Rows are append-only runtime evidence, never catalog content.</summary>
public sealed class EventRecord
{
    public required string Id { get; set; }
    public required string TypeId { get; set; }
    public int TypeVersion { get; set; }
    public string Scope { get; set; } = string.Empty;
    public required string PayloadJson { get; set; }
    public DateTime Timestamp { get; set; }
    public required string CorrelationId { get; set; }
    public string CausationId { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int Sequence { get; set; }
    public string RootOperationId { get; set; } = string.Empty;
    public ICollection<EventEntity> Entities { get; set; } = new List<EventEntity>();
}

public sealed class EventEntity { public int Id { get; set; } public required string EventId { get; set; } public required string EntityId { get; set; } public int Ordinal { get; set; } public EventRecord? Event { get; set; } }
public sealed record EventSummary(string Id, string TypeId, int TypeVersion, DateTime Timestamp, string CorrelationId, int Sequence, string RootOperationId, IReadOnlyList<string> EntityIds);
public interface IEventLedger { Task<IReadOnlyList<EventSummary>> FindAsync(string? id = null, string? correlationId = null, string? type = null, string? entityId = null, int limit = 50, CancellationToken cancellationToken = default); Task WriteAcceptedAsync(IReadOnlyList<ProposedEvent> proposals, string correlationId, CancellationToken cancellationToken = default); Task AttachRootOperationAsync(string correlationId, string operationId, CancellationToken cancellationToken = default); }
