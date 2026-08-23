namespace DantesRoleplay.Retrieval;

/// <summary>One derived, trusted-GM search document. Canonical knowledge remains in world state.</summary>
public sealed record KnowledgeLexicalDocument(
    string KnowledgeId,
    string WorldId,
    string Kind,
    string Status,
    string SubjectId,
    string Sensitivity,
    long? ValidFromMinute,
    long? ValidUntilMinute,
    string ContentHash,
    string Text);

public sealed record KnowledgeLexicalSearchRequest(
    string WorldId,
    string Query,
    IReadOnlyList<string>? Kinds = null,
    IReadOnlyList<string>? SubjectIds = null,
    bool IncludeArchived = false,
    long? AsOfMinute = null,
    int Limit = 20,
    /// <summary>
    /// Host-only pre-limit allowlist. A null value has no extra restriction; an empty value
    /// deliberately matches nothing. It is not a player authorization substitute.
    /// </summary>
    IReadOnlyList<string>? AllowedKnowledgeIds = null);

public sealed record KnowledgeLexicalCandidate(string KnowledgeId, double Rank);

/// <summary>
/// Replaceable derived lexical-index boundary. It is not a truth, permission, or player-query
/// authority, and callers must hydrate/recheck candidates before returning them.
/// </summary>
public interface IKnowledgeLexicalIndex
{
    Task ReplaceWorldAsync(
        string worldId,
        IReadOnlyList<KnowledgeLexicalDocument> documents,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        IReadOnlyList<KnowledgeLexicalDocument> documents,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeLexicalCandidate>> SearchAsync(
        KnowledgeLexicalSearchRequest request,
        CancellationToken cancellationToken = default);
}
