using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Tests;

namespace DantesRoleplay.Play.Tests;

public sealed class ConversationMemoryTests : IDisposable
{
    private static readonly DateTime CapturedAt = new(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc);
    private readonly SqliteFixture fixture = new();

    [Fact]
    public void Visible_message_batch_is_atomic_ordered_and_replay_safe()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var binding = Binding();
        store.Connect(binding);
        var append = Turn(binding, "turn.1", "request.1",
            Message("item.user.1", ConversationMemoryRoles.User,
                ConversationMemoryMessageKinds.UserPrompt, "First prompt."),
            Message("item.assistant.1", ConversationMemoryRoles.Assistant,
                ConversationMemoryMessageKinds.AssistantCommentary, "Visible progress."),
            Message("item.user.2", ConversationMemoryRoles.User,
                ConversationMemoryMessageKinds.UserPrompt, "Steering message."),
            Message("item.assistant.2", ConversationMemoryRoles.Assistant,
                ConversationMemoryMessageKinds.AssistantFinal, "Visible final answer."));

        var applied = store.AppendTurn(append);
        var replay = store.AppendTurn(append);

        Assert.False(applied.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(applied.ReceiptId, replay.ReceiptId);
        Assert.Equal([1, 2, 3, 4], applied.Messages.Select(value => value.Ordinal));
        Assert.Equal(["item.user.1", "item.assistant.1", "item.user.2", "item.assistant.2"],
            applied.Messages.Select(value => value.SourceMessageId));
        Assert.Equal(4, store.GetState(binding.Scope)!.TotalMessageCount);
        Assert.Equal(4, db.Set<ApplicationConversationMemoryMessageRecord>().Count());
        Assert.Single(db.Set<ApplicationConversationMemoryDeliveryRecord>());
    }

    [Fact]
    public void Changed_replay_and_oversized_batches_fail_without_partial_messages()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var binding = Binding();
        store.Connect(binding);
        store.AppendTurn(Turn(binding, "turn.1", "request.1"));

