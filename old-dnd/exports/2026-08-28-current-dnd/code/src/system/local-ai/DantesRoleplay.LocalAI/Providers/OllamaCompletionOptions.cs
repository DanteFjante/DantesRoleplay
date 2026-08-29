namespace DantesRoleplay.DataAccess.Retrieval;

public sealed class OllamaCompletionOptions
{
    public bool Enabled { get; init; }
    public Uri Endpoint { get; init; } = new("http://localhost:11434");
    public string Model { get; init; } = "qwen3:8b";
    public string Profile { get; init; } = "standard";
    public int MaxPromptCharacters { get; init; } = 72_000;
    public int MaxResponseCharacters { get; init; } = 16_000;
    public int MaxOutputTokens { get; init; } = 1_024;
    public int MaxConcurrentRequests { get; init; } = 1;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan ReadinessCache { get; init; } = TimeSpan.FromMinutes(1);
    public string KeepAlive { get; init; } = "5m";
    public IReadOnlySet<string> AllowedTaskClasses { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public string? Validate()
    {
        var providerError = ValidateProviderSettings();
        if (providerError is not null)
            return providerError;
        if (AllowedTaskClasses.Count is 0 or > 20 || AllowedTaskClasses.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Length > 100))
            return "AllowedTaskClasses must contain between 1 and 20 bounded values.";
        return null;
    }

    /// <summary>
    /// Validates the provider-wide startup settings independently from the task allowlist. Host
    /// configuration views use this seam without pretending that a task/provider registration is
    /// already active.
    /// </summary>
    public string? ValidateProviderSettings()
    {
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("http" or "https") || !Endpoint.IsLoopback)
            return "Endpoint must be an absolute loopback HTTP or HTTPS URI.";
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200)
            return "Model must be nonblank and at most 200 characters.";
        if (string.IsNullOrWhiteSpace(Profile) || Profile != Profile.Trim() || Profile.Length > 100)
            return "Profile must be trimmed, nonblank, and at most 100 characters.";
        if (MaxPromptCharacters is < 1_000 or > 200_000)
            return "MaxPromptCharacters must be between 1000 and 200000.";
        if (MaxResponseCharacters is < 1_000 or > 100_000)
            return "MaxResponseCharacters must be between 1000 and 100000.";
        if (MaxOutputTokens is < 64 or > 8_192)
            return "MaxOutputTokens must be between 64 and 8192.";
        if (MaxConcurrentRequests is < 1 or > 8)
            return "MaxConcurrentRequests must be between 1 and 8.";
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(10))
            return "Timeout must be greater than zero and no more than ten minutes.";
        if (ReadinessCache < TimeSpan.Zero || ReadinessCache > TimeSpan.FromMinutes(10))
            return "ReadinessCache must be between zero and ten minutes.";
        if (string.IsNullOrWhiteSpace(KeepAlive) || KeepAlive.Length > 20)
            return "KeepAlive must be nonblank and at most 20 characters.";
        return null;
    }
}
