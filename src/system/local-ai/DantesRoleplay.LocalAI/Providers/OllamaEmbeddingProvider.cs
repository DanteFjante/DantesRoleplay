using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.DataAccess.Retrieval;

/// <summary>
/// Disabled-by-default local Ollama embedding adapter. It returns stable failures so callers can
/// fall back to FTS instead of making local-model availability a correctness dependency.
/// </summary>
public sealed class OllamaEmbeddingProvider(HttpClient http, OllamaEmbeddingOptions options)
    : ITextEmbeddingProvider
{
    private readonly SemaphoreSlim _readinessGate = new(1, 1);
    private EmbeddingProviderStatus? _cachedStatus;
    private DateTimeOffset _cachedUntil;

    public Task<EmbeddingProviderStatus> CheckAsync(
        CancellationToken cancellationToken = default) =>
        StatusAsync(forceRefresh: true, cancellationToken);

    private async Task<EmbeddingProviderStatus> StatusAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await _readinessGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedStatus is not null && DateTimeOffset.UtcNow < _cachedUntil)
                return _cachedStatus;
            var status = await CheckCoreAsync(cancellationToken);
            _cachedStatus = status;
            _cachedUntil = DateTimeOffset.UtcNow.Add(options.ReadinessCache);
            return status;
        }
        finally
        {
            _readinessGate.Release();
        }
    }

    private async Task<EmbeddingProviderStatus> CheckCoreAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return EmbeddingProviderStatus.Unavailable(
                "EMBEDDING_DISABLED", "The local embedding provider is disabled.");

        var invalid = options.Validate();
        if (invalid is not null)
            return EmbeddingProviderStatus.Unavailable("EMBEDDING_CONFIG_INVALID", invalid);

        try
        {
            using var timeout = Timeout(cancellationToken);
            var tags = await GetAsync<TagsResponse>("api/tags", timeout.Token);
            var installed = (tags.Models ?? []).FirstOrDefault(x =>
                string.Equals(x.Name, options.Model, StringComparison.Ordinal) ||
                string.Equals(x.Model, options.Model, StringComparison.Ordinal));
            if (installed is null)
                return EmbeddingProviderStatus.Unavailable(
                    "EMBEDDING_MODEL_MISSING",
                    $"Configured Ollama model '{options.Model}' is not installed.");

            var show = await PostAsync<ShowRequest, ShowResponse>(
                "api/show", new(options.Model), timeout.Token);
            if (!(show.Capabilities ?? []).Contains("embedding", StringComparer.Ordinal))
                return EmbeddingProviderStatus.Unavailable(
                    "EMBEDDING_CAPABILITY_MISSING",
                    $"Configured Ollama model '{options.Model}' does not report embedding capability.");

            var dimensions = EmbeddingDimensions(show.ModelInfo ??
                new Dictionary<string, JsonElement>(StringComparer.Ordinal));
            if (dimensions != options.ExpectedDimensions)
                return EmbeddingProviderStatus.Unavailable(
                    "EMBEDDING_DIMENSION_MISMATCH",
                    $"Configured model reports {dimensions} dimensions; expected {options.ExpectedDimensions}.");

            return new(true, new(
                "ollama",
                options.Model,
                installed.Digest,
                dimensions));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EmbeddingProviderStatus.Unavailable(
                "EMBEDDING_TIMEOUT", "Ollama did not answer before the configured timeout.");
        }
        catch (OperationCanceledException)
        {
            return EmbeddingProviderStatus.Unavailable(
                "EMBEDDING_CANCELLED", "The embedding readiness check was cancelled.");
        }
        catch (HttpRequestException exception)
        {
            return EmbeddingProviderStatus.Unavailable(
                "EMBEDDING_UNAVAILABLE", Safe(exception.Message));
        }
        catch (JsonException exception)
        {
            return EmbeddingProviderStatus.Unavailable(
                "EMBEDDING_RESPONSE_INVALID", Safe(exception.Message));
        }
    }

    public async Task<EmbeddingBatchResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count is 0 || inputs.Count > options.MaxBatchSize)
            return EmbeddingBatchResult.Failure(
                "EMBEDDING_BATCH_INVALID",
                $"Embedding input count must be between 1 and {options.MaxBatchSize}.");

        if (inputs.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > options.MaxInputCharacters))
            return EmbeddingBatchResult.Failure(
                "EMBEDDING_INPUT_INVALID",
                $"Every embedding input must be nonblank and at most {options.MaxInputCharacters} characters.");

        var status = await StatusAsync(forceRefresh: false, cancellationToken);
        if (!status.Ready || status.Identity is null)
            return EmbeddingBatchResult.Failure(status.ErrorCode, status.ErrorMessage);

        try
        {
            using var timeout = Timeout(cancellationToken);
            var response = await PostAsync<EmbedRequest, EmbedResponse>(
                "api/embed", new(options.Model, inputs, false), timeout.Token);

            var embeddings = response.Embeddings ?? [];
            if (embeddings.Count != inputs.Count)
                return EmbeddingBatchResult.Failure(
                    "EMBEDDING_COUNT_MISMATCH",
                    $"Ollama returned {embeddings.Count} vectors for {inputs.Count} inputs.");

            foreach (var vector in embeddings)
            {
                if (vector.Length != status.Identity.Dimensions)
                    return EmbeddingBatchResult.Failure(
                        "EMBEDDING_DIMENSION_MISMATCH",
                        $"Ollama returned a {vector.Length}-element vector; expected {status.Identity.Dimensions}.");
                if (vector.Any(value => !float.IsFinite(value)))
                    return EmbeddingBatchResult.Failure(
                        "EMBEDDING_VALUE_INVALID", "Ollama returned a non-finite vector value.");
            }

            return new(status.Identity, embeddings);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EmbeddingBatchResult.Failure(
                "EMBEDDING_TIMEOUT", "Ollama did not answer before the configured timeout.");
        }
        catch (OperationCanceledException)
        {
            return EmbeddingBatchResult.Failure(
                "EMBEDDING_CANCELLED", "The embedding request was cancelled.");
        }
        catch (HttpRequestException exception)
        {
            return EmbeddingBatchResult.Failure("EMBEDDING_UNAVAILABLE", Safe(exception.Message));
        }
        catch (JsonException exception)
        {
            return EmbeddingBatchResult.Failure("EMBEDDING_RESPONSE_INVALID", Safe(exception.Message));
        }
    }

    private CancellationTokenSource Timeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(options.Timeout);
        return source;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(options.Endpoint, path), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new JsonException("Ollama returned an empty JSON response.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            new Uri(options.Endpoint, path), request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new JsonException("Ollama returned an empty JSON response.");
    }

    private static int EmbeddingDimensions(IReadOnlyDictionary<string, JsonElement> modelInfo)
    {
        var values = modelInfo
            .Where(pair => pair.Key.EndsWith(".embedding_length", StringComparison.Ordinal))
            .Select(pair => pair.Value.ValueKind == JsonValueKind.Number &&
                            pair.Value.TryGetInt32(out var value) ? value : 0)
            .Where(value => value > 0)
            .Distinct()
            .ToArray();
        return values.Length == 1 ? values[0] : 0;
    }

    private static string Safe(string value) =>
        value.Length <= 500 ? value : value[..500];

    private sealed record TagsResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<TagModel>? Models);
    private sealed record TagModel(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("digest")] string Digest);
    private sealed record ShowRequest([property: JsonPropertyName("model")] string Model);
    private sealed record ShowResponse(
        [property: JsonPropertyName("capabilities")] IReadOnlyList<string>? Capabilities,
        [property: JsonPropertyName("model_info")] IReadOnlyDictionary<string, JsonElement>? ModelInfo);
    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("truncate")] bool Truncate);
    private sealed record EmbedResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<float[]>? Embeddings);
}
