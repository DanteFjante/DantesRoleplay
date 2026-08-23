using DantesRoleplay.DataAccess;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class AuthorizedKnowledgeAnswerCoordinatorTests
{
    private const string Campaign = "campaign.test";
    private const string FactId = "fact.hidden-ledger";

    [Fact]
    public async Task Actor_answer_strips_canonical_ids_and_preserves_perspective()
    {
        var resolver = new Resolver(Set());
        var coordinator = new AuthorizedKnowledgeAnswerCoordinator(resolver, new Completion("""
            {"selectedIds":["fact.hidden-ledger"],"statements":[{"text":"The archive holds an old toll ledger.","citations":["fact.hidden-ledger"]}],"unresolved":[],"unknown":false}
            """));

        var result = await coordinator.AnswerAsync(new(Campaign, "What does the archive hold?"));

        Assert.True(result.Answered);
        var statement = Assert.Single(result.Statements);
        Assert.Equal("suspected", statement.Stance);
        Assert.Equal("statement", statement.PresentationKind);
        Assert.DoesNotContain(FactId, statement.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(FactId, System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denied_audience_does_not_call_the_model()
    {
        var completion = new Completion("{}");
        var coordinator = new AuthorizedKnowledgeAnswerCoordinator(new Resolver(AuthorizedKnowledgeCandidateSet.Denied()), completion);

        var result = await coordinator.AnswerAsync(new(Campaign, "anything"));

        Assert.Equal("denied", result.Status);
        Assert.Equal(0, completion.Calls);
    }

    [Fact]
    public async Task Mixed_perspectives_are_rejected()
    {
        var set = Set() with
        {
            Candidates = [
                new(FactId, "Archive ledger", "known", "statement", "a"),
                new("rumour.wharf", "The wharf is haunted", "believed", "rumour", "b")]
        };
        var coordinator = new AuthorizedKnowledgeAnswerCoordinator(new Resolver(set), new Completion("""
            {"selectedIds":["fact.hidden-ledger","rumour.wharf"],"statements":[{"text":"The ledger says the wharf is haunted.","citations":["fact.hidden-ledger","rumour.wharf"]}],"unresolved":[],"unknown":false}
            """));

        var result = await coordinator.AnswerAsync(new(Campaign, "wharf"));

        Assert.Equal("unknown", result.Status);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public async Task Model_echoing_a_canonical_id_in_display_text_is_rejected()
    {
        var coordinator = new AuthorizedKnowledgeAnswerCoordinator(new Resolver(Set()), new Completion("""
            {"selectedIds":["fact.hidden-ledger"],"statements":[{"text":"fact.hidden-ledger says the archive has a ledger.","citations":["fact.hidden-ledger"]}],"unresolved":[],"unknown":false}
            """));

        var result = await coordinator.AnswerAsync(new(Campaign, "archive"));

        Assert.Equal("unknown", result.Status);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public async Task Changing_authorized_input_retries_once_then_fails_closed()
    {
        var first = Set();
        var changed = Set() with { Candidates = [new(FactId, "Archive ledger", "suspected", "statement", "changed")] };
        var resolver = new Resolver(first, changed, first, changed);
        var completion = new Completion("""
            {"selectedIds":["fact.hidden-ledger"],"statements":[{"text":"The archive holds an old toll ledger.","citations":["fact.hidden-ledger"]}],"unresolved":[],"unknown":false}
            """);
        var coordinator = new AuthorizedKnowledgeAnswerCoordinator(resolver, completion);

        var result = await coordinator.AnswerAsync(new(Campaign, "archive"));

        Assert.Equal("unknown", result.Status);
        Assert.Equal("KNOWLEDGE_INPUT_STALE", result.ErrorCode);
        Assert.Equal(2, completion.Calls);
    }

    private static AuthorizedKnowledgeCandidateSet Set() => new(
        true, true, "policy.1",
        [new(FactId, "Archive\nAn old toll ledger\nMarket", "suspected", "statement", "stable")],
        false);

    private sealed class Resolver(params AuthorizedKnowledgeCandidateSet[] sets) : IAuthorizedKnowledgeCandidateResolver
    {
        private int _index;
        public Task<AuthorizedKnowledgeCandidateSet> ResolveAsync(AuthorizedKnowledgeAnswerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(sets[Math.Min(_index++, sets.Length - 1)]);
    }

    private sealed class Completion(string json) : ILocalStructuredCompletionProvider
    {
        public int Calls { get; private set; }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelStatus(true, new("test", "qwen3:8b", "v1")));
        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new StructuredCompletionResult(new("test", "qwen3:8b", "v1"), json, 1));
        }
    }
}
