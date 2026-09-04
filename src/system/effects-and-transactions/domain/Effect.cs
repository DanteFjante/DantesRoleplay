namespace DantesRoleplay.Effects;

/// <summary>
/// The complete vocabulary of structural change. Nothing else can alter world state.
///
/// ARCHITECTURE.md §3.11: no game words. There is no `condition.add` or `resource.modify` here —
/// a condition is a component, a resource is a field in one, and inventing verbs for them would
/// put the game's vocabulary in the kernel. "Poison the goblin" is
/// <see cref="ComponentAdd"/> with a definition of the game's choosing.
///
/// Kept as string constants rather than an enum because effects arrive as JSON from a sandbox,
/// and an unknown string must produce a named validation problem rather than a parse failure.
/// </summary>
public static class EffectType
{
    public const string EntityCreate = "entity.create";
    public const string EntityDelete = "entity.delete";

    public const string ComponentAdd = "component.add";
    public const string ComponentSet = "component.set";
    public const string ComponentMerge = "component.merge";
    public const string ComponentRemove = "component.remove";

    /// <summary>
    /// Atomically replaces one authoritative clock component and records the corresponding
    /// structural event. The kernel validates only generic monotonic/revision invariants; the
    /// catalog remains responsible for calculating elapsed time.
    /// </summary>
    public const string ClockAdvance = "clock.advance";

    public const string ContainmentMove = "containment.move";

    public const string RelationshipCreate = "relationship.create";
    public const string RelationshipRemove = "relationship.remove";

    /// <summary>Every verb the engine accepts. The list a caller is shown when it gets one wrong.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        EntityCreate, EntityDelete,
        ComponentAdd, ComponentSet, ComponentMerge, ComponentRemove, ClockAdvance,
        ContainmentMove,
        RelationshipCreate, RelationshipRemove
    ];
}

/// <summary>
/// One proposed change. A mechanic returns these; it never writes anything itself (§3.3).
///
/// Deliberately a flat record with optional fields rather than a hierarchy: it has to survive a
/// round trip through JSON from the sandbox, and a shape a mechanic can get subtly wrong is worse
/// than a few unused fields. Which fields matter is decided per type by the validator, which can
/// then say exactly what was missing.
/// </summary>
public sealed record Effect
{
    /// <summary>One of <see cref="EffectType"/>.</summary>
    public required string Type { get; init; }

    /// <summary>The entity acted on. For containment, the thing being moved.</summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>Component definition id, for the component.* verbs.</summary>
    public string DefinitionId { get; init; } = string.Empty;

    /// <summary>
    /// The second entity: the container for containment.move, the target for relationship.*.
    /// Empty on containment.move means "take it out of whatever holds it".
    /// </summary>
    public string ToEntityId { get; init; } = string.Empty;

    /// <summary>Relationship kind. Free text, defined by the game.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Containment slot — "carried", "equipped". Free text.</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>Display name, for entity.create.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>JSON object payload for component and relationship data.</summary>
    public string Data { get; init; } = "{}";

    /// <summary>Generic clock metadata used only by <see cref="EffectType.ClockAdvance"/>.</summary>
    public string CalendarId { get; init; } = string.Empty;
    public long PreviousMinute { get; init; }
    public long DeltaMinutes { get; init; }
    public long ResultingMinute { get; init; }
    public long PreviousClockRevision { get; init; }
    public long ResultingClockRevision { get; init; }
    public string EventTypeId { get; init; } = string.Empty;
    public string SubjectEntityId { get; init; } = string.Empty;
    public string ActivityId { get; init; } = string.Empty;

    public override string ToString() =>
        $"{Type}({(EntityId.Length > 0 ? EntityId : "-")}{(DefinitionId.Length > 0 ? $", {DefinitionId}" : "")})";
}

/// <param name="Index">Position in the submitted list, so a caller can find the offending effect.</param>
/// <param name="Effect">Human-readable rendering of what was submitted.</param>
/// <param name="Problem">What is wrong, and what would make it right.</param>
public sealed record EffectProblem(int Index, string Effect, string Problem);

/// <summary>
/// Outcome of validating, and possibly applying, a list of effects.
/// </summary>
/// <param name="Applied">False for a dry run, or when validation rejected the list.</param>
/// <param name="Count">How many effects were applied. Zero unless <paramref name="Applied"/>.</param>
/// <param name="Problems">Empty means the list is valid.</param>
public sealed record EffectResult(bool Applied, int Count, IReadOnlyList<EffectProblem> Problems)
{
    public bool Valid => Problems.Count == 0;
    public bool Blocked { get; init; }
    public string BlockCode { get; init; } = string.Empty;
    public string BlockReason { get; init; } = string.Empty;
    public IReadOnlyList<DantesRoleplay.Events.ProposedEvent> ProposedEvents { get; init; } = [];
    public IReadOnlyList<DantesRoleplay.Events.GuardEvaluation> GuardEvaluations { get; init; } = [];

    /// <summary>
    /// The events this change recorded, in sequence order.
    ///
    /// Distinct from <see cref="ProposedEvents"/>: those are what was put to the guards, these are
    /// what survived and exist. The chain loop queues these, so a proposal that was refused cannot
    /// become something a reaction runs against.
    /// </summary>
    public IReadOnlyList<DantesRoleplay.Events.EventDetail> AcceptedEvents { get; init; } = [];
    public string CorrelationId { get; init; } = string.Empty;
}
