namespace DantesRoleplay.DataAccess.Retrieval;

/// <summary>Optional host configuration for derived knowledge retrieval.</summary>
public sealed class KnowledgeRetrievalOptions
{
    public OllamaEmbeddingOptions Embedding { get; init; } = new();
    public SqliteVecOptions Vector { get; init; } = new();
    public OllamaCompletionOptions Completion { get; init; } = new()
    {
        // Consumer-owned allowlist. The local-AI component deliberately owns no game task names.
        AllowedTaskClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "information.answer",
            "knowledge.answer",
            "knowledge.proposals",
            "knowledge.read-plan",
            "knowledge.read-answer",
            "knowledge.authorized-answer",
            "routing.propose",
            "story-plan.verify-procedures"
        }
    };
    public KnowledgeBackgroundOptions Background { get; init; } = new();
    public int BackfillBatchSize { get; init; } = 16;
    public int CandidateLimit { get; init; } = 60;
    public int ReciprocalRankConstant { get; init; } = 60;

    internal string? Validate()
    {
        if (BackfillBatchSize is < 1 or > 256)
            return "BackfillBatchSize must be between 1 and 256.";
        if (BackfillBatchSize > Embedding.MaxBatchSize)
            return "BackfillBatchSize cannot exceed the embedding provider batch size.";
        if (CandidateLimit is < 1 or > 100)
            return "CandidateLimit must be between 1 and 100.";
        if (ReciprocalRankConstant is < 1 or > 1_000)
            return "ReciprocalRankConstant must be between 1 and 1000.";
        var completion = Completion.Validate();
        if (completion is not null) return completion;
        var background = Background.Validate();
        if (background is not null) return background;
        return null;
    }
}
