using DantesRoleplay.DataAccess;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeFactAnswerCoordinatorTests
{
    private const string World = "world.test";
    private const string FactId = "fact.market-ledger";

    [Fact]
    public async Task Supported_answer_preserves_candidate_id_and_kind()
    {
        var completion = new Completion("""
            {"normalizedQuestion":"What is in the market archive?","selectedFactIds":["fact.market-ledger"],"statements":[{"text":"The archive contains the old toll ledger.","citations":[{"knowledgeId":"fact.market-ledger","kind":"fact"}]}],"unresolved":[],"unknown":false}
            """);
        var coordinator = new KnowledgeFactAnswerCoordinator(new Search([Hit()]), completion);

        var result = await coordinator.AnswerAsync(new(World, "market archive"));

        Assert.True(result.Ok);
        Assert.Equal("local-model", result.Mode);
        Assert.False(result.Unknown);
        Assert.Equal([FactId], result.SelectedFactIds);
        Assert.Equal("fact", Assert.Single(Assert.Single(result.Statements).Citations).Kind);
        Assert.Equal(1, completion.Calls);
    }

    [Theory]
    [InlineData("{\"normalizedQuestion\":\"x\",\"selectedFactIds\":[\"fact.invented\"],\"statements\":[{\"text\":\"x\",\"citations\":[{\"knowledgeId\":\"fact.invented\",\"kind\":\"fact\"}]}],\"unresolved\":[],\"unknown\":false}")]
    [InlineData("{\"normalizedQuestion\":\"x\",\"selectedFactIds\":[\"fact.market-ledger\"],\"statements\":[{\"text\":\"x\",\"citations\":[{\"knowledgeId\":\"fact.market-ledger\",\"kind\":\"rumour\"}]}],\"unresolved\":[],\"unknown\":false}")]
    public async Task Invented_id_or_changed_kind_falls_back_without_answer(string json)
    {
        var coordinator = new KnowledgeFactAnswerCoordinator(new Search([Hit()]), new Completion(json));

        var result = await coordinator.AnswerAsync(new(World, "market archive"));

        Assert.True(result.Ok);
        Assert.True(result.Unknown);
        Assert.Empty(result.Statements);
        Assert.Equal("LOCAL_MODEL_SEMANTIC_INVALID", result.FallbackCode);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task Unavailable_model_returns_deterministic_candidates_and_unknown()
    {
        var completion = new Completion(error: "LOCAL_MODEL_DISABLED");
        var coordinator = new KnowledgeFactAnswerCoordinator(new Search([Hit()]), completion);

        var result = await coordinator.AnswerAsync(new(World, "market archive"));

        Assert.Equal("deterministic", result.Mode);
        Assert.True(result.Unknown);
        Assert.Empty(result.Statements);
        Assert.Equal("LOCAL_MODEL_DISABLED", result.FallbackCode);
        Assert.Equal([FactId], result.Candidates.Select(candidate => candidate.KnowledgeId));
    }

    [Fact]
    public async Task No_candidate_returns_unknown_without_calling_model()
    {
        var completion = new Completion("{}");
        var coordinator = new KnowledgeFactAnswerCoordinator(new Search([]), completion);

        var result = await coordinator.AnswerAsync(new(World, "missing subject"));

        Assert.True(result.Unknown);
        Assert.Equal("KNOWLEDGE_NOT_FOUND", result.FallbackCode);
        Assert.Equal(0, completion.Calls);
    }

    private static KnowledgeHybridSearchHit Hit() =>
        new(FactId, "fact", "active", "location.market", "open", "The archive contains the old toll ledger.", 1, 1, null, null, false);

    private sealed class Search(IReadOnlyList<KnowledgeHybridSearchHit> hits) : IKnowledgeHybridSearchCoordinator
    {
        public Task<KnowledgeHybridRebuildResult> RebuildWorldAsync(string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<KnowledgeHybridRebuildResult> SynchronizeWorldAsync(string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<KnowledgeHybridSearchResult> SearchAsync(KnowledgeLexicalSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeHybridSearchResult(request.WorldId, request.AsOfMinute ?? 0, "lexical", hits));
    }

    private sealed class Completion(string json = "", string error = "") : ILocalStructuredCompletionProvider
    {
        public int Calls { get; private set; }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelStatus(true, new("test", "qwen3:8b", "v1")));
        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(error.Length == 0
                ? new StructuredCompletionResult(new("test", "qwen3:8b", "v1"), json, 5)
                : StructuredCompletionResult.Failure(error, "unavailable"));
        }
    }
}
