namespace DantesRoleplay.Events;

/// <summary>
/// One reaction subscription running against one accepted event.
///
/// Append-only, and only successful executions that survive the transaction remain: a chain that
/// rolls back leaves none of these, because a record of a reaction that did not ultimately happen
/// is worse than no record. What it exists to answer is "why did the world change like that?", and
/// the honest answer is the whole derivation — which subscription, at which version, running which
/// mechanic at which version, from which seed.
///
/// The seed matters more than it looks. A reaction may roll dice, so the chain is only reproducible
/// if the seed is derived rather than drawn — and only auditable if the derivation is recorded
/// alongside the result. Storing the seed lets a past chain be replayed exactly; storing the
/// projection lets it be replayed without the world having to still be in that state.
/// </summary>
public sealed class EventExecution
{
    public required string Id { get; set; }

    /// <summary>The accepted event this ran against.</summary>
    public required string EventId { get; set; }

    /// <summary>
    /// Position in the chain, ascending across the whole correlation — not within one event.
    /// A limit on total executions is only meaningful if they share a counter.
    /// </summary>
    public int Ordinal { get; set; }

    public required string SubscriptionId { get; set; }

    /// <summary>
    /// Fixed when the subscription was first selected for this chain, so a revision cannot change
    /// the semantics of a run already under way. Normally impossible inside one transaction; the
    /// version is recorded anyway, because "normally impossible" is not a guarantee.
    /// </summary>
    public int SubscriptionVersion { get; set; }

    public required string MechanicId { get; set; }

    public int MechanicVersion { get; set; }

    /// <summary>Derived, never drawn. See the class note.</summary>
    public long Seed { get; set; }

    /// <summary>The frozen input the mechanic saw, so the run can be replayed without the world.</summary>
    public string ProjectionJson { get; set; } = "{}";

    /// <summary>The whole validated output, so a later reading is not limited to what was counted.</summary>
    public string OutputJson { get; set; } = "{}";

    public int EffectCount { get; set; }

    /// <summary>Derived events proposed by this execution. Always zero until Slice 5c.</summary>
    public int EventCount { get; set; }

    public string Narration { get; set; } = string.Empty;

    /// <summary>The mechanic's ctx.log lines, as a JSON array.</summary>
    public string LogJson { get; set; } = "[]";

    public int ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Which execution limit this run came up against, empty when none did.
    ///
    /// Recorded on a SUCCESSFUL execution — a mechanic that finished just under the statement
    /// budget succeeded, and that is exactly the run worth noticing before it starts failing.
    /// </summary>
    public string LimitHit { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public EventRecord? Event { get; set; }
}
