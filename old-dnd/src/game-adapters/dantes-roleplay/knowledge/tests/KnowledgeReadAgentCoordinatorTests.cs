using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeReadAgentCoordinatorTests
{
    private static readonly LocalModelIdentity Identity = new("test", "qwen3:8b", "digest", "standard");

    [Fact]
    public async Task Bounded_plan_runs_scoped_searches_and_returns_only_cited_candidates()
    {
        var model = new Completion(
            """{"normalizedQuestion":"Where is the ledger?","searchQueries":["old toll ledger archive"]}""",
            """{"selectedFactIds":["fact.ledger"],"statements":[{"text":"The ledger is in the archive.","citations":[{"knowledgeId":"fact.ledger","kind":"fact"}]}],"unresolved":[],"unknown":false}""");
        var search = new Search(request => request.Query.Contains("toll", StringComparison.Ordinal)
            ? Result(Hit("fact.ledger", "fact"))
            : Result());
        var coordinator = new KnowledgeReadAgentCoordinator(search, model);

        var result = await coordinator.AnswerAsync(new("world.test", "Where is the ledger?"));

        Assert.True(result.Ok);
        Assert.True(result.Mode == "read-agent", $"{result.FallbackCode}: {result.FallbackMessage}");
        Assert.False(result.Unknown);
        Assert.Equal(["Where is the ledger?", "old toll ledger archive"], search.Queries);
        Assert.Equal("fact.ledger", Assert.Single(result.SelectedFactIds));
        Assert.Equal(2, model.Calls);
    }

    [Fact]
    public async Task Invalid_read_plan_cannot_issue_id_tool_or_command_searches()
    {
        var model = new Completion(
            """{"normalizedQuestion":"Find it","searchQueries":["tool SQL secret.fact.hidden"]}""");
        var search = new Search(_ => Result(Hit("fact.safe", "fact")));
        var coordinator = new KnowledgeReadAgentCoordinator(search, model);

        var result = await coordinator.AnswerAsync(new("world.test", "Find it"));

        Assert.Equal("deterministic", result.Mode);
        Assert.True(result.Unknown);
        Assert.Equal(["Find it"], search.Queries);
        Assert.Equal("LOCAL_MODEL_SEMANTIC_INVALID", result.FallbackCode);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task Invented_answer_id_and_model_identity_drift_fail_closed()
    {
        var invented = new KnowledgeReadAgentCoordinator(
            new Search(_ => Result(Hit("fact.safe", "fact"))),
            new Completion(
                """{"normalizedQuestion":"Safe?","searchQueries":[]}""",
                """{"selectedFactIds":["secret.unsupplied"],"statements":[{"text":"Leak","citations":[{"knowledgeId":"secret.unsupplied","kind":"secret"}]}],"unresolved":[],"unknown":false}"""));
        var inventedResult = await invented.AnswerAsync(new("world.test", "Safe?"));
        Assert.Equal("LOCAL_MODEL_SEMANTIC_INVALID", inventedResult.FallbackCode);
        Assert.Empty(inventedResult.Statements);

        var drift = new KnowledgeReadAgentCoordinator(
            new Search(_ => Result(Hit("fact.safe", "fact"))),
            new Completion(
                [Identity, Identity with { Revision = "changed" }],
                """{"normalizedQuestion":"Safe?","searchQueries":[]}""",
                """{"selectedFactIds":[],"statements":[],"unresolved":["unknown"],"unknown":true}"""));
        var driftResult = await drift.AnswerAsync(new("world.test", "Safe?"));
        Assert.Equal("LOCAL_MODEL_IDENTITY_CHANGED", driftResult.FallbackCode);
    }

    [Fact]
    public async Task Live_qwen3_8b_runs_bounded_read_chain_when_enabled()
    {
        if (Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_COMPLETION") != "1") return;
        var search = new Search(_ => Result(new KnowledgeHybridSearchHit(
            "fact.ledger", "fact", "active", "location.archive", "open",
            "The old toll ledger is stored in the north archive cabinet.",
            1, 1, null, null, false)));
        var coordinator = new KnowledgeReadAgentCoordinator(
            search,
            new OllamaStructuredCompletionProvider(new HttpClient(), new()
            {
                Enabled = true,
                Model = "qwen3:8b",
                Timeout = TimeSpan.FromMinutes(2)
            }));

        var result = await coordinator.AnswerAsync(new(
            "world.test", "Where is the old toll ledger?"));

        Assert.True(result.Ok, $"{result.ErrorCode}: {result.ErrorMessage}");
        Assert.True(result.Mode == "read-agent", $"{result.FallbackCode}: {result.FallbackMessage}");
        Assert.Contains("fact.ledger", result.SelectedFactIds);
        Assert.InRange(search.Queries.Count, 1, 3);
    }

    private static KnowledgeHybridSearchHit Hit(string id, string kind) =>
        new(id, kind, "active", "location.market", "open", "Safe canonical summary.", 1, 1, null, null, false);
    private static KnowledgeHybridSearchResult Result(params KnowledgeHybridSearchHit[] hits) =>
        new("world.test", 42, "lexical", hits);

    private sealed class Search(Func<KnowledgeLexicalSearchRequest, KnowledgeHybridSearchResult> result)
        : IKnowledgeHybridSearchCoordinator
    {
        public List<string> Queries { get; } = [];
        public Task<KnowledgeHybridSearchResult> SearchAsync(KnowledgeLexicalSearchRequest request, CancellationToken cancellationToken = default)
        {
            Queries.Add(request.Query);
            return Task.FromResult(result(request));
        }
        public Task<KnowledgeHybridRebuildResult> RebuildWorldAsync(string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<KnowledgeHybridRebuildResult> SynchronizeWorldAsync(string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Completion : ILocalStructuredCompletionProvider
    {
        private readonly Queue<string> _json;
        private readonly Queue<LocalModelIdentity> _identities;
        public int Calls { get; private set; }

        public Completion(params string[] json) : this(Enumerable.Repeat(Identity, json.Length), json) { }
        public Completion(IEnumerable<LocalModelIdentity> identities, params string[] json)
        {
            _json = new(json);
            _identities = new(identities);
        }

        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new StructuredCompletionResult(_identities.Dequeue(), _json.Dequeue(), 2, 3, 4));
        }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
