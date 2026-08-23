namespace DantesRoleplay.DataAccess.Retrieval;

public sealed class KnowledgeBackgroundOptions
{
    public int EmbeddingQueueCapacity { get; init; } = 16;
    public int ProposalQueueCapacity { get; init; } = 32;
    public int MaxRetainedJobs { get; init; } = 256;
    public int MaxAttempts { get; init; } = 2;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    internal string? Validate()
    {
        if (EmbeddingQueueCapacity is < 1 or > 1_000 || ProposalQueueCapacity is < 1 or > 1_000)
            return "Background queue capacities must be between 1 and 1000.";
        if (MaxRetainedJobs is < 16 or > 10_000)
            return "MaxRetainedJobs must be between 16 and 10000.";
        if (MaxAttempts is < 1 or > 5)
            return "MaxAttempts must be between 1 and 5.";
        if (RetryDelay < TimeSpan.Zero || RetryDelay > TimeSpan.FromMinutes(1))
            return "RetryDelay must be between zero and one minute.";
        return null;
    }
}
