using DantesRoleplay.Assistants;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class AssistantConversationTests
{
    private const string Operator = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
