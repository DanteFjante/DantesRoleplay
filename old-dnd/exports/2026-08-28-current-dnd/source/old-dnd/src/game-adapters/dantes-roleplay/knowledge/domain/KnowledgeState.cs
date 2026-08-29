namespace DantesRoleplay.World;

/// <summary>Closed current epistemic states for one actor and one knowledge record.</summary>
public static class KnowledgeEpistemicStates
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "known", "familiar", "suspected", "believed", "doubted", "disbelieved", "unknown"
    };
}

public sealed record KnowledgeStateProblem(string Code, string Path, string Reason);

/// <summary>Trusted-host request to replace one actor's one current explicit knowledge state.</summary>
public sealed record RecordKnowledgeStateRequest(string ActorId, string KnowledgeId, string State);

/// <summary>Trusted-host request to record one current-scope dissemination baseline.</summary>
public sealed record RecordKnowledgeBaselineRequest(string ScopeId, string KnowledgeId);

public sealed record KnowledgeStateWriteResult(
    string Status,
    string ActorOrScopeId,
    string KnowledgeId,
    string? State,
    IReadOnlyList<KnowledgeStateProblem> Problems)
{
    public bool Recorded => Status == "recorded";
}

/// <summary>
/// The deterministic, trusted-GM answer to what one actor currently knows about one record.
/// It is an in-world epistemic result, not an authorization decision or player-facing projection.
/// </summary>
public sealed record EffectiveKnowledgeState(
    string ActorId,
    string KnowledgeId,
    string WorldId,
    string State,
    string SourceKind,
    string? SourceEntityId);

public sealed record EffectiveKnowledgeStateResult(
    EffectiveKnowledgeState? Value,
    IReadOnlyList<KnowledgeStateProblem> Problems)
{
    public bool Resolved => Value is not null && Problems.Count == 0;
}

/// <summary>
/// Owns Slice 1's governed baseline/current-state writes and its trusted-GM effective-state read.
/// It deliberately does not own acquisition history, player authorization, MCP exposure, or search.
/// </summary>
public interface IKnowledgeStateCoordinator
{
    Task<KnowledgeStateWriteResult> RecordStateAsync(
        RecordKnowledgeStateRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeStateWriteResult> RecordBaselineAsync(
        RecordKnowledgeBaselineRequest request,
        CancellationToken cancellationToken = default);

    Task<EffectiveKnowledgeStateResult> ResolveAsync(
        string actorId,
        string knowledgeId,
        CancellationToken cancellationToken = default);
}
