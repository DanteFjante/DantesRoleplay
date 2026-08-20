namespace DantesRoleplay.Effects;

/// <summary>
/// What one effect actually did, captured as it was applied.
///
/// The event producer consumes receipts, never raw effects. The difference matters: an effect is a
/// request, and a request does not know what it displaced. Reading prior state after the fact is
/// impossible — it is gone — so it is read here, one step before the store overwrites it, inside
/// the same transaction that will either commit both or neither.
///
/// This is what lets the ledger answer "what did this rule actually do?" rather than only "what did
/// it set?". A ledger that records the new value alone can say a character's hit points became 3;
/// it cannot say whether that was a scratch or nearly fatal, and that is most of what a reader of
/// an audit trail came for.
///
/// <see cref="BeforeJson"/> and <see cref="AfterJson"/> are canonical JSON or null, and null means
/// "there was nothing there", not "we did not look" — a created entity has no before and a deleted
/// one has no after. Their SHAPE depends on the effect: an entity snapshot for entity effects, the
/// component's data for component effects, container and slot for a move, the relationship's data
/// for a relationship. Each event type's schema says which, and the type is fixed per event type,
/// so a reader never has to guess.
/// </summary>
/// <param name="Index">Position in the effect batch. Events carry it so a reader can line an event
/// up with the effect that produced it without counting.</param>
/// <param name="EntityId">The id the effect actually touched, which is not always the id it named:
/// an entity created without one is given an id by the store.</param>
public sealed record EffectReceipt(
    int Index,
    string EntityId,
    string? BeforeJson,
    string? AfterJson)
{
    /// <summary>
    /// A receipt for an effect that was never applied — the dry-run path that reports proposals
    /// without touching the world. It has no before and no after because nothing was read and
    /// nothing was written, which is the honest answer rather than a fabricated snapshot.
    /// </summary>
    public static EffectReceipt Unapplied(int index, string entityId) => new(index, entityId, null, null);
}
