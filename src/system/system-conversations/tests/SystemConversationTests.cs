using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Assistants;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Retrieval;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemConversations;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.SystemConversations.Tests;

public sealed class SystemConversationTests
{
    private const string Operator =
        "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Reference =
        "capability:system.applications@1#AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task System_turn_is_scope_isolated_context_bound_read_only_and_replayed()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var provider = new FakeProvider(Response(Reference));
        var context = new FakeContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var service = Service(store, provider, context);
        var request = new SystemConversationCreate(
            "What applications are registered?", "system-request.one");

        var first = await service.CreateAsync(RequestContext(), request);
        var replay = await service.CreateAsync(RequestContext(), request);
        var advisoryRead = await store.GetAsync(
            Operator, first.Summary.Id, scope: AssistantConversationScopes.Advisory);

        Assert.Equal(first.Summary.Id, replay.Summary.Id);
        Assert.Equal(AssistantConversationScopes.System, first.Summary.Scope);
        Assert.Null(advisoryRead);
        Assert.Equal(["user", "assistant"], first.Messages.Select(value => value.Role));
        var receipt = Assert.Single(first.Turns).Context!;
        Assert.Equal(AssistantTurnContextProfiles.SystemReadV1, receipt.Profile);
        Assert.Equal(new string('B', 64), receipt.Fingerprint);
        Assert.Equal(AssistantTurnResponseDispositions.Answered, receipt.Disposition);
        Assert.Equal([Reference], receipt.SourceReferences);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, context.Calls);
        Assert.Single(await db.Operations.Where(value =>
            value.Tool == "control.assistant.local-message").ToListAsync());
        Assert.Empty(await db.Entities.ToListAsync());
        Assert.Empty(await db.Components.ToListAsync());
        Assert.Empty(await db.HostSettingOverrides.ToListAsync());
        Assert.Empty(await db.Events.ToListAsync());
    }

    [Fact]
    public async Task Unauthorized_request_and_invented_evidence_fail_before_claim_or_assistant_message()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var deniedProvider = new FakeProvider(Response(Reference));
        var deniedContext = new FakeContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var denied = Service(store, deniedProvider, deniedContext);

        var exception = await Assert.ThrowsAsync<SystemConversationException>(() => denied.CreateAsync(
            new(
                TrustedPrincipalContext.Unauthenticated("TEST_UNAUTHENTICATED"),
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                "system-chat-test"),
            new("Tell me about the system", "system-request.denied")));

        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", exception.Code);
        Assert.Empty(await db.AssistantConversations.ToListAsync());
        Assert.Equal(0, deniedProvider.Calls);
        Assert.Equal(0, deniedContext.Calls);

        var invalidProvider = new FakeProvider(Response("capability:system.invented@1#" + new string('C', 64)));
        var invalid = Service(store, invalidProvider, new FakeContext());
        var result = await invalid.CreateAsync(RequestContext(), new(
            "Invent a citation", "system-request.invalid-evidence"));

        var turn = Assert.Single(result.Turns);
        Assert.Equal(AssistantConversationStatuses.Failed, turn.Status);
        Assert.Equal("SYSTEM_CHAT_EVIDENCE_INVALID", turn.ErrorCode);
        Assert.Null(turn.Context);
        Assert.Single(result.Messages);
        Assert.Equal("user", result.Messages[0].Role);
    }

    [Fact]
    public async Task Materializer_uses_only_descriptors_active_system_procedures_and_application_metadata()
    {
        var applications = new InMemoryApplicationRegistry();
        applications.Register(new(
            ApplicationIdentifier.Parse("fixture-app"), "Fixture", "System-visible metadata", []));
        var validator = new BoundedJsonSchemaValidator();
        var catalog = new SystemCapabilityCatalog(
            [new ApplicationsSystemCapabilityHandler(applications), new IrrelevantCapabilityHandler()],
            validator,
            new PrivateOperatorAuthorizationPolicy());
        var materializer = new SystemConversationContextMaterializer(catalog, new EmptyProcedureStore());

        var snapshot = await materializer.MaterializeAsync(
            "Which applications exist?", RequestContext());

        Assert.Equal(AssistantTurnContextProfiles.SystemReadV1, snapshot.Profile);
        Assert.Equal(64, snapshot.Fingerprint.Length);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(snapshot.Json) <=
            SystemConversationContextMaterializer.MaximumContextBytes);
        using var document = JsonDocument.Parse(snapshot.Json);
        var root = document.RootElement;
        Assert.Equal(SystemCapabilityIds.Applications,
            Assert.Single(root.GetProperty("capabilities").EnumerateArray())
                .GetProperty("id").GetString());
        Assert.Equal("fixture-app",
            Assert.Single(root.GetProperty("applications").EnumerateArray())
                .GetProperty("id").GetString());
        Assert.Empty(root.GetProperty("procedures").EnumerateArray());
        Assert.Equal(snapshot.SourceReferences,
            root.GetProperty("evidenceReferences").EnumerateArray()
                .Select(value => value.GetString()!).ToArray());
        Assert.False(root.TryGetProperty("stateSpaces", out _));
        Assert.DoesNotContain("entities", snapshot.Json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(snapshot.SourceReferences,
            value => value.StartsWith("capability:system.applications@1#", StringComparison.Ordinal));
        Assert.Contains(snapshot.SourceReferences,
            value => value.StartsWith("application:fixture-app@1#", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migration_defaults_existing_conversations_to_advisory_and_has_no_pending_model_changes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"system-chat-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={path}").Options;
            await using (var db = new DantesRoleplayDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20260825190327_TriggerSchedulingPhoneCompanion");
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO assistant_conversation
                    (Id, OperatorId, Provider, Title, Revision, Status, ExternalThreadId, CreatedAtUtc, UpdatedAtUtc)
                    VALUES
                    ('conversation.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                     'principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                     'local', 'Legacy advisory', 1, 'completed', NULL,
                     '2026-08-25 20:00:00', '2026-08-25 20:00:00')
                    """);
                await migrator.MigrateAsync();
            }
            await using (var db = new DantesRoleplayDbContext(options))
            {
                var row = await db.AssistantConversations.AsNoTracking().SingleAsync();
                Assert.Equal(AssistantConversationScopes.Advisory, row.Scope);
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static SystemConversationService Service(
        IAssistantConversationStore store,
        ILocalStructuredCompletionProvider provider,
        ISystemConversationContextMaterializer context) =>
        new(store, provider, context, new PrivateOperatorAuthorizationPolicy(),
            new BoundedJsonSchemaValidator());

    private static SystemConversationRequestContext RequestContext() => new(
        TrustedPrincipalContext.VerifiedPrincipal(Operator, "test"),
        PrivateOperatorAuthorizationPolicy.PrivateHostScope,
        "system-chat-test");

    private static StructuredCompletionResult Response(string reference) => new(
        new("ollama", "fixture-model", "fixture-revision", "standard"),
        JsonSerializer.Serialize(new
        {
            disposition = "answered",
            reply = "The fixture application is registered.",
            evidence = new[] { reference }
        }),
        4, 10, 6);

    private sealed class FakeProvider(StructuredCompletionResult result)
        : ILocalStructuredCompletionProvider
    {
        public int Calls { get; private set; }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelStatus(true, result.Identity));
        public Task<StructuredCompletionResult> CompleteAsync(
            StructuredCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(SystemConversationService.TaskClass, request.TaskClass);
            Assert.Contains("evidenceReferences array; never construct or alter a reference",
                request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("SYSTEM CONTEXT", request.UserPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("tools", request.ResponseSchema, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeContext : ISystemConversationContextMaterializer
    {
        public int Calls { get; private set; }
        public Task<SystemConversationContextSnapshot> MaterializeAsync(
            string query,
            SystemConversationRequestContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SystemConversationContextSnapshot(
                AssistantTurnContextProfiles.SystemReadV1,
                "{\"profile\":\"system-read-v1\"}",
                new string('B', 64),
                [Reference]));
        }
    }

    private sealed class EmptyProcedureStore : IProcedureStore
    {
        public Task<IReadOnlyList<ProcedureSummary>> FindAsync(
            string? query = null, string? category = null, bool includeInactive = false,
            int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureSummary>>([]);
        public Task<ProcedureDetail?> GetAsync(
            string id, int? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProcedureDetail?>(null);
        public Task<WriteProcedureResult> WriteAsync(
            WriteProcedureRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ProcedureSummary>> GetVersionsAsync(
            string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureSummary>>([]);
        public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<IReadOnlyList<ProcedureCategoryCount>> GetCategoriesAsync(
            bool includeInactive = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureCategoryCount>>([]);
        public Task<IReadOnlyList<WriteCheck>> CheckAsync(
            WriteProcedureRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WriteCheck>>([]);
    }

    private sealed class IrrelevantCapabilityHandler : ISystemReadCapabilityHandler
    {
        public SystemCapabilityRegistration Registration { get; } = new(
            "system.unrelated",
            1,
            "fixture-owner",
            "Inspect unrelated diagnostics.",
            SystemCapabilityMode.Read,
            """{"type":"object","additionalProperties":false}""",
            """{"type":"object","additionalProperties":false}""",
            ["procedure.system.inspect"],
            PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.PrivateOperatorMetadata,
            false,
            false);

        public Task<SystemCapabilityHandlerResult> ReadAsync(
            JsonElement input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The unrelated capability must not be invoked.");
    }
}
