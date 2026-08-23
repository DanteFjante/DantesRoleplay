namespace DantesRoleplay.Events;

public interface IEventTypeStore
{
    Task<IReadOnlyList<EventTypeSummary>> FindAsync(string? query = null, string? category = null, string? scope = null, bool includeInactive = false, int limit = 50, CancellationToken cancellationToken = default);
    Task<EventTypeDetail?> GetAsync(string id, int? version = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventTypeCheck>> CheckAsync(WriteEventTypeRequest request, CancellationToken cancellationToken = default);
    Task<WriteEventTypeResult> WriteAsync(WriteEventTypeRequest request, CancellationToken cancellationToken = default);
}

public sealed record EventTypeSummary(string Id, string Category, string Name, string Description, string Scope, EventTypeStatus Status, int Version);
public sealed record EventTypeDetail(string Id, string Category, string Name, string Description, string PayloadSchema, string Scope, EventTypeStatus Status, int Version, int LatestVersion, string CreatedBy, string ChangeNote, DateTime CreatedAt) { public string SourceHash { get; init; } = string.Empty; }
public sealed record EventTypeCheck(string Name, bool Passed, string Detail, bool Blocking = true);
public sealed record WriteEventTypeRequest { public required string Id { get; init; } public required string Category { get; init; } public required string Name { get; init; } public required string PayloadSchema { get; init; } public string Description { get; init; } = string.Empty; public string Scope { get; init; } = string.Empty; public EventTypeStatus? Status { get; init; } public string CreatedBy { get; init; } = "llm"; public string ChangeNote { get; init; } = string.Empty; }
public sealed record WriteEventTypeResult(EventTypeDetail EventType, bool Created);
