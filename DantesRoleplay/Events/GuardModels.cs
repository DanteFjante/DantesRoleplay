namespace DantesRoleplay.Events;

/// <summary>One structural proposal, held only for the enclosing transaction in Slice 3.</summary>
public sealed record ProposedEvent(
    string Type,
    string PayloadJson,
    IReadOnlyList<string> EntityIds,
    string Scope,
    int Ordinal);

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
