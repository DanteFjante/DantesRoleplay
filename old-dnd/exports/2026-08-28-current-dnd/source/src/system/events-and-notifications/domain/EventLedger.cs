namespace DantesRoleplay.Events;

/// <summary>
/// An accepted structural event.
///
/// Append-only runtime evidence, never catalog content: a row says that a particular world change
/// was proposed, survived every registered guard, and committed in the same transaction as the
/// change itself. Nothing rewrites one, and the catalog does not carry them — a ledger that could
/// be written from a file would not be evidence of anything.
/// </summary>
public sealed class EventRecord
{
    public required string Id { get; set; }

    public required string TypeId { get; set; }

    /// <summary>The event type version in force when this was accepted. Types are versioned; rows are not.</summary>
    public int TypeVersion { get; set; }

    public string Scope { get; set; } = string.Empty;

    /// <summary>Canonical JSON, conforming to the registered payload schema of <see cref="TypeId"/>.</summary>
    public required string PayloadJson { get; set; }

    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Every event produced by one root world change shares this. It IS the root operation id —
    /// see <see cref="RootOperationId"/> — so "what else happened because of this?" and "which
    /// audited call caused it?" are the same lookup.
    /// </summary>
    public required string CorrelationId { get; set; }

    /// <summary>The event that caused this one. Empty at depth 0; reactive chains fill it in.</summary>
    public string CausationId { get; set; } = string.Empty;

    /// <summary>0 for a structural event from a root world change. Reactions increment it.</summary>
    public int Depth { get; set; }

    /// <summary>Position within the correlation, ascending. Stable, so a chain replays in order.</summary>
    public int Sequence { get; set; }

    /// <summary>
    /// The audited operation this event belongs to, allocated BEFORE the transaction opened. Equal
    /// to <see cref="CorrelationId"/> in this release; both columns exist because a later release
    /// may let one operation open more than one correlation, and widening a column that rows
    /// already depend on is harder than carrying two that currently agree.
    /// </summary>
    public string RootOperationId { get; set; } = string.Empty;

    /// <summary>
    /// The reaction execution that DECLARED this event, when a rule asserted it rather than the
    /// kernel deriving it from a world change. Empty for every structural event, because nothing
    /// declared those — they follow from the change itself.
    ///
    /// One column rather than four. The execution row already names the subscription, its version,
    /// the mechanic, its version and the seed, so copying them here would be five places one fact
    /// could disagree with itself. Not a declared foreign key, for the same reason
    /// <see cref="RootOperationId"/> is not: both rows are written in one transaction and the
    /// execution is inserted last.
    /// </summary>
    public string ProducerExecutionId { get; set; } = string.Empty;

    public ICollection<EventEntity> Entities { get; set; } = new List<EventEntity>();
}

/// <summary>
/// One entity an event concerns, in declared order.
///
/// A join row rather than a JSON array on the event, so "everything that ever happened to this
/// creature" is an index seek instead of a scan over every payload in the ledger.
/// </summary>
public sealed class EventEntity
{
    public int Id { get; set; }

    public required string EventId { get; set; }

    public required string EntityId { get; set; }

    public int Ordinal { get; set; }

    public EventRecord? Event { get; set; }
}

/// <summary>What a listing shows. No payload — see <see cref="EventDetail"/> for that.</summary>
public sealed record EventSummary(
    string Id,
    string TypeId,
    int TypeVersion,
    string Scope,
    DateTime Timestamp,
    string CorrelationId,
    string CausationId,
    int Depth,
    int Sequence,
    string RootOperationId,
    IReadOnlyList<string> EntityIds);

/// <summary>One event in full, payload included. Returned when a caller asks for a specific id.</summary>
public sealed record EventDetail(
    string Id,
    string TypeId,
    int TypeVersion,
    string Scope,
    string PayloadJson,
    DateTime Timestamp,
    string CorrelationId,
    string CausationId,
    int Depth,
    int Sequence,
    string RootOperationId,
    IReadOnlyList<string> EntityIds,

    /// <summary>Empty unless a rule declared this event; then, the execution that did.</summary>
    string ProducerExecutionId = "");

/// <summary>The complete key of one newest-first event-history page boundary.</summary>
public sealed record EventHistoryCursor(DateTime Timestamp, int Sequence, string Id);

/// <summary>Closed, indexed inputs for a newest-first event-history page.</summary>
public sealed record EventHistoryQuery(
    string? TypeId = null,
    string? EntityId = null,
    string? RootOperationId = null,
    EventHistoryCursor? Before = null,
    int Limit = 25);

/// <summary>A newest-first page with the exact key needed to continue toward older events.</summary>
public sealed record EventHistoryPage(
    IReadOnlyList<EventSummary> Events,
    EventHistoryCursor? NextCursor);

/// <summary>
/// Reads and appends the structural event ledger.
///
/// There is no update and no delete, and no method here takes an event id to modify. That is the
/// whole design: the ledger is the record of what the world did, and a record that can be revised
/// is a record nobody can rely on.
/// </summary>
public interface IEventLedger
{
    /// <summary>
    /// Lists committed events newest-first. The cursor is a complete ordering key, so a continuation
    /// cannot repeat or skip rows merely because two accepted events share a timestamp.
    /// </summary>
    Task<EventHistoryPage> ListRecentAsync(
        EventHistoryQuery query,
        CancellationToken cancellationToken = default);

    /// <param name="afterSequence">Exclusive lower bound on sequence, for paging through a chain.</param>
    /// <param name="from">Inclusive UTC lower bound on timestamp.</param>
    /// <param name="to">Exclusive UTC upper bound on timestamp.</param>
    Task<IReadOnlyList<EventSummary>> FindAsync(
        string? correlationId = null,
        string? causationId = null,
        string? rootOperationId = null,
        string? type = null,
        string? entityId = null,
        int? afterSequence = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<EventDetail?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends every proposal that survived the guards, in proposal order.
    /// </summary>
    /// <param name="rootOperationId">
    /// The operation id allocated before the transaction opened. It becomes both the correlation id
    /// and the root operation id of every row written here — one value, so the link exists the
    /// moment the row does rather than being patched in by a second write that could fail on its
    /// own.
    /// </param>
    /// <returns>
    /// The rows as written, in sequence order. The caller needs their ids and sequences to route
    /// reactions against them, and reading them back by correlation afterwards would also return
    /// events from earlier batches in the same chain.
    /// </returns>
    Task<IReadOnlyList<EventDetail>> WriteAcceptedAsync(
        IReadOnlyList<ProposedEvent> proposals,
        string rootOperationId,
        CancellationToken cancellationToken = default);
}
