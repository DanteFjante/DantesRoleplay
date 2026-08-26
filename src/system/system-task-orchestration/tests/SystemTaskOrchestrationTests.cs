using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Assistants;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Retrieval;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SystemTasks.Tests;

public sealed class SystemTaskOrchestrationTests
{
    private const string Operator =
        "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Local_planning_reads_then_prepares_inert_writes_and_replays_without_more_model_calls()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var conversationId = await ConversationAsync(store);
        var capability = new FixtureWriteHandler();
        var catalog = Catalog(capability);
        var descriptors = catalog.Discover(Invocation()).Capabilities;
        var read = descriptors.Single(value => value.Mode == SystemCapabilityMode.Read);
        var write = descriptors.Single(value => value.Mode == SystemCapabilityMode.Write);
        var references = descriptors.Select(Reference).ToArray();
        var provider = new QueueProvider(
            Response("continue", "Inspect current metadata.", [Reference(read)],
                [(read.Id, JsonSerializer.SerializeToElement(new { }))]),
            Response("prepared", "Register the requested fixture.", [Reference(write)],
                [(write.Id, JsonSerializer.SerializeToElement(new { name = "alpha" }))]));
        var service = Service(db, store, catalog, provider, references);
        var request = new SystemTaskPrepareRequest(
            SystemTaskOperations.Resolve, "Register fixture alpha", null, "task.resolve.alpha");

        var prepared = await service.PrepareAsync(Context(), conversationId, request);
        var replay = await service.PrepareAsync(Context(), conversationId, request);

