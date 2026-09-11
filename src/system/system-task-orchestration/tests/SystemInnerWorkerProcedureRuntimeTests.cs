using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using DantesRoleplay.TriggerScheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public void Procedure_worker_host_policy_matches_system_capability_governance_identity()
    {
        var applications = new InMemoryApplicationRegistry();
        var application = applications.Register(new(Application, "Fixture", "Fixture application.", []));
        var principal = TrustedPrincipalContext.VerifiedPrincipal(
            "principal." + new string('a', 64), "test");
        var host = new InteractionInvocationHost(principal, application, "state", "grant@1",
            "worker-command", "state-revision", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));
        var request = new SystemInnerWorkerRequest(host,
            new("system.inspect", 1, new string('A', 64)),
            "{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"Inspect the registry.\"}",
            "{\"type\":\"object\"}");
        var catalog = new SystemCapabilityCatalog(
            [new ApplicationsSystemCapabilityHandler(applications)],
            new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy());
        var context = new SystemCapabilityInvocationContext(principal,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope, "inner-worker-policy");

        var selection = new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
            new("web.inner", "Inner AI", "Perform the bounded host-selected procedure.")
        ]), catalog).Resolve(request, context, DateTime.UtcNow);

        var binding = Assert.Single(selection.ToolBindings);
        Assert.Equal(SystemCapabilityIds.Applications, binding.CapabilityVersion.ExactDefinitionId);
        Assert.Equal(SystemCapabilityMode.Read, binding.Mode);
        Assert.Equal("system_applications", binding.Definition.Name);
    }

    [Fact]
    public async Task Procedure_worker_and_due_trigger_routes_use_the_registered_durable_runner()
    {
        await using var database = await InnerWorkerDatabase.CreateAsync();
        var db = database.Db;
        var setup = Setup(db);
        await ActivateAsync(setup);
        var activation = setup.Activation.Current(Application)!;
        var application = setup.Applications.Get(Application)!;
        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var state = stateSpaces.Create(new("state", application, activation.ActivationFingerprint,
            activation.ResolutionFingerprint));
        await SeedInnerWorkerGrantAsync(db);
        var procedureFile = ProcedureFile.Parse(File.ReadAllText(Path.Combine(root,
            RelativePath.Replace('/', Path.DirectorySeparatorChar))), RelativePath);
        var procedure = await new ProcedureStore(db).WriteAsync(new WriteProcedureRequest
        {
            Id = procedureFile.Id, Category = procedureFile.Category, Name = procedureFile.Name,
            Description = procedureFile.Description, Governs = procedureFile.Governs,
            Matches = procedureFile.Matches, Instructions = procedureFile.Instructions,
            Constraints = procedureFile.Constraints, Status = procedureFile.Status,
            CreatedBy = "fixture", ChangeNote = "Focused worker fixture."
        });
        var deadline = DateTime.UtcNow.AddMinutes(2);
        const string ephemeralParent = "workflow-service.command";
        var host = InnerWorkerHost(application, state, "inner-worker-command", deadline,
            ephemeralParent);
        StandingGrantTargetResolution target;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            target = await setup.Resolver.ResolveCurrentAsync(host, procedure.Procedure.Id, "procedure");
            await transaction.CommitAsync();
        }
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, target.Status);
        Assert.NotEqual(procedure.Procedure.SourceHash, target.Target!.ContentFingerprint);
        var selected = new SystemTaskSelectedDefinition(target.Target.DefinitionId,
            target.Target.Revision, target.Target.ContentFingerprint);

        var catalog = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
            setup.Sources, setup.Roots, setup.Extensions).UsePreparationCache(
                new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority());
        var snapshots = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), catalog,
            new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)), setup.Activation);
        var retrieval = new InteractionFeatureRetriever(snapshots, namespaces: setup.Namespaces,
            changes: setup.Activation);
        var interactionAuthority = new PrivateHostInteractionAuthorizationPolicy(stateSpaces);
        var contexts = new InteractionTaskContextMaterializer(interactionAuthority, retrieval,
            snapshots, new UnusedReadModels());
        var resolver = new SystemInnerWorkerProcedureResolver(
            new InteractionEnvelopeFactory(setup.Applications, setup.Activation, stateSpaces, interactionAuthority),
            contexts, snapshots, new SystemInnerWorkerPreparation(new ProcedureStore(db), contexts),
            new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
                new("web.inner", "Inner AI", "Perform the bounded host-selected procedure.")
            ])), TimeProvider.System);
        var standingPolicy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var durable = new SqliteSystemTaskDurableService(db, standingPolicy, setup.Resolver,
            stateSpaces, TimeProvider.System);
        var service = new SystemInnerWorkerService(resolver, durable);
        var provider = new SuccessfulProcedureProvider();
        var systemAi = new SystemAiAgentService([], new AiService([provider]));
        var invoker = new SystemInnerWorkerProcedureInvoker(systemAi);
        var lifecycleServices = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(durable)
            .AddSingleton(resolver)
            .AddSingleton(invoker)
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .AddSingleton<SystemTaskAiInvocationLifecycleFactory>()
            .AddScoped<SystemInnerWorkerProcedureExecutor>()
            .AddSingleton<SystemTaskWorkflowBackgroundWorker>()
            .BuildServiceProvider();
        using (lifecycleServices)
        {
            const string schema = """
                {"additionalProperties":false,"properties":{"answer":{"type":"string"},"summary":{"type":"string"}},"required":["answer","summary"],"type":"object"}
                """;
            var assignment = JsonSerializer.Serialize(new
            {
                format = SystemInnerWorkerAssignmentV1.Format,
                instruction = "Inspect the active runtime definition and return a short answer."
            });
            var ordinary = await service.SubmitAsync(new(
                InnerWorkerHost(application, state, "inner-worker-ordinary", deadline,
                    ephemeralParent), selected, assignment, schema));
            Assert.Equal(InteractionInvocationResultTag.Failed, ordinary.Tag);
            Assert.Equal("SYSTEM_TASK_PARENT_UNKNOWN", ordinary.Code);

            var submitted = await service.SubmitEphemeralRootAsync(new(host, selected, assignment, schema));
            Assert.True(submitted.Tag == InteractionInvocationResultTag.Pending,
                submitted.Code + ": " + submitted.SafeMessage);
            Assert.Equal(15, host.Budget.RemainingOperations);
            var admitted = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .SingleAsync(value => value.TaskId == submitted.TaskHandle!.TaskId);
            Assert.Null(admitted.ParentTaskId);
            Assert.Equal(admitted.TaskId, admitted.RootTaskId);
            Assert.Equal(ephemeralParent, admitted.ParentCommandId);
            Assert.Contains("\"originatingParentMode\":\"ephemeral-root\"",
                admitted.AdmissionPayloadJson, StringComparison.Ordinal);

            var worker = lifecycleServices.GetRequiredService<SystemTaskWorkflowBackgroundWorker>();
            Assert.True(await worker.RunOnceAsync("inner-worker"));

            var readHost = InnerWorkerHost(application, state, "inner-worker-read", deadline);
            var completed = await service.GetAsync(readHost, submitted.TaskHandle!);
            Assert.Equal(InteractionInvocationResultTag.Completed, completed.Tag);
            Assert.Contains("\"answer\":\"42\"", completed.DataJson, StringComparison.Ordinal);
            Assert.StartsWith("inner-result.", completed.CompletionEvidenceReference, StringComparison.Ordinal);
            Assert.Equal(1, provider.Calls);
            Assert.Equal(1L, await db.Set<SystemTaskAiDispatchEvidenceRecord>().LongCountAsync(
                value => value.Kind == "dispatch" && value.DispatchKind == "provider"));
            Assert.Equal(1L, await db.Set<SystemTaskAiDispatchEvidenceRecord>().LongCountAsync(
                value => value.Kind == "usage" && value.IsComplete == 1));

            var replay = await service.SubmitEphemeralRootAsync(new(
                InnerWorkerHost(application, state, "inner-worker-command", deadline,
                    ephemeralParent), selected, assignment, schema));
            Assert.Equal(submitted.TaskHandle, replay.TaskHandle);
            Assert.False(await worker.RunOnceAsync("inner-worker"));
            Assert.Equal(1, provider.Calls);

            var triggerNow = DateTimeOffset.UtcNow;
            var triggerClock = new RuntimeTriggerClock(
                triggerNow.AddTicks(-(triggerNow.Ticks % TimeSpan.TicksPerSecond)));
            var triggerHost = InnerWorkerHost(application, state, "trigger-binding-command", deadline);
            var triggerTarget = TriggerProcedureWorkflowTarget.Create(triggerHost, selected,
                assignment, schema, TimeSpan.FromMinutes(2));
            var triggerStore = new SqliteTriggerSchedulingStore(db, triggerClock,
                durableTasks: durable);
            await triggerStore.AppendOneTimeTriggerAsync(OneTimeTriggerDefinition.Create(
                Application, "demo.runtime.inner-trigger", 1, triggerClock.UtcNow,
                TriggerMisfirePolicy.FireOnce, TriggerFireTarget.ProcedureWorkflow,
                procedureWorkflow: triggerTarget));
            var triggerWorker = new SqliteOneTimeTriggerWorker(db, triggerClock, triggerStore,
                new SystemTaskTriggerTransactionParticipant(db,
                    new TriggerNotificationTransactionParticipant(db, triggerClock), durable,
                    resolver));

            var fired = await triggerWorker.RunBatchAsync("trigger-inner-worker");

            Assert.Equal(1, fired.Completed);
            var triggerTask = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .SingleAsync(value => value.CommandId != submitted.TaskHandle!.CommandId);
            Assert.NotNull(triggerTask.AdmissionPayloadJson);
            Assert.Contains("\"innerWorker\"", triggerTask.AdmissionPayloadJson,
                StringComparison.Ordinal);
            Assert.Equal(2L, await db.Set<SystemTaskAiCeilingRecord>().LongCountAsync());
            Assert.True(await worker.RunOnceAsync("inner-worker-trigger-trigger"));
            var triggerResult = await service.GetAsync(
                InnerWorkerHost(application, state, "trigger-result-read", deadline),
                new(triggerTask.TaskId, triggerTask.CommandId));
            Assert.Equal(InteractionInvocationResultTag.Completed, triggerResult.Tag);
            Assert.Contains("\"answer\":\"42\"", triggerResult.DataJson,
                StringComparison.Ordinal);
            Assert.Equal(2, provider.Calls);

            var existingTaskIds = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .Select(value => value.TaskId).ToArrayAsync();
            var recurringHost = InnerWorkerHost(application, state,
                "recurring-trigger-binding-command", deadline);
            var recurringTarget = TriggerProcedureWorkflowTarget.Create(recurringHost, selected,
                assignment, schema, TimeSpan.FromMinutes(2));
            var recurringDefinition = RecurringTriggerDefinition.Create(
                Application, "demo.runtime.recurring-inner-trigger", 1,
                RecurrencePattern.Daily(1,
                    new TimeOnly(triggerClock.UtcNow.Hour, triggerClock.UtcNow.Minute,
                        triggerClock.UtcNow.Second), "Etc/UTC"),
                misfirePolicy: TriggerMisfirePolicy.FireOnce,
                target: TriggerFireTarget.ProcedureWorkflow,
                procedureWorkflow: recurringTarget);
            var recurringAppend = await triggerStore.AppendRecurringTriggerAsync(recurringDefinition);
            var recurringReplay = await triggerStore.AppendRecurringTriggerAsync(recurringDefinition);
            var recurringWorker = new SqliteRecurringTriggerWorker(db, triggerClock,
                new SystemTaskTriggerTransactionParticipant(db,
                    new TriggerNotificationTransactionParticipant(db, triggerClock), durable,
                    resolver));

            var recurringFire = await recurringWorker.RunBatchAsync("recurring-inner-worker");
            var recurringDuplicate = await recurringWorker.RunBatchAsync("recurring-inner-worker-replay");

            Assert.Equal(TriggerSchedulingWriteDisposition.Appended, recurringAppend.Disposition);
            Assert.Equal(TriggerSchedulingWriteDisposition.Replay, recurringReplay.Disposition);
            Assert.Equal(1, recurringFire.Completed);
            Assert.Equal(0, recurringDuplicate.Completed);
            var recurringTask = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
                .SingleAsync(value => !existingTaskIds.Contains(value.TaskId));
            Assert.Equal(3L, await db.Set<SystemTaskAiCeilingRecord>().LongCountAsync());
            Assert.Single(await db.RecurringTriggerFireReceipts.AsNoTracking().ToArrayAsync());
            Assert.True(await worker.RunOnceAsync("inner-worker-recurring-trigger"));
            var recurringResult = await service.GetAsync(
                InnerWorkerHost(application, state, "recurring-trigger-result-read", deadline),
                new(recurringTask.TaskId, recurringTask.CommandId));
            Assert.Equal(InteractionInvocationResultTag.Completed, recurringResult.Tag);
            Assert.Contains("\"answer\":\"42\"", recurringResult.DataJson,
                StringComparison.Ordinal);
            Assert.Equal(3, provider.Calls);
        }
    }

    [Fact]
    public void Procedure_worker_assignment_rejects_authority_and_unknown_grammar_fields()
    {
        var authority = Assert.Throws<InteractionContractException>(() => SystemInnerWorkerAssignmentV1.Parse("""
            {"format":"dantes-roleplay/inner-procedure-assignment/v1","instruction":"inspect","allowedTools":["root"]}
            """));
        Assert.Equal("INNER_WORKER_ASSIGNMENT_UNSUPPORTED", authority.Code);
        var version = Assert.Throws<InteractionContractException>(() => SystemInnerWorkerAssignmentV1.Parse("""
            {"format":"dantes-roleplay/inner-procedure-assignment/v2","instruction":"inspect"}
            """));
        Assert.Equal("INNER_WORKER_ASSIGNMENT_UNSUPPORTED", version.Code);
    }

    private static InteractionInvocationHost InnerWorkerHost(ApplicationRevision application,
        StateSpaceView state, string command, DateTime deadline, string? parentCommand = null) => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        application, state.StateSpaceId, "inner-grant@1", command, InteractionStateRevision.From(state),
        InteractionExecutionProfile.Workflow, new InteractionInvocationBudget(16, deadline),
        parentCommand);

    private static async Task SeedInnerWorkerGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("inner-grant@1", "inner-grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.StateSpace, "state",
            [StandingGrantCapability.Read, StandingGrantCapability.Execute, StandingGrantCapability.ReadTask, StandingGrantCapability.CancelTask],
            new(StandingGrantDefinitionMode.ApplicationOwned, [], [
                new("demo.runtime", true, [CatalogNamespaceKinds.Procedure]),
                new("demo.runtime.pure", false, [CatalogNamespaceKinds.Mechanic]),
                new("demo.runtime.query", false, [CatalogNamespaceKinds.Query])]),
            [], 16, DateTime.UtcNow.AddMinutes(10), false, "inner-grant-operation");
        grant = grant with { ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant) };
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
            Scope = "stateSpace", StateSpaceId = grant.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = grant.ContentFingerprint, MaximumOperations = grant.MaximumOperations,
            ExpiresAtUtc = grant.ExpiresAtUtc, Revoked = false, IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = 1 });
        await db.SaveChangesAsync();
    }

    private sealed class SuccessfulProcedureProvider : IAiProvider
    {
        internal int Calls { get; private set; }
        public AiProviderInfo Info { get; } = new("codex", "Controlled Codex fixture");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(InteractionRoleProfile.Inner.Model, request.Model);
            Assert.Empty(request.Tools);
            Assert.Null(request.ToolExecutor);
            Assert.NotEmpty(request.ResponseSchemaJson);
            return Task.FromResult(new AiProviderResponse(true, null, "done",
                "{\"answer\":\"42\",\"summary\":\"Inspection completed.\"}", [],
                Usage: new(7, 2, 9, true)));
        }
    }

    private sealed class RuntimeTriggerClock(DateTimeOffset value) : TimeProvider, ITriggerClock
    {
        public DateTimeOffset UtcNow { get; } = value;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class UnusedReadModels : IApplicationReadModelService
    {
        public Task<ApplicationReadModelResult> ReadAsync(ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
            "The procedure-only fixture must not invent a query read model.");
    }

    private sealed class InnerWorkerDatabase(string path, DantesRoleplayDbContext db) : IAsyncDisposable
    {
        internal DantesRoleplayDbContext Db { get; } = db;

        internal static async Task<InnerWorkerDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"inner-worker-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + path).Options;
            var db = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new(path, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
