using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.MCPServer;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Operations;
using DantesRoleplay.Play;
using DantesRoleplay.Tests;

namespace DantesRoleplay.McpProtocol.Tests;

public sealed class ConversationMemoryProtocolTests : IDisposable
{
    private const string VerifiedPrincipal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly SqliteFixture fixture = new();

    [Fact]
    public async Task Existing_query_and_commit_verbs_enforce_host_scope_and_preserve_visible_batch()
    {
        await using var db = fixture.CreateContext();
        var stateSpaces = RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var authorization = new AllowingAuthorizer();
        var seat = new Seat(VerifiedPrincipal, "play-fixture");
        var hostBinding = HostBinding();
        var log = new OperationLog(db);
        var connect = JsonSerializer.Serialize(new
        {
            operation = "connect",
            applicationId = "play-fixture",
            stateSpaceId = "play-fixture-space",
            sessionContextId = "session.fixture",
            sourceProjectId = "project.fixture",
            repositoryRoot = "C:\\repo\\fixture",
            sourceThreadId = "thread.fixture",
            requestToken = "connect.fixture"
        });

        var connected = await new CommitMcpTool().CommitAsync(log, "system.conversation-memory", connect,
            privateOperator: authorization, applicationStateSpaces: stateSpaces,
            conversationMemory: store, conversationMemoryHostBinding: hostBinding,
            localKnowledgeSeats: seat);
        Assert.True(connected.Ok, connected.Error?.Why);
        var append = JsonSerializer.Serialize(new
        {
            operation = "append",
            applicationId = "play-fixture",
            stateSpaceId = "play-fixture-space",
            sessionContextId = "session.fixture",
            sourceProjectId = "project.fixture",
            repositoryRoot = "C:\\repo\\fixture",
            sourceThreadId = "thread.fixture",
            sourceTurnId = "turn.fixture",
            messages = new object[]
            {
                new { sourceMessageId = "item.user", role = "user", sourceKind = "user-prompt", text = "Prompt.", sourceAtUtc = "2026-09-12T18:00:00Z" },
                new { sourceMessageId = "item.commentary", role = "assistant", sourceKind = "assistant-commentary", text = "Visible progress.", sourceAtUtc = "2026-09-12T18:00:01Z" },
                new { sourceMessageId = "item.final", role = "assistant", sourceKind = "assistant-final", text = "Answer.", sourceAtUtc = "2026-09-12T18:00:02Z" }
            },
            captureProvenance = "codex-app-server/thread-read@0.153.4",
            capturedAtUtc = "2026-09-12T18:00:03Z",
            requestToken = "capture.fixture"
        });
        var captured = await new CommitMcpTool().CommitAsync(log, "system.conversation-memory", append,
            privateOperator: authorization, applicationStateSpaces: stateSpaces,
            conversationMemory: store, conversationMemoryHostBinding: hostBinding,
            localKnowledgeSeats: seat);
        Assert.True(captured.Ok, captured.Error?.Why);
        var queryRequest = "{\"operation\":\"messages\",\"sessionContextId\":\"session.fixture\",\"limit\":16}";
        var read = await new QueryMcpTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: log, notifications: null!,
            kind: "system.conversation-memory", applicationId: "play-fixture",
            stateSpaceId: "play-fixture-space", request: queryRequest,
            privateOperator: authorization, localKnowledgeSeats: seat,
            applicationEntityStateSpaces: stateSpaces, conversationMemory: store,
            conversationMemoryHostBinding: hostBinding);

        Assert.True(read.Ok, read.Error?.Why);
        var data = JsonSerializer.SerializeToElement(read.Data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(3, data.GetProperty("messages").GetArrayLength());
        Assert.Equal("Visible progress.", data.GetProperty("messages")[1].GetProperty("text").GetString());
        Assert.Equal("assistant-commentary", data.GetProperty("messages")[1].GetProperty("sourceKind").GetString());
        var dreamRecorder = new RecordingDreamRecorder();
        var derive = JsonSerializer.Serialize(new
        {
            operation = "derive", applicationId = "play-fixture", stateSpaceId = "play-fixture-space",
            sessionContextId = "session.fixture", sourceProjectId = "project.fixture",
            repositoryRoot = "C:\\repo\\fixture", sourceThreadId = "thread.fixture",
            taskId = "task.fixture", commandId = "command.fixture", sourceRevision = 2,
            sourceMessageIds = new[] { "item.user", "item.commentary", "item.final" },
            requestToken = "derive.fixture"
        });
        var derived = await new CommitMcpTool().CommitAsync(log, "system.conversation-memory", derive,
            privateOperator: authorization, applicationStateSpaces: stateSpaces,
            conversationMemory: store, conversationMemoryDreams: dreamRecorder,
            conversationMemoryHostBinding: hostBinding, localKnowledgeSeats: seat);
        Assert.True(derived.Ok, derived.Error?.Why);
        Assert.Equal(VerifiedPrincipal, dreamRecorder.Principal?.PrincipalId);
        Assert.Equal("session.fixture", dreamRecorder.Request?.Scope.SessionContextId);
        var wrong = await new QueryMcpTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: log, notifications: null!,
            kind: "system.conversation-memory", applicationId: "other",
            stateSpaceId: "play-fixture-space", request: queryRequest,
            privateOperator: authorization, localKnowledgeSeats: seat,
            applicationEntityStateSpaces: stateSpaces, conversationMemory: store,
            conversationMemoryHostBinding: hostBinding);
        Assert.False(wrong.Ok);
        Assert.Equal("CONVERSATION_MEMORY_SCOPE_DENIED", wrong.Error?.Code);

