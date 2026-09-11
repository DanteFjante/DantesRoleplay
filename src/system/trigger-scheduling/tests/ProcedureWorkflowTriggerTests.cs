using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class ProcedureWorkflowTriggerTests : IDisposable
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ResultSchema = "{\"additionalProperties\":true,\"type\":\"object\"}";
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("jobs");
    private static readonly SystemTaskSelectedDefinition Procedure = new("jobs.procedure", 1, Hash);
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Due_workflow_trigger_atomically_enqueues_once_and_survives_worker_restart()
    {
        await using var db = fixture.CreateContext();
        var clock = new WorkflowClock(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        var authority = Authority(db, clock);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock, durableTasks: authority.Durable);
        await store.AppendOneTimeTriggerAsync(Definition(authority.State, clock.UtcNow));

        var first = await Worker(db, clock, authority).RunBatchAsync("worker.workflow.first");

        Assert.Equal(1, first.Completed);
        Assert.Equal(1, await db.Set<SystemTaskLifecycleRecord>().CountAsync());
        var task = Assert.Single(await db.Set<SystemTaskLifecycleRecord>().AsNoTracking().ToArrayAsync());
        Assert.Equal("procedure-workflow", task.Purpose);
        Assert.Equal("queued", task.State);
        Assert.Equal(Procedure.ExactDefinitionId, task.DefinitionId);
        Assert.Equal(Assignment("Run the admitted schedule."), task.InputJson);
        Assert.NotNull(task.AdmissionPayloadJson);
        Assert.Contains("\"innerWorker\"", task.AdmissionPayloadJson, StringComparison.Ordinal);
        Assert.Single(await db.Set<SystemTaskAiCeilingRecord>().ToArrayAsync());
        Assert.Single(db.TriggerFireReceipts);
        Assert.Equal("completed", Assert.Single(db.TriggerFireWork).State);
        Assert.Empty(db.Notifications);

        await using var restarted = fixture.CreateContext();
        var restartedAuthority = Authority(restarted, clock);
        var second = await Worker(restarted, clock, restartedAuthority)
            .RunBatchAsync("worker.workflow.restarted");
        Assert.Equal(0, second.Examined);
        Assert.Equal(1, await restarted.Set<SystemTaskLifecycleRecord>().CountAsync());
    }

    [Fact]
    public async Task Revoked_grant_at_fire_fails_without_enqueuing_or_emitting_notification()
    {
        await using var db = fixture.CreateContext();
        var clock = new WorkflowClock(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        var authority = Authority(db, clock);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock, durableTasks: authority.Durable);
        await store.AppendOneTimeTriggerAsync(Definition(authority.State, clock.UtcNow));
        authority.Policy.Allowed = false;

        var result = await Worker(db, clock, authority).RunBatchAsync("worker.workflow.revoked");

        Assert.Equal(1, result.Failed);
        Assert.Empty(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());
        Assert.Empty(db.TriggerFireReceipts);
        Assert.Empty(db.Notifications);
        Assert.Equal("failed", Assert.Single(db.TriggerFireWork).State);
        Assert.Equal("permanent-handler", Assert.Single(db.TriggerFireWork).FailureKind);
    }

    [Fact]
    public async Task Ai_enrollment_failure_rolls_back_task_and_fire_evidence_for_retry()
    {
        await using var db = fixture.CreateContext();
        var clock = new WorkflowClock(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        var authority = Authority(db, clock);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock, durableTasks: authority.Durable);
        await store.AppendOneTimeTriggerAsync(Definition(authority.State, clock.UtcNow));
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_workflow_ai_enrollment
            BEFORE INSERT ON system_task_ai_ceiling
            BEGIN SELECT RAISE(ABORT, 'fixture enrollment failure'); END;
            """);

        var result = await Worker(db, clock, authority).RunBatchAsync("worker.workflow.enrollment-failure");

        Assert.Equal(1, result.Retried);
        Assert.Empty(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());
        Assert.Empty(await db.Set<SystemTaskAiCeilingRecord>().ToArrayAsync());
        Assert.Empty(db.TriggerFireReceipts);
        Assert.Empty(db.Notifications);
        Assert.Equal("retry", Assert.Single(db.TriggerFireWork).State);
    }

    [Fact]
    public async Task Matching_observation_atomically_enqueues_workflow_without_notification_and_deduplicates_replay()
    {
        await using var db = fixture.CreateContext();
        var clock = new WorkflowClock(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        var authority = Authority(db, clock);
        RegisterApplication(db);
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"transition\":{\"type\":\"string\"}}}";
        var structureHash = TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(schema));
        var appendParticipant = new ObservationTriggerAppendParticipant(db, clock);
        var scheduling = new SqliteTriggerSchedulingStore(db, clock, [appendParticipant], authority.Durable);
        await scheduling.AppendStructureAsync(ObservationStructureDefinition.Create(App,
            "jobs.observation.transition", 1, SystemJsonSchemaProfile.Version2Id, schema,
            structureHash, "Workflow observation."));
        await scheduling.AppendSourceAsync(ObservationSourceDefinition.Create(App, "jobs.observer", 1,
            ObservationSourceStatus.Enabled,
            [ObservationStructureReference.Create("jobs.observation.transition", 1)],
            [Principal], TimeSpan.FromHours(1), 10));
        var schemas = new BoundedJsonSchemaValidator();
        var components = new SqliteEntityComponentStore(db,
            new SqliteComponentTypeRegistry(db, schemas), schemas);
        var matchStore = new SqliteObservationTriggerStore(db, new WorkflowStateSpaceRegistry(), components,
            [new ClosedScalarsObservationMatchAdapter()], clock, authority.Durable);
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "workflow-trigger-test"),
            authority.State.ApplicationRevision, authority.State.StateSpaceId, "grant.jobs",
            "binding.jobs.observer", InteractionStateRevision.From(authority.State),
            InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(4, clock.UtcNow.AddHours(1).UtcDateTime));
        var workflow = TriggerProcedureWorkflowTarget.Create(host, Procedure,
            Assignment("Inspect the admitted observation."), ResultSchema, TimeSpan.FromMinutes(2));
        await matchStore.AppendAsync(ObservationTriggerDefinition.Create(App, "jobs.observer.workflow", 1,
            ObservationTriggerLifecycle.Active, "jobs.observer", 1, "jobs.observation.transition", 1,
            structureHash, ObservationMatchAdapterReference.Create(ClosedScalarsObservationMatchAdapter.StableId, 1),
            "{\"matches\":[{\"property\":\"transition\",\"value\":\"entered\"}]}",
            TriggerFireTarget.ProcedureWorkflow,
            TriggerNotificationTarget.Create("scheduled.workflow", "Scheduled workflow"), workflow));
        var submission = ObservationSubmission.Create(
            "observation-request.0123456789abcdef0123456789abcdef",
            ObservationSourceReference.Create("jobs.observer", "device", "arrival.1"),
            ObservationStructureReference.Create("jobs.observation.transition", 1),
            clock.UtcNow.AddMinutes(-1), "{\"transition\":\"entered\"}");
        var principal = TrustedPrincipalContext.VerifiedPrincipal(Principal, "workflow-trigger-test");
        var firstAppend = await scheduling.AppendObservationAsync(principal, App, submission);
        var replay = await scheduling.AppendObservationAsync(principal, App, submission);
        var worker = new SqliteObservationTriggerWorker(db, clock, matchStore,
            new SystemTaskTriggerTransactionParticipant(db,
                new TriggerNotificationTransactionParticipant(db, clock), authority.Durable,
                authority.ProcedureWorkers));

        var first = await worker.RunBatchAsync("worker.observer.workflow");
        var second = await worker.RunBatchAsync("worker.observer.workflow");

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, firstAppend.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(1, first.Completed);
        Assert.Equal(0, second.Completed);
        Assert.Single(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());
        Assert.Equal("matched", Assert.Single(db.ObservationTriggerMatchReceipts).Disposition);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Recurring_workflow_stages_one_distinct_durable_task_for_each_exact_occurrence()
    {
        await using var db = fixture.CreateContext();
        var clock = new WorkflowClock(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        var authority = Authority(db, clock);
        RegisterApplication(db);
        var store = new SqliteTriggerSchedulingStore(db, clock, durableTasks: authority.Durable);
        var definition = RecurringDefinition(authority.State, clock.UtcNow);

        var appended = await store.AppendRecurringTriggerAsync(definition);
        var replay = await store.AppendRecurringTriggerAsync(definition);
        var first = await RecurringWorker(db, clock, authority)
            .RunBatchAsync("worker.workflow.recurring.first");
        clock.Advance(TimeSpan.FromDays(1));
        var second = await RecurringWorker(db, clock, authority)
            .RunBatchAsync("worker.workflow.recurring.second");

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, appended.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(1, first.Completed);
        Assert.Equal(1, second.Completed);
        var tasks = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
            .OrderBy(value => value.CreatedAtUtc).ToArrayAsync();
        Assert.Equal(2, tasks.Length);
        Assert.Equal(2, tasks.Select(value => value.CommandId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(tasks, task =>
        {
            Assert.Equal("procedure-workflow", task.Purpose);
            Assert.Equal(Procedure.ExactDefinitionId, task.DefinitionId);
            Assert.Equal(Assignment("Run the recurring schedule."), task.InputJson);
        });
        Assert.Equal(2, await db.Set<SystemTaskAiCeilingRecord>().CountAsync());
        Assert.Equal(2, await db.RecurringTriggerFireReceipts.CountAsync());
        Assert.Equal(2, await db.RecurringTriggerFireWork.CountAsync());
        Assert.Empty(db.Notifications);
        Assert.Equal(clock.UtcNow.AddDays(1).UtcDateTime,
            Assert.Single(db.RecurringTriggerState).NextOccurrenceAtUtc);
        var binding = Assert.Single(await db.Set<RecurringTriggerWorkflowBindingRecord>()
            .AsNoTracking().ToArrayAsync());
        Assert.Equal(definition.ProcedureWorkflow!.Fingerprint, binding.BindingFingerprint);
        Assert.Equal(ResultSchema, binding.ResultSchemaJson);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("legacy-schema")]
    [InlineData("stale-fingerprint")]
    public async Task Recurring_workflow_rejects_missing_legacy_or_stale_binding_without_side_effects(string corruption)
    {
        await using var db = fixture.CreateContext();
        var clock = new WorkflowClock(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        var authority = Authority(db, clock);
        RegisterApplication(db);
        await new SqliteTriggerSchedulingStore(db, clock, durableTasks: authority.Durable)
            .AppendRecurringTriggerAsync(RecurringDefinition(authority.State, clock.UtcNow));
        var sql = corruption switch
        {
            "missing" => "DELETE FROM trigger_recurring_workflow_binding",
            "legacy-schema" => "UPDATE trigger_recurring_workflow_binding SET ResultSchemaJson=NULL, ResultSchemaFingerprint=NULL",
            "stale-fingerprint" => $"UPDATE trigger_recurring_workflow_binding SET BindingFingerprint='{new string('B', 64)}'",
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        await db.Database.ExecuteSqlRawAsync(sql);
        db.ChangeTracker.Clear();

        var result = await RecurringWorker(db, clock, authority)
            .RunBatchAsync("worker.workflow.recurring.invalid");

        Assert.Equal(1, result.Failed);
        Assert.Empty(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());
        Assert.Empty(await db.Set<SystemTaskAiCeilingRecord>().ToArrayAsync());
        Assert.Empty(db.RecurringTriggerFireReceipts);
        Assert.Empty(db.Notifications);
        Assert.Equal("failed", Assert.Single(db.RecurringTriggerFireWork).State);
    }

    private static OneTimeTriggerDefinition Definition(StateSpaceView state, DateTimeOffset dueAt)
    {
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "workflow-trigger-test"),
            state.ApplicationRevision, state.StateSpaceId, "grant.jobs", "binding.jobs.workflow",
            InteractionStateRevision.From(state), InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(4, dueAt.AddHours(1).UtcDateTime));
        var workflow = TriggerProcedureWorkflowTarget.Create(host, Procedure,
            Assignment("Run the admitted schedule."), ResultSchema, TimeSpan.FromMinutes(2));
        return OneTimeTriggerDefinition.Create(App, "jobs.workflow", 1, dueAt,
            TriggerMisfirePolicy.FireOnce, TriggerFireTarget.ProcedureWorkflow,
            procedureWorkflow: workflow);
    }

    private static RecurringTriggerDefinition RecurringDefinition(StateSpaceView state, DateTimeOffset dueAt)
    {
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "workflow-trigger-test"),
            state.ApplicationRevision, state.StateSpaceId, "grant.jobs", "binding.jobs.recurring",
            InteractionStateRevision.From(state), InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(4, dueAt.AddHours(1).UtcDateTime));
        var workflow = TriggerProcedureWorkflowTarget.Create(host, Procedure,
            Assignment("Run the recurring schedule."), ResultSchema, TimeSpan.FromMinutes(2));
        return RecurringTriggerDefinition.Create(App, "jobs.workflow.recurring", 1,
            RecurrencePattern.Daily(1, TimeOnly.FromDateTime(dueAt.UtcDateTime), "Etc/UTC"),
            misfirePolicy: TriggerMisfirePolicy.FireOnce,
            target: TriggerFireTarget.ProcedureWorkflow,
            procedureWorkflow: workflow);
    }

    private static SqliteOneTimeTriggerWorker Worker(DantesRoleplayDbContext db, WorkflowClock clock,
        AuthorityFixture authority) => new(db, clock,
        new SqliteTriggerSchedulingStore(db, clock, durableTasks: authority.Durable),
        new SystemTaskTriggerTransactionParticipant(db,
            new TriggerNotificationTransactionParticipant(db, clock), authority.Durable,
            authority.ProcedureWorkers));

    private static SqliteRecurringTriggerWorker RecurringWorker(DantesRoleplayDbContext db,
        WorkflowClock clock, AuthorityFixture authority) => new(db, clock,
        new SystemTaskTriggerTransactionParticipant(db,
            new TriggerNotificationTransactionParticipant(db, clock), authority.Durable,
            authority.ProcedureWorkers));

    private static AuthorityFixture Authority(DantesRoleplayDbContext db, WorkflowClock clock)
    {
        var registry = new WorkflowStateSpaceRegistry();
        var policy = new WorkflowGrantPolicy(clock);
        var resolver = new WorkflowTargetResolver();
        return new(registry.State, policy,
            new SqliteSystemTaskDurableService(db, policy, resolver, registry, clock),
            new FixtureProcedureResolver());
    }

    private static string Assignment(string instruction) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            format = SystemInnerWorkerAssignmentV1.Format,
            instruction
        }));

    private sealed class FixtureProcedureResolver : ISystemInnerWorkerProcedureResolver
    {
        public Task<SystemInnerWorkerProcedurePreparationResult> ResolveAsync(
            SystemInnerWorkerRequest worker, CancellationToken cancellationToken = default)
        {
            var profile = new AiAgentProfile("web.inner", "Fixture inner worker",
                "Perform the selected fixture procedure.", "Follow the fixture procedure.");
            var schemaHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                worker.ResultSchemaJson)));
            var request = new AiRequest("codex", InteractionRoleProfile.Inner.Model, [],
                AiRequestKind.Task, AiReasoningEffort.Low,
                ResponseSchemaJson: worker.ResultSchemaJson, AllowedTools: [], MaximumToolRounds: 0,
                MaximumToolCalls: 0, MaximumDuration: TimeSpan.FromMinutes(2));
            var prepared = new SystemInnerWorkerPreparedRequest(profile, request,
                worker.ProcedureVersion!.Fingerprint, Hash, schemaHash, [], 1);
            return Task.FromResult(new SystemInnerWorkerProcedurePreparationResult(
                worker, prepared, new("web.inner", 1, Hash), [],
                new("manual.fixture", Hash), new SystemInnerWorkerAiBudget(toolCalls: 0),
                new(worker.InvocationHost.Principal, "fixture", "fixture-correlation")
                {
                    ApplicationId = worker.InvocationHost.ApplicationRevision.ApplicationId,
                    StateSpaceId = worker.InvocationHost.StateSpaceId!,
                    ResolutionFingerprint = Hash
                }));
        }
    }

    private static void RegisterApplication(DantesRoleplayDbContext db) =>
        new SqliteApplicationRegistry(db).Register(new(App, "Jobs", "Procedure workflow trigger tests.", []));

    private sealed record AuthorityFixture(StateSpaceView State, WorkflowGrantPolicy Policy,
        SqliteSystemTaskDurableService Durable,
        ISystemInnerWorkerProcedureResolver ProcedureWorkers);

    private sealed class WorkflowClock(DateTimeOffset value) : TimeProvider, ITriggerClock
    {
        private DateTimeOffset value = value;
        public DateTimeOffset UtcNow => value;
        public override DateTimeOffset GetUtcNow() => value;
        internal void Advance(TimeSpan duration) => value = value.Add(duration);
    }

    private sealed class WorkflowStateSpaceRegistry : IStateSpaceRegistry
    {
        internal StateSpaceView State { get; } = new("state.jobs",
            new ApplicationRevision(App, 1, Hash, []), new string('B', 64), 1,
            DateTime.UnixEpoch, DateTime.UnixEpoch);
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == State.StateSpaceId ? State : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId, int limit) =>
            new(applicationId == App ? [State] : [], null);
    }

    private sealed class WorkflowTargetResolver : IStandingGrantTargetResolver
    {
        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
        {
            var target = new StandingGrantDefinitionTarget(selection.DefinitionId, selection.Kind, App,
                CatalogNamespaceIdentity.NamespaceOf(selection.DefinitionId), "ownership.jobs",
                selection.Revision, selection.ContentFingerprint);
            return Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Available,
                "WORKFLOW_TARGET_AVAILABLE", target, new(1, Hash, 1, Hash)));
        }

        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
            string exactDefinitionId, string kind, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class WorkflowGrantPolicy(WorkflowClock clock) : IStandingGrantPolicy
    {
        internal bool Allowed { get; set; } = true;
        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
            StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
        {
            var grant = new StandingGrantRevision(host.GrantReference, "grant.family.jobs", 1, Hash,
                host.Principal.PrincipalId, App, StandingGrantScope.StateSpace, host.StateSpaceId,
                [StandingGrantCapability.Execute, StandingGrantCapability.ReadTask, StandingGrantCapability.CancelTask],
                new StandingGrantDefinitionAllowance(StandingGrantDefinitionMode.ExactIds,
                    [Procedure.ExactDefinitionId], []), [], 4,
                clock.UtcNow.AddHours(1).UtcDateTime, false, "operation.jobs");
            return Task.FromResult(new StandingGrantDecision(Allowed,
                Allowed ? "STANDING_GRANT_ALLOWED" : "STANDING_GRANT_DENIED", Allowed ? grant : null,
                new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, "workflow-trigger-test",
                    host.StateSpaceId!, host.CommandId, Allowed,
                    Allowed ? "STANDING_GRANT_ALLOWED" : "STANDING_GRANT_DENIED")));
        }
    }
}