        Assert.Equal(prepared.Summary.Id, replay.Summary.Id);
        Assert.Equal(SystemTaskStatuses.Prepared, prepared.Summary.Status);
        Assert.Equal(64, prepared.Summary.PlanFingerprint.Length);
        Assert.Equal(["read", "write"], prepared.Steps.Select(value => value.Mode));
        Assert.Equal(SystemCapabilityPreflightStatuses.Ready, prepared.Steps[1].PreflightStatus);
        Assert.Equal(2, prepared.Rounds.Count);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, capability.ReadCalls);
        Assert.Equal(1, capability.PreflightCalls);
        Assert.Equal(0, capability.ExecuteCalls);
        Assert.Empty(await db.SystemTaskConfirmations.ToListAsync());
        Assert.Empty(await db.SystemTaskExecutions.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_and_execution_require_exact_plan_and_return_replayable_per_step_receipts()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var conversationId = await ConversationAsync(store);
        var capability = new FixtureWriteHandler();
        var catalog = Catalog(capability);
        var descriptor = catalog.Discover(Invocation()).Capabilities.Single(value =>
            value.Mode == SystemCapabilityMode.Write);
        var service = Service(db, store, catalog, new QueueProvider(), [Reference(descriptor)]);
        var input = JsonSerializer.SerializeToElement(new { name = "bravo" });
        var task = await service.PrepareAsync(Context(), conversationId, new(
            SystemTaskOperations.Submit, "Register fixture bravo",
            [new(descriptor.Id, input)], "task.submit.bravo"));

        var confirmation = await service.ConfirmAsync(Context(), task.Summary.Id,
            new(task.Summary.PlanFingerprint, "task.confirm.bravo"));
        var request = new SystemTaskExecutionRequest(
            confirmation.Id, task.Summary.PlanFingerprint, "task.execute.bravo");
        var receipt = await service.ExecuteAsync(Context(), task.Summary.Id, request);
        var replay = await service.ExecuteAsync(Context(), task.Summary.Id, request);

        Assert.Equal(receipt.Id, replay.Id);
        Assert.Equal(SystemTaskExecutionStatuses.Succeeded, receipt.Status);
        var step = Assert.Single(receipt.Steps);
        Assert.Equal(SystemTaskStepStatuses.Succeeded, step.Status);
        Assert.Matches("^[0-9a-f]{32}$", step.OperationId);
        Assert.Equal(64, step.OutputFingerprint.Length);
        Assert.Equal(new string('D', 64), step.ReadBackFingerprint);
        Assert.Equal(1, capability.ExecuteCalls);
        Assert.True(confirmation.ExpiresAtUtc - confirmation.ConfirmedAtUtc <= TimeSpan.FromMinutes(5.01));
        Assert.Single(await db.SystemTaskExecutionSteps.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Unauthorized_planning_fails_before_claim_context_or_model()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var capability = new FixtureWriteHandler();
        var provider = new QueueProvider();
        var context = new FixtureContext([]);
        var service = new SystemTaskService(db, store, Catalog(capability), context, provider,
            new PrivateOperatorAuthorizationPolicy(), new BoundedJsonSchemaValidator());

        var exception = await Assert.ThrowsAsync<SystemTaskException>(() => service.PrepareAsync(new(
                TrustedPrincipalContext.Unauthenticated("TEST_UNAUTHENTICATED"),
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "task-test"),
            "conversation.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", new(
                SystemTaskOperations.Resolve, "Do something", null, "task.denied")));

        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", exception.Code);
        Assert.Empty(await db.SystemTasks.ToListAsync());
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, context.Calls);
    }

    [Fact]
    public async Task Submitted_write_limit_is_rejected_before_any_owner_preflight()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var conversationId = await ConversationAsync(store);
        var capability = new FixtureWriteHandler();
        var catalog = Catalog(capability);
        var descriptor = catalog.Discover(Invocation()).Capabilities.Single(value =>
            value.Mode == SystemCapabilityMode.Write);
        var service = Service(db, store, catalog, new QueueProvider(), [Reference(descriptor)]);
        var agenda = Enumerable.Range(1, 9).Select(index => new SystemTaskAgendaItem(
            descriptor.Id, JsonSerializer.SerializeToElement(new { name = $"item-{index}" }))).ToArray();

        var task = await service.PrepareAsync(Context(), conversationId, new(
            SystemTaskOperations.Submit, "Too many writes", agenda, "task.submit.too-many"));

        Assert.Equal(SystemTaskStatuses.NeedsInput, task.Summary.Status);
        Assert.Equal("SYSTEM_TASK_WRITE_LIMIT", task.ErrorCode);
        Assert.Empty(task.Steps);
        Assert.Equal(0, capability.PreflightCalls);
        Assert.Equal(0, capability.ExecuteCalls);
    }

    [Fact]
    public async Task Changed_current_precondition_stops_before_write_with_a_stale_receipt()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new AssistantConversationStore(db, new OperationLog(db));
        var conversationId = await ConversationAsync(store);
        var capability = new FixtureWriteHandler();
        var catalog = Catalog(capability);
        var descriptor = catalog.Discover(Invocation()).Capabilities.Single(value =>
            value.Mode == SystemCapabilityMode.Write);
        var service = Service(db, store, catalog, new QueueProvider(), [Reference(descriptor)]);
        var task = await service.PrepareAsync(Context(), conversationId, new(
            SystemTaskOperations.Submit, "Register fixture stale",
            [new(descriptor.Id, JsonSerializer.SerializeToElement(new { name = "stale" }))],
            "task.submit.stale"));
        var confirmation = await service.ConfirmAsync(Context(), task.Summary.Id,
            new(task.Summary.PlanFingerprint, "task.confirm.stale"));
        capability.Precondition = new string('E', 64);

        var receipt = await service.ExecuteAsync(Context(), task.Summary.Id, new(
            confirmation.Id, task.Summary.PlanFingerprint, "task.execute.stale"));

        Assert.Equal(SystemTaskExecutionStatuses.Stale, receipt.Status);
        Assert.Equal(SystemTaskStepStatuses.Stale, Assert.Single(receipt.Steps).Status);
        Assert.Equal(0, capability.ExecuteCalls);
    }

    [Fact]
    public async Task Generic_host_executes_a_confirmed_application_registration_through_its_typed_owner()
    {
        var path = Path.Combine(Path.GetTempPath(), $"system-task-owner-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILocalStructuredCompletionProvider>(new QueueProvider());
            services.AddDantesRoleplayDataAccess($"Data Source={path}");
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
            await db.Database.MigrateAsync();
            var store = scope.ServiceProvider.GetRequiredService<IAssistantConversationStore>();
            var conversationId = await ConversationAsync(store);
            var catalog = scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
            var descriptor = catalog.Discover(Invocation()).Capabilities.Single(value =>
                value.Id == SystemCapabilityIds.ApplicationRegister);
            var tasks = scope.ServiceProvider.GetRequiredService<ISystemTaskService>();
            var task = await tasks.PrepareAsync(Context(), conversationId, new(
                SystemTaskOperations.Submit, "Register the fixture application",
                [new(descriptor.Id, JsonSerializer.SerializeToElement(new
                {
                    applicationId = "fixture-app",
                    displayName = "Fixture",
                    description = "Registered through a confirmed system task.",
                    baseApplications = Array.Empty<string>()
                }))], "task.submit.real-owner"));
            var confirmation = await tasks.ConfirmAsync(Context(), task.Summary.Id,
                new(task.Summary.PlanFingerprint, "task.confirm.real-owner"));

            var receipt = await tasks.ExecuteAsync(Context(), task.Summary.Id, new(
                confirmation.Id, task.Summary.PlanFingerprint, "task.execute.real-owner"));

            Assert.Equal(SystemTaskExecutionStatuses.Succeeded, receipt.Status);
            var registered = scope.ServiceProvider.GetRequiredService<IApplicationRegistry>()
                .Describe(ApplicationIdentifier.Parse("fixture-app"));
            Assert.NotNull(registered);
            Assert.Equal("Fixture", registered.DisplayName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Expired_execution_lease_repairs_a_missing_receipt_by_replaying_the_owner_token()
    {
        var path = Path.Combine(Path.GetTempPath(), $"system-task-recovery-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILocalStructuredCompletionProvider>(new QueueProvider());
            services.AddDantesRoleplayDataAccess($"Data Source={path}");
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
            await db.Database.MigrateAsync();
            var store = scope.ServiceProvider.GetRequiredService<IAssistantConversationStore>();
            var conversationId = await ConversationAsync(store);
            var catalog = scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
            var descriptor = catalog.Discover(Invocation()).Capabilities.Single(value =>
                value.Id == SystemCapabilityIds.ApplicationRegister);
            var tasks = scope.ServiceProvider.GetRequiredService<ISystemTaskService>();
            var task = await tasks.PrepareAsync(Context(), conversationId, new(
                SystemTaskOperations.Submit, "Register after a simulated receipt crash",
                [new(descriptor.Id, JsonSerializer.SerializeToElement(new
                {
                    applicationId = "recovery-app", displayName = "Recovery",
                    description = "Receipt recovery fixture.", baseApplications = Array.Empty<string>()
                }))], "task.submit.recovery"));
            var confirmation = await tasks.ConfirmAsync(Context(), task.Summary.Id,
                new(task.Summary.PlanFingerprint, "task.confirm.recovery"));
            var request = new SystemTaskExecutionRequest(
                confirmation.Id, task.Summary.PlanFingerprint, "task.execute.recovery");
            var executionId = "system-task-receipt." + Guid.NewGuid().ToString("N");
            var requestJson = JsonSerializer.Serialize(new
                { confirmationId = confirmation.Id, planFingerprint = task.Summary.PlanFingerprint });
            var requestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
            var decision = new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                Context().Principal, PrivateOperatorCapability.Modify,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "task-test"));
            db.SystemTaskExecutions.Add(new()
            {
                Id = executionId, TaskId = task.Summary.Id, ConfirmationId = confirmation.Id,
                PrincipalReference = Operator, IdempotencyKey = request.IdempotencyKey,
                RequestFingerprint = requestFingerprint, PlanFingerprint = task.Summary.PlanFingerprint,
                Status = SystemTaskExecutionStatuses.Running, SafeSummary = "Interrupted fixture.",
                ErrorCode = "", ErrorMessage = "",
                AuthorizationEvidenceJson = JsonSerializer.Serialize(decision.Evidence),
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3), StartedAtUtc = DateTime.UtcNow.AddMinutes(-3),
                LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
            var planned = Assert.Single(task.Steps, value => value.Mode == "write");
            var preflight = await catalog.PreflightWriteAsync(planned.CapabilityId,
                planned.DescriptorFingerprint, planned.Input.GetRawText(), [], Invocation());
            Assert.True(preflight.Ok);
            db.SystemTaskExecutionSteps.Add(new()
            {
                ExecutionId = executionId, Ordinal = planned.Ordinal, TaskStepId = planned.StepId,
                Status = SystemTaskStepStatuses.Running,
                ExecutionEvidenceJson = preflight.Preflight!.ExecutionEvidenceJson,
                OperationId = "", OutputJson = "", OutputFingerprint = "", ReadBackFingerprint = "",
                ErrorCode = "", ErrorMessage = "", CompletedAtUtc = null
            });
            await db.SaveChangesAsync();
            var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(
                "dantes-roleplay/system-task-execution/v1\0" + executionId + "\0" + planned.Ordinal));
            var token = Convert.ToHexStringLower(tokenHash)[..32];
            var committed = await catalog.ExecuteWriteAsync(planned.CapabilityId,
                planned.DescriptorFingerprint, planned.Input.GetRawText(), new(
                    Invocation(), token, task.Summary.Intent, descriptor.ProcedureIds, decision.Evidence,
                    preflight.Preflight.ExecutionEvidenceJson));
            Assert.True(committed.Ok);
            Assert.Equal(SystemTaskStepStatuses.Running,
                Assert.Single(await db.SystemTaskExecutionSteps.AsNoTracking().ToListAsync()).Status);

            var repaired = await tasks.ExecuteAsync(Context(), task.Summary.Id, request);

            Assert.Equal(SystemTaskExecutionStatuses.Succeeded, repaired.Status);
            Assert.Equal(token, Assert.Single(repaired.Steps).OperationId);
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationRegistry>()
                .Get(ApplicationIdentifier.Parse("recovery-app")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Migration_creates_task_receipt_tables_and_matches_the_current_model()
    {
        var path = Path.Combine(Path.GetTempPath(), $"system-task-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={path}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var tables = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name LIKE 'system_task%'")
                .ToListAsync();
            Assert.Equal(6, tables.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static SystemTaskService Service(DantesRoleplayDbContext db,
        IAssistantConversationStore store, ISystemCapabilityCatalog catalog,
        ILocalStructuredCompletionProvider provider, IReadOnlyList<string> references) => new(
            db, store, catalog, new FixtureContext(references), provider,
            new PrivateOperatorAuthorizationPolicy(), new BoundedJsonSchemaValidator());

    private static SystemCapabilityCatalog Catalog(FixtureWriteHandler write) => new(
        [write], new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy(), [write]);

    private static async Task<string> ConversationAsync(IAssistantConversationStore store)
    {
        var begun = await store.BeginTurnAsync(new(
            Operator, "local", null, null, "Fixture system conversation", "conversation.fixture",
            new string('A', 64), AssistantConversationScopes.System));
        return begun.ConversationId;
    }

    private static SystemTaskRequestContext Context() => new(
        TrustedPrincipalContext.VerifiedPrincipal(Operator, "test"),
        PrivateOperatorAuthorizationPolicy.PrivateHostScope, "task-test");

    private static SystemCapabilityInvocationContext Invocation() => new(
        TrustedPrincipalContext.VerifiedPrincipal(Operator, "test"),
        PrivateOperatorAuthorizationPolicy.PrivateHostScope, "task-test");

    private static string Reference(SystemCapabilityDescriptor value) =>
        $"capability:{value.Id}@{value.Version}#{value.Fingerprint}";

    private static StructuredCompletionResult Response(string disposition, string summary,
        IReadOnlyList<string> evidence, IReadOnlyList<(string Id, JsonElement Input)> steps) => new(
        new("ollama", "fixture-model", "fixture-revision", "standard"),
        JsonSerializer.Serialize(new
        {
            disposition,
            summary,
            evidence,
            steps = steps.Select(value => new { capabilityId = value.Id, input = value.Input })
        }), 4, 10, 6);

    private sealed class QueueProvider(params StructuredCompletionResult[] results)
        : ILocalStructuredCompletionProvider
    {
        private readonly Queue<StructuredCompletionResult> _results = new(results);
        public int Calls { get; private set; }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelStatus(true, new("ollama", "fixture-model", "fixture-revision")));
        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(SystemTaskService.TaskClass, request.TaskClass);
            Assert.DoesNotContain("tools", request.ResponseSchema, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FixtureContext(IReadOnlyList<string> references) : ISystemTaskContextMaterializer
    {
        public int Calls { get; private set; }
        public Task<SystemTaskContextSnapshot> MaterializeAsync(string query,
            SystemTaskRequestContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SystemTaskContextSnapshot(SystemTaskContextMaterializer.Profile,
                "{\"profile\":\"system-task-plan-v1\"}", new string('B', 64), references));
        }
    }

    private sealed class FixtureWriteHandler : ISystemReadCapabilityHandler, ISystemWriteCapabilityHandler
    {
        private const string InputSchema = """
            {"type":"object","additionalProperties":false,"properties":{"name":{"type":"string","minLength":1,"maxLength":80}}}
            """;
        private const string OutputSchema = """
            {"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string","minLength":1,"maxLength":80}}}
            """;
        public int ReadCalls { get; private set; }
        public int PreflightCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public string Precondition { get; set; } = new string('C', 64);

        SystemCapabilityRegistration ISystemReadCapabilityHandler.Registration => new(
            "system.fixture.read", 1, "fixture-owner", "Read fixture metadata.",
            SystemCapabilityMode.Read, InputSchema, OutputSchema,
            ["procedure.system.fixture-read"], PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false);

        SystemCapabilityRegistration ISystemWriteCapabilityHandler.Registration => new(
            "system.fixture.write", 1, "fixture-owner", "Write fixture metadata.",
            SystemCapabilityMode.Write, InputSchema, OutputSchema,
            ["procedure.system.fixture-write"], PrivateOperatorCapability.Modify,
            SystemCapabilitySensitivity.PrivateOperatorMetadata, true, true);

        public Task<SystemCapabilityHandlerResult> ReadAsync(JsonElement input,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(SystemCapabilityHandlerResult.Success(
                JsonSerializer.SerializeToElement(new { value = "current" })));
        }

        public Task<SystemCapabilityWritePreflight> PreflightAsync(JsonElement input,
            IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
            CancellationToken cancellationToken = default)
        {
            PreflightCalls++;
            return Task.FromResult(SystemCapabilityWritePreflight.Ready(Precondition,
                "Write exact fixture metadata.", ["fixture:metadata"]));
        }

        public Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(JsonElement input,
            SystemCapabilityWriteExecutionContext context, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(SystemCapabilityWriteHandlerResult.Success(
                JsonSerializer.SerializeToElement(new { value = input.GetProperty("name").GetString()! }),
                context.RequestToken, new string('D', 64)));
        }
    }
}
