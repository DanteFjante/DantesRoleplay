namespace DantesRoleplay.Notifications;

/// <summary>
/// Where a notice stands with its reader. The one mutable thing about a notification.
///
/// One-way to <see cref="Archived"/> in this release. Letting archived return to unread would make
/// "I have dealt with this" a state a later mistake could quietly undo, and a lifecycle that can
/// run backwards is one nobody can reason about at a glance.
/// </summary>
public enum NotificationState { Unread, Read, Archived }

/// <summary>
/// Something a rule wants a person told about.
///
/// Every field but the delivery state is immutable, and that is the design rather than caution.
/// A notice is evidence that a rule, at a version, running on a seed, inside one committed change,
/// decided this was worth saying. Editing its text afterwards would leave a record that looks like
/// evidence and is not.
///
/// It is emphatically NOT a delivery mechanism. Nothing here pushes, mails, polls or schedules. A
/// notification is a row somebody reads when they ask; the moment it also meant "and it was sent",
/// the system would be making a promise it cannot keep inside a database transaction.
/// </summary>
public sealed class Notification
{
    public required string Id { get; set; }

    /// <summary>A dotted grouping, e.g. <c>combat.wound</c>. What a reader filters by first.</summary>
    public required string Topic { get; set; }

    /// <summary>One line. What the notice is, readable without opening anything else.</summary>
    public required string Subject { get; set; }

    /// <summary>The detail, in prose. Empty is allowed: some notices are entirely their subject.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The chain this notice belongs to. Equal to the root operation id, as events are.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>The event being handled when the rule asked for this.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// The reaction execution that created it. As with an event's producer, one column rather than
    /// four: the execution row already names the subscription, the mechanic and both versions.
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;

    public string RootOperationId { get; set; } = string.Empty;

    /// <summary>Position within the chain, so notices from one change read back in the order they were made.</summary>
    public int Ordinal { get; set; }

    public DateTime CreatedAt { get; set; }

    public NotificationState State { get; set; } = NotificationState.Unread;

    /// <summary>Set when first read, cleared if marked unread again, retained through archiving.</summary>
    public DateTime? ReadAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public ICollection<NotificationEntity> Entities { get; set; } = new List<NotificationEntity>();
}

/// <summary>
/// One entity a notification concerns, in declared order.
///
/// A join row rather than an id list in the body, for the same reason events use one: "everything
/// anyone was told about this creature" has to be an index seek. Reading it out of prose would
/// mean matching text, which finds the wrong things and misses the right ones.
/// </summary>
public sealed class NotificationEntity
{
    public int Id { get; set; }

    public required string NotificationId { get; set; }

    public required string EntityId { get; set; }

    public int Ordinal { get; set; }

    public Notification? Notification { get; set; }
}

/// <summary>What a mechanic asks for. Turned into a <see cref="Notification"/> only if it commits.</summary>
public sealed record DeclaredNotification
{
    public string Topic { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    /// <summary>Every id must name a live entity, so the notice is findable the way it will be looked for.</summary>
    public IReadOnlyList<string> EntityIds { get; init; } = [];
}

/// <summary>One notification as a reader sees it.</summary>
public sealed record NotificationView(
    string Id,
    string Topic,
    string Subject,
    string Body,
    string CorrelationId,
    string EventId,
    string ExecutionId,
    string RootOperationId,
    int Ordinal,
    DateTime CreatedAt,
    NotificationState State,
    DateTime? ReadAt,
    DateTime? ArchivedAt,
    IReadOnlyList<string> EntityIds);

/// <summary>
/// Reads notifications and moves their delivery state. Nothing here writes content.
///
/// The split is the whole contract: content and links arrive only from a reaction that committed
/// with its entire chain, and an administrative call can move a notice through its lifecycle
/// without being able to change what it says.
/// </summary>
public interface INotificationStore
{
    Task<IReadOnlyList<NotificationView>> FindAsync(
        string? id = null,
        NotificationState? state = null,
        string? topic = null,
        string? entityId = null,
        string? correlationId = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves one notice's delivery state. Idempotent for the state it already holds, so a client
    /// that retries a call it is unsure about does not have to find out which way it went.
    /// </summary>
    Task<NotificationResult> SetStateAsync(
        string id,
        NotificationState state,
        CancellationToken cancellationToken = default);
}

/// <param name="Problem">Empty on success; otherwise why the transition was refused.</param>
public sealed record NotificationResult(NotificationView? Notification, string Problem = "")
{
    public bool Ok => Problem.Length == 0;
}
