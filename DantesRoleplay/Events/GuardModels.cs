namespace DantesRoleplay.Events;

/// <summary>
/// One structural proposal: an event that will exist if every guard allows it.
///
/// It carries everything the accepted row will except the three things that are only true once it
/// is accepted — id, sequence and timestamp. That is deliberate. A guard has to be able to see the
/// chain it is being asked about (which correlation, what caused this, how deep), and a proposal
/// that carried only a payload would leave the host inventing that context at the moment of
/// execution, differently in each place that executes one.
/// </summary>
/// <param name="Ordinal">Position in the proposing batch. Becomes the accepted row's sequence.</param>
/// <param name="Depth">0 for a root world change. A reaction's children are one deeper.</param>
/// <param name="CorrelationId">The root operation id; see EventRecord.RootOperationId.</param>
/// <param name="CausationId">The event being handled, when this was proposed by a reaction to it.</param>
public sealed record ProposedEvent(
    string Type,
    string PayloadJson,
    IReadOnlyList<string> EntityIds,
    string Scope,
    int Ordinal,
    int Depth = 0,
    string CorrelationId = "",
    string CausationId = "");

/// <summary>Explanation of one deterministic guard evaluation; not a durable execution record.</summary>
public sealed record GuardEvaluation(
    string SubscriptionId,
    int SubscriptionVersion,
    string MechanicId,
    int MechanicVersion,
    int Order,
    long Seed,
    string Decision,
    string Code = "",
    string Reason = "");

public sealed record GuardResult(bool Allowed, IReadOnlyList<GuardEvaluation> Evaluations, string Code = "", string Reason = "")
{
    public static GuardResult Allow(IReadOnlyList<GuardEvaluation> evaluations) => new(true, evaluations);
    public static GuardResult Deny(IReadOnlyList<GuardEvaluation> evaluations, string code, string reason) => new(false, evaluations, code, reason);
}

/// <summary>Evaluates registered guards against proposals inside the caller's ambient transaction.</summary>
public interface IGuardRouter
{
    Task<GuardResult> EvaluateAsync(IReadOnlyList<ProposedEvent> proposals, CancellationToken cancellationToken = default);
}
