namespace DantesRoleplay.World;

public sealed record KnowledgeTimelineProblem(string Code, string Path, string Reason);

public sealed record RecordKnowledgeValidityRequest(string KnowledgeId, long ValidFromMinute, long? ValidUntilMinute);
public sealed record RecordKnowledgeContradictionRequest(string FirstKnowledgeId, string SecondKnowledgeId);
public sealed record RecordKnowledgeSupersessionRequest(string NewerKnowledgeId, string PriorKnowledgeId);

public sealed record KnowledgeTimelineWriteResult(
    string Status,
    string FromKnowledgeId,
    string ToKnowledgeId,
    IReadOnlyList<KnowledgeTimelineProblem> Problems)
{
    public bool Recorded => Status is "recorded" or "replayed";
}

public sealed record KnowledgeTimelineProjection(
    string KnowledgeId,
    string SubjectId,
    long? ValidFromMinute,
    long? ValidUntilMinute,
    string TemporalStatus,
    bool Contested,
    IReadOnlyList<string> ContradictsKnowledgeIds,
    IReadOnlyList<string> SupersedesKnowledgeIds);

public sealed record KnowledgeHistoryResult(
    string WorldId,
    long AsOfMinute,
    IReadOnlyList<KnowledgeTimelineProjection> Records,
    IReadOnlyList<KnowledgeTimelineProblem> Problems)
{
    public bool Read => Problems.Count == 0;
}

/// <summary>
/// Trusted host timeline boundary for knowledge validity and proposition links. It has no player
/// authorization, MCP, search, or generic temporal-engine responsibility.
/// </summary>
public interface IKnowledgeTimelineCoordinator
{
    Task<KnowledgeTimelineWriteResult> RecordValidityAsync(
        RecordKnowledgeValidityRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeTimelineWriteResult> RecordContradictionAsync(
        RecordKnowledgeContradictionRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeTimelineWriteResult> RecordSupersessionAsync(
        RecordKnowledgeSupersessionRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeHistoryResult> ReadAsOfAsync(
        string worldId,
        long? asOfMinute = null,
        CancellationToken cancellationToken = default);
}
