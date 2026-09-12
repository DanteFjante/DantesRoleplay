using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.AI;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class ConditionalWorkflowObserverTests : IDisposable
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("observer");
    private readonly SqliteFixture fixture = new();

    [Fact]
    public async Task Component_change_captures_once_outside_jint_and_matching_worker_routes_retained_workflow()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, matches: true);
        await setup.Store.AppendAsync(Definition(setup, 1, ConditionalTriggerLifecycle.Active,
            [ConditionalTriggerDependency.Create("world", setup.Type)], []));

        var applied = await setup.Applier.ApplyAsync(Batch(setup.Type, 1, 'a'));
        var first = await setup.Worker.RunBatchAsync("observer.component");
        var second = await setup.Worker.RunBatchAsync("observer.component.replay");

        Assert.True(applied.Applied);
        Assert.Equal(1, setup.Capture.Calls);
        Assert.Equal(1, setup.Evaluator.Calls);
        Assert.Equal(1, first.Completed);
        Assert.Equal(0, second.Examined);
        var lease = Assert.Single(setup.Target.Leases);
        Assert.Equal(TriggerFireTarget.ProcedureWorkflow, lease.Target);
        Assert.Equal(1, lease.TriggerVersion);
        Assert.Single(db.TriggerCausalAllowances);
        Assert.Equal(64, db.TriggerCausalAllowances.Single().MaximumOperations);
        Assert.Equal("due", Assert.Single(db.ConditionalTriggerFireReceipts).Disposition);
    }

    [Fact]
    public async Task False_and_unrelated_changes_do_not_route_a_workflow()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, matches: false);
        await setup.Store.AppendAsync(Definition(setup, 1, ConditionalTriggerLifecycle.Active,
            [ConditionalTriggerDependency.Create("world", setup.Type)], []));

        await setup.Applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = "observer-space", ExecutionIdentity = Identity('b'),
            Effects = [new ApplicationEcsEffect
            {
                Type = ApplicationEcsEffectType.ComponentSet, EntityId = "world",
                ComponentType = setup.OtherType, DataJson = "{\"value\":2}", ExpectedRevision = 1
            }]
        });
        Assert.Empty(db.ConditionalTriggerFireWork);
        await setup.Applier.ApplyAsync(Batch(setup.Type, 1, 'c'));

        var result = await setup.Worker.RunBatchAsync("observer.false");

        Assert.Equal(1, result.Completed);
        Assert.Empty(setup.Target.Leases);
        Assert.Equal("not-matched", Assert.Single(db.ConditionalTriggerFireReceipts).Disposition);
    }

    [Theory]
    [InlineData(ConditionalTriggerLifecycle.Active)]
    [InlineData(ConditionalTriggerLifecycle.Paused)]
    [InlineData(ConditionalTriggerLifecycle.Cancelled)]
    public async Task Queued_observer_retains_revision_when_future_definition_changes(
        ConditionalTriggerLifecycle nextLifecycle)
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, matches: true);
        var dependencies = new[] { ConditionalTriggerDependency.Create("world", setup.Type) };
        await setup.Store.AppendAsync(Definition(setup, 1, ConditionalTriggerLifecycle.Active,
            dependencies, []));
        await setup.Applier.ApplyAsync(Batch(setup.Type, 1, 'd'));
        await setup.Store.AppendAsync(Definition(setup, 2, nextLifecycle, dependencies, []));

        var result = await setup.Worker.RunBatchAsync("observer.retained");

        Assert.Equal(1, result.Completed);
        Assert.Equal(1, Assert.Single(setup.Target.Leases).TriggerVersion);
    }

    [Fact]
    public async Task Relationship_change_matches_the_declared_direction_and_anchor()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, matches: true);
        var relationships = new[]
        {
            ConditionalTriggerRelationshipDependency.Create("observer.knows", "world")
        };
        await setup.Store.AppendAsync(Definition(setup, 1, ConditionalTriggerLifecycle.Active,
            [], relationships));
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await setup.Source.StageAsync(new ApplicationEcsEffectBatch
            {
                StateSpaceId = "observer-space",
                Effects = [new ApplicationEcsEffect
                {
                    Type = ApplicationEcsEffectType.RelationshipSet, EntityId = "world",
                    TargetEntityId = "other", QualifiedRelationshipKind = "observer.knows"
                }]
            }, [new ApplicationEcsEffectReceipt(0, ApplicationEcsEffectType.RelationshipSet,
                "world", "", 1, TargetEntityId: "other",
                QualifiedRelationshipKind: "observer.knows")], new string('e', 32));
            await transaction.CommitAsync();
        }

        var result = await setup.Worker.RunBatchAsync("observer.relationship");

        Assert.Equal(1, result.Completed);
        Assert.Single(setup.Target.Leases);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Causal_reservation_is_narrowed_by_current_grant_and_transferred_to_actual_task(
        int grantMaximum)
    {
        await using var db = fixture.CreateContext();
        var setup = await ActualSetupAsync(db, grantMaximum);
        await setup.Store.AppendAsync(Definition(setup.Setup, 1, ConditionalTriggerLifecycle.Active,
            [ConditionalTriggerDependency.Create("world", setup.Setup.Type)], [], maximumOperationsPerFire: 8));
        await setup.Setup.Applier.ApplyAsync(Batch(setup.Setup.Type, 1, 'f'));

        var result = await setup.Worker.RunBatchAsync("observer.actual.narrowed");
        var replay = await setup.Worker.RunBatchAsync("observer.actual.narrowed.replay");

        Assert.Equal(1, result.Completed);
        Assert.Equal(0, replay.Examined);
        Assert.Equal(grantMaximum, Assert.Single(db.TriggerCausalReservations).Operations);
        Assert.Equal(grantMaximum, Assert.Single(db.TriggerCausalAllowances).ReservedOperations);
        Assert.Equal(grantMaximum,
            Assert.Single(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync()).AdmittedOperations);
        Assert.Single(await db.Set<SystemTaskAiCeilingRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Grant_revoked_after_source_commit_blocks_task_and_causal_debit()
    {
        await using var db = fixture.CreateContext();
        var setup = await ActualSetupAsync(db, grantMaximum: 8);
        await setup.Store.AppendAsync(Definition(setup.Setup, 1, ConditionalTriggerLifecycle.Active,
            [ConditionalTriggerDependency.Create("world", setup.Setup.Type)], [], maximumOperationsPerFire: 4));

        var applied = await setup.Setup.Applier.ApplyAsync(Batch(setup.Setup.Type, 1, '0'));
        setup.Policy.Allowed = false;
        var result = await setup.Worker.RunBatchAsync("observer.actual.revoked");

        Assert.True(applied.Applied);
        Assert.Equal(1, result.Failed);
        Assert.Empty(db.TriggerCausalReservations);
        Assert.Equal(0, Assert.Single(db.TriggerCausalAllowances).ReservedOperations);
        Assert.Empty(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Enrollment_failure_rolls_back_causal_debit_and_retry_reserves_once()
    {
        await using var db = fixture.CreateContext();
        var setup = await ActualSetupAsync(db, grantMaximum: 8);
        await setup.Store.AppendAsync(Definition(setup.Setup, 1, ConditionalTriggerLifecycle.Active,
            [ConditionalTriggerDependency.Create("world", setup.Setup.Type)], [], maximumOperationsPerFire: 4));
        await setup.Setup.Applier.ApplyAsync(Batch(setup.Setup.Type, 1, '1'));
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_observer_ai_enrollment
            BEFORE INSERT ON system_task_ai_ceiling
            BEGIN SELECT RAISE(ABORT, 'fixture enrollment failure'); END;
            """);

        var failed = await setup.Worker.RunBatchAsync("observer.actual.rollback");

        Assert.Equal(1, failed.Retried);
        Assert.Empty(db.TriggerCausalReservations);
        Assert.Equal(0, Assert.Single(db.TriggerCausalAllowances).ReservedOperations);
        Assert.Empty(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_observer_ai_enrollment");
        setup.Clock.Advance(SqliteConditionalTriggerWorker.FirstRetryDelay);

        var retried = await setup.Worker.RunBatchAsync("observer.actual.retry");

        Assert.Equal(1, retried.Completed);
        Assert.Equal(4, Assert.Single(db.TriggerCausalReservations).Operations);
        Assert.Equal(4, Assert.Single(db.TriggerCausalAllowances).ReservedOperations);
        Assert.Single(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Shared_source_allowance_bounds_fan_out_without_rolling_back_source_operation()
    {
        await using var db = fixture.CreateContext();
        var setup = await ActualSetupAsync(db, grantMaximum: 16);
        var dependency = new[] { ConditionalTriggerDependency.Create("world", setup.Setup.Type) };
        for (var index = 0; index < 9; index++)
            await setup.Store.AppendAsync(Definition(setup.Setup, 1, ConditionalTriggerLifecycle.Active,
                dependency, [], maximumOperationsPerFire: 8,
                id: $"observer.workflow.{(char)('a' + index)}"));

        var applied = await setup.Setup.Applier.ApplyAsync(Batch(setup.Setup.Type, 1, '2'));
        var first = await setup.Worker.RunBatchAsync("observer.actual.fanout.1");
        var second = await setup.Worker.RunBatchAsync("observer.actual.fanout.2");

        Assert.True(applied.Applied);
        Assert.Equal(8, first.Completed);
        Assert.Equal(1, second.Failed);
        Assert.Equal(64, Assert.Single(db.TriggerCausalAllowances).ReservedOperations);
        Assert.Equal(8, await db.TriggerCausalReservations.CountAsync());
        Assert.Equal(8, await db.Set<SystemTaskLifecycleRecord>().CountAsync());
        Assert.Equal(1, await db.ConditionalTriggerFireWork.CountAsync(value =>
            value.State == "failed" && value.FailureKind == "permanent-handler"));
    }

    private static ConditionalTriggerDefinition Definition(Setup setup, int version,
        ConditionalTriggerLifecycle lifecycle, IReadOnlyList<ConditionalTriggerDependency> components,
        IReadOnlyList<ConditionalTriggerRelationshipDependency> relationships,
        int maximumOperationsPerFire = 4, string id = "observer.workflow")
    {
        var workflowHost = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "test"), setup.State.ApplicationRevision,
            setup.State.StateSpaceId, "grant.observer", "observer-binding",
            InteractionStateRevision.From(setup.State), InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, setup.Clock.UtcNow.AddMinutes(5).UtcDateTime));
        var workflow = TriggerProcedureWorkflowTarget.Create(workflowHost,
            new SystemTaskSelectedDefinition("observer.procedure", 1, Hash),
            "{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"Run.\"}",
            "{\"type\":\"object\"}", TimeSpan.FromMinutes(2));
        var requirements = "{}";
        var selection = new ApplicationObserverPredicateSelection(App,
            new("observer.predicate", "mechanic", 1, Hash),
            new(1, Hash, 1, setup.State.ApplicationRevision.Fingerprint), Hash,
            requirements, PredicateProof.Requirements(requirements),
            new Dictionary<string, string>());
        return ConditionalTriggerDefinition.Create(App, id, version, lifecycle,
            ConditionalTriggerKind.StateCondition, ConditionalTriggerActivation.Level,
            ConditionalTriggerRearm.OnFalse, setup.State.StateSpaceId, components,
            ConditionalTriggerAdapterReference.Create(ConditionalTriggerObserverPredicate.StableAdapterId, 1),
            "{}", TriggerFireTarget.ProcedureWorkflow,
            TriggerNotificationTarget.Create("observer", "Observer"), relationships,
            ConditionalTriggerObserverPredicate.Create(selection, maximumOperationsPerFire), workflow);
    }

    private static async Task<Setup> SetupAsync(DantesRoleplayDbContext db, bool matches)
    {
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(App, "Observer", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        var state = spaces.Create(new("observer-space", revision, Hash));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var typeRow = types.Define(new(App, "observer.value",
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var otherRow = types.Define(new(App, "observer.other",
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var type = new EcsComponentReference(typeRow.QualifiedId, typeRow.Version, typeRow.SchemaHash);
        var otherType = new EcsComponentReference(otherRow.QualifiedId, otherRow.Version, otherRow.SchemaHash);
        var components = new SqliteEntityComponentStore(db, types, schemas);
        await components.CreateEntityAsync(state.StateSpaceId, "world", "World");
        await components.CreateEntityAsync(state.StateSpaceId, "other", "Other");
        await components.AddComponentAsync(new(state.StateSpaceId, "world", type, "{\"value\":0}", 0));
        await components.AddComponentAsync(new(state.StateSpaceId, "world", otherType, "{\"value\":1}", 0));
        var clock = new ObserverClock(DateTimeOffset.UtcNow);
        var durable = new SqliteSystemTaskDurableService(db, new GrantPolicy(clock, 16),
            new TargetResolver(), spaces, clock);
        var store = new SqliteConditionalTriggerStore(db, spaces, types, components, [], clock, durable);
        var capture = new Capture(db);
        var source = new ConditionalTriggerEcsTransactionParticipant(db, store, clock, capture);
        var applier = new ApplicationEcsEffectApplier(db, components, spaces,
            new OperationLog(db), null, [source]);
        var target = new Target();
        var evaluator = new Evaluator(db, matches);
        var worker = new SqliteConditionalTriggerWorker(db, clock, target, evaluator);
        return new(state, type, otherType, store, source, applier, worker, capture, evaluator, target, clock);
    }

    private static async Task<ActualSetup> ActualSetupAsync(DantesRoleplayDbContext db, int grantMaximum)
    {
        var clock = new ObserverClock(DateTimeOffset.UtcNow);
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(App, "Observer", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        var state = spaces.Create(new("observer-space", revision, Hash));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var typeRow = types.Define(new(App, "observer.value",
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var otherRow = types.Define(new(App, "observer.other",
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var type = new EcsComponentReference(typeRow.QualifiedId, typeRow.Version, typeRow.SchemaHash);
        var otherType = new EcsComponentReference(otherRow.QualifiedId, otherRow.Version, otherRow.SchemaHash);
        var components = new SqliteEntityComponentStore(db, types, schemas);
        await components.CreateEntityAsync(state.StateSpaceId, "world", "World");
        await components.CreateEntityAsync(state.StateSpaceId, "other", "Other");
        await components.AddComponentAsync(new(state.StateSpaceId, "world", type, "{\"value\":0}", 0));
        await components.AddComponentAsync(new(state.StateSpaceId, "world", otherType, "{\"value\":1}", 0));
        var policy = new GrantPolicy(clock, grantMaximum);
        var durable = new SqliteSystemTaskDurableService(db, policy,
            new TargetResolver(), spaces, clock);
        var store = new SqliteConditionalTriggerStore(db, spaces, types, components, [], clock, durable);
        var capture = new Capture(db);
        var source = new ConditionalTriggerEcsTransactionParticipant(db, store, clock, capture);
        var applier = new ApplicationEcsEffectApplier(db, components, spaces,
            new OperationLog(db), null, [source]);
        var evaluator = new Evaluator(db, true);
        var target = new SystemTaskTriggerTransactionParticipant(db,
            new TriggerNotificationTransactionParticipant(db, clock), durable, new ProcedureResolver());
        var worker = new SqliteConditionalTriggerWorker(db, clock, target, evaluator);
        return new(new(state, type, otherType, store, source, applier, worker,
            capture, evaluator, new Target(), clock), worker, clock, policy);
    }

    private static ApplicationEcsEffectBatch Batch(EcsComponentReference type, int expected, char id) => new()
    {
        StateSpaceId = "observer-space", ExecutionIdentity = Identity(id),
        Effects = [new ApplicationEcsEffect
        {
            Type = ApplicationEcsEffectType.ComponentSet, EntityId = "world",
            ComponentType = type, DataJson = "{\"value\":1}", ExpectedRevision = expected
        }]
    };

    private static ApplicationEcsExecutionIdentity Identity(char value) =>
        new(new string(value, 32), new string(char.ToUpperInvariant(value), 64));

    public void Dispose() => fixture.Dispose();

    private sealed class Capture(DantesRoleplayDbContext db) : IApplicationObserverPredicateInputCapture
    {
        public int Calls { get; private set; }
        public Task<ApplicationObserverPredicateCapturedInput> CaptureAsync(InteractionInvocationHost host,
            ApplicationObserverPredicateSelection selection, string admittedInputJson,
            string? admittedEventJson, long seed, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            Calls++;
            return Task.FromResult(new ApplicationObserverPredicateCapturedInput(selection,
                new MechanicProjection { StateSpaceId = host.StateSpaceId!, Input = admittedInputJson,
                    Event = admittedEventJson!, Seed = seed }, Hash, Hash, Hash, Hash));
        }
    }

    private sealed class Evaluator(DantesRoleplayDbContext db, bool matches) : IApplicationObserverPredicateEvaluator
    {
        public int Calls { get; private set; }
        public Task<ApplicationObserverPredicateResult> EvaluateAsync(InteractionInvocationHost freshHost,
            ApplicationObserverPredicateCapturedInput captured, CancellationToken cancellationToken = default)
        {
            Assert.Null(db.Database.CurrentTransaction);
            Calls++;
            return Task.FromResult(new ApplicationObserverPredicateResult(true, matches,
                "OBSERVER_PREDICATE_EVALUATED"));
        }
    }

    private sealed class Target : ITriggerFireTransactionParticipant
    {
        public List<TriggerFireLease> Leases { get; } = [];
        public bool IsAvailable => true;
        public Task<TriggerFireAttemptResult> StageAsync(TriggerFireLease lease,
            CancellationToken cancellationToken = default)
        {
            Leases.Add(lease);
            return Task.FromResult(TriggerFireAttemptResult.Succeeded());
        }
    }

    private sealed class ObserverClock(DateTimeOffset value) : TimeProvider, ITriggerClock
    {
        private DateTimeOffset value = value;
        public DateTimeOffset UtcNow => value;
        public override DateTimeOffset GetUtcNow() => value;
        public void Advance(TimeSpan duration) => value = value.Add(duration);
    }

    private sealed class TargetResolver : IStandingGrantTargetResolver
    {
        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Available,
                "AVAILABLE", new StandingGrantDefinitionTarget(selection.DefinitionId, selection.Kind, App,
                    "observer", "observer-owner", selection.Revision, selection.ContentFingerprint),
                new StandingGrantActivationOrigin(1, Hash, host.ApplicationRevision.Revision,
                    host.ApplicationRevision.Fingerprint)));

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            DantesRoleplay.ApplicationActivation.ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class GrantPolicy(ObserverClock clock, int maximum) : IStandingGrantPolicy
    {
        internal bool Allowed { get; set; } = true;
        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
            StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
        {
            var grant = new StandingGrantRevision(host.GrantReference, "observer-grant", 1, Hash,
                host.Principal.PrincipalId, App, StandingGrantScope.StateSpace, host.StateSpaceId,
                [StandingGrantCapability.Execute],
                new StandingGrantDefinitionAllowance(StandingGrantDefinitionMode.ExactIds,
                    ["observer.procedure"], []), [], maximum,
                clock.UtcNow.AddHours(1).UtcDateTime, false, "observer-operation");
            return Task.FromResult(new StandingGrantDecision(Allowed,
                Allowed ? "ALLOWED" : "STANDING_GRANT_DENIED", Allowed ? grant : null,
                new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, "execute",
                    host.StateSpaceId!, host.CommandId, Allowed,
                    Allowed ? "ALLOWED" : "STANDING_GRANT_DENIED")));
        }
    }

    private sealed class ProcedureResolver : ISystemInnerWorkerProcedureResolver
    {
        public Task<SystemInnerWorkerProcedurePreparationResult> ResolveAsync(SystemInnerWorkerRequest worker,
            CancellationToken cancellationToken = default)
        {
            var profile = new AiAgentProfile("web.inner", "Observer worker", "Run.", "Run.");
            var schemaHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(worker.ResultSchemaJson)));
            var request = new AiRequest("codex", InteractionRoleProfile.Inner.Model, [], AiRequestKind.Task,
                AiReasoningEffort.Low, ResponseSchemaJson: worker.ResultSchemaJson, AllowedTools: [],
                MaximumToolRounds: 0, MaximumToolCalls: 0, MaximumDuration: TimeSpan.FromMinutes(2));
            var prepared = new SystemInnerWorkerPreparedRequest(profile, request,
                worker.ProcedureVersion!.Fingerprint, Hash, schemaHash, [], 1);
            return Task.FromResult(new SystemInnerWorkerProcedurePreparationResult(worker, prepared,
                new("web.inner", 1, Hash), [], new("manual.observer", Hash),
                new SystemInnerWorkerAiBudget(toolCalls: 0),
                new(worker.InvocationHost.Principal, "observer", "observer-correlation")
                {
                    ApplicationId = App, StateSpaceId = worker.InvocationHost.StateSpaceId!,
                    ResolutionFingerprint = Hash
                }, []));
        }
    }

    private sealed record Setup(StateSpaceView State, EcsComponentReference Type,
        EcsComponentReference OtherType, SqliteConditionalTriggerStore Store,
        ConditionalTriggerEcsTransactionParticipant Source, ApplicationEcsEffectApplier Applier,
        SqliteConditionalTriggerWorker Worker, Capture Capture, Evaluator Evaluator, Target Target,
        ITriggerClock Clock);
    private sealed record ActualSetup(Setup Setup, SqliteConditionalTriggerWorker Worker,
        ObserverClock Clock, GrantPolicy Policy)
    {
        public SqliteConditionalTriggerStore Store => Setup.Store;
    }
}
