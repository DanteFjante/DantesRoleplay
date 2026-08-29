namespace DantesRoleplay.World;

/// <summary>Closed kinds for the minimal durable source of a knowledge consequence.</summary>
public static class KnowledgeInteractionKinds
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "conversation", "observation", "document", "discovery", "other"
    };
}

/// <summary>Closed methods by which a knower acquired one knowledge record.</summary>
public static class KnowledgeAcquisitionMethods
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "observed", "told", "read", "inferred", "taught", "recalled"
    };
}

public sealed record KnowledgeAcquisitionProblem(string Code, string Path, string Reason);

public sealed record KnowledgeAcquisitionInput(
    string AcquisitionId,
    string KnowerId,
    string KnowledgeId,
    string Method,
    string ResultingState);

/// <summary>
/// Trusted-host request to record one accepted interaction and all knowledge it directly teaches.
/// The interaction id is caller-supplied so replay has a durable, deterministic identity.
/// </summary>
public sealed record RecordKnowledgeInteractionRequest(
    string InteractionId,
    string Name,
    string WorldId,
    string Kind,
    string Summary,
    IReadOnlyList<string> ParticipantIds,
    IReadOnlyList<KnowledgeAcquisitionInput> Acquisitions);

public sealed record RecordedKnowledgeAcquisition(
    string AcquisitionId,
    string KnowerId,
    string KnowledgeId,
    string State,
    bool StateUpdated,
    bool Replayed);

public sealed record KnowledgeInteractionWriteResult(
    string Status,
    string InteractionId,
    IReadOnlyList<RecordedKnowledgeAcquisition> Acquisitions,
    IReadOnlyList<KnowledgeAcquisitionProblem> Problems)
{
    public bool Recorded => Status is "recorded" or "replayed";
}

/// <summary>
/// Owns Slice 2's atomic, sourced and replay-safe learning consequence. It is a trusted-host
/// write boundary; it does not provide authorization, player reads, or generic interaction play.
/// </summary>
public interface IKnowledgeAcquisitionCoordinator
{
    Task<KnowledgeInteractionWriteResult> RecordInteractionAsync(
        RecordKnowledgeInteractionRequest request,
        CancellationToken cancellationToken = default);
}