        var wrongSession = await new QueryMcpTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: log, notifications: null!,
            kind: "system.conversation-memory", applicationId: "play-fixture",
            stateSpaceId: "play-fixture-space",
            request: "{\"operation\":\"messages\",\"sessionContextId\":\"session.other\",\"limit\":16}",
            privateOperator: authorization, localKnowledgeSeats: seat,
            applicationEntityStateSpaces: stateSpaces, conversationMemory: store,
            conversationMemoryHostBinding: hostBinding);
        Assert.False(wrongSession.Ok);
        Assert.Equal("CONVERSATION_MEMORY_SCOPE_DENIED", wrongSession.Error?.Code);
        Assert.Equal(PrivateOperatorCapability.Read, authorization.LastCapability);
    }

    [Fact]
    public async Task Denied_commit_does_not_parse_or_touch_the_memory_store()
    {
        await using var db = fixture.CreateContext();
        var denied = new DenyingAuthorizer();
        var result = await new CommitMcpTool().CommitAsync(new OperationLog(db),
            "system.conversation-memory", "not-json", privateOperator: denied,
            conversationMemory: new ThrowingStore(), localKnowledgeSeats: new Seat("principal.fixture", "play-fixture"));

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.Equal(PrivateOperatorCapability.Modify, denied.LastCapability);
    }

    private static SqliteStateSpaceRegistry RegisterStateSpace(DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(ApplicationIdentifier.Parse("play-fixture"), "Play Fixture", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        spaces.Create(new("play-fixture-space", revision, new string('A', 64)));
        return spaces;
    }

    private static ConversationMemoryHostBinding HostBinding() => new(new(
        new(VerifiedPrincipal, "play-fixture", "play-fixture-space", "session.fixture"),
        "codex", "project.fixture", "C:\\repo\\fixture", "thread.fixture"));

    private sealed class Seat(string principalId, string applicationId) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => new(true, principalId, applicationId,
            "campaign.fixture", null, DantesRoleplay.Knowledge.KnowledgeAudienceRole.GameMaster);
    }

    private sealed class AllowingAuthorizer : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorCapability? LastCapability { get; private set; }
        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
        {
            LastCapability = capability;
            return new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                PrivateOperatorPrincipal.Create("memory-test", "operator"), capability,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "memory-test"));
        }
    }

    private sealed class DenyingAuthorizer : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorCapability? LastCapability { get; private set; }
        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
        {
            LastCapability = capability;
            return new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                TrustedPrincipalContext.Unauthenticated("PRIVATE_OPERATOR_UNAUTHENTICATED"), capability,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "memory-test"));
        }
    }

    private sealed class ThrowingStore : IConversationMemoryStore
    {
        private static Exception Touched() => new InvalidOperationException("Memory store must not be touched.");
        public ConversationMemoryJournalDocument Connect(ConversationMemoryBinding binding) => throw Touched();
        public ConversationMemoryJournalDocument? GetState(ConversationMemoryScope scope, bool includeArchived = false) => throw Touched();
        public ConversationMemoryAppendResult AppendTurn(ConversationMemoryTurnAppend append) => throw Touched();
        public ConversationMemoryMessagePage GetMessages(ConversationMemoryScope scope, int? beforeOrdinal, int limit, bool includeArchived = false) => throw Touched();
        public IReadOnlyList<ConversationMemoryMessageDocument> GetSourceMessages(ConversationMemoryScope scope, int sourceRevision, IReadOnlyList<string> sourceMessageIds) => throw Touched();
        public ConversationMemoryJournalDocument MarkRetryPending(ConversationMemoryScope scope, string failureCode) => throw Touched();
        public ConversationMemoryJournalDocument Retry(ConversationMemoryScope scope) => throw Touched();
        public ConversationMemoryJournalDocument Disconnect(ConversationMemoryScope scope) => throw Touched();
        public ConversationMemoryJournalDocument Archive(ConversationMemoryScope scope) => throw Touched();
        public ConversationMemoryDeleteResult Delete(ConversationMemoryScope scope, string? messageId = null) => throw Touched();
        public ConversationMemoryDerivedCandidateDocument AppendDerivedCandidate(ConversationMemoryDerivedCandidateAppend append) => throw Touched();
        public IReadOnlyList<ConversationMemoryDerivedCandidateDocument> GetDerivedCandidates(ConversationMemoryScope scope, int limit = 20, bool includeArchived = false) => throw Touched();
    }

    private sealed class RecordingDreamRecorder : IConversationMemoryDreamRecorder
    {
        public TrustedPrincipalContext? Principal { get; private set; }
        public ConversationMemoryDreamRecordRequest? Request { get; private set; }

        public Task<ConversationMemoryDerivedCandidateDocument> RetainCompletedAsync(
            TrustedPrincipalContext principal, ConversationMemoryDreamRecordRequest request,
            string correlationId, CancellationToken cancellationToken = default)
        {
            Principal = principal;
            Request = request;
            return Task.FromResult(new ConversationMemoryDerivedCandidateDocument(
                "derived.fixture", request.Task, request.SourceRevision, request.SourceMessageIds,
                new string('A', 64), "{}", new string('B', 64), "evidence.fixture", "private",
                "candidate", DateTime.UnixEpoch));
        }
    }

    public void Dispose() => fixture.Dispose();
}
