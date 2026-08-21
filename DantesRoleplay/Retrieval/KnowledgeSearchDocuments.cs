namespace DantesRoleplay.Retrieval;

/// <summary>
/// Builds the one canonical, atomic search document used by both FTS and embeddings. Implementors
/// read canonical world state; derived indexes never become a second document authority.
/// </summary>
public interface IKnowledgeSearchDocumentSource
{
    Task<IReadOnlyList<KnowledgeLexicalDocument>> ReadWorldAsync(
        string worldId,
        CancellationToken cancellationToken = default);

    Task<KnowledgeLexicalDocument?> ReadAsync(
        string knowledgeId,
        CancellationToken cancellationToken = default);
}
