using DantesRoleplay.Effects;
using DantesRoleplay.Notifications;

namespace DantesRoleplay.Events;

/// <summary>
/// One reaction that ran, and what it wants done.
///
/// The execution row is already complete here; the effects are not yet applied. That split is the
/// whole shape of the design: the router decides WHAT should happen and the effect applier remains
/// the only thing that changes the world. Letting the router apply its own effects would also make
/// it and the applier depend on each other, which no amount of care makes a good arrangement.
/// </summary>
public sealed record ReactionOutcome(
    EventExecution Execution,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<DeclaredEvent> Events,
    IReadOnlyList<DeclaredNotification> Notifications);

/// <param name="Code">A stable failure code when routing aborted. Empty on success.</param>
public sealed record EventRoutingResult(
    bool Ok,
    IReadOnlyList<ReactionOutcome> Outcomes,
    string Code = "",
    string Reason = "")
{
    public static EventRoutingResult Allow(IReadOnlyList<ReactionOutcome> outcomes) => new(true, outcomes);

    public static EventRoutingResult Abort(string code, string reason) => new(false, [], code, reason);
}

/// <summary>
/// Runs the reaction subscriptions registered against accepted events.
///
/// Every failure aborts. A reaction whose mechanic is missing, throws, hits a limit, or returns
/// something invalid takes the entire root world change down with it — there is no "the change
/// happened but the consequence did not". That is the whole reason reactions run inside the root
/// transaction rather than after it: a rule that fires on a change is part of that change.
/// </summary>
public interface IEventRouter
{
    /// <param name="rootSeed">
    /// Every reaction seed is derived from this, so a chain replays exactly. Derived in turn from
    /// the correlation id when the caller supplies none, which keeps a replay reproducible from
    /// the audit row alone.
    /// </param>
    /// <param name="budget">
    /// Counted against before any mechanic runs. A limit checked afterwards has already paid the
    /// cost it exists to bound.
    /// </param>
    /// <param name="ordinal">
    /// Where this event's executions start in the chain-wide sequence. Passed in rather than kept
    /// by the router, because the router handles one event at a time and the ordinal spans them.
    /// </param>
    Task<EventRoutingResult> RouteAsync(
        IReadOnlyList<EventDetail> accepted,
        long rootSeed,
        ChainBudget budget,
        int ordinal = 0,
        CancellationToken cancellationToken = default);
}
