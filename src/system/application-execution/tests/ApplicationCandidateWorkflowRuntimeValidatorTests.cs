using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string WorkflowServiceId = "demo.workflow.service";
    private const string WorkflowReadId = "demo.workflow.read";
    private const string WorkflowQueryId = "demo.workflow.query";
    private const string WorkflowActionId = "demo.workflow.create";
    private const string WorkflowSpaceId = "workflow-space";
    private const string WorkflowServiceSourcePath = "content/mechanics/workflow-service.js";

    [Fact]
    public async Task Workflow_candidate_runs_real_reads_and_validates_catalog_job_without_procedure_store_row()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"workflow-publication-{Guid.NewGuid():N}.db");
        DantesRoleplayDbContext? db = null;
        try
        {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite("Filename=" + databasePath).Options;
        db = new DantesRoleplayDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration(
            "demo.workflow", "human-domain-label", "Workflow validation fixture.",
            [CatalogNamespaceKinds.Mechanic, CatalogNamespaceKinds.Query],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        WriteWorkflowMechanic("workflow-read", WorkflowReadId, "{}",
            "return {data:{value:7}};");
        WriteWorkflowMechanic("workflow-action", WorkflowActionId, "{}",
            "return {data:{entityId:ctx.input.entityId},effects:[{type:'entity.create',entityId:ctx.input.entityId,name:'Candidate'}]};");
        await ActivateAsync(setup);

        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications,
            setup.Activation, setup.Sources, setup.Roots, setup.Extensions);
        var first = materializer.Build(Application).Records;
        var readRecord = first.Single(value => value.QualifiedId == WorkflowReadId);
        var actionRecord = first.Single(value => value.QualifiedId == WorkflowActionId);
        var procedureRecord = first.Single(value => value.QualifiedId == "demo.runtime.inspect");
        var schemas = new BoundedJsonSchemaValidator();
        var serviceInput = schemas.Compile("{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"mode\",\"entityId\",\"assignment\"],\"properties\":{\"mode\":{\"enum\":[\"action\",\"job\"]},\"entityId\":{\"type\":\"string\"},\"assignment\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"format\",\"instruction\"],\"properties\":{\"format\":{\"const\":\"dantes-roleplay/inner-procedure-assignment/v1\"},\"instruction\":{\"type\":\"string\"}}}}}");
        var readOutput = schemas.Compile("{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}");
        var actionOutput = schemas.Compile("{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"entityId\"],\"properties\":{\"entityId\":{\"type\":\"string\"}}}");
        var queryJson = JsonSerializer.Serialize(new
        {
            id = WorkflowQueryId,
            category = "workflow.fixture",
            name = "Workflow read",
            description = "Read a bounded fixture value.",
            matches = new[] { "read workflow fixture" },
            roles = new Dictionary<string, string>(),
            executor = ApplicationQueryContract.MechanicProjectionExecutor,
            projection = new
            {
                qualifiedId = WorkflowReadId,
                version = readRecord.Version,
                contentHash = readRecord.ContentFingerprint,
                outputSchemaHash = readOutput.SchemaHash
            },
            outputSchema = JsonDocument.Parse(readOutput.NormalizedSchema).RootElement,
            exposure = "model-visible",
            status = "active"
        });
        WriteWorkflowFile("content/queries/workflow-query.json", queryJson);
        var query = ApplicationQueryContract.Parse(queryJson, Application);
        var contract = new InteractionQueryContractReference(query.Executor,
            query.ProjectionQualifiedId, query.ProjectionVersion, query.ProjectionContentHash,
            query.OutputSchemaHash, query.OutputSchemaJson, query.Exposure, query.Roles.Keys);
        var service = new ApplicationReadOnlyServiceDefinition(serviceInput.SchemaHash,
            serviceInput.NormalizedSchema, actionOutput.SchemaHash, actionOutput.NormalizedSchema,
            [new("read", WorkflowQueryId, contract, new Dictionary<string, string>(), schemas,
                readOutput.NormalizedSchema)], schemas,
            [new("create", WorkflowActionId, actionRecord.Version, actionRecord.ContentFingerprint,
                new Dictionary<string, string>())],
            [new("inspect", "demo.runtime.inspect", procedureRecord.Version,
                procedureRecord.ContentFingerprint, actionOutput.SchemaHash,
                actionOutput.NormalizedSchema, schemas)]);
        await ActivateAsync(setup, setup.Activation.Current(Application)!.ActivationFingerprint);
        Assert.Null(await new ProcedureStore(db).GetAsync("demo.runtime.inspect", procedureRecord.Version));
        await SeedWorkflowAuthoringGrantAsync(db);

        var active = setup.Activation.Current(Application)!;
        var revision = new ApplicationRevision(Application, 1,
            setup.Applications.Get(Application)!.Fingerprint, []);
        var spaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var state = spaces.Create(new(WorkflowSpaceId, revision,
            active.ActivationFingerprint, active.ResolutionFingerprint));
        await SeedWorkflowStateGrantAsync(db);
        db.ChangeTracker.Clear();
        var candidateSource = "if(ctx.input.mode==='job'){ctx.services.read('read',{});ctx.services.job('inspect',ctx.input.assignment);return {data:{entityId:'must-not-return'}};}"
            + "ctx.services.action('create',{entityId:ctx.input.entityId});"
            + "ctx.services.read('read',{});"
            + "ctx.services.action('create',{entityId:'must-not-run'});"
            + "return {data:{entityId:'must-not-return'}};";
        var authoring = Service(db, setup);
        var candidateMarkdown = WorkflowMechanicMarkdown("workflow-service", WorkflowServiceId,
            "{\"service\":" + service.ToJson() + "}");
        var written = await authoring.WriteCandidateAsync(
            WorkflowAuthoringHost(setup, "workflow-write", 16),
            new(null, 0, active.ActivationFingerprint, "runtime", null,
                "Validate a new stateful workflow service.",
                [
                    new("file:content/mechanics/workflow-service.md", "catalog",
                        "content/mechanics/workflow-service.md", "text/markdown", candidateMarkdown),
                    new("file:" + WorkflowServiceSourcePath, "catalog", WorkflowServiceSourcePath,
                        "text/javascript", candidateSource)
                ]));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleAsync();
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId,
            row.Revision, row.ContentFingerprint);
        var selected = await new ApplicationCandidateSelectionReader(db, setup.Applications,
            setup.Activation, setup.Resolver).ReadAsync(
                WorkflowAuthoringHost(setup, "workflow-selection", 16), candidate);
        var definition = new StandingGrantDefinitionReference(WorkflowServiceId, "mechanic",
            selected!.Targets[0].Revision, selected.Targets[0].ContentFingerprint);

        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
            new CatalogCursorCodec(new byte[32]), setup.Activation);
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        var edges = new SqliteStateSpaceEdgeStore(db, spaces);
        var engine = new JintMechanicEngine();
        var mapping = new ApplicationMechanicProjectionMappingResolver(catalogs, spaces, types, edges);
        var projection = new ApplicationMechanicProjectionResolver(db, spaces);
        var evaluator = new ApplicationMechanicEvaluator(catalogs, projection, engine);
        var operations = new OperationLog(db);
        var effectApplier = new ApplicationEcsEffectApplier(db, entities, spaces, operations, edges);
        var runner = new ApplicationActionRunner(catalogs, setup.Activation, spaces, types, entities,
            edges, mapping, evaluator, effectApplier, operations,
            new ApplicationEcsEffectBatchBuilder(types, entities, edges));
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var readModels = new ApplicationReadModelService(catalogs, setup.Activation, spaces,
            mapping, evaluator, schemas);
        var serviceDefinitions = new ApplicationReadOnlyServiceDefinitionReader(schemas);
        var reviewClosures = new ApplicationCandidateWorkflowReviewClosureReader(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Resolver, materializer, serviceDefinitions);
        var ownerClosure = Assert.IsType<ApplicationCandidateWorkflowReviewClosureEvidence>(
            (await reviewClosures.ReadAsync(WorkflowAuthoringHost(setup, "workflow-review", 16),
                candidate, selected)).Evidence);
        var runtime = new ApplicationCandidateWorkflowRuntimeValidator(db, setup.Applications,
            setup.Activation, reviewClosures, catalogs, spaces, mapping, evaluator, runner,
            effectApplier, new SqliteStandingGrantReadCandidateReader(db), setup.Resolver, policy,
            new StandingGrantApplicationReadModelInvocationAdapter(policy, setup.Resolver,
                spaces, readModels), schemas, engine);
        var expectedEffects = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(new[]
        {
            new ApplicationEcsEffect { Type = ApplicationEcsEffectType.EntityCreate,
                EntityId = "candidate", Name = "Candidate" }
        }));
        var request = new ApplicationCandidateValidationRequest(candidate,
        [
            new(definition, "{\"mode\":\"action\",\"entityId\":\"candidate\",\"assignment\":{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"inspect\"}}",
                "{\"entityId\":\"candidate\"}", WorkflowSpaceId, InteractionStateRevision.From(state),
                new Dictionary<string, string>(), expectedEffects),
            new(definition, "{\"mode\":\"job\",\"entityId\":\"unused\",\"assignment\":{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"inspect\"}}",
                "{}", WorkflowSpaceId, InteractionStateRevision.From(state),
                new Dictionary<string, string>(), "[]")
        ]);
        var beforeOperations = await db.Set<Operation>().CountAsync();

        var report = await runtime.ValidateAsync(request,
            WorkflowAuthoringHost(setup, "workflow-validate", 16));

        Assert.True(report.Status == ApplicationCandidateRuntimeStatus.Completed,
            JsonSerializer.Serialize(report));
        Assert.Equal(ownerClosure.EvidenceFingerprint, report.SelectionEvidenceFingerprint);
        Assert.Equal(ownerClosure.Dependencies.Select(value => new ApplicationCandidateDependency(
            value.DefinitionId, value.Revision, value.ContentFingerprint)), report.Dependencies);
        Assert.Equal(2, report.Samples.Count);
        var actionSample = report.Samples[0];
        Assert.Empty(actionSample.ReadEvidence!);
        Assert.Equal(ApplicationCandidateRuntimeCompletionBoundary.ProposedAction,
            actionSample.CompletionBoundary);
        var actionProposal = Assert.Single(actionSample.Proposals!);
        Assert.Equal(ApplicationCandidateRuntimeProposalKind.Action, actionProposal.Kind);
        Assert.Equal([ApplicationEcsEffectType.EntityCreate], actionProposal.EffectKinds);
        Assert.NotNull(actionProposal.BatchFingerprint);
        Assert.NotNull(actionProposal.OutputFingerprint);
        var jobSample = report.Samples[1];
        Assert.Single(jobSample.ReadEvidence!);
        Assert.Equal(ApplicationCandidateRuntimeCompletionBoundary.ProposedJob,
            jobSample.CompletionBoundary);
        var jobProposal = Assert.Single(jobSample.Proposals!);
        Assert.Equal(ApplicationCandidateRuntimeProposalKind.Job, jobProposal.Kind);
        Assert.Null(jobProposal.BatchFingerprint);
        Assert.Null(jobProposal.OutputFingerprint);
        Assert.Null(await entities.GetEntityAsync(WorkflowSpaceId, "candidate"));
        Assert.Null(await entities.GetEntityAsync(WorkflowSpaceId, "must-not-run"));
        Assert.Equal(0, await db.Set<SystemTaskLifecycleRecord>().CountAsync());
        Assert.Equal(beforeOperations + 1, await db.Set<Operation>().CountAsync());
        var fingerprint = ApplicationCandidateWorkflowRuntimeValidator.ReportFingerprint(report);
        Assert.True(await runtime.CurrentAsync(request, report, fingerprint,
            WorkflowAuthoringHost(setup, "workflow-current", 16)));
        var changedExpectation = request with
        {
            Samples = request.Samples.Select((sample, index) =>
                index == 0 ? sample with { ExpectedEffectsJson = "[]" } : sample).ToArray()
        };
        Assert.False(await runtime.CurrentAsync(changedExpectation, report, fingerprint,
            WorkflowAuthoringHost(setup, "workflow-current-expectation", 16)));
        Assert.False(await runtime.CurrentAsync(request, report, new string('A', 64),
            WorkflowAuthoringHost(setup, "workflow-current-bad", 16)));

        var features = new InteractionFeatureRetriever(catalogs, namespaces: setup.Namespaces,
            changes: setup.Activation);
        var manuals = new InteractionManualContextService(new ProcedureStore(db), features, policy,
            setup.Resolver, setup.Activation, ["system"]);
        var reviewGate = new SystemTaskApplicationValidationGate(db, setup.Applications,
            setup.Activation, setup.Resolver, policy, TimeProvider.System,
            manuals: manuals, features: features, reviewClosures: [reviewClosures]);
        SystemTaskValidationAuthority reviewAuthority;
        await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(
            db, TimeProvider.System, false, default))
            reviewAuthority = await reviewGate.CheckAsync(
                WorkflowReviewHost(setup, "workflow-review-probe"), candidate, true);
        Assert.Null(SystemTaskApplicationValidationGate.ExecutionPrerequisite(reviewAuthority));
        Assert.Equal(ownerClosure.EvidenceFingerprint,
            reviewAuthority.ReviewClosure!.EvidenceFingerprint);
        var provider = new RetainedReviewProvider(reviewAuthority.ReviewInput!, "justifiedNew");
        var invoker = new SystemInnerWorkerValidationInvoker(new AiService([provider]), TimeProvider.System);
        var reviewTasks = new SystemTaskApplicationValidationService(db, reviewGate, TimeProvider.System);
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(reviewGate);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        await using var serviceProvider = services.BuildServiceProvider();
        var lifecycles = new SystemTaskAiInvocationLifecycleFactory(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        var submitted = await reviewTasks.SubmitAsync(
            ValidationProfile(WorkflowReviewHost(setup, "workflow-review-task"), candidate),
            written.Receipt!.OperationId, "workflow-write");
        Assert.Equal(InteractionInvocationResultTag.Pending, submitted.Tag);
        Assert.True(await reviewTasks.RunNextAsync("workflow-review-worker", invoker,
            ReviewerConfiguration(), lifecycles));
        var completedReview = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
            .SingleAsync(value => value.TaskId == submitted.TaskHandle!.TaskId);
        Assert.Equal("completed", completedReview.State);
        Assert.Equal(1, provider.Calls);

        var reviewed = new ApplicationCandidateReviewedWorkflowUpdateReader(
            new ApplicationCandidateReviewedClosureReceiptReader(db, setup.Applications, reviewGate));
        var authoringService = new SqliteApplicationAuthoringService(db, setup.Applications,
            setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver, operations,
            preparation: null, manuals: null, reviewedPureUpdates: null, synchronization: null,
            statefulRuntime: null, reviewedWorkflowUpdates: reviewed, workflowRuntime: runtime);
        var gateway = new ApplicationCandidateCapabilityGateway(
            CandidateCatalog(db, setup, authoringService, reviewTasks));
        var principal = TrustedPrincipalContext.VerifiedPrincipal(
            "principal." + new string('a', 64), "test");
        var priorActivationFingerprint = setup.Activation.Current(Application)!.ActivationFingerprint;
        var validationInput = JsonSerializer.Serialize(new
        {
            applicationId = Application.Value,
            candidateId = candidate.CandidateId,
            revision = candidate.Revision,
            contentFingerprint = candidate.ContentFingerprint,
            samples = request.Samples.Skip(1).Take(1).Select(sample => new
            {
                definition = new
                {
                    definitionId = sample.Definition.DefinitionId,
                    kind = sample.Definition.Kind,
                    revision = sample.Definition.Revision,
                    contentFingerprint = sample.Definition.ContentFingerprint
                },
                inputJson = sample.InputJson,
                expectedDataJson = sample.ExpectedDataJson,
                stateSpaceId = sample.StateSpaceId,
                stateRevision = sample.StateRevision,
                roleEntityIds = sample.RoleEntityIds,
                expectedEffectsJson = sample.ExpectedEffectsJson
            })
        });
        var currentAuthorGrant = await db.Set<StandingGrantCurrentRecord>()
            .SingleAsync(value => value.GrantId == "workflow-author");
        var priorAuthorGrant = SqliteStandingGrantPolicy.Parse(await db.Set<StandingGrantRevisionRecord>()
            .SingleAsync(value => value.GrantId == "workflow-author" && value.Revision == 1));
        var narrowGrant = priorAuthorGrant with
        {
            GrantReference = "workflow-author@2", Revision = 2, MaximumOperations = 3,
            IssuedByOperationId = "workflow-author-narrow-operation"
        };
        narrowGrant = narrowGrant with
        {
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(narrowGrant)
        };
        db.Add(new Operation { Id = narrowGrant.IssuedByOperationId,
            Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = narrowGrant.GrantId, Revision = narrowGrant.Revision,
            GrantReference = narrowGrant.GrantReference,
            PrincipalReference = narrowGrant.PrincipalReference,
            ApplicationId = narrowGrant.ApplicationId.Value, Scope = "application",
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(narrowGrant),
            ContentFingerprint = narrowGrant.ContentFingerprint,
            MaximumOperations = narrowGrant.MaximumOperations,
            ExpiresAtUtc = narrowGrant.ExpiresAtUtc,
            IssuedByOperationId = narrowGrant.IssuedByOperationId
        });
        currentAuthorGrant.Revision = 2;
        await db.SaveChangesAsync();
        var insufficient = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate, validationInput,
            "workflow-authoring-too-small", "website");
        Assert.True(insufficient.Ok, insufficient.Error?.Code + ": " + insufficient.Error?.Message);
        var insufficientRow = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.OperationId == insufficient.OperationId);
        Assert.NotEqual("valid", insufficientRow.Outcome);
        Assert.Contains("WORKFLOW_JOB_PROPOSAL_INVALID", insufficientRow.DiagnosticsJson);
        Assert.Empty(await db.Set<ApplicationCandidatePublicationRecord>().AsNoTracking().ToArrayAsync());
        Assert.Equal(priorActivationFingerprint,
            setup.Activation.Current(Application)!.ActivationFingerprint);
        Assert.Null(await entities.GetEntityAsync(WorkflowSpaceId, "candidate"));
        Assert.Equal(1, await db.Set<SystemTaskLifecycleRecord>().CountAsync());
        (await db.Set<StandingGrantCurrentRecord>()
            .SingleAsync(value => value.GrantId == "workflow-author")).Revision = 1;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var validated = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate, validationInput,
            "workflow-authoring-validate", "website");
        Assert.True(validated.Ok, validated.Error?.Code + ": " + validated.Error?.Message);
        var validationRow = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.OperationId == validated.OperationId);
        Assert.True(validationRow.Outcome == "valid", validationRow.DiagnosticsJson);
        Assert.True(validationRow.DependenciesComplete);
        Assert.Equal(completedReview.CompletionEvidenceReference, validationRow.ReuseEvidenceReference);
        var validationOperation = await db.Operations.AsNoTracking()
            .SingleAsync(value => value.Id == validationRow.OperationId);
        Assert.True(ApplicationCandidateWorkflowOperationProof.TryRead(
            validationOperation, validationRow, candidate, [definition],
            out var retainedRequest, out var retainedReport));
        Assert.Equal(request.Candidate, retainedRequest!.Candidate);
        Assert.Equal(ApplicationCandidateRuntimeStatus.Completed, retainedReport!.Status);
        var validationReplay = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate, validationInput,
            "workflow-authoring-validate", "website");
        Assert.True(validationReplay.Ok, validationReplay.Error?.Code + ": " + validationReplay.Error?.Message);
        Assert.Equal(validated.OperationId, validationReplay.OperationId);
        Assert.Equal(2, await db.Set<ApplicationCandidateValidationRecord>().CountAsync());
        Assert.Null(await entities.GetEntityAsync(WorkflowSpaceId, "candidate"));
        Assert.Null(await entities.GetEntityAsync(WorkflowSpaceId, "must-not-run"));
        Assert.Equal(1, await db.Set<SystemTaskLifecycleRecord>().CountAsync());

        var activated = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateActivate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                validationOperationId = validationRow.OperationId
            }), "workflow-authoring-activate", "website");
        Assert.True(activated.Ok, activated.Error?.Code + ": " + activated.Error?.Message);
        var activationReplay = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateActivate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                validationOperationId = validationRow.OperationId
            }), "workflow-authoring-activate", "website");
        Assert.True(activationReplay.Ok,
            activationReplay.Error?.Code + ": " + activationReplay.Error?.Message);
        Assert.Equal(activated.OperationId, activationReplay.OperationId);
        Assert.Single(await db.Set<ApplicationCandidatePublicationRecord>().AsNoTracking().ToArrayAsync());
        var rebound = spaces.Get(WorkflowSpaceId)!;
        Assert.Equal(setup.Activation.Current(Application)!.ActivationFingerprint,
            rebound.ManifestFingerprint);
        Assert.True(catalogs.TryGet(Application, out var activeCatalog));
        var published = activeCatalog.Inspect(new(Application, Application.Value, WorkflowServiceId)).Summary;
        Assert.Equal(definition.ContentFingerprint, published.ContentFingerprint);

        var transactions = new SqliteEcsWriteTransactionFactory(db);
        var standingReads = new StandingGrantApplicationReadModelInvocationAdapter(policy,
            setup.Resolver, spaces, readModels);
        var actionAdapter = new ApplicationActionInvocationAdapter(
            new PrivateHostInteractionAuthorizationPolicy(spaces), spaces, runner, operations,
            null, setup.Resolver, policy, transactions);
        var durable = new SqliteSystemTaskDurableService(
            db, policy, setup.Resolver, spaces, TimeProvider.System);
        var invocationAuthority = new PrivateHostInteractionAuthorizationPolicy(spaces);
        var contexts = new InteractionTaskContextMaterializer(invocationAuthority, features,
            catalogs, readModels);
        var workerResolver = new SystemInnerWorkerProcedureResolver(
            new InteractionEnvelopeFactory(setup.Applications, setup.Activation, spaces,
                invocationAuthority), contexts, catalogs,
            new SystemInnerWorkerPreparation(new ProcedureStore(db), contexts),
            new SystemInnerWorkerHostPolicy(new AiAgentProfileRegistry([
                new("web.inner", "Inner AI", "Perform the bounded host-selected procedure.")
            ])), TimeProvider.System);
        var innerWorker = new SystemInnerWorkerService(workerResolver, durable);
        var workflowAdapter = new ApplicationReadOnlyServiceInvocationAdapter(catalogs,
            serviceDefinitions, standingReads, schemas, engine, spaces, setup.Resolver, policy,
            actionAdapter, transactions, innerWorker, durable);
        var workflowHost = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            revision, WorkflowSpaceId, "workflow-state@1", "workflow-execute",
            InteractionStateRevision.From(rebound), InteractionExecutionProfile.Workflow,
            new(16, DateTime.UtcNow.AddMinutes(2)));
        var execution = await ((IApplicationWorkflowServiceInvocationAdapter)workflowAdapter).InvokeAsync(
            new(workflowHost,
                new(published.QualifiedId, published.Version, published.ContentFingerprint),
                service, new Dictionary<string, string>(),
                "{\"mode\":\"action\",\"entityId\":\"candidate\",\"assignment\":{\"format\":\"dantes-roleplay/inner-procedure-assignment/v1\",\"instruction\":\"inspect\"}}",
                ExecutionLimits.ReadModel));
        Assert.True(execution.Tag == InteractionInvocationResultTag.Completed,
            execution.Code + ": " + execution.SafeMessage + " / " + execution.DataJson);
        Assert.Equal(2, execution.PreviousCommits.Count);
        Assert.Equal("{\"entityId\":\"must-not-return\"}", execution.DataJson);
        Assert.NotNull(await entities.GetEntityAsync(WorkflowSpaceId, "candidate"));
        Assert.NotNull(await entities.GetEntityAsync(WorkflowSpaceId, "must-not-run"));
        }
        finally
        {
            if (db is not null) await db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private void WriteWorkflowMechanic(string file, string id, string requirements, string source)
    {
        WriteWorkflowFile($"content/mechanics/{file}.md", WorkflowMechanicMarkdown(file, id, requirements));
        WriteWorkflowFile($"content/mechanics/{file}.js", source);
    }

    private static string WorkflowMechanicMarkdown(string file, string id, string requirements) => $$"""
            ---
            id: {{id}}
            category: workflow.fixture
            name: {{file}}
            status: active
            ---

            ## Description
            Generic workflow candidate fixture.

            ## Requirements
            ```json
            {{requirements}}
            ```
            """;

    private void WriteWorkflowFile(string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static InteractionInvocationHost WorkflowAuthoringHost(
        SetupState setup, string command, int operations) => InteractionInvocationHost.ForApplication(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []),
        "workflow-author@1", command, InteractionExecutionProfile.Atomic,
        new(Math.Min(operations, 8), DateTime.UtcNow.AddMinutes(2)));

    private static InteractionInvocationHost WorkflowReviewHost(
        SetupState setup, string command) => InteractionInvocationHost.ForApplication(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []),
        "workflow-author@1", command, InteractionExecutionProfile.ReadOnly,
        new(8, DateTime.UtcNow.AddMinutes(2)));

    private static async Task SeedWorkflowAuthoringGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("workflow-author@1", "workflow-author", 1,
            new string('0', 64), "principal." + new string('a', 64), Application,
            StandingGrantScope.Application, null,
            [StandingGrantCapability.Author, StandingGrantCapability.Read,
                StandingGrantCapability.Validate, StandingGrantCapability.Activate],
            new(StandingGrantDefinitionMode.ExactIds,
                [WorkflowServiceId, WorkflowReadId, WorkflowQueryId, WorkflowActionId,
                    "demo.runtime.inspect"], []),
            [], 8, DateTime.UtcNow.AddMinutes(10), false, "workflow-author-operation");
        AddGrant(db, grant);
        await db.SaveChangesAsync();
    }

    private static async Task SeedWorkflowStateGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("workflow-state@1", "workflow-state", 1,
            new string('0', 64), "principal." + new string('a', 64), Application,
            StandingGrantScope.StateSpace, WorkflowSpaceId,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ExactIds,
                [WorkflowServiceId, WorkflowReadId, WorkflowQueryId, WorkflowActionId,
                    "demo.runtime.inspect"], []),
            [ApplicationEcsEffectType.EntityCreate], 16, DateTime.UtcNow.AddMinutes(10),
            false, "workflow-state-operation");
        AddGrant(db, grant);
        await db.SaveChangesAsync();
    }

    private static void AddGrant(DantesRoleplayDbContext db, StandingGrantRevision grant)
    {
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId,
            Revision = grant.Revision,
            GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference,
            ApplicationId = grant.ApplicationId.Value,
            Scope = grant.Scope == StandingGrantScope.Application ? "application" : "stateSpace",
            StateSpaceId = grant.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations,
            ExpiresAtUtc = grant.ExpiresAtUtc,
            Revoked = grant.Revoked,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = grant.Revision });
    }
}
