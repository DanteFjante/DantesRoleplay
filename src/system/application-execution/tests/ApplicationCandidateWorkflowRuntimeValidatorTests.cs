using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;

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
    public async Task Workflow_candidate_runs_real_reads_and_stops_after_one_typed_action_dry_run()
    {
        await using var db = fixture.CreateContext();
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
        _ = await new ProcedureStore(db).WriteAsync(new WriteProcedureRequest
        {
            Id = "demo.runtime.inspect",
            Category = "runtime.inspect",
            Name = "Inspect runtime",
            Description = "Inspect the active runtime definition.",
            Governs = "query(kind: \"runtime.inspect\")",
            Instructions = "1. Inspect it.",
            Constraints = "- Preserve it.",
            Status = ProcedureStatus.Active,
            CreatedBy = "fixture",
            ChangeNote = "Workflow validation fixture."
        });
        await SeedWorkflowAuthoringGrantAsync(db);

        var active = setup.Activation.Current(Application)!;
        var revision = new ApplicationRevision(Application, 1,
            setup.Applications.Get(Application)!.Fingerprint, []);
        var spaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var state = spaces.Create(new(WorkflowSpaceId, revision,
            active.ActivationFingerprint, active.ResolutionFingerprint));
        await SeedWorkflowStateGrantAsync(db);
        db.ChangeTracker.Clear();
        var candidateSource = "ctx.services.read('read',{});"
            + "if(ctx.input.mode==='job'){ctx.services.job('inspect',ctx.input.assignment);return {data:{entityId:'must-not-return'}};}"
            + "ctx.services.action('create',{entityId:ctx.input.entityId});"
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
                spaces, readModels), new ProcedureStore(db), schemas, engine);
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
        Assert.All(report.Samples, sample => Assert.Single(sample.ReadEvidence!));
        var actionSample = report.Samples[0];
        Assert.Equal(ApplicationCandidateRuntimeCompletionBoundary.ProposedAction,
            actionSample.CompletionBoundary);
        var actionProposal = Assert.Single(actionSample.Proposals!);
        Assert.Equal(ApplicationCandidateRuntimeProposalKind.Action, actionProposal.Kind);
        Assert.Equal([ApplicationEcsEffectType.EntityCreate], actionProposal.EffectKinds);
        Assert.NotNull(actionProposal.BatchFingerprint);
        Assert.NotNull(actionProposal.OutputFingerprint);
        var jobSample = report.Samples[1];
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
        new(operations, DateTime.UtcNow.AddMinutes(2)));

    private static async Task SeedWorkflowAuthoringGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("workflow-author@1", "workflow-author", 1,
            new string('0', 64), "principal." + new string('a', 64), Application,
            StandingGrantScope.Application, null,
            [StandingGrantCapability.Author, StandingGrantCapability.Read, StandingGrantCapability.Validate],
            new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.workflow", true, [CatalogNamespaceKinds.Mechanic])]),
            [], 16, DateTime.UtcNow.AddMinutes(10), false, "workflow-author-operation");
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
