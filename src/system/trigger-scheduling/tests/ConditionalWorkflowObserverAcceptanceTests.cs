using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.TriggerScheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Theory]
    [InlineData("component")]
    [InlineData("outgoing-relationship")]
    public async Task Committed_ecs_change_runs_retained_catalog_predicate_and_durable_procedure_once(
        string changeKind)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"observer-acceptance-{Guid.NewGuid():N}.db");
        try
        {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite("Filename=" + databasePath).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration(
            "demo.runtime.pure", "human-domain-label", "Observer predicate acceptance fixture.",
            [CatalogNamespaceKinds.Mechanic, CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed observer predicate acceptance fixture."));
        const string predicateRequirements =
            "{\"inputSchema\":{\"additionalProperties\":false,\"properties\":{},\"type\":\"object\"}}";
        WritePureAction("""
            return { data: { matches:
                ctx.event.source === 'ecs-operation' &&
                ctx.event.effects.length === 1 &&
                ctx.event.effects[0].EntityId === 'world'
            } };
            """, predicateRequirements);
        await ActivateAsync(setup);

        var activation = setup.Activation.Current(Application)!;
        var application = setup.Applications.Get(Application)!;
        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var state = stateSpaces.Create(new("state", application, activation.ActivationFingerprint,
            activation.ResolutionFingerprint));
        await SeedInnerWorkerGrantAsync(db);

        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var componentRow = types.Define(new(Application, "demo.runtime.pure.observer-value",
            "{\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"integer\"}},\"required\":[\"value\"],\"type\":\"object\"}"));
        var component = new EcsComponentReference(
            componentRow.QualifiedId, componentRow.Version, componentRow.SchemaHash);
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        await entities.CreateEntityAsync(state.StateSpaceId, "world", "World");
        await entities.CreateEntityAsync(state.StateSpaceId, "other", "Other");
        await entities.AddComponentAsync(new(state.StateSpaceId, "world", component,
            "{\"value\":0}", 0));
        var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);

        var workflowHost = InnerWorkerHost(application, state,
            "observer-workflow-binding", DateTime.UtcNow.AddMinutes(2));
        var predicateReadHost = new InteractionInvocationHost(workflowHost.Principal,
            workflowHost.ApplicationRevision, workflowHost.StateSpaceId!,
            workflowHost.GrantReference, "observer-predicate-selection",
            workflowHost.StateRevision!, InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(1, workflowHost.Budget.DeadlineUtc));
        var mechanicResolution = await setup.Resolver.ResolveCurrentAsync(
            predicateReadHost,
            PureActionId, CatalogNamespaceKinds.Mechanic);
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, mechanicResolution.Status);
        var mechanicTarget = Assert.IsType<StandingGrantDefinitionTarget>(mechanicResolution.Target);
        var mechanicOrigin = Assert.IsType<StandingGrantActivationOrigin>(
            mechanicResolution.CurrentActivation);
        var canonicalRequirements = InteractionCanonicalJson.CanonicalizeObject(predicateRequirements);
        var selection = new ApplicationObserverPredicateSelection(Application,
            new(mechanicTarget.DefinitionId, mechanicTarget.Kind, mechanicTarget.Revision,
                mechanicTarget.ContentFingerprint),
            mechanicOrigin,
            Assert.Single(activation.Sources, value => value.SourceId == "catalog")
                .RegistrationFingerprint,
            canonicalRequirements, PredicateProof.Requirements(canonicalRequirements),
            new Dictionary<string, string>(StringComparer.Ordinal));

        var procedureFile = ProcedureFile.Parse(File.ReadAllText(Path.Combine(root,
            RelativePath.Replace('/', Path.DirectorySeparatorChar))), RelativePath);
        var procedure = await new ProcedureStore(db).WriteAsync(new WriteProcedureRequest
        {
            Id = procedureFile.Id,
            Category = procedureFile.Category,
            Name = procedureFile.Name,
            Description = procedureFile.Description,
            Governs = procedureFile.Governs,
            Matches = procedureFile.Matches,
            Instructions = "obsolete manual instructions",
            Constraints = procedureFile.Constraints,
            Status = procedureFile.Status,
            CreatedBy = "fixture",
            ChangeNote = "Observer workflow acceptance fixture."
        });
        StandingGrantTargetResolution procedureResolution;
        await using (var read = await db.Database.BeginTransactionAsync())
        {
            procedureResolution = await setup.Resolver.ResolveCurrentAsync(
                workflowHost, procedure.Procedure.Id, CatalogNamespaceKinds.Procedure);
            await read.CommitAsync();
        }
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, procedureResolution.Status);
        var procedureTarget = Assert.IsType<StandingGrantDefinitionTarget>(procedureResolution.Target);
        var selectedProcedure = new SystemTaskSelectedDefinition(procedureTarget.DefinitionId,
            procedureTarget.Revision, procedureTarget.ContentFingerprint);

        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications,
            setup.Activation, setup.Sources, setup.Roots, setup.Extensions).UsePreparationCache(
                new ActivatedApplicationCatalogSnapshotCache(),
                new ActivatedApplicationCatalogCacheAuthority());
        var catalogs = Catalogs(setup);
        var mapping = new ApplicationMechanicProjectionMappingResolver(catalogs, stateSpaces,
            types, edges);
        var projections = new ApplicationMechanicProjectionResolver(db, stateSpaces);
        var observedGrantPolicy = new ObservedStandingGrantPolicy(
            new SqliteStandingGrantPolicy(db, setup.Resolver));
        var actualPredicate = new CatalogJavaScriptObserverPredicateAdapter(db, setup.Activation,
            materializer, setup.Resolver, observedGrantPolicy,
            mapping, projections,
            new ApplicationMechanicEvaluator(catalogs, projections, new JintMechanicEngine()),
            schemas);
        var observedPredicate = new TransactionObservedPredicate(db, actualPredicate);

        var snapshots = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
            new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)), setup.Activation);
        var retrieval = new InteractionFeatureRetriever(snapshots, namespaces: setup.Namespaces,
            changes: setup.Activation);
        var interactionAuthority = new PrivateHostInteractionAuthorizationPolicy(stateSpaces);
        var contexts = new InteractionTaskContextMaterializer(interactionAuthority, retrieval,
            snapshots, new UnusedReadModels());
        var procedureResolver = new SystemInnerWorkerProcedureResolver(
            new InteractionEnvelopeFactory(setup.Applications, setup.Activation, stateSpaces,
                interactionAuthority),
            contexts, snapshots, new SystemInnerWorkerPreparation(new ProcedureStore(db), contexts),
            new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
                new("web.inner", "Inner AI", "Perform the bounded host-selected procedure.")
            ])), TimeProvider.System);
        var durable = new SqliteSystemTaskDurableService(db,
            observedGrantPolicy, setup.Resolver, stateSpaces,
            TimeProvider.System);
        var clock = new RuntimeTriggerClock(DateTimeOffset.UtcNow);
        var triggerStore = new SqliteConditionalTriggerStore(db, stateSpaces, types, entities,
            [], clock, durable);
        var source = new ConditionalTriggerEcsTransactionParticipant(db, triggerStore, clock,
            observedPredicate);
        var applier = new ApplicationEcsEffectApplier(db, entities, stateSpaces,
            new OperationLog(db), edges, [source]);
        const string resultSchema =
            "{\"additionalProperties\":false,\"properties\":{\"answer\":{\"type\":\"string\"},\"summary\":{\"type\":\"string\"}},\"required\":[\"answer\",\"summary\"],\"type\":\"object\"}";
        var assignment = JsonSerializer.Serialize(new
        {
            format = SystemInnerWorkerAssignmentV1.Format,
            instruction = "Inspect the committed observer change."
        });
        var workflow = TriggerProcedureWorkflowTarget.Create(workflowHost, selectedProcedure,
            assignment, resultSchema, TimeSpan.FromMinutes(2));
        var componentDependencies = changeKind == "component"
            ? new[] { ConditionalTriggerDependency.Create("world", component) }
            : [];
        var relationshipDependencies = changeKind == "outgoing-relationship"
            ? new[] { ConditionalTriggerRelationshipDependency.Create(
                "demo.runtime.pure.observer-link", "world", incoming: false) }
            : [];
        await triggerStore.AppendAsync(ConditionalTriggerDefinition.Create(Application,
            "demo.runtime.observer.acceptance", 1, ConditionalTriggerLifecycle.Active,
            ConditionalTriggerKind.StateCondition, ConditionalTriggerActivation.Level,
            ConditionalTriggerRearm.OnFalse, state.StateSpaceId, componentDependencies,
            ConditionalTriggerAdapterReference.Create(
                ConditionalTriggerObserverPredicate.StableAdapterId, 1),
            "{}", TriggerFireTarget.ProcedureWorkflow,
            TriggerNotificationTarget.Create("observer.acceptance", "Observer acceptance"),
            relationshipDependencies,
            ConditionalTriggerObserverPredicate.Create(selection, maximumOperationsPerFire: 16),
            workflow));

        var effect = changeKind == "component"
            ? new ApplicationEcsEffect
            {
                Type = ApplicationEcsEffectType.ComponentSet,
                EntityId = "world",
                ComponentType = component,
                DataJson = "{\"value\":1}",
                ExpectedRevision = 1
            }
            : new ApplicationEcsEffect
            {
                Type = ApplicationEcsEffectType.RelationshipSet,
                EntityId = "world",
                TargetEntityId = "other",
                QualifiedRelationshipKind = "demo.runtime.pure.observer-link",
                DataJson = "{}",
                ExpectedRevision = 0
            };
        var batch = new ApplicationEcsEffectBatch
        {
            StateSpaceId = state.StateSpaceId,
            ExecutionIdentity = new(changeKind == "component" ? new string('c', 32) : new string('d', 32),
                new string('A', 64)),
            Effects = [effect]
        };

        var applied = await applier.ApplyAsync(batch);
        Assert.True(applied.Applied,
            string.Join(';', applied.Problems.Select(problem => problem.Code + ":" + problem.Message)));
        var sourceReplay = await applier.ApplyAsync(batch);

        Assert.True(sourceReplay.Replayed);
        Assert.Equal(1, observedPredicate.Captures);
        var staged = Assert.Single(await db.ConditionalTriggerFireWork.AsNoTracking().ToArrayAsync());
        Assert.Equal(applied.OperationId, staged.ChangeOperationId);
        Assert.False(string.IsNullOrWhiteSpace(staged.PredicateCaptureJson));

        var triggerWorker = new SqliteConditionalTriggerWorker(db, clock,
            new SystemTaskTriggerTransactionParticipant(db,
                new TriggerNotificationTransactionParticipant(db, clock), durable,
                procedureResolver), observedPredicate);
        var fired = await triggerWorker.RunBatchAsync("observer-acceptance");
        var fireReplay = await triggerWorker.RunBatchAsync("observer-acceptance-replay");

        Assert.Equal(1, fired.Completed);
        Assert.Equal(0, fireReplay.Examined);
        Assert.Equal(1, observedPredicate.Evaluations);
        Assert.True(observedPredicate.EvaluatedOutsideWriter);
        Assert.Contains(StandingGrantCapability.Read, observedGrantPolicy.CheckedCapabilities);
        Assert.Contains(StandingGrantCapability.Execute, observedGrantPolicy.CheckedCapabilities);
        var receipt = Assert.Single(await db.ConditionalTriggerFireReceipts.AsNoTracking()
            .ToArrayAsync());
        Assert.Equal(staged.FireId, receipt.Id);
        Assert.Equal(applied.OperationId, receipt.ChangeOperationId);
        Assert.Equal("due", receipt.Disposition);
        var allowance = Assert.Single(await db.TriggerCausalAllowances.AsNoTracking().ToArrayAsync());
        Assert.Equal(64, allowance.MaximumOperations);
        Assert.Equal(16, allowance.ReservedOperations);
        Assert.Equal(16, Assert.Single(await db.TriggerCausalReservations.AsNoTracking()
            .ToArrayAsync()).Operations);
        var task = Assert.Single(await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
            .ToArrayAsync());
        Assert.Equal(16, task.AdmittedOperations);
        Assert.Single(await db.Set<SystemTaskAiCeilingRecord>().AsNoTracking().ToArrayAsync());

        var provider = new SuccessfulProcedureProvider(procedureFile.Instructions,
            procedureFile.Constraints);
        var invoker = new SystemInnerWorkerProcedureInvoker(
            new SystemAiAgentService([], new AiService([provider])));
        using var lifecycleServices = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(durable)
            .AddSingleton(procedureResolver)
            .AddSingleton(invoker)
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .AddSingleton<SystemTaskAiInvocationLifecycleFactory>()
            .AddScoped<SystemInnerWorkerProcedureExecutor>()
            .AddSingleton<SystemTaskWorkflowBackgroundWorker>()
            .BuildServiceProvider();
        var workflowWorker = lifecycleServices
            .GetRequiredService<SystemTaskWorkflowBackgroundWorker>();

        Assert.True(await workflowWorker.RunOnceAsync("observer-procedure"));
        Assert.False(await workflowWorker.RunOnceAsync("observer-procedure-replay"));
        var result = await new SystemInnerWorkerService(procedureResolver, durable).GetAsync(
            InnerWorkerHost(application, state, "observer-result-read",
                DateTime.UtcNow.AddMinutes(2)),
            new(task.TaskId, task.CommandId));

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Contains("\"answer\":\"42\"", result.DataJson, StringComparison.Ordinal);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, await db.Set<SystemTaskAiDispatchEvidenceRecord>().CountAsync(value =>
            value.Kind == "dispatch" && value.DispatchKind == "provider"));
        Assert.Equal(1, await db.Set<SystemTaskAiDispatchEvidenceRecord>().CountAsync(value =>
            value.Kind == "usage" && value.IsComplete == 1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
        }
    }

    private sealed class TransactionObservedPredicate(
        DantesRoleplayDbContext db,
        CatalogJavaScriptObserverPredicateAdapter inner)
        : IApplicationObserverPredicateInputCapture, IApplicationObserverPredicateEvaluator
    {
        internal int Captures { get; private set; }
        internal int Evaluations { get; private set; }
        internal bool EvaluatedOutsideWriter { get; private set; }

        public async Task<ApplicationObserverPredicateCapturedInput> CaptureAsync(
            InteractionInvocationHost host,
            ApplicationObserverPredicateSelection selection,
            string admittedInputJson,
            string? admittedEventJson,
            long seed,
            CancellationToken cancellationToken = default)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            Captures++;
            return await inner.CaptureAsync(host, selection, admittedInputJson,
                admittedEventJson, seed, cancellationToken);
        }

        public async Task<ApplicationObserverPredicateResult> EvaluateAsync(
            InteractionInvocationHost freshHost,
            ApplicationObserverPredicateCapturedInput captured,
            CancellationToken cancellationToken = default)
        {
            EvaluatedOutsideWriter = db.Database.CurrentTransaction is null;
            Assert.True(EvaluatedOutsideWriter);
            Evaluations++;
            return await inner.EvaluateAsync(freshHost, captured, cancellationToken);
        }
    }

    private sealed class ObservedStandingGrantPolicy(IStandingGrantPolicy inner)
        : IStandingGrantPolicy
    {
        internal HashSet<StandingGrantCapability> CheckedCapabilities { get; } = [];

        public async Task<StandingGrantDecision> EvaluateAsync(
            InteractionInvocationHost host,
            StandingGrantRequirement requirement,
            CancellationToken cancellationToken = default)
        {
            var decision = await inner.EvaluateAsync(host, requirement, cancellationToken);
            if (decision.Allowed)
                CheckedCapabilities.Add(requirement.Capability);
            return decision;
        }
    }
}
