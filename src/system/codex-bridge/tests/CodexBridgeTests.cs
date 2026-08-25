using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CodexBridgeTests
{
    private const string Operator = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Default_pin_matches_the_reviewed_cli_version()
    {
        Assert.Equal("0.149.1", Options().PinnedVersion);
    }

    [Fact]
    public async Task Start_stream_persist_resume_and_replay_use_one_external_call_per_key()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var sessions = new[]
        {
            CompletedSession("external-thread-1", "external-turn-1", "First answer."),
            CompletedSession("external-thread-1", "external-turn-2", "Second answer.")
        };
        var factory = new FakeFactory(sessions);
        var options = Options();
        var registry = new CodexTurnRegistry(options);
        var service = Service(db, factory, registry, options);
        var request = new AssistantConversationCreate("codex", "Inspect the repository", "codex:first");

        var firstEvents = await CollectAsync(service.CreateAsync(Operator, request));
        var first = Assert.IsType<AssistantConversationDocument>(firstEvents[^1].Conversation);
        var replayEvents = await CollectAsync(service.CreateAsync(Operator, request));
        var replay = Assert.IsType<AssistantConversationDocument>(Assert.Single(replayEvents).Conversation);
        var secondEvents = await CollectAsync(service.SendAsync(
            Operator, first.Summary.Id, new(1, "Continue the inspection", "codex:second")));
        var second = Assert.IsType<AssistantConversationDocument>(secondEvents[^1].Conversation);

        Assert.Equal(first.Summary.Id, replay.Summary.Id);
        Assert.Equal(2, factory.CreateCalls);
        Assert.Null(sessions[0].ResumedThreadId);
        Assert.Equal("external-thread-1", sessions[1].ResumedThreadId);
        Assert.Equal("external-thread-1", second.ExternalThreadId);
        Assert.Equal(["external-turn-1", "external-turn-2"], second.Turns.Select(turn => turn.ExternalTurnId));
        Assert.Equal(["First answer.", "Second answer."],
            second.Messages.Where(message => message.Role == "assistant").Select(message => message.Content));
        Assert.Equal(2, second.Activities.Count);
        Assert.All(second.Activities, activity => Assert.Equal("command", activity.Kind));
        Assert.Equal(2, await db.Operations.CountAsync(operation => operation.Tool == "control.assistant.codex-message"));
    }

    [Fact]
    public async Task Explicit_cancel_interrupts_once_and_reconciles_the_turn_as_cancelled()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var session = new BlockingSession();
        var options = Options();
        var registry = new CodexTurnRegistry(options);
        var service = Service(db, new FakeFactory([session]), registry, options);
        await using var stream = service.CreateAsync(
            Operator, new("codex", "Wait for cancellation", "codex:cancel")).GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        var accepted = Assert.IsType<AssistantConversationDocument>(stream.Current.Conversation);
        var turnId = Assert.Single(accepted.Turns).Id;
        var cancelled = await service.CancelAsync(Operator, accepted.Summary.Id, turnId);
        Assert.True(cancelled.Accepted);
        Assert.True(await stream.MoveNextAsync());
        var completed = Assert.IsType<AssistantConversationDocument>(stream.Current.Conversation);

        Assert.Equal(1, session.InterruptCalls);
        Assert.Equal(AssistantConversationStatuses.Cancelled, Assert.Single(completed.Turns).Status);
        Assert.Equal("CODEX_TURN_CANCELLED", completed.Turns[0].ErrorCode);
        Assert.False(await registry.InterruptAsync(turnId, CancellationToken.None));
    }

    [Fact]
    public async Task Process_failure_and_host_restart_are_durable_and_never_replay_a_prompt()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var options = Options();
        var factory = new FakeFactory([new ThrowingSession()]);
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var service = new CodexConversationService(store, factory, new(options), options);

        var failedEvents = await CollectAsync(service.CreateAsync(
            Operator, new("codex", "Crash safely", "codex:crash")));
        var failed = Assert.IsType<AssistantConversationDocument>(failedEvents[^1].Conversation);
        Assert.Equal(AssistantConversationStatuses.Failed, Assert.Single(failed.Turns).Status);
        Assert.Equal("CODEX_PROCESS_EXITED", failed.Turns[0].ErrorCode);

        var pending = await store.BeginTurnAsync(new(
            Operator, "codex", null, null, "do not replay", "codex:pending", new string('A', 64)));
        Assert.Equal(1, await store.RecoverInterruptedAsync());
        var recovered = await store.GetAsync(Operator, pending.ConversationId);
        Assert.Equal("CODEX_PROCESS_INTERRUPTED", Assert.Single(recovered!.Turns).ErrorCode);
        Assert.Equal(1, factory.CreateCalls);
        Assert.Single(recovered.Messages);
    }

    [Fact]
    public async Task Saturation_fails_the_second_durable_turn_without_starting_another_process()
    {
        using var fixture = new SqliteFixture();
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var options = Options(maximumConcurrentTurns: 1);
        var firstSession = new BlockingSession();
        var factory = new FakeFactory([firstSession]);
        var registry = new CodexTurnRegistry(options);
        var firstService = Service(firstDb, factory, registry, options);
        var secondService = Service(secondDb, factory, registry, options);
        await using var firstStream = firstService.CreateAsync(
            Operator, new("codex", "First", "codex:saturation-1")).GetAsyncEnumerator();
        Assert.True(await firstStream.MoveNextAsync());

        var secondEvents = await CollectAsync(secondService.CreateAsync(
            Operator, new("codex", "Second", "codex:saturation-2")));
        var second = Assert.IsType<AssistantConversationDocument>(Assert.Single(secondEvents).Conversation);
        Assert.Equal("CODEX_SATURATED", Assert.Single(second.Turns).ErrorCode);
        Assert.Equal(1, factory.CreateCalls);

        var firstDocument = Assert.IsType<AssistantConversationDocument>(firstStream.Current.Conversation);
        await firstService.CancelAsync(Operator, firstDocument.Summary.Id, firstDocument.Turns[0].Id);
        Assert.True(await firstStream.MoveNextAsync());
    }

    [Fact]
    public async Task External_ids_are_immutable_and_activity_replay_is_idempotent()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var begin = await store.BeginTurnAsync(new(
            Operator, "codex", null, null, "inspect", "codex:binding", new string('B', 64)));
        await store.MarkRunningAsync(begin.TurnId);
        await store.BindCodexTurnAsync(new(begin.TurnId, "thread-one", "turn-one", "inProgress"));

        var mismatch = await Assert.ThrowsAsync<AssistantConversationException>(() =>
            store.BindCodexTurnAsync(new(begin.TurnId, "thread-two", "turn-one", "inProgress")));
        var first = await store.AppendCodexActivityAsync(new(
            begin.TurnId, "item-one", 1, "command", "completed", "rg --files (completed)"));
        var replay = await store.AppendCodexActivityAsync(new(
            begin.TurnId, "item-one", 2, "command", "failed", "must not replace"));

        Assert.Equal("CODEX_THREAD_MISMATCH", mismatch.Code);
        Assert.Equal(first, replay);
        Assert.Single(await db.AssistantTurnActivities.ToListAsync());
    }

    [Fact]
    public void Protocol_normalization_excludes_reasoning_and_never_persists_patch_or_command_output()
    {
        Assert.Null(Normalize("item/completed", new
        {
            item = new { id = "reason-1", type = "reasoning", content = new[] { "hidden" } }
        }));
        var delta = Normalize("item/agentMessage/delta", new { delta = "Visible" });
        var command = Normalize("item/completed", new
        {
            item = new
            {
                id = "command-1", type = "commandExecution", command = "rg --files",
                status = "completed", aggregatedOutput = "secret raw output"
            }
        });
        var file = Normalize("item/completed", new
        {
            item = new
            {
                id = "file-1", type = "fileChange", status = "failed",
                changes = new[] { new { path = "README.md", patch = "secret patch" } }
            }
        });

        Assert.Equal("Visible", delta!.Delta);
        Assert.Equal("command", command!.Activity!.Kind);
        Assert.DoesNotContain("secret raw output", command.Activity.Summary);
        Assert.Equal("file-change", file!.Activity!.Kind);
        Assert.Contains("README.md", file.Activity.Summary);
        Assert.DoesNotContain("secret patch", file.Activity.Summary);
    }

    [Fact]
    public void Protocol_frames_fix_repo_one_request_approval_sandbox_and_network_policy_without_browser_overrides()
    {
        var options = Options();
        var start = CodexAppServerProcessSession.BuildThreadParameters(options, null);
        var resume = CodexAppServerProcessSession.BuildThreadParameters(options, "thread-existing");
        var turn = CodexAppServerProcessSession.BuildTurnParameters(options, "thread-existing", "Inspect only");

        Assert.Equal(options.RepositoryRoot, start.GetProperty("cwd").GetString());
        Assert.Equal("on-request", start.GetProperty("approvalPolicy").GetString());
        Assert.Equal("read-only", start.GetProperty("sandbox").GetString());
        Assert.Equal(CodexBridgeModels.Luna, start.GetProperty("model").GetString());
        Assert.Equal("thread-existing", resume.GetProperty("threadId").GetString());
        Assert.Equal(options.RepositoryRoot, resume.GetProperty("cwd").GetString());
        Assert.False(resume.TryGetProperty("model", out _));
        Assert.Equal("on-request", turn.GetProperty("approvalPolicy").GetString());
        Assert.Equal(options.RepositoryRoot, turn.GetProperty("cwd").GetString());
        Assert.Equal("readOnly", turn.GetProperty("sandboxPolicy").GetProperty("type").GetString());
        Assert.False(turn.GetProperty("sandboxPolicy").GetProperty("networkAccess").GetBoolean());
        Assert.False(turn.TryGetProperty("model", out _));
        Assert.False(turn.TryGetProperty("developerInstructions", out _));
    }

    [Fact]
    public void Jsonl_seam_omits_jsonrpc_denies_server_requests_and_rejects_invalid_or_oversize_lines()
    {
        var request = CodexAppServerProcessSession.BuildRequest(7, "turn/start", new { threadId = "thread-1" });
        var notification = CodexAppServerProcessSession.BuildNotification("initialized");
        using var idDocument = JsonDocument.Parse("42");
        var denied = CodexAppServerProcessSession.BuildDeniedResponse(
            idDocument.RootElement, "item/fileChange/requestApproval");

        Assert.DoesNotContain("jsonrpc", request, StringComparison.Ordinal);
        Assert.Equal("turn/start", CodexAppServerProcessSession.ParseProtocolLine(request, 1024)
            .GetProperty("method").GetString());
        Assert.Equal("initialized", CodexAppServerProcessSession.ParseProtocolLine(notification, 1024)
            .GetProperty("method").GetString());
        var denial = CodexAppServerProcessSession.ParseProtocolLine(denied, 1024);
        Assert.Equal(42, denial.GetProperty("id").GetInt32());
        Assert.Equal(-32001, denial.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("unsupported", denial.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("CODEX_PROTOCOL_INVALID", Assert.Throws<CodexBridgeException>(() =>
            CodexAppServerProcessSession.ParseProtocolLine("not-json", 1024)).Code);
        Assert.Equal("CODEX_PROTOCOL_OVERSIZE", Assert.Throws<CodexBridgeException>(() =>
            CodexAppServerProcessSession.ParseProtocolLine("{\"value\":\"" + new string('x', 1024) + "\"}", 100)).Code);
    }

    [Fact]
    public async Task One_request_accept_is_durable_dispatched_once_and_reconciled_by_external_request()
    {
        using var fixture = new SqliteFixture();
        await using var streamDb = fixture.CreateContext();
        await using var decisionDb = fixture.CreateContext();
        var options = Options();
        var session = new ApprovalSession(CodexApprovalKinds.Command, canAccept: true);
        var factory = new FakeFactory([session]);
        var registry = new CodexTurnRegistry(options);
        var streamService = Service(streamDb, factory, registry, options);
        var decisionService = Service(decisionDb, factory, registry, options);
        await using var stream = streamService.CreateAsync(
            Operator, new("codex", "Run the focused checks", "codex:approval-accept")).GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        var accepted = Assert.IsType<AssistantConversationDocument>(stream.Current.Conversation);
        Assert.True(await stream.MoveNextAsync());
        var requestEvent = stream.Current;
        var pending = Assert.IsType<AssistantTurnApprovalDocument>(requestEvent.Approval);
        Assert.Equal(AssistantConversationStatuses.AwaitingApproval,
            Assert.Single(requestEvent.Conversation!.Turns).Status);

        var decision = await decisionService.ApproveAsync(
            Operator, accepted.Summary.Id, pending.TurnId, pending.Id,
            new(pending.Revision, CodexApprovalDecisions.Accept));
        var duplicate = await Assert.ThrowsAsync<AssistantConversationException>(() =>
            decisionService.ApproveAsync(
                Operator, accepted.Summary.Id, pending.TurnId, pending.Id,
                new(pending.Revision, CodexApprovalDecisions.Accept)));

        Assert.Equal(CodexApprovalStatuses.Dispatched, decision.Approval.Status);
        Assert.Equal(CodexApprovalDecisions.Accept, decision.Approval.Decision);
        Assert.Equal("CODEX_APPROVAL_NOT_PENDING", duplicate.Code);
        Assert.Equal([CodexApprovalDecisions.Accept], session.Decisions);

        session.Continue();
        Assert.True(await stream.MoveNextAsync());
        var completed = Assert.IsType<AssistantConversationDocument>(stream.Current.Conversation);
        Assert.Equal(CodexApprovalStatuses.Resolved, Assert.Single(completed.Approvals).Status);
        Assert.Equal(AssistantConversationStatuses.Completed, Assert.Single(completed.Turns).Status);
        Assert.Single(await decisionDb.Operations
            .Where(operation => operation.Tool == "control.assistant.codex-approval").ToListAsync());
    }

    [Fact]
    public async Task Simultaneous_approval_decisions_have_one_durable_winner_and_one_dispatch()
    {
        using var fixture = new SqliteFixture();
        await using var streamDb = fixture.CreateContext();
        await using var firstDecisionDb = fixture.CreateContext();
        await using var secondDecisionDb = fixture.CreateContext();
        var options = Options();
        var session = new ApprovalSession(CodexApprovalKinds.Command, canAccept: true);
        var factory = new FakeFactory([session]);
        var registry = new CodexTurnRegistry(options);
        var streamService = Service(streamDb, factory, registry, options);
        var firstService = Service(firstDecisionDb, factory, registry, options);
        var secondService = Service(secondDecisionDb, factory, registry, options);
        await using var stream = streamService.CreateAsync(
            Operator, new("codex", "Run one command", "codex:approval-race")).GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        var conversation = Assert.IsType<AssistantConversationDocument>(stream.Current.Conversation);
        Assert.True(await stream.MoveNextAsync());
        var pending = Assert.IsType<AssistantTurnApprovalDocument>(stream.Current.Approval);
        var input = new CodexApprovalDecisionInput(pending.Revision, CodexApprovalDecisions.Accept);

        var attempts = await Task.WhenAll(
            Record.ExceptionAsync(() => firstService.ApproveAsync(
                Operator, conversation.Summary.Id, pending.TurnId, pending.Id, input)),
            Record.ExceptionAsync(() => secondService.ApproveAsync(
                Operator, conversation.Summary.Id, pending.TurnId, pending.Id, input)));

        Assert.Single(attempts, exception => exception is null);
        Assert.Single(attempts, exception =>
            exception is AssistantConversationException { Code: "CODEX_APPROVAL_NOT_PENDING" });
        Assert.Equal([CodexApprovalDecisions.Accept], session.Decisions);
        session.Continue();
        Assert.True(await stream.MoveNextAsync());
    }

    [Fact]
    public async Task Missing_process_after_durable_decision_marks_the_approval_failed()
    {
        using var fixture = new SqliteFixture();
        await using (var setupDb = fixture.CreateContext())
        {
            var store = new AssistantConversationStore(setupDb, new OperationLog(setupDb));
            var begin = await store.BeginTurnAsync(new(
                Operator, "codex", null, null, "inspect", "codex:approval-missing", new string('C', 64)));
            await store.MarkRunningAsync(begin.TurnId);
            await store.BindCodexTurnAsync(new(begin.TurnId, "thread-missing", "turn-missing", "inProgress"));
            await store.AppendCodexApprovalAsync(new(
                begin.TurnId, "number:81", "item-81", null, CodexApprovalKinds.Command,
                new string('D', 64), "Run tests.",
                new("Testing", "dotnet test", ".", [], "", "", []), true,
                DateTime.UtcNow.AddMinutes(1)));
        }

        await using var decisionDb = fixture.CreateContext();
        var approval = await decisionDb.AssistantTurnApprovals.AsNoTracking().SingleAsync();
        var service = Service(decisionDb, new FakeFactory([]), new CodexTurnRegistry(Options()), Options());
        var failure = await Assert.ThrowsAsync<CodexBridgeException>(() => service.ApproveAsync(
            Operator, approval.ConversationId, approval.TurnId, approval.Id,
            new(approval.Revision, CodexApprovalDecisions.Accept)));

        decisionDb.ChangeTracker.Clear();
        var failed = await decisionDb.AssistantTurnApprovals.AsNoTracking().SingleAsync();
        Assert.Equal("CODEX_APPROVAL_SESSION_UNKNOWN", failure.Code);
        Assert.Equal(CodexApprovalStatuses.Failed, failed.Status);
        Assert.Equal(CodexApprovalDecisions.Accept, failed.Decision);
        Assert.Single(await decisionDb.Operations
            .Where(operation => operation.Tool == "control.assistant.codex-approval").ToListAsync());
    }

    [Fact]
    public async Task Non_approvable_request_rejects_accept_but_decline_dispatches_once()
    {
        using var fixture = new SqliteFixture();
        await using var streamDb = fixture.CreateContext();
        await using var decisionDb = fixture.CreateContext();
        var options = Options();
        var session = new ApprovalSession(CodexApprovalKinds.FileChange, canAccept: false);
        var factory = new FakeFactory([session]);
        var registry = new CodexTurnRegistry(options);
        var streamService = Service(streamDb, factory, registry, options);
        var decisionService = Service(decisionDb, factory, registry, options);
        await using var stream = streamService.CreateAsync(
            Operator, new("codex", "Try an unsafe change", "codex:approval-decline")).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var conversation = Assert.IsType<AssistantConversationDocument>(stream.Current.Conversation);
        Assert.True(await stream.MoveNextAsync());
        var pending = Assert.IsType<AssistantTurnApprovalDocument>(stream.Current.Approval);

        var blocked = await Assert.ThrowsAsync<AssistantConversationException>(() =>
            decisionService.ApproveAsync(Operator, conversation.Summary.Id, pending.TurnId, pending.Id,
                new(pending.Revision, CodexApprovalDecisions.Accept)));
        var declined = await decisionService.ApproveAsync(
            Operator, conversation.Summary.Id, pending.TurnId, pending.Id,
            new(pending.Revision, CodexApprovalDecisions.Decline));

        Assert.Equal("CODEX_APPROVAL_NOT_ACCEPTABLE", blocked.Code);
        Assert.Equal(CodexApprovalDecisions.Decline, declined.Approval.Decision);
        Assert.Equal([CodexApprovalDecisions.Decline], session.Decisions);
        session.Continue();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(AssistantConversationStatuses.Completed,
            Assert.Single(stream.Current.Conversation!.Turns).Status);
    }

    [Fact]
    public async Task Approval_cancel_dispatches_once_and_reconciles_the_turn_as_cancelled()
    {
        using var fixture = new SqliteFixture();
        await using var streamDb = fixture.CreateContext();
        await using var decisionDb = fixture.CreateContext();
        var options = Options();
        var session = new ApprovalSession(CodexApprovalKinds.Network, canAccept: true);
        var factory = new FakeFactory([session]);
        var registry = new CodexTurnRegistry(options);
        var streamService = Service(streamDb, factory, registry, options);
        var decisionService = Service(decisionDb, factory, registry, options);
        await using var stream = streamService.CreateAsync(
            Operator, new("codex", "Try network access", "codex:approval-cancel")).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var conversation = Assert.IsType<AssistantConversationDocument>(stream.Current.Conversation);
        Assert.True(await stream.MoveNextAsync());
        var pending = Assert.IsType<AssistantTurnApprovalDocument>(stream.Current.Approval);

        await decisionService.ApproveAsync(
            Operator, conversation.Summary.Id, pending.TurnId, pending.Id,
            new(pending.Revision, CodexApprovalDecisions.Cancel));
        session.Continue();
        Assert.True(await stream.MoveNextAsync());

        Assert.Equal([CodexApprovalDecisions.Cancel], session.Decisions);
        Assert.Equal(AssistantConversationStatuses.Cancelled,
            Assert.Single(stream.Current.Conversation!.Turns).Status);
        Assert.Equal(CodexApprovalStatuses.Resolved,
            Assert.Single(stream.Current.Conversation.Approvals).Status);
    }

    [Fact]
    public async Task Approval_request_replay_requires_the_same_fingerprint_and_expiry_is_terminal()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var begin = await store.BeginTurnAsync(new(
            Operator, "codex", null, null, "inspect", "codex:approval-store", new string('C', 64)));
        await store.MarkRunningAsync(begin.TurnId);
        await store.BindCodexTurnAsync(new(begin.TurnId, "thread-approval", "turn-approval", "inProgress"));
        var details = new CodexApprovalDetails("tests", "dotnet test", ".", [], "", "", []);
        var append = new CodexApprovalAppend(
            begin.TurnId, "number:17", "item-17", null, CodexApprovalKinds.Command,
            new string('D', 64), "Run tests.", details, true, DateTime.UtcNow.AddMinutes(1));

        var first = await store.AppendCodexApprovalAsync(append);
        var replay = await store.AppendCodexApprovalAsync(append);
        var mismatch = await Assert.ThrowsAsync<AssistantConversationException>(() =>
            store.AppendCodexApprovalAsync(append with { RequestFingerprint = new string('E', 64) }));
        var row = await db.AssistantTurnApprovals.SingleAsync(item => item.Id == first.Id);
        row.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        var expired = await store.ExpireCodexApprovalAsync(first.Id);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal("CODEX_APPROVAL_REQUEST_MISMATCH", mismatch.Code);
        Assert.NotNull(expired);
        Assert.Equal(CodexApprovalStatuses.Expired, expired!.Approval.Status);
        Assert.Equal(CodexApprovalDecisions.Cancel, expired.Decision);
    }

    [Fact]
    public void Approval_protocol_responses_are_one_request_and_permission_grants_are_turn_scoped()
    {
        using var idDocument = JsonDocument.Parse("\"request-7\"");
        using var permissionsDocument = JsonDocument.Parse("""
            {"network":{"enabled":true},"fileSystem":null}
            """);
        var command = CodexAppServerProcessSession.BuildApprovalResponse(
            idDocument.RootElement, CodexApprovalKinds.Command, CodexApprovalDecisions.Accept, default);
        var permission = CodexAppServerProcessSession.BuildApprovalResponse(
            idDocument.RootElement, CodexApprovalKinds.Permissions, CodexApprovalDecisions.Accept,
            permissionsDocument.RootElement);
        var declined = CodexAppServerProcessSession.BuildApprovalResponse(
            idDocument.RootElement, CodexApprovalKinds.Permissions, CodexApprovalDecisions.Decline,
            permissionsDocument.RootElement);

        var commandFrame = CodexAppServerProcessSession.ParseProtocolLine(command, 4096);
        var permissionFrame = CodexAppServerProcessSession.ParseProtocolLine(permission, 4096);
        var declinedFrame = CodexAppServerProcessSession.ParseProtocolLine(declined, 4096);
        Assert.Equal("accept", commandFrame.GetProperty("result").GetProperty("decision").GetString());
        Assert.Equal("turn", permissionFrame.GetProperty("result").GetProperty("scope").GetString());
        Assert.True(permissionFrame.GetProperty("result").GetProperty("permissions")
            .GetProperty("network").GetProperty("enabled").GetBoolean());
        Assert.Empty(declinedFrame.GetProperty("result").GetProperty("permissions").EnumerateObject());
        Assert.DoesNotContain("acceptForSession", command + permission + declined, StringComparison.Ordinal);
    }

    private static CodexProtocolEvent? Normalize(string method, object parameters)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(parameters));
        return CodexAppServerProcessSession.NormalizeNotification(method, document.RootElement);
    }

    private static CodexConversationService Service(
        DantesRoleplayDbContext db, ICodexAppServerFactory factory,
        CodexTurnRegistry registry, CodexBridgeOptions options) =>
        new(new AssistantConversationStore(db, new OperationLog(db)), factory, registry, options);

    private static CodexBridgeOptions Options(int maximumConcurrentTurns = 2) => new(
        "fake-codex", Path.GetFullPath("."), MaximumConcurrentTurns: maximumConcurrentTurns,
        TurnTimeout: TimeSpan.FromSeconds(30));

    private static FakeSession CompletedSession(string threadId, string turnId, string reply) => new(
        threadId, turnId,
        [
            new("delta", Delta: reply[..Math.Min(5, reply.Length)]),
            new("activity", Activity: new($"command-{turnId}", "command", "completed", "rg --files (completed)")),
            new("reply", Reply: reply),
            new("terminal", Status: "completed")
        ]);

    private static async Task<List<CodexConversationEvent>> CollectAsync(
        IAsyncEnumerable<CodexConversationEvent> stream)
    {
        var result = new List<CodexConversationEvent>();
        await foreach (var item in stream) result.Add(item);
        return result;
    }

    private sealed class FakeFactory(IEnumerable<ICodexAppServerSession> sessions) : ICodexAppServerFactory
    {
        private readonly Queue<ICodexAppServerSession> sessions = new(sessions);
        public int CreateCalls { get; private set; }
        public Task<CodexBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CodexBridgeStatus(true, "codex", CodexBridgeVersions.CurrentPinnedVersion,
                CodexBridgeVersions.CurrentPinnedVersion,
                Path.GetFullPath("."), "read-only", false, "", ""));
        public Task<ICodexAppServerSession> CreateAsync(CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(sessions.Dequeue());
        }
    }

    private sealed class FakeSession(
        string externalThreadId, string externalTurnId, IReadOnlyList<CodexProtocolEvent> events)
        : ICodexAppServerSession
    {
        public string? ResumedThreadId { get; private set; }
        public Task<CodexTurnStartResult> StartTurnAsync(
            string? externalThreadIdArgument, string message, CancellationToken cancellationToken = default)
        {
            ResumedThreadId = externalThreadIdArgument;
            return Task.FromResult(new CodexTurnStartResult(
                externalThreadId, externalTurnId, "gpt-5.6-codex", "openai", "inProgress"));
        }
        public async IAsyncEnumerable<CodexProtocolEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var item in events) yield return item;
        }
        public Task RespondApprovalAsync(
            string externalRequestId, string decision, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task InterruptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingSession : ICodexAppServerSession
    {
        private readonly Channel<CodexProtocolEvent> events = Channel.CreateUnbounded<CodexProtocolEvent>();
        public int InterruptCalls { get; private set; }
        public Task<CodexTurnStartResult> StartTurnAsync(
            string? externalThreadId, string message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CodexTurnStartResult(
                externalThreadId ?? "blocking-thread", "blocking-turn", "gpt-5.6-codex", "openai", "inProgress"));
        public IAsyncEnumerable<CodexProtocolEvent> ReadEventsAsync(CancellationToken cancellationToken = default) =>
            events.Reader.ReadAllAsync(cancellationToken);
        public Task RespondApprovalAsync(
            string externalRequestId, string decision, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task InterruptAsync(CancellationToken cancellationToken = default)
        {
            InterruptCalls++;
            events.Writer.TryWrite(new("terminal", Status: "interrupted"));
            events.Writer.TryComplete();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { events.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    private sealed class ThrowingSession : ICodexAppServerSession
    {
        public Task<CodexTurnStartResult> StartTurnAsync(
            string? externalThreadId, string message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CodexTurnStartResult(
                "crash-thread", "crash-turn", "gpt-5.6-codex", "openai", "inProgress"));
        public async IAsyncEnumerable<CodexProtocolEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new CodexBridgeException("CODEX_PROCESS_EXITED", "The fake process exited.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        public Task RespondApprovalAsync(
            string externalRequestId, string decision, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task InterruptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ApprovalSession(string kind, bool canAccept) : ICodexAppServerSession
    {
        private readonly TaskCompletionSource continued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Decisions { get; } = [];

        public Task<CodexTurnStartResult> StartTurnAsync(
            string? externalThreadId, string message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CodexTurnStartResult(
                externalThreadId ?? "approval-thread", "approval-turn", "gpt-5.6-codex", "openai", "inProgress"));

        public async IAsyncEnumerable<CodexProtocolEvent> ReadEventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new("approval", Approval: new(
                "number:71", "approval-item", "", kind, new string('A', 64),
                kind == CodexApprovalKinds.FileChange ? "Change outside repository." : "Run focused tests.",
                new("Testing", kind == CodexApprovalKinds.Command ? "dotnet test" : "", ".",
                    kind == CodexApprovalKinds.FileChange ? ["outside repository"] : [], "", "", []),
                canAccept));
            await continued.Task.WaitAsync(cancellationToken);
            yield return new("approval-resolved", ExternalRequestId: "number:71");
            if (Decisions.Single() == CodexApprovalDecisions.Cancel)
                yield return new("terminal", Status: "interrupted");
            else
            {
                yield return new("reply", Reply: "Approval handled.");
                yield return new("terminal", Status: "completed");
            }
        }

        public Task RespondApprovalAsync(
            string externalRequestId, string decision, CancellationToken cancellationToken = default)
        {
            Assert.Equal("number:71", externalRequestId);
            Decisions.Add(decision);
            return Task.CompletedTask;
        }

        public Task InterruptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Continue() => continued.TrySetResult();
        public ValueTask DisposeAsync() { continued.TrySetCanceled(); return ValueTask.CompletedTask; }
    }
}
