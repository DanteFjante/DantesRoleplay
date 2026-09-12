using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Tests;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.CodexBridge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks.Persistence;
using System.Text.Json;

namespace DantesRoleplay.Play.Tests;

public sealed class ConversationMemoryTests : IDisposable
{
    private const string VerifiedPrincipal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
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
        var restartedRetry = store.AppendTurn(append with { CapturedAtUtc = CapturedAt.AddMinutes(5) });

        Assert.False(applied.Replayed);
        Assert.True(replay.Replayed);
        Assert.True(restartedRetry.Replayed);
        Assert.Equal(applied.ReceiptId, replay.ReceiptId);
        Assert.Equal(applied.ReceiptId, restartedRetry.ReceiptId);
        Assert.All(restartedRetry.Messages, value => Assert.Equal(CapturedAt, value.CapturedAtUtc));
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
        Assert.Equal("CONVERSATION_MEMORY_NOT_CONNECTED",
            Assert.Throws<ConversationMemoryException>(() => store.Retry(binding.Scope)).Code);
        store.Connect(binding);
        store.MarkRetryPending(binding.Scope, "CODEX_READ_FAILED");
        Assert.Equal(ConversationMemoryStatuses.RetryPending, store.GetState(binding.Scope)!.Status);
        store.Retry(binding.Scope);
        Assert.Equal(ConversationMemoryStatuses.Connected, store.GetState(binding.Scope)!.Status);
        store.Archive(binding.Scope);
        Assert.Null(store.GetState(binding.Scope));
        Assert.Equal(ConversationMemoryStatuses.Archived, store.GetState(binding.Scope, true)!.Status);
    }

    [Fact]
    public void Message_page_revision_and_rows_come_from_one_sqlite_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), "conversation-memory-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = $"Data Source={path};Pooling=False";
        try
        {
            var plain = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connectionString).Options;
            using (var setup = new DantesRoleplayDbContext(plain))
            {
                setup.Database.EnsureCreated();
                setup.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                RegisterStateSpace(setup);
                var store = new ApplicationConversationMemoryStore(setup);
                store.Connect(Binding());
                store.AppendTurn(Turn(Binding(), "turn.snapshot.1", "request.snapshot.1"));
            }

            using var writerDb = new DantesRoleplayDbContext(plain);
            var writer = new ApplicationConversationMemoryStore(writerDb);
            var interceptor = new AfterJournalRead(() => writer.AppendTurn(
                Turn(Binding(), "turn.snapshot.2", "request.snapshot.2",
                    Message("item.user.snapshot.2", ConversationMemoryRoles.User,
                        ConversationMemoryMessageKinds.UserPrompt, "Later prompt."),
                    Message("item.assistant.snapshot.2", ConversationMemoryRoles.Assistant,
                        ConversationMemoryMessageKinds.AssistantFinal, "Later answer."))));
            var reading = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite(connectionString).AddInterceptors(interceptor).Options;
            using var readerDb = new DantesRoleplayDbContext(reading);

            var page = new ApplicationConversationMemoryStore(readerDb)
                .GetMessages(Binding().Scope, null, 8);

            Assert.True(interceptor.Fired);
            Assert.Equal(2, page.JournalRevision);
            Assert.Equal(2, page.Messages.Count);
            using var verify = new DantesRoleplayDbContext(plain);
            Assert.Equal(3, new ApplicationConversationMemoryStore(verify).GetState(Binding().Scope)!.Revision);
            Assert.Equal(4, verify.Set<ApplicationConversationMemoryMessageRecord>().Count());
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
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
        Assert.Equal("CONVERSATION_MEMORY_ARCHIVED", Assert.Throws<ConversationMemoryException>(() =>
            store.GetDerivedCandidates(binding.Scope)).Code);
        Assert.Single(store.GetDerivedCandidates(binding.Scope, includeArchived: true));
        Assert.Equal("CONVERSATION_MEMORY_ARCHIVED", Assert.Throws<ConversationMemoryException>(() =>
            store.AppendDerivedCandidate(new(binding.Scope,
                new("task.dream.2", "command.dream.2"), captured.Journal.Revision,
                captured.Messages.Select(value => value.SourceMessageId).ToArray(),
                "{\"summary\":\"Late candidate.\"}", new string('A', 64),
                "inner-result.dream.2", CapturedAt.AddMinutes(1)))).Code);
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

    [Fact]
    public async Task Dream_source_tool_reads_only_exact_current_revision_and_message_ids()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var binding = Binding() with { Scope = Binding().Scope with { PrincipalId = VerifiedPrincipal } };
        store.Connect(binding);
        var captured = store.AppendTurn(Turn(binding, "turn.1", "request.1"));
        var handler = new ConversationMemorySystemCapabilityHandler(store);
        var context = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.VerifiedPrincipal(VerifiedPrincipal, "test"), "inner.procedure", "dream-test")
        {
            ApplicationId = ApplicationIdentifier.Parse("play-fixture"),
            StateSpaceId = "play-fixture-space"
        };
        var input = JsonSerializer.SerializeToElement(new
        {
            sessionContextId = "session.fixture",
            sourceRevision = captured.Journal.Revision,
            sourceMessageIds = captured.Messages.Select(value => value.SourceMessageId).ToArray()
        });

        var result = await handler.ReadAsync(input, context);
        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(2, result.Data!.Value.GetProperty("messages").GetArrayLength());
        var catalog = new SystemCapabilityCatalog([handler], new BoundedJsonSchemaValidator(),
            new PrivateOperatorAuthorizationPolicy());
        var catalogContext = context with
        {
            Scope = PrivateOperatorAuthorizationPolicy.PrivateHostScope
        };
        var validated = await catalog.ReadAsync(SystemCapabilityIds.ConversationMemory,
            input.GetRawText(), catalogContext);
        Assert.True(validated.Ok, validated.Error?.Code + ": " + validated.Error?.Message);
        var message = validated.Data!.Value.GetProperty("messages")[0];
        Assert.True(message.TryGetProperty("sourceMessageId", out _));
        Assert.False(message.TryGetProperty("SourceMessageId", out _));
        store.Disconnect(binding.Scope);
        var stale = await handler.ReadAsync(input, context);
        Assert.False(stale.Ok);
        Assert.Equal("CONVERSATION_MEMORY_SOURCE_STALE", stale.Error?.Code);
    }

    [Fact]
    public void Dream_procedure_selects_only_the_bounded_conversation_memory_read_capability()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var applications = new SqliteApplicationRegistry(db);
        var application = applications.Get(ApplicationIdentifier.Parse("play-fixture"))!;
        var principal = TrustedPrincipalContext.VerifiedPrincipal(VerifiedPrincipal, "test");
        var host = new InteractionInvocationHost(principal, application, "play-fixture-space", "grant@1",
            "dream.command", new string('B', 64), InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));
        var request = new SystemInnerWorkerRequest(host,
            new("play-fixture.procedure.conversation-dream", 1, new string('A', 64)),
            "{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"Consolidate the selected messages.\"}",
            "{\"type\":\"object\",\"additionalProperties\":false}");
        var catalog = new SystemCapabilityCatalog(
            [new ConversationMemorySystemCapabilityHandler(new ApplicationConversationMemoryStore(db))],
            new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy());
        var governed = SystemInnerWorkerGovernedReferences.Parse(
                "system capability system.conversation-memory")
            .Where(value => value.Kind == SystemInnerWorkerGovernedReferenceKind.SystemCapability)
            .Select(value => value.QualifiedId).ToArray();
        var selection = new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
            new("web.inner", "Inner AI", "Perform only the host-selected conversation dream procedure.")
        ]), catalog).Resolve(request,
            new(principal, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "dream-policy"),
            DateTime.UtcNow, governedSystemCapabilities: governed);

        var tool = Assert.Single(selection.ToolBindings);
        Assert.Equal(SystemCapabilityIds.ConversationMemory, tool.CapabilityVersion.ExactDefinitionId);
        Assert.Equal(SystemCapabilityMode.Read, tool.Mode);
        Assert.Equal("system_conversation-memory", tool.Definition.Name);

        var wrongProcedure = new SystemInnerWorkerRequest(host,
            new("play-fixture.procedure.other", 1, new string('A', 64)),
            request.InputJson, request.ResultSchemaJson);
        var wrongProcedureError = Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
                new("web.inner", "Inner AI", "Perform only the host-selected procedure.")
            ]), catalog).Resolve(wrongProcedure,
                new(principal, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "dream-policy-denied"),
                DateTime.UtcNow, governedSystemCapabilities: governed));
        Assert.Equal("INNER_WORKER_SYSTEM_CAPABILITY_NOT_CONFIGURED", wrongProcedureError.Code);

        var wrongApplicationHost = new InteractionInvocationHost(principal,
            new ApplicationRevision(ApplicationIdentifier.Parse("other-fixture"), application.Revision,
                application.Fingerprint, []), "play-fixture-space", "grant@1", "dream.other-application",
            new string('B', 64), InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));
        var wrongApplication = new SystemInnerWorkerRequest(wrongApplicationHost,
            request.ProcedureVersion!, request.InputJson, request.ResultSchemaJson);
        var wrongApplicationError = Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
                new("web.inner", "Inner AI", "Perform only the host-selected procedure.")
            ]), catalog).Resolve(wrongApplication,
                new(principal, PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                    "dream-policy-wrong-application"), DateTime.UtcNow,
                governedSystemCapabilities: governed));
        Assert.Equal("INNER_WORKER_SYSTEM_CAPABILITY_NOT_CONFIGURED", wrongApplicationError.Code);
    }

    [Fact]
    public async Task Dream_recorder_uses_authorized_terminal_task_output_and_owner_evidence()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var store = new ApplicationConversationMemoryStore(db);
        var binding = Binding() with { Scope = Binding().Scope with { PrincipalId = VerifiedPrincipal } };
        store.Connect(binding);
        var captured = store.AppendTurn(Turn(binding, "turn.dream", "request.dream"));
        var candidate = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            sourceRevision = captured.Journal.Revision,
            sourceMessageIds = captured.Messages.Select(value => value.SourceMessageId).ToArray(),
            summary = "A retained possible recollection."
        }));
        var schemaFingerprint = new string('C', 64);
        var admission = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            innerWorker = new { OutputSchemaFingerprint = schemaFingerprint }
        }));
        var task = new SystemTaskLifecycleRecord
        {
            TaskId = "task.dream.completed", CommandId = "command.dream.completed",
            PayloadFingerprint = new string('D', 64), RootTaskId = "task.dream.completed",
            State = "completed", Purpose = "procedure-workflow", PrincipalReference = VerifiedPrincipal,
            AuthenticationMethod = "test", ApplicationId = "play-fixture", ApplicationRevision = 1,
            ApplicationFingerprint = new string('E', 64), AdmissionPayloadJson = admission,
            ActivationRevision = 1, ActivationFingerprint = new string('1', 64),
            ActivationApplicationRevision = 1, ActivationApplicationFingerprint = new string('E', 64),
            BaseApplicationsJson = "[]", StateSpaceId = "play-fixture-space", GrantReference = "grant@1",
            StateRevision = new string('F', 64), ExecutionProfile = "workflow", AdmittedOperations = 1,
            DeadlineUtc = "2026-09-12T19:00:00.0000000Z", DefinitionId = "play-fixture.procedure.conversation-dream",
            DefinitionVersion = 1, DefinitionFingerprint = new string('A', 64), InputJson = "{}",
            ResultJson = candidate, CompletionEvidenceReference = "inner-result.dream.completed",
            CreatedAtUtc = "2026-09-12T18:00:00.0000000Z", UpdatedAtUtc = "2026-09-12T18:01:00.0000000Z",
            CompletedAtUtc = "2026-09-12T18:01:00.0000000Z"
        };
        db.Set<SystemTaskRootBudgetRecord>().Add(new()
        {
            RootTaskId = task.RootTaskId, MaximumOperations = 1, ConsumedOperations = 1
        });
        db.Set<SystemTaskLifecycleRecord>().Add(task);
        db.SaveChanges();
        var recorder = new ConversationMemoryDreamRecorder(db, store,
            new CompletedWorkerGateway(candidate, task.CompletionEvidenceReference),
            new SequenceTimeProvider(CapturedAt.AddMinutes(2)));

        var retained = await recorder.RetainCompletedAsync(
            TrustedPrincipalContext.VerifiedPrincipal(VerifiedPrincipal, "test"),
            new(binding.Scope, new(task.TaskId, task.CommandId), captured.Journal.Revision,
                captured.Messages.Select(value => value.SourceMessageId).ToArray(), "derive.request.1"),
            "derive.request.1");

        Assert.Equal(candidate, retained.CandidateJson);
        Assert.Equal(schemaFingerprint, retained.ResultSchemaFingerprint);
        Assert.Equal(task.CompletionEvidenceReference, retained.CompletionEvidenceReference);
        Assert.Equal("private", retained.Audience);
    }

    [Fact]
    public async Task Codex_capture_requeues_a_failed_journal_append_then_acknowledges_the_durable_replay()
    {
        using var db = fixture.CreateContext();
        RegisterStateSpace(db);
        var inner = new ApplicationConversationMemoryStore(db);
        var failing = new FailFirstAppendStore(inner);
        var spool = new CodexCaptureCorrelationSpool();
        var captureBinding = new CodexGameplayCaptureBinding(
            "C:\\repo\\fixture", "project.fixture", "session.fixture", "thread.fixture");
        var adapter = new CodexCaptureAdapter(new CaptureReadClient(), captureBinding, spool);
        var service = new ConversationMemoryCodexCaptureService(
            failing, Binding(), adapter, captureBinding,
            new SequenceTimeProvider(CapturedAt, CapturedAt.AddMinutes(5)));
        service.Connect();
        Assert.True(service.TryAcceptHook(new("Stop", "thread.fixture", "C:\\repo\\fixture",
            "C:\\ignored.jsonl", "turn.capture", "")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureNextAsync());
        Assert.Equal(1, spool.Count);
        var captured = await service.CaptureNextAsync();

        Assert.NotNull(captured);
        Assert.False(captured.Replayed);
        Assert.Equal(0, spool.Count);
        Assert.Equal(["item.user", "item.assistant"],
            inner.GetMessages(Binding().Scope, null, 8).Messages.Select(value => value.SourceMessageId));
        Assert.NotEqual(Binding().Scope.SessionContextId, captureBinding.ExternalCodexThreadId);
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

    private sealed class CaptureReadClient : ICodexThreadReadClient
    {
        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CodexCaptureVersions.SupportedCliVersion);

        public Task<JsonElement> ReadThreadAsync(
            string threadId, bool includeTurns, CancellationToken cancellationToken = default) =>
            Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                thread = new
                {
                    id = threadId,
                    turns = new[]
                    {
                        new
                        {
                            id = "turn.capture", status = "completed",
                            items = new object[]
                            {
                                new { id = "item.user", type = "userMessage", content = new[] { new { type = "text", text = "Prompt." } }, visibility = "visible" },
                                new { id = "item.assistant", type = "agentMessage", text = "Answer.", phase = "final_answer", visibility = "visible" }
                            }
                        }
                    }
                }
            }));
    }

    private sealed class FailFirstAppendStore(IConversationMemoryStore inner) : IConversationMemoryStore
    {
        private bool failed;
        public ConversationMemoryJournalDocument Connect(ConversationMemoryBinding binding) => inner.Connect(binding);
        public ConversationMemoryAppendResult AppendTurn(ConversationMemoryTurnAppend append)
        {
            if (!failed) { failed = true; throw new InvalidOperationException("Injected journal failure."); }
            return inner.AppendTurn(append);
        }
        public ConversationMemoryJournalDocument MarkRetryPending(ConversationMemoryScope scope, string failureCode) => inner.MarkRetryPending(scope, failureCode);
        public ConversationMemoryJournalDocument? GetState(ConversationMemoryScope scope, bool includeArchived = false) => inner.GetState(scope, includeArchived);
        public ConversationMemoryMessagePage GetMessages(ConversationMemoryScope scope, int? beforeOrdinal, int limit, bool includeArchived = false) => inner.GetMessages(scope, beforeOrdinal, limit, includeArchived);
        public IReadOnlyList<ConversationMemoryMessageDocument> GetSourceMessages(ConversationMemoryScope scope, int sourceRevision, IReadOnlyList<string> sourceMessageIds) => inner.GetSourceMessages(scope, sourceRevision, sourceMessageIds);
        public ConversationMemoryJournalDocument Retry(ConversationMemoryScope scope) => inner.Retry(scope);
        public ConversationMemoryJournalDocument Disconnect(ConversationMemoryScope scope) => inner.Disconnect(scope);
        public ConversationMemoryJournalDocument Archive(ConversationMemoryScope scope) => inner.Archive(scope);
        public ConversationMemoryDeleteResult Delete(ConversationMemoryScope scope, string? messageId = null) => inner.Delete(scope, messageId);
        public ConversationMemoryDerivedCandidateDocument AppendDerivedCandidate(ConversationMemoryDerivedCandidateAppend append) => inner.AppendDerivedCandidate(append);
        public IReadOnlyList<ConversationMemoryDerivedCandidateDocument> GetDerivedCandidates(ConversationMemoryScope scope, int limit = 20, bool includeArchived = false) => inner.GetDerivedCandidates(scope, limit, includeArchived);
    }

    private sealed class SequenceTimeProvider(params DateTime[] values) : TimeProvider
    {
        private int index;
        public override DateTimeOffset GetUtcNow() => new(values[Math.Min(index++, values.Length - 1)], TimeSpan.Zero);
    }

    private sealed class AfterJournalRead(Action callback) : DbCommandInterceptor
    {
        private int fired;
        public bool Fired => fired != 0;

        public override InterceptionResult DataReaderDisposing(
            DbCommand command,
            DataReaderDisposingEventData eventData,
            InterceptionResult result)
        {
            if (command.CommandText.Contains("application_conversation_memory_journal", StringComparison.Ordinal)
                && Interlocked.Exchange(ref fired, 1) == 0)
                callback();
            return result;
        }
    }

    private sealed class CompletedWorkerGateway(string dataJson, string evidence)
        : IApplicationCandidateCapabilityGateway
    {
        public ApplicationCandidateCapabilityDiscoveryResult Discover(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId, string correlationId) =>
            throw new NotSupportedException();

        public Task<ApplicationCandidateCapabilityInvocationResult> InvokeAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId, string capabilityId,
            string inputJson, string? idempotencyKey, string correlationId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(SystemCapabilityIds.InnerWorkerRead, capabilityId);
            return Task.FromResult(new ApplicationCandidateCapabilityInvocationResult(
                true, capabilityId, "read", JsonSerializer.SerializeToElement(new
                {
                    tag = "completed", dataJson, completionEvidenceReference = evidence
                }), "", "", null));
        }
    }

    public void Dispose() => fixture.Dispose();
}
