namespace DantesRoleplay.Events;

/// <summary>
/// The bounds on one reactive chain, and the running count against them.
///
/// A chain is a loop whose termination depends on rules somebody wrote at runtime, which is to say
/// it does not terminate by construction. Two rules that react to each other are not a bug anyone
/// will notice while writing them — each is reasonable on its own — so the limits are not a safety
/// net over a rare case, they are the only thing standing between a plausible pair of rules and a
/// transaction that never ends.
///
/// Every limit fails the WHOLE root change with its own code. A chain cut off half way would leave
/// a world in a state no rule intended and no reader could explain: the first three consequences
/// applied, the fourth not, and nothing recording why.
///
/// Proposals count, not just accepted events. A chain whose events are mostly vetoed still costs
/// the work of proposing and guarding them, so counting only what survives would let a veto-heavy
/// pair of rules run indefinitely inside the budget.
/// </summary>
public sealed class ChainBudget
{
    /// <summary>How deep a consequence may be from the change the caller actually asked for.</summary>
    public const int MaxDepth = 8;

    /// <summary>Accepted plus proposed events in one chain.</summary>
    public const int MaxEvents = 100;

    /// <summary>Guard plus reaction executions in one chain.</summary>
    public const int MaxExecutions = 100;

    private readonly Dictionary<string, int> _perSubscription = new(StringComparer.Ordinal);

    public int Events { get; private set; }

    public int Executions { get; private set; }

    /// <summary>Null when the depth is within budget, otherwise the code that fails the chain.</summary>
    public string? CheckDepth(int depth) => depth <= MaxDepth
        ? null
        : "EVENT_DEPTH_LIMIT";

    /// <summary>Counts proposed events. Null when within budget, otherwise the failing code.</summary>
    public string? CountEvents(int count)
    {
        Events += count;
        return Events <= MaxEvents ? null : "EVENT_COUNT_LIMIT";
    }

    /// <summary>
    /// Counts one execution, against both the chain total and this subscription's own limit.
    ///
    /// Checked BEFORE the mechanic runs. A limit enforced afterwards has already paid the cost it
    /// exists to bound.
    /// </summary>
    public string? CountExecution(string subscriptionId, int maxPerChain)
    {
        Executions++;

        if (Executions > MaxExecutions)
        {
            return "EXECUTION_COUNT_LIMIT";
        }

        var used = _perSubscription.GetValueOrDefault(subscriptionId) + 1;
        _perSubscription[subscriptionId] = used;

        // A per-subscription limit of zero or less would mean "never run", which is what disabling
        // the subscription is for. Treat it as unbounded rather than silently dead.
        return maxPerChain > 0 && used > maxPerChain
            ? "SUBSCRIPTION_EXECUTION_LIMIT"
            : null;
    }

    /// <summary>
    /// Checks a bounded batch without consuming it. Fan-out uses this before its first receiver so
    /// a later candidate cannot leave an already-run earlier candidate as partial chain evidence.
    /// </summary>
    public string? CheckExecutions(string subscriptionId, int count, int maxPerChain)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (Executions + count > MaxExecutions) return "EXECUTION_COUNT_LIMIT";
        return maxPerChain > 0 && ExecutionsOf(subscriptionId) + count > maxPerChain
            ? "SUBSCRIPTION_EXECUTION_LIMIT"
            : null;
    }

    /// <summary>How many times one subscription has run in this chain. For evidence, not control.</summary>
    public int ExecutionsOf(string subscriptionId) => _perSubscription.GetValueOrDefault(subscriptionId);

    /// <summary>What a limit code means, for the failure a caller actually reads.</summary>
    public static string Explain(string code) => code switch
    {
        "EVENT_DEPTH_LIMIT" =>
            $"A consequence chained more than {MaxDepth} deep. Two rules are probably reacting to "
            + "each other; read the chain with query(kind: \"events\", correlationId: ...).",
        "EVENT_COUNT_LIMIT" =>
            $"One change proposed more than {MaxEvents} events. Vetoed proposals count, so a chain "
            + "whose events are mostly refused still spends this budget.",
        "EXECUTION_COUNT_LIMIT" =>
            $"One change ran more than {MaxExecutions} guards and reactions.",
        "SUBSCRIPTION_EXECUTION_LIMIT" =>
            "One subscription ran more times in a single change than its own limit allows.",
        _ => "A chain limit was reached."
    };
}
