using System.Net;
using System.Text;
using DantesRoleplay.DataAccess.Retrieval;

namespace DantesRoleplay.Tests;

public sealed class OllamaEmbeddingProviderTests
{
    [Fact]
    public async Task Disabled_provider_does_not_call_ollama()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not run"));
        var provider = new OllamaEmbeddingProvider(new HttpClient(handler), new());

        var status = await provider.CheckAsync();
        var result = await provider.EmbedAsync(["the sun is hot"]);

        Assert.False(status.Ready);
        Assert.Equal("EMBEDDING_DISABLED", status.ErrorCode);
        Assert.False(result.Ok);
        Assert.Equal("EMBEDDING_DISABLED", result.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Ready_provider_records_digest_and_validates_every_vector()
    {
        var handler = ValidHandler(3, "digest-123", [[0.1f, 0.2f, 0.3f], [0.3f, 0.2f, 0.1f]]);
        var provider = Provider(handler, expectedDimensions: 3);

        var result = await provider.EmbedAsync(["one", "two"]);

        Assert.True(result.Ok);
        Assert.Equal("ollama", result.Identity!.Provider);
        Assert.Equal("qwen3-embedding:4b", result.Identity.Model);
        Assert.Equal("digest-123", result.Identity.Revision);
        Assert.Equal(3, result.Identity.Dimensions);
        Assert.Equal(2, result.Vectors.Count);
        Assert.Equal(3, handler.Calls); // tags, show, embed
    }

    [Fact]
    public async Task Readiness_rejects_model_dimension_drift_before_embedding()
    {
        var handler = ValidHandler(4, "changed-model", [[0.1f, 0.2f, 0.3f, 0.4f]]);
        var provider = Provider(handler, expectedDimensions: 3);

        var result = await provider.EmbedAsync(["one"]);

        Assert.False(result.Ok);
        Assert.Equal("EMBEDDING_DIMENSION_MISMATCH", result.ErrorCode);
        Assert.Equal(2, handler.Calls); // tags and show; embed is never called
    }

    [Fact]
    public async Task Response_vector_count_must_match_the_batch()
    {
        var handler = ValidHandler(3, "digest-123", [[0.1f, 0.2f, 0.3f]]);
        var provider = Provider(handler, expectedDimensions: 3);

        var result = await provider.EmbedAsync(["one", "two"]);

        Assert.False(result.Ok);
        Assert.Equal("EMBEDDING_COUNT_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task Batch_and_input_limits_fail_without_http()
    {
        var handler = ValidHandler(3, "unused", []);
        var provider = new OllamaEmbeddingProvider(new HttpClient(handler), new()
        {
            Enabled = true,
            ExpectedDimensions = 3,
            MaxBatchSize = 1,
            MaxInputCharacters = 3
        });

        var tooMany = await provider.EmbedAsync(["one", "two"]);
        var tooLong = await provider.EmbedAsync(["four"]);

        Assert.Equal("EMBEDDING_BATCH_INVALID", tooMany.ErrorCode);
        Assert.Equal("EMBEDDING_INPUT_INVALID", tooLong.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Local_provider_rejects_a_non_loopback_endpoint_without_http()
    {
        var handler = ValidHandler(3, "unused", []);
        var provider = new OllamaEmbeddingProvider(new HttpClient(handler), new()
        {
            Enabled = true,
            Endpoint = new("https://example.com"),
            ExpectedDimensions = 3
        });

        var status = await provider.CheckAsync();

        Assert.False(status.Ready);
        Assert.Equal("EMBEDDING_CONFIG_INVALID", status.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Local_qwen3_embedding_profile_can_be_checked_explicitly()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_INTEGRATION"),
                "1",
                StringComparison.Ordinal)) return;

        var provider = new OllamaEmbeddingProvider(new HttpClient(), new()
        {
            Enabled = true,
            Model = "qwen3-embedding:4b",
            ExpectedDimensions = 2560,
            Timeout = TimeSpan.FromMinutes(2)
        });

        var result = await provider.EmbedAsync(["the sun is hot", "a hidden council letter"]);

        Assert.True(result.Ok, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.All(result.Vectors, vector => Assert.Equal(2560, vector.Length));
    }

    [Fact]
    public async Task Local_qwen3_profile_passes_the_fixed_knowledge_recall_set()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_INTEGRATION"),
                "1",
                StringComparison.Ordinal)) return;

        var provider = new OllamaEmbeddingProvider(new HttpClient(), new()
        {
            Enabled = true,
            Model = "qwen3-embedding:4b",
            ExpectedDimensions = 2560,
            Timeout = TimeSpan.FromMinutes(2)
        });
        var result = await provider.EmbedAsync(
        [
            "concealed family papers",
            "Oren's Correspondence\nOren's family sealed the observatory after hiding correspondence that implicates the old council.",
            "The Old Toll Ledger\nThe market archive contains the old gate-toll ledger.",
            "current price charged at the market",
            "Revised Market Toll\nThe market toll is two silver pieces after the council revision.",
            "The Observatory Signal\nA light still answers from the sealed observatory after midnight.",
            "nighttime beacon from a sealed tower"
        ]);

        Assert.True(result.Ok, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.True(Similarity(result.Vectors[0], result.Vectors[1]) > Similarity(result.Vectors[0], result.Vectors[2]) + 0.05);
        Assert.True(Similarity(result.Vectors[3], result.Vectors[4]) > Similarity(result.Vectors[3], result.Vectors[5]) + 0.05);
        Assert.True(Similarity(result.Vectors[6], result.Vectors[5]) > Similarity(result.Vectors[6], result.Vectors[2]) + 0.05);
    }

    private static OllamaEmbeddingProvider Provider(StubHandler handler, int expectedDimensions) =>
        new(new HttpClient(handler), new()
        {
            Enabled = true,
            ExpectedDimensions = expectedDimensions
        });

    private static double Similarity(float[] left, float[] right)
    {
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftLength += left[index] * left[index];
            rightLength += right[index] * right[index];
        }
        return dot / (Math.Sqrt(leftLength) * Math.Sqrt(rightLength));
    }

    private static StubHandler ValidHandler(
        int dimensions,
        string digest,
        IReadOnlyList<float[]> vectors) => new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        var json = path switch
        {
            "/api/tags" => System.Text.Json.JsonSerializer.Serialize(new
            {
                models = new[] { new { name = "qwen3-embedding:4b", model = "qwen3-embedding:4b", digest } }
            }),
            "/api/show" => System.Text.Json.JsonSerializer.Serialize(new
            {
                capabilities = new[] { "tools", "embedding" },
                model_info = new Dictionary<string, int> { ["qwen3.embedding_length"] = dimensions }
            }),
            "/api/embed" => System.Text.Json.JsonSerializer.Serialize(new { embeddings = vectors }),
            _ => throw new InvalidOperationException($"Unexpected request path {path}")
        };
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    });

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response(request));
        }
    }
}
