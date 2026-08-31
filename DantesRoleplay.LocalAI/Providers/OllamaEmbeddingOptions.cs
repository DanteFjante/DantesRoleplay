namespace DantesRoleplay.DataAccess.Retrieval;

public sealed class OllamaEmbeddingOptions
{
    public bool Enabled { get; init; }
    public Uri Endpoint { get; init; } = new("http://localhost:11434");
    public string Model { get; init; } = "qwen3-embedding:4b";
    public int ExpectedDimensions { get; init; } = 2560;
    public int MaxBatchSize { get; init; } = 32;
    public int MaxInputCharacters { get; init; } = 8_000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ReadinessCache { get; init; } = TimeSpan.FromMinutes(1);

    internal string? Validate()
    {
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("http" or "https"))
            return "Endpoint must be an absolute HTTP or HTTPS URI.";
        if (!Endpoint.IsLoopback)
            return "Endpoint must be loopback for the local-only embedding provider.";
        if (string.IsNullOrWhiteSpace(Model)) return "Model must be nonblank.";
        if (ExpectedDimensions <= 0) return "ExpectedDimensions must be positive.";
        if (MaxBatchSize is < 1 or > 256) return "MaxBatchSize must be between 1 and 256.";
        if (MaxInputCharacters is < 1 or > 1_000_000)
            return "MaxInputCharacters must be between 1 and 1000000.";
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(10))
            return "Timeout must be greater than zero and no more than ten minutes.";
        if (ReadinessCache < TimeSpan.Zero || ReadinessCache > TimeSpan.FromMinutes(10))
            return "ReadinessCache must be between zero and ten minutes.";
        return null;
    }
}
