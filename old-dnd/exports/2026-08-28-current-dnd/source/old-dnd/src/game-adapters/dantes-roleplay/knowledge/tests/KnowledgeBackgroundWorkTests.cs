using DantesRoleplay.Content;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class KnowledgeBackgroundWorkTests
{
    private const string World = "world.test";
    private const string Fact = "fact.test.one";

    [Fact]
    public async Task Embedding_and_proposal_queues_are_separately_bounded()
    {
        var queue = new KnowledgeBackgroundQueue(new()
        {
            EmbeddingQueueCapacity = 1,
            ProposalQueueCapacity = 1,
            MaxRetainedJobs = 16
        });

        var embedding = await queue.EnqueueAsync(new(KnowledgeBackgroundJobKind.EmbeddingSync, World));
        var proposal = await queue.EnqueueAsync(new(KnowledgeBackgroundJobKind.KnowledgeProposals, World, [Fact]));
        var rejectedEmbedding = await queue.EnqueueAsync(new(KnowledgeBackgroundJobKind.EmbeddingSync, World));
        var rejectedProposal = await queue.EnqueueAsync(new(KnowledgeBackgroundJobKind.KnowledgeProposals, World, [Fact]));

        Assert.Equal("queued", embedding.Status);
        Assert.Equal("queued", proposal.Status);
        Assert.Equal("BACKGROUND_QUEUE_FULL", rejectedEmbedding.ErrorCode);
        Assert.Equal("BACKGROUND_QUEUE_FULL", rejectedProposal.ErrorCode);
        Assert.True(queue.Cancel(embedding.JobId));
        Assert.Equal("cancelled", queue.Get(embedding.JobId)!.Status);
        Assert.False(queue.Cancel(embedding.JobId));
    }

    [Fact]
    public async Task Valid_review_proposal_uses_only_supplied_ids_and_writes_nothing()
    {
        var document = Document('a');
        var fingerprint = ContentHash.Of(document.KnowledgeId, document.ContentHash);
        var completion = new Completion($$"""
            {"sourceFingerprint":"{{fingerprint}}","aliases":[{"knowledgeId":"{{Fact}}","values":["old toll record"]}],"tags":[{"knowledgeId":"{{Fact}}","values":["market-ledger"]}],"duplicates":[],"contradictions":[]}
            """);
        var source = new Documents(document, document);
        var processor = new KnowledgeBackgroundJobProcessor(new Hybrid(), source, completion);

        var outcome = await processor.ProcessAsync(new(
            "knowledge-job.test", KnowledgeBackgroundJobKind.KnowledgeProposals, World, [Fact]));

        Assert.Equal("completed", outcome.Status);
        Assert.Equal("standard", outcome.ModelProfile);
        Assert.NotNull(outcome.Proposal);
        Assert.Equal(["old toll record"], Assert.Single(outcome.Proposal!.Aliases).Aliases);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task Revised_source_discards_completed_model_output_as_stale()
    {
        var before = Document('a');
        var after = Document('b');
        var fingerprint = ContentHash.Of(before.KnowledgeId, before.ContentHash);
        var completion = new Completion($$"""
            {"sourceFingerprint":"{{fingerprint}}","aliases":[],"tags":[],"duplicates":[],"contradictions":[]}
            """);
        var processor = new KnowledgeBackgroundJobProcessor(new Hybrid(), new Documents(before, after), completion);

        var outcome = await processor.ProcessAsync(new(
            "knowledge-job.test", KnowledgeBackgroundJobKind.KnowledgeProposals, World, [Fact]));

        Assert.Equal("stale", outcome.Status);
        Assert.Equal("BACKGROUND_INPUT_STALE", outcome.ErrorCode);
        Assert.Null(outcome.Proposal);
    }

    [Fact]
    public async Task Unsupported_proposal_id_is_rejected()
    {
        var document = Document('a');
        var fingerprint = ContentHash.Of(document.KnowledgeId, document.ContentHash);
        var completion = new Completion($$"""
            {"sourceFingerprint":"{{fingerprint}}","aliases":[{"knowledgeId":"secret.not-supplied","values":["leak"]}],"tags":[],"duplicates":[],"contradictions":[]}
            """);
        var processor = new KnowledgeBackgroundJobProcessor(new Hybrid(), new Documents(document, document), completion);

        var outcome = await processor.ProcessAsync(new(
            "knowledge-job.test", KnowledgeBackgroundJobKind.KnowledgeProposals, World, [Fact]));

        Assert.Equal("failed", outcome.Status);
        Assert.Equal("BACKGROUND_PROPOSAL_INVALID", outcome.ErrorCode);
        Assert.Null(outcome.Proposal);
    }

    [Fact]
    public async Task Live_qwen3_8b_returns_review_only_proposal_when_enabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_COMPLETION"),
                "1",
                StringComparison.Ordinal)) return;
        var document = Document('a');
        var processor = new KnowledgeBackgroundJobProcessor(
            new Hybrid(),
            new Documents(document, document),
            new OllamaStructuredCompletionProvider(new HttpClient(), new()
            {
                Enabled = true,
                Model = "qwen3:8b",
                Timeout = TimeSpan.FromMinutes(2)
            }));

        var outcome = await processor.ProcessAsync(new(
            "knowledge-job.live", KnowledgeBackgroundJobKind.KnowledgeProposals, World, [Fact]));

        Assert.Equal("completed", outcome.Status);
        Assert.NotNull(outcome.Proposal);
        Assert.All(outcome.Proposal!.Aliases, alias => Assert.Equal(Fact, alias.KnowledgeId));
        Assert.All(outcome.Proposal.Tags, tag => Assert.Equal(Fact, tag.KnowledgeId));
        Assert.Empty(outcome.Proposal.Duplicates);
        Assert.Empty(outcome.Proposal.Contradictions);
    }

    private static KnowledgeLexicalDocument Document(char hash) =>
        new(Fact, World, "fact", "active", "location.market", "open", null, null, new string(hash, 64),
            "Old Toll Ledger\nThe archive contains the old toll ledger.\nMarket\nfact.test.one\nlocation.market");

    private sealed class Documents(params KnowledgeLexicalDocument[] sequence) : IKnowledgeSearchDocumentSource
    {
        private int _index;
        public int Reads => _index;
        public Task<IReadOnlyList<KnowledgeLexicalDocument>> ReadWorldAsync(string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<KnowledgeLexicalDocument?> ReadAsync(string knowledgeId, CancellationToken cancellationToken = default)
        {
            var value = sequence[Math.Min(_index, sequence.Length - 1)];
            _index++;
            return Task.FromResult<KnowledgeLexicalDocument?>(value);
        }
    }

    private sealed class Completion(string json) : ILocalStructuredCompletionProvider
    {
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredCompletionResult(new("test", "qwen3:8b", "v1"), json, 5));
    }

    private sealed class Hybrid : IKnowledgeHybridSearchCoordinator
    {
        public Task<KnowledgeHybridRebuildResult> RebuildWorldAsync(string worldId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<KnowledgeHybridRebuildResult> SynchronizeWorldAsync(string worldId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeHybridRebuildResult(worldId, 1, 1, true, "generation", EmbeddedDocuments: 0));
        public Task<KnowledgeHybridSearchResult> SearchAsync(KnowledgeLexicalSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
