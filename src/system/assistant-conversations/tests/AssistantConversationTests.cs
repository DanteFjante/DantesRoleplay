using DantesRoleplay.Assistants;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Retrieval;
using DantesRoleplay.SystemConversations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class AssistantConversationTests
{
    private const string Operator = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Interrupted_turn_lookup_is_principal_provider_scope_bound_and_never_returns_message_content()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var capture = new AssistantTurnContextCapture(AssistantTurnContextProfiles.SystemReadV1,
            new string('A', 64), ["surface:inner"]);
        var begin = await store.BeginTurnAsync(new(Operator, "local", null, null,
            "private prompt must not leave the durable lookup", "recover.turn", new string('A', 64),
            AssistantConversationScopes.System, capture));
        await store.CompleteTurnAsync(new(begin.TurnId, AssistantConversationStatuses.Failed, null,
            "RECOVERY_FIXTURE", "The request ended safely.", "local", "fixture", "", "", 0, 0, 0));

        var found = await store.FindByIdempotencyKeyAsync(Operator, "local", "recover.turn",
            AssistantConversationScopes.System, context: capture);

        Assert.NotNull(found);
        Assert.Equal(begin.ConversationId, found!.ConversationId);
        Assert.Equal(begin.TurnId, found.TurnId);
        Assert.Equal(AssistantConversationStatuses.Failed, found.Status);
        Assert.Null(await store.FindByIdempotencyKeyAsync(Operator, "other", "recover.turn",
            AssistantConversationScopes.System, context: capture));
        Assert.Null(await store.FindByIdempotencyKeyAsync("principal.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "local", "recover.turn", AssistantConversationScopes.System, context: capture));
        Assert.Null(await store.FindByIdempotencyKeyAsync(Operator, "local", "recover.turn",
            AssistantConversationScopes.Advisory, context: capture));
        var forgedCapture = capture with { SourceReferences = ["surface:outer"] };
        Assert.Null(await store.FindByIdempotencyKeyAsync(Operator, "local", "recover.turn",
            AssistantConversationScopes.System, context: forgedCapture));
        var replayConflict = await Assert.ThrowsAsync<AssistantConversationException>(() => store.BeginTurnAsync(new(
            Operator, "local", null, null, "private prompt must not leave the durable lookup", "recover.turn",
            new string('A', 64), AssistantConversationScopes.System, forgedCapture)));
        Assert.Equal("ASSISTANT_IDEMPOTENCY_CONFLICT", replayConflict.Code);
        Assert.DoesNotContain("private prompt", System.Text.Json.JsonSerializer.Serialize(found), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Omitted_context_replay_proves_legacy_system_identity_and_rejects_captured_web_turns()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        const string message = "Normalized legacy request.";
        var legacyHash = SystemConversationRequestIdentity.Hash(message);
        var legacy = await store.BeginTurnAsync(new(Operator, SystemConversationRequestIdentity.Provider, null, null,
            message, "legacy-empty", legacyHash, AssistantConversationScopes.System));
        await store.CompleteTurnAsync(new(legacy.TurnId, AssistantConversationStatuses.Failed, null,
            "LEGACY_FAILURE", "The request ended safely.", "", "", "", "", 0, 0, 0));

        Assert.NotNull(await store.FindByIdempotencyKeyAsync(Operator, SystemConversationRequestIdentity.Provider,
            "legacy-empty", AssistantConversationScopes.System));
        Assert.True((await store.BeginTurnAsync(new(Operator, SystemConversationRequestIdentity.Provider, null, null,
            message, "legacy-empty", legacyHash, AssistantConversationScopes.System))).Replay);

        var capture = new AssistantTurnContextCapture(AssistantTurnContextProfiles.SystemReadV1,
            new string('B', 64), ["surface:inner"]);
        var captured = await store.BeginTurnAsync(new(Operator, SystemConversationRequestIdentity.Provider, null, null,
            message, "captured-same-hash", legacyHash, AssistantConversationScopes.System, capture));
        await store.CompleteTurnAsync(new(captured.TurnId, AssistantConversationStatuses.Failed, null,
            "CAPTURED_FAILURE", "The request ended safely.", "", "", "", "", 0, 0, 0));

        Assert.Null(await store.FindByIdempotencyKeyAsync(Operator, SystemConversationRequestIdentity.Provider,
            "captured-same-hash", AssistantConversationScopes.System));
        var capturedConflict = await Assert.ThrowsAsync<AssistantConversationException>(() => store.BeginTurnAsync(new(
            Operator, SystemConversationRequestIdentity.Provider, null, null, message, "captured-same-hash",
            legacyHash, AssistantConversationScopes.System)));
        Assert.Equal("ASSISTANT_IDEMPOTENCY_CONFLICT", capturedConflict.Code);

        var oldFailedWeb = await store.BeginTurnAsync(new(Operator, SystemConversationRequestIdentity.Provider, null, null,
            "old failed web request", "old-web-failed", new string('D', 64), AssistantConversationScopes.System));
        await store.CompleteTurnAsync(new(oldFailedWeb.TurnId, AssistantConversationStatuses.Failed, null,
            "OLD_WEB_FAILURE", "The request ended safely.", "", "", "", "", 0, 0, 0));
        Assert.Null(await store.FindByIdempotencyKeyAsync(Operator, SystemConversationRequestIdentity.Provider,
            "old-web-failed", AssistantConversationScopes.System));
        var oldFailedConflict = await Assert.ThrowsAsync<AssistantConversationException>(() => store.BeginTurnAsync(new(
            Operator, SystemConversationRequestIdentity.Provider, null, null, "old failed web request", "old-web-failed",
            new string('D', 64), AssistantConversationScopes.System)));
        Assert.Equal("ASSISTANT_IDEMPOTENCY_CONFLICT", oldFailedConflict.Code);

        var oldWeb = await store.BeginTurnAsync(new(Operator, SystemConversationRequestIdentity.Provider, null, null,
            "old web request", "old-web-complete", new string('C', 64), AssistantConversationScopes.System, capture));
        await store.CompleteTurnAsync(new(oldWeb.TurnId, AssistantConversationStatuses.Completed, "Done.", "", "",
            "ollama", "fixture", "", "inner", 0, 0, 0, Context: new(
                AssistantTurnContextProfiles.SystemReadV1, capture.Fingerprint, capture.SourceReferences,
                AssistantTurnResponseDispositions.Answered)));
        Assert.Null(await store.FindByIdempotencyKeyAsync(Operator, SystemConversationRequestIdentity.Provider,
            "old-web-complete", AssistantConversationScopes.System));
    }

    [Fact]
    public async Task Successful_turn_is_schema_bound_audited_and_same_key_replays_without_second_call()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var provider = new FakeProvider(new(
            new("ollama", "qwen3:8b", "digest", "standard"), "{\"reply\":\"Hello.\"}", 12, 8, 3));
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var service = new AssistantConversationService(store, provider);
        var request = new AssistantConversationCreate("local", "  Hello?\r\n", "request:first");

        var first = await service.CreateAsync(Operator, request);
        var replay = await service.CreateAsync(Operator, request);

        Assert.Equal(first.Summary.Id, replay.Summary.Id);
        Assert.Equal(1, first.Summary.Revision);
        Assert.Equal(AssistantConversationStatuses.Completed, first.Summary.Status);
        Assert.Equal(["user", "assistant"], first.Messages.Select(message => message.Role));
        Assert.Equal("Hello?", first.Messages[0].Content);
        Assert.Equal("Hello.", first.Messages[1].Content);
        Assert.Equal("qwen3:8b", Assert.Single(first.Turns).Model);
        Assert.Equal(1, provider.Calls);
        Assert.Single(await db.Operations.Where(item => item.Tool == "control.assistant.local-message").ToListAsync());
    }

    [Theory]
    [InlineData("LOCAL_MODEL_UNAVAILABLE")]
    [InlineData("LOCAL_MODEL_TIMEOUT")]
    [InlineData("LOCAL_MODEL_SATURATED")]
    public async Task Provider_failure_is_terminal_visible_and_has_no_assistant_message(string errorCode)
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var provider = new FakeProvider(StructuredCompletionResult.Failure(
            errorCode, "The local model did not complete."));
        var service = new AssistantConversationService(
            new AssistantConversationStore(db, new OperationLog(db)), provider);

        var conversation = await service.CreateAsync(Operator,
            new("local", "Are you there?", "request:failure"));

        var turn = Assert.Single(conversation.Turns);
        Assert.Equal(AssistantConversationStatuses.Failed, turn.Status);
        Assert.Equal(errorCode, turn.ErrorCode);
        Assert.Single(conversation.Messages);
        Assert.Equal("user", conversation.Messages[0].Role);
        Assert.False((await db.Operations.SingleAsync()).Success);
        Assert.Empty(await db.Entities.ToListAsync());
        Assert.Empty(await db.HostSettingOverrides.ToListAsync());
    }

    [Fact]
    public async Task Unexpected_provider_exception_is_reconciled_without_exposing_details()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var service = new AssistantConversationService(
            new AssistantConversationStore(db, new OperationLog(db)), new ThrowingProvider());

        var conversation = await service.CreateAsync(Operator,
            new("local", "Do not remain running", "request:unexpected"));

        var turn = Assert.Single(conversation.Turns);
        Assert.Equal(AssistantConversationStatuses.Failed, turn.Status);
        Assert.Equal("ASSISTANT_PROVIDER_FAILURE", turn.ErrorCode);
        Assert.DoesNotContain("secret", turn.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(conversation.Messages);
    }

    [Fact]
    public async Task Request_cancellation_is_reconciled_to_a_durable_cancelled_turn()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var provider = new CancellingProvider();
        var service = new AssistantConversationService(
            new AssistantConversationStore(db, new OperationLog(db)), provider);
        using var cancellation = new CancellationTokenSource();

        var pending = service.CreateAsync(Operator,
            new("local", "Cancel this request", "request:cancel"), cancellation.Token);
        await provider.Started.Task;
        cancellation.Cancel();
        var conversation = await pending;

        var turn = Assert.Single(conversation.Turns);
        Assert.Equal(AssistantConversationStatuses.Cancelled, turn.Status);
        Assert.Equal("ASSISTANT_REQUEST_CANCELLED", turn.ErrorCode);
        Assert.Single(conversation.Messages);
        Assert.False((await db.Operations.SingleAsync()).Success);
    }

    [Fact]
    public async Task Stale_revision_and_changed_idempotency_payload_are_conflicts()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var provider = new FakeProvider(new(
            new("ollama", "model", "digest"), "{\"reply\":\"ok\"}", 1));
        var service = new AssistantConversationService(
            new AssistantConversationStore(db, new OperationLog(db)), provider);
        var conversation = await service.CreateAsync(Operator, new("local", "first", "key:1"));

        var stale = await Assert.ThrowsAsync<AssistantConversationException>(() => service.SendAsync(
            Operator, conversation.Summary.Id, new(2, "second", "key:2")));
        Assert.Equal("ASSISTANT_REVISION_STALE", stale.Code);
        var conflict = await Assert.ThrowsAsync<AssistantConversationException>(() => service.CreateAsync(
            Operator, new("local", "different", "key:1")));
        Assert.Equal("ASSISTANT_IDEMPOTENCY_CONFLICT", conflict.Code);
        Assert.Equal(1, provider.Calls);
        Assert.Single(await db.AssistantTurns.ToListAsync());
    }

    [Fact]
    public async Task Idempotency_key_cannot_cross_create_and_append_request_targets()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var provider = new FakeProvider(new(
            new("ollama", "model", "digest"), "{\"reply\":\"ok\"}", 1));
        var service = new AssistantConversationService(
            new AssistantConversationStore(db, new OperationLog(db)), provider);
        var conversation = await service.CreateAsync(Operator, new("local", "first", "key:create"));
        conversation = await service.SendAsync(
            Operator, conversation.Summary.Id, new(1, "second", "key:append"));

        var appendAsCreate = await Assert.ThrowsAsync<AssistantConversationException>(() =>
            service.CreateAsync(Operator, new("local", "second", "key:append")));
        var createAsAppend = await Assert.ThrowsAsync<AssistantConversationException>(() =>
            service.SendAsync(Operator, conversation.Summary.Id, new(2, "first", "key:create")));

        Assert.Equal("ASSISTANT_IDEMPOTENCY_CONFLICT", appendAsCreate.Code);
        Assert.Equal("ASSISTANT_IDEMPOTENCY_CONFLICT", createAsAppend.Code);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, await db.AssistantTurns.CountAsync());
    }

    [Fact]
    public async Task Startup_recovery_fails_pending_turn_without_retrying_provider()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var begin = await store.BeginTurnAsync(new(
            Operator, "local", null, null, "pending", "key:pending", new string('A', 64)));

        Assert.Equal(1, await store.RecoverInterruptedAsync());
        Assert.Equal(0, await store.RecoverInterruptedAsync());
        var conversation = await store.GetAsync(Operator, begin.ConversationId);
        Assert.Equal(AssistantConversationStatuses.Failed, conversation!.Summary.Status);
        Assert.Equal("ASSISTANT_PROCESS_INTERRUPTED", Assert.Single(conversation.Turns).ErrorCode);
        Assert.Single(conversation.Messages);
        Assert.Single(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Conversation_removal_is_owner_and_revision_checked_and_cascades_chat_history()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var service = new AssistantConversationService(store, new FakeProvider(new(
            new("ollama", "model", "digest"), "{\"reply\":\"retained\"}", 1)));
        var conversation = await service.CreateAsync(
            Operator, new("local", "remove me", "key:remove"));

        Assert.False(await store.DeleteAsync(
            "principal.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            conversation.Summary.Id,
            conversation.Summary.Revision));
        var stale = await Assert.ThrowsAsync<AssistantConversationException>(() => store.DeleteAsync(
            Operator, conversation.Summary.Id, conversation.Summary.Revision + 1));
        Assert.Equal("ASSISTANT_REVISION_STALE", stale.Code);

        Assert.True(await store.DeleteAsync(
            Operator, conversation.Summary.Id, conversation.Summary.Revision));
        Assert.Null(await store.GetAsync(Operator, conversation.Summary.Id));
        Assert.Empty(await db.AssistantTurns.ToListAsync());
        Assert.Empty(await db.AssistantMessages.ToListAsync());
        Assert.Contains(await db.Operations.ToListAsync(), value =>
            value.Tool == "control.assistant.remove-conversation" && value.Subject == conversation.Summary.Id);
    }

    [Fact]
    public async Task Direct_ai_activity_uses_the_existing_durable_activity_envelope()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var begin = await store.BeginTurnAsync(new(
            Operator,
            "local",
            null,
            null,
            "hello",
            "key:direct-ai",
            new string('A', 64),
            AssistantConversationScopes.System));
        await store.MarkRunningAsync(begin.TurnId);
        await store.AppendActivityAsync(new(
            begin.TurnId,
            "fixture-request",
            1,
            "dynamic-tool",
            "completed",
            "Provider-neutral request completed."));
        await store.CompleteTurnAsync(new(
            begin.TurnId,
            AssistantConversationStatuses.Failed,
            null,
            "AI_FIXTURE",
            "Fixture completion.",
            "ollama",
            "fixture-model",
            "fixture-revision",
            "outer",
            0,
            0,
            0));

        var rows = await store.ListAsync(
            Operator, "local", null, null, 10, scope: AssistantConversationScopes.System);

        Assert.Equal(begin.ConversationId, Assert.Single(rows).Id);
        Assert.Equal("dynamic-tool", Assert.Single((await store.GetAsync(
            Operator, begin.ConversationId, scope: AssistantConversationScopes.System))!.Activities).Kind);
    }

    private sealed class FakeProvider(StructuredCompletionResult result) : ILocalStructuredCompletionProvider
    {
        public int Calls { get; private set; }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result.Ok
                ? new LocalModelStatus(true, result.Identity)
                : LocalModelStatus.Unavailable(result.ErrorCode, result.ErrorMessage));
        public Task<StructuredCompletionResult> CompleteAsync(
            StructuredCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(AssistantConversationService.TaskClass, request.TaskClass);
            Assert.DoesNotContain("tools", request.ResponseSchema, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingProvider : ILocalStructuredCompletionProvider
    {
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalModelStatus.Unavailable("LOCAL_MODEL_UNAVAILABLE", "Unavailable."));

        public Task<StructuredCompletionResult> CompleteAsync(
            StructuredCompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret provider detail");
    }

    private sealed class CancellingProvider : ILocalStructuredCompletionProvider
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelStatus(true, new("ollama", "model", "digest")));

        public async Task<StructuredCompletionResult> CompleteAsync(
            StructuredCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