        var conflict = Assert.Throws<ConversationMemoryException>(() => store.AppendTurn(
            Turn(binding, "turn.1", "request.1",
                Message("item.user.1", ConversationMemoryRoles.User,
                    ConversationMemoryMessageKinds.UserPrompt, "Changed prompt."),
                Message("item.assistant.1", ConversationMemoryRoles.Assistant,
                    ConversationMemoryMessageKinds.AssistantFinal, "Answer."))));
        Assert.Equal("CONVERSATION_MEMORY_REPLAY_CONFLICT", conflict.Code);
        var oversized = Assert.Throws<ConversationMemoryException>(() => store.AppendTurn(
            Turn(binding, "turn.2", "request.2",
                Message("item.user.2", ConversationMemoryRoles.User,
                    ConversationMemoryMessageKinds.UserPrompt, new string('u', 15_000)),
                Message("item.assistant.2", ConversationMemoryRoles.Assistant,
                    ConversationMemoryMessageKinds.AssistantFinal, new string('a', 10_000)))));
        Assert.Equal("CONVERSATION_MEMORY_TURN_TOO_LARGE", oversized.Code);
        Assert.Equal(2, db.Set<ApplicationConversationMemoryMessageRecord>().Count());
    }

    [Fact]
    public async Task Concurrent_delivery_uses_the_shared_database_gate_and_keeps_one_receipt()
    {
        using (var setup = fixture.CreateContext())
        {
            RegisterStateSpace(setup);
            new ApplicationConversationMemoryStore(setup).Connect(Binding());
        }
        using var firstDb = fixture.CreateContext();
        using var secondDb = fixture.CreateContext();
        var first = new ApplicationConversationMemoryStore(firstDb);
        var second = new ApplicationConversationMemoryStore(secondDb);
        using var start = new ManualResetEventSlim();
        var append = Turn(Binding(), "turn.concurrent", "request.concurrent");

        var firstTask = Task.Run(() => { start.Wait(); return first.AppendTurn(append); });
        var secondTask = Task.Run(() => { start.Wait(); return second.AppendTurn(append); });
        start.Set();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, results.OfType<ConversationMemoryAppendResult>().Count(value => value.Replayed));
        using var verify = fixture.CreateContext();
        Assert.Equal(2, verify.Set<ApplicationConversationMemoryMessageRecord>().Count());
        Assert.Single(verify.Set<ApplicationConversationMemoryDeliveryRecord>());
    }

    [Fact]
    public void Scope_lifecycle_and_paging_never_cross_the_linked_session()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var binding = Binding();
        store.Connect(binding);
        store.AppendTurn(Turn(binding, "turn.1", "request.1"));
        store.AppendTurn(Turn(binding, "turn.2", "request.2",
            Message("item.user.2", ConversationMemoryRoles.User,
                ConversationMemoryMessageKinds.UserPrompt, "Second prompt."),
            Message("item.assistant.2", ConversationMemoryRoles.Assistant,
                ConversationMemoryMessageKinds.AssistantFinal, "Second answer.")));
        var page = store.GetMessages(binding.Scope, null, 2);
        Assert.Equal([3, 4], page.Messages.Select(value => value.Ordinal));
        Assert.Equal(3, page.NextBeforeOrdinal);
        var wrong = binding.Scope with { PrincipalId = "principal.other" };
        Assert.Null(store.GetState(wrong));
        Assert.Equal("CONVERSATION_MEMORY_NOT_FOUND",
            Assert.Throws<ConversationMemoryException>(() => store.GetMessages(wrong, null, 2)).Code);

        store.Disconnect(binding.Scope);
        Assert.Equal("CONVERSATION_MEMORY_NOT_CONNECTED", Assert.Throws<ConversationMemoryException>(() =>
            store.AppendTurn(Turn(binding, "turn.3", "request.3"))).Code);
        store.MarkRetryPending(binding.Scope, "CODEX_READ_FAILED");
        Assert.Equal(ConversationMemoryStatuses.RetryPending, store.GetState(binding.Scope)!.Status);
        store.Retry(binding.Scope);
        Assert.Equal(ConversationMemoryStatuses.Connected, store.GetState(binding.Scope)!.Status);
        store.Archive(binding.Scope);
        Assert.Null(store.GetState(binding.Scope));
        Assert.Equal(ConversationMemoryStatuses.Archived, store.GetState(binding.Scope, true)!.Status);
    }

    [Fact]
    public void Derived_candidate_pins_sources_and_blocks_source_deletion_without_gameplay_writes()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var binding = Binding();
        store.Connect(binding);
        var captured = store.AppendTurn(Turn(binding, "turn.1", "request.1"));
        var entityCount = db.Entities.Count();
        var candidate = store.AppendDerivedCandidate(new(binding.Scope,
            new("task.dream.1", "command.dream.1"), captured.Journal.Revision,
            captured.Messages.Select(value => value.SourceMessageId).ToArray(),
            "{\"summary\":\"A possible recollection.\"}", new string('A', 64),
            "inner-result.dream.1", CapturedAt));
        var replay = store.AppendDerivedCandidate(new(binding.Scope,
            new("task.dream.1", "command.dream.1"), captured.Journal.Revision,
            captured.Messages.Select(value => value.SourceMessageId).ToArray(),
            "{\"summary\":\"A possible recollection.\"}", new string('A', 64),
            "inner-result.dream.1", CapturedAt));

        Assert.Equal(candidate.Id, replay.Id);
        Assert.Equal(candidate.SourceFingerprint, replay.SourceFingerprint);
        Assert.Equal(candidate.CandidateJson, replay.CandidateJson);
        Assert.Equal(candidate.SourceMessageIds, replay.SourceMessageIds);
        Assert.Equal("private", candidate.Audience);
        Assert.Equal("candidate", candidate.Status);
        Assert.Equal(captured.Messages.Select(value => value.SourceMessageId), candidate.SourceMessageIds);
        Assert.Equal(entityCount, db.Entities.Count());
        var blocked = store.Delete(binding.Scope, captured.Messages[0].Id);
        Assert.False(blocked.Deleted);
        Assert.Equal([candidate.Id], blocked.RetainedDerivedReferences);
        Assert.Equal(ConversationMemoryStatuses.Archived, store.Archive(binding.Scope).Status);
    }

    [Fact]
    public void Unreferenced_delete_tombstones_text_and_preserves_source_identity()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var binding = Binding();
        store.Connect(binding);
        var captured = store.AppendTurn(Turn(binding, "turn.1", "request.1"));

        var deleted = store.Delete(binding.Scope, captured.Messages[0].Id);

        Assert.True(deleted.Deleted);
        Assert.Equal(1, deleted.TombstonedMessages);
        var page = store.GetMessages(binding.Scope, null, 8, includeArchived: true);
        var tombstone = page.Messages.Single(value => value.Id == captured.Messages[0].Id);
        Assert.Null(tombstone.Text);
        Assert.Equal("deleted", tombstone.Status);
        Assert.Equal(captured.Messages[0].SourceMessageId, tombstone.SourceMessageId);
        Assert.Equal(captured.Messages[0].TextFingerprint, tombstone.TextFingerprint);
    }

    private static ConversationMemoryBinding Binding() => new(
        new("principal.fixture", "play-fixture", "play-fixture-space", "session.fixture"),
        "codex", "project.fixture", "C:\\repo\\fixture", "thread.fixture");

    private static ConversationMemoryTurnAppend Turn(
        ConversationMemoryBinding binding, string turnId, string requestToken,
        params ConversationMemoryCapturedMessage[] messages) => new(
        binding, turnId, messages.Length == 0
            ? [
                Message("item.user.1", ConversationMemoryRoles.User,
                    ConversationMemoryMessageKinds.UserPrompt, "Prompt."),
                Message("item.assistant.1", ConversationMemoryRoles.Assistant,
                    ConversationMemoryMessageKinds.AssistantFinal, "Answer.")
            ]
            : messages,
        "codex-app-server/thread-read@0.153.4", CapturedAt, requestToken);

    private static ConversationMemoryCapturedMessage Message(
        string id, string role, string kind, string text) => new(id, role, kind, text, CapturedAt);

    private static void RegisterStateSpace(DantesRoleplayDbContext db)
    {
        if (db.Set<ApplicationStateSpaceRecord>().Any()) return;
        var applications = new SqliteApplicationRegistry(db);
        var application = ApplicationIdentifier.Parse("play-fixture");
        var revision = applications.Register(new(application, "Play Fixture", "", []));
        new SqliteStateSpaceRegistry(db, applications).Create(
            new("play-fixture-space", revision, new string('A', 64)));
    }

    public void Dispose() => fixture.Dispose();
}
