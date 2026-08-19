namespace DantesRoleplay.Events;

public interface ISubscriptionStore
{
    Task<IReadOnlyList<SubscriptionSummary>> FindAsync(string? query = null, string? category = null, string? scope = null, bool includeInactive = false, int limit = 50, CancellationToken cancellationToken = default);
    Task<SubscriptionDetail?> GetAsync(string id, int? version = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionCheck>> CheckAsync(WriteSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<WriteSubscriptionResult> WriteAsync(WriteSubscriptionRequest request, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionSummary(string Id, string Category, string EventTypeId, string EventMechanicId, SubscriptionMode Mode, int Order, string Scope, SubscriptionStatus Status, int Version, bool DependenciesHealthy);
public sealed record SubscriptionDetail(string Id, string Category, string EventTypeId, string EventMechanicId, SubscriptionMode Mode, int Order, string FixedRoleEntityIdsJson, string TrackedEntityIdsJson, string PayloadEqualsJson, int MaxExecutionsPerChain, string Scope, SubscriptionStatus Status, int Version, int LatestVersion, string CreatedBy, string ChangeNote, DateTime CreatedAt, bool DependenciesHealthy) { public string SourceHash { get; init; } = string.Empty; }
public sealed record SubscriptionCheck(string Name, bool Passed, string Detail, bool Blocking = true);
public sealed record WriteSubscriptionRequest
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string EventTypeId { get; init; }
    public required string EventMechanicId { get; init; }
    public required SubscriptionMode Mode { get; init; }
    public int Order { get; init; }
    public string FixedRoleEntityIdsJson { get; init; } = "{}";
    public string TrackedEntityIdsJson { get; init; } = "[]";
    public string PayloadEqualsJson { get; init; } = "{}";
    public int MaxExecutionsPerChain { get; init; } = 1;
    public string Scope { get; init; } = string.Empty;
    public SubscriptionStatus? Status { get; init; }
    public string CreatedBy { get; init; } = "llm";
    public string ChangeNote { get; init; } = string.Empty;
}
public sealed record WriteSubscriptionResult(SubscriptionDetail Subscription, bool Created);
