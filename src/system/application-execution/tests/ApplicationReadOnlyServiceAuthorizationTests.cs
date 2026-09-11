using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationReadOnlyServiceAuthorizationTests
{
    [Fact]
    public async Task Workflow_service_commits_real_nested_action_and_replays_its_exact_receipt()
    {
        await using var fixture = await Fixture.CreateAsync(workflow: true);
        var operationCount = await fixture.Db.Set<Operation>().CountAsync();
        var firstRequest = fixture.WorkflowRequest("command.workflow.set", "{\"value\":11}");

        var first = await fixture.WorkflowService.InvokeAsync(firstRequest);

        Assert.True(first.Tag == InteractionInvocationResultTag.Completed, first.ToJson());
        Assert.Equal("{\"entityId\":\"subject\",\"value\":11}", first.DataJson);
        var firstReceipt = Assert.Single(first.PreviousCommits);
        Assert.Single(firstReceipt.Effects);
        Assert.Equal(14, firstRequest.Host.Budget.RemainingOperations); // workflow root + child action
        var committed = (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!;
        Assert.Equal(2, committed.Revision);
        Assert.Equal("{\"value\":11}", committed.ValueJson);
        Assert.Equal(operationCount + 1, await fixture.Db.Set<Operation>().CountAsync());

        var replayRequest = fixture.WorkflowRequest("command.workflow.set", "{\"value\":11}");
        var replay = await fixture.WorkflowService.InvokeAsync(replayRequest);

        Assert.Equal(InteractionInvocationResultTag.Completed, replay.Tag);
        Assert.Equal(firstReceipt.OperationId, Assert.Single(replay.PreviousCommits).OperationId);
        var unchanged = (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!;
        Assert.Equal(2, unchanged.Revision);
        Assert.Equal("{\"value\":11}", unchanged.ValueJson);
        Assert.Equal(operationCount + 1, await fixture.Db.Set<Operation>().CountAsync());
    }

    [Fact]
    public async Task Workflow_service_rejects_undeclared_action_without_dispatch_or_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(
            workflow: true,
            workflowSource: "ctx.services.action('undeclared',{value:ctx.input.value});return {data:{entityId:'subject',value:ctx.input.value}};");
        var request = fixture.WorkflowRequest("command.workflow.undeclared", "{\"value\":12}");
        var operationCount = await fixture.Db.Set<Operation>().CountAsync();

        var result = await fixture.WorkflowService.InvokeAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("SERVICE_ACTION_ALIAS_INVALID", result.Code);
        Assert.Empty(result.PreviousCommits);
        Assert.Equal(15, request.Host.Budget.RemainingOperations); // root only; no child dispatch
        Assert.Equal(operationCount, await fixture.Db.Set<Operation>().CountAsync());
        var component = (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!;
        Assert.Equal(1, component.Revision);
        Assert.Equal("{\"value\":7}", component.ValueJson);
    }

    [Fact]
    public async Task Workflow_child_rechecks_exact_current_execute_grant_before_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(workflow: true, includeActionInGrant: false);
        var request = fixture.WorkflowRequest("command.workflow.denied", "{\"value\":13}");
        var operationCount = await fixture.Db.Set<Operation>().CountAsync();

        var result = await fixture.WorkflowService.InvokeAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", result.Code);
        Assert.Empty(result.PreviousCommits);
        Assert.Equal(14, request.Host.Budget.RemainingOperations); // root + denied child
        Assert.Equal(operationCount, await fixture.Db.Set<Operation>().CountAsync());
        var component = (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!;
        Assert.Equal(1, component.Revision);
        Assert.Equal("{\"value\":7}", component.ValueJson);
    }

    [Fact]
    public async Task Workflow_child_rejects_an_actual_effect_missing_from_the_current_grant()
    {
        await using var fixture = await Fixture.CreateAsync(
            workflow: true, includeActionEffectInGrant: false);
        var request = fixture.WorkflowRequest("command.workflow.effect-denied", "{\"value\":15}");
        var operationCount = await fixture.Db.Set<Operation>().CountAsync();

        var result = await fixture.WorkflowService.InvokeAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", result.Code);
        Assert.Empty(result.PreviousCommits);
        Assert.Equal(operationCount, await fixture.Db.Set<Operation>().CountAsync());
        var component = (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!;
        Assert.Equal(1, component.Revision);
        Assert.Equal("{\"value\":7}", component.ValueJson);
    }

    [Fact]
    public async Task Workflow_child_rechecks_revocation_after_evaluation_inside_the_commit_transaction()
    {
        await using var fixture = await Fixture.CreateAsync(workflow: true, revokeAfterEvaluation: true);
        var request = fixture.WorkflowRequest("command.workflow.revoked-before-commit", "{\"value\":16}");
        var actionAuditCount = await fixture.Db.Set<Operation>()
            .CountAsync(value => value.Tool == ApplicationEcsExecutionIdentity.AuditTool);

        var result = await fixture.WorkflowService.InvokeAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", result.Code);
        Assert.Empty(result.PreviousCommits);
        Assert.Equal(actionAuditCount, await fixture.Db.Set<Operation>()
            .CountAsync(value => value.Tool == ApplicationEcsExecutionIdentity.AuditTool));
        var component = (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!;
        Assert.Equal(1, component.Revision);
        Assert.Equal("{\"value\":7}", component.ValueJson);
    }

    [Fact]
    public async Task Workflow_failure_after_commit_preserves_the_successful_child_receipt()
    {
        await using var fixture = await Fixture.CreateAsync(
            workflow: true,
            workflowSource: "ctx.services.action('setCounter',{value:ctx.input.value});ctx.services.action('undeclared',{value:99});return {data:{entityId:'subject',value:ctx.input.value}};");
        var request = fixture.WorkflowRequest("command.workflow.partial", "{\"value\":14}");
        var operationCount = await fixture.Db.Set<Operation>().CountAsync();

        var result = await fixture.WorkflowService.InvokeAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("SERVICE_ACTION_ALIAS_INVALID", result.Code);
        Assert.Single(result.PreviousCommits);
        Assert.Equal(14, request.Host.Budget.RemainingOperations); // root + committed child
        Assert.Equal(operationCount + 1, await fixture.Db.Set<Operation>().CountAsync());
        var component = (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!;
        Assert.Equal(2, component.Revision);
        Assert.Equal("{\"value\":14}", component.ValueJson);
    }

    [Fact]
    public async Task Retained_service_completes_through_real_standing_authority_and_sqlite_read()
    {
        await using var fixture = await Fixture.CreateAsync();
        var progress = new ApplicationServiceProgressChannel();
        var request = fixture.Request("command.service.complete", progress: progress);
        var operationCount = await fixture.Db.Set<Operation>().CountAsync();
        var taskCount = await fixture.Db.Set<SystemTaskRecord>().CountAsync();

        var result = await fixture.Service.InvokeAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("{\"entityId\":\"subject\",\"value\":7}", result.DataJson);
        Assert.StartsWith("service-process-local.", result.CompletionEvidenceReference, StringComparison.Ordinal);
        Assert.Null(result.ReadEvidence);
        Assert.Null(result.Receipt);
        Assert.Null(result.TaskHandle);
        Assert.Empty(result.PreviousCommits);
        Assert.Equal(operationCount, await fixture.Db.Set<Operation>().CountAsync());
        Assert.Equal(taskCount, await fixture.Db.Set<SystemTaskRecord>().CountAsync());
        Assert.Equal(14, request.Host.Budget.RemainingOperations); // root computation + one real read
        var frame = Assert.Single(await DrainAsync(progress));
        Assert.Equal("{\"phase\":\"read\"}", frame.DataJson);
        Assert.Equal(1, frame.Sequence);
        Assert.Equal("{\"value\":7}", (await fixture.Entities.GetComponentAsync(
            Fixture.SpaceId, "subject", fixture.CounterType.QualifiedId))!.ValueJson);
    }

    [Fact]
    public async Task Missing_and_wrong_principal_grants_fail_without_data()
    {
        await using var fixture = await Fixture.CreateAsync();

        var missing = await fixture.Service.InvokeAsync(fixture.Request(
            "command.service.missing", grantReference: "missing@1"));
        var wrongPrincipal = await fixture.Service.InvokeAsync(fixture.Request(
            "command.service.wrong-principal", principal: PrivateOperatorPrincipal.Create("test", "other")));

        AssertNoData(missing);
        AssertNoData(wrongPrincipal);
        Assert.NotEqual(InteractionInvocationResultTag.Completed, missing.Tag);
        Assert.NotEqual(InteractionInvocationResultTag.Completed, wrongPrincipal.Tag);
    }

    [Fact]
    public async Task Query_not_allowlisted_by_the_real_grant_is_terminal()
    {
        await using var fixture = await Fixture.CreateAsync(includeQueryInGrant: false);
        var request = fixture.Request("command.service.query-denied");

        var result = await fixture.Service.InvokeAsync(request);

        AssertNoData(result);
        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", result.Code);
        Assert.Equal(14, request.Host.Budget.RemainingOperations); // root + denied read
    }

    [Fact]
    public async Task Revoked_current_revision_denies_repeats_even_with_old_row_tracked()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.InvokeAsync(fixture.Request("command.service.before-revoke"));
        Assert.Equal(InteractionInvocationResultTag.Completed, first.Tag);
        _ = await fixture.Db.Set<StandingGrantRevisionRecord>().SingleAsync(
            row => row.GrantReference == Fixture.GrantReference);
        await fixture.RevokeAsync();

        var denied = await fixture.Service.InvokeAsync(fixture.Request("command.service.revoked.first"));
        var repeated = await fixture.Service.InvokeAsync(fixture.Request("command.service.revoked.repeat"));

        AssertNoData(denied);
        AssertNoData(repeated);
        Assert.NotEqual(InteractionInvocationResultTag.Completed, denied.Tag);
        Assert.NotEqual(InteractionInvocationResultTag.Completed, repeated.Tag);
    }

    private static void AssertNoData(InteractionInvocationResult result)
    {
        Assert.Null(result.DataJson);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Null(result.ReadEvidence);
        Assert.Null(result.Receipt);
        Assert.Null(result.TaskHandle);
    }

    private static async Task<IReadOnlyList<ApplicationServiceProgressFrame>> DrainAsync(
        ApplicationServiceProgressChannel progress)
    {
        var frames = new List<ApplicationServiceProgressFrame>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var frame in progress.Reader.ReadAllAsync(timeout.Token)) frames.Add(frame);
        return frames;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string SpaceId = "service-space";
        public const string GrantReference = "service-grant@1";
        private const string SourceId = "catalog";
        private const string RootId = "service-fixture-root";
        private const string ReadId = "service-fixture.runtime.counter-projection";
        private const string QueryId = "service-fixture.runtime.counter-query";
        private const string ServiceId = "service-fixture.runtime.counter-service";
        private const string ActionId = "service-fixture.runtime.set-counter";
        private const string OutputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"entityId\",\"value\"],\"properties\":{\"entityId\":{\"type\":\"string\"},\"value\":{\"type\":\"integer\"}}}";
        private const string CounterSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}";
        private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("service-fixture");

        private readonly SqliteFixture sqlite;
        private readonly string root;
        private readonly ApplicationRevision revision;
        private readonly TrustedPrincipalContext principal;
        private readonly IStateSpaceRegistry stateSpaces;
        private readonly CatalogRecordView serviceRecord;
        private readonly ApplicationReadOnlyServiceDefinition serviceDefinition;

        private Fixture(
            SqliteFixture sqlite,
            DantesRoleplayDbContext db,
            string root,
            ApplicationRevision revision,
            TrustedPrincipalContext principal,
            IStateSpaceRegistry stateSpaces,
            SqliteEntityComponentStore entities,
            RegisteredComponentTypeVersion counterType,
            IApplicationReadOnlyServiceInvocationAdapter service,
            IApplicationWorkflowServiceInvocationAdapter workflowService,
            CatalogRecordView serviceRecord,
            ApplicationReadOnlyServiceDefinition serviceDefinition)
        {
            this.sqlite = sqlite;
            Db = db;
            this.root = root;
            this.revision = revision;
            this.principal = principal;
            this.stateSpaces = stateSpaces;
            Entities = entities;
            CounterType = counterType;
            Service = service;
            WorkflowService = workflowService;
            this.serviceRecord = serviceRecord;
            this.serviceDefinition = serviceDefinition;
        }

        public DantesRoleplayDbContext Db { get; }
        public SqliteEntityComponentStore Entities { get; }
        public RegisteredComponentTypeVersion CounterType { get; }
        public IApplicationReadOnlyServiceInvocationAdapter Service { get; }
        public IApplicationWorkflowServiceInvocationAdapter WorkflowService { get; }

        public static async Task<Fixture> CreateAsync(
            bool includeQueryInGrant = true,
            bool workflow = false,
            string? workflowSource = null,
            bool includeActionInGrant = true,
            bool includeActionEffectInGrant = true,
            bool revokeAfterEvaluation = false)
        {
            var sqlite = new SqliteFixture();
            var db = sqlite.CreateContext();
            var root = Path.Combine(Path.GetTempPath(), $"service-authorization-{Guid.NewGuid():N}");
            try
            {
                var applications = new SqliteApplicationRegistry(db);
                var revision = applications.Register(new(
                    Application, "Service fixture", "Real standing-authority service fixture.", []));
                var sources = new SqliteSourceRegistry(db);
                sources.Register(new(Application, SourceId, RootId, "content/**/*", SourceTrust.Trusted, 0,
                    "service-fixture-catalog"));
                var extensions = new SqliteApplicationExtensionRegistry(db, sources);
                var namespaces = new SqliteCatalogNamespaceRegistry(db);
                var kinds = new[]
                {
                    CatalogNamespaceKinds.Mechanic,
                    CatalogNamespaceKinds.Query,
                    CatalogNamespaceKinds.ComponentType
                };
                namespaces.Register(new CatalogNamespaceRegistration(
                    "service-fixture", "fixture-domain", "Service fixture root.", kinds,
                    ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
                namespaces.Register(new CatalogNamespaceRegistration(
                    "service-fixture.runtime", "fixture-domain", "Service runtime.", kinds,
                    ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
                var roots = new Root(root);
                var previews = new ApplicationPreviewService(applications, sources,
                    new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
                    new SourceOverlayResolver());
                var activation = new ApplicationActivationService(db, previews, extensions, sources, roots,
                    new ProjectionImpactService(applications, new SqliteProjectionImpactSnapshotReader(db)),
                    new OperationLog(db));

                WriteMechanic(root, "counter-projection", ReadId,
                    "{\"roles\":{\"subject\":{\"components\":[\"counter\"]}}}",
                    "var c=JSON.parse(ctx.roles.subject.components.counter);return {data:{entityId:ctx.roles.subject.id,value:c.value}};");
                WriteMechanic(root, "set-counter", ActionId,
                    "{\"roles\":{\"subject\":{\"components\":[\"counter\"]}},\"inputSchema\":" + CounterSchema + "}",
                    "return {effects:[{type:'component.set',entityId:ctx.roles.subject.id,definitionId:'counter',data:JSON.stringify({value:ctx.input.value})}]};");
                var first = await ActivateAsync(previews, activation, expected: null);
                var materializer = new ActivatedApplicationCatalogMaterializer(
                    applications, activation, sources, roots, extensions);
                var readRecord = materializer.Build(Application).Records.Single(record => record.QualifiedId == ReadId);
                var actionRecord = materializer.Build(Application).Records.Single(record => record.QualifiedId == ActionId);

                var schemas = new BoundedJsonSchemaValidator();
                var output = schemas.Compile(OutputSchema);
                var queryJson = JsonSerializer.Serialize(new
                {
                    id = QueryId,
                    category = "runtime.counter",
                    name = "Counter",
                    description = "Reads one fixture counter.",
                    matches = new[] { "read counter" },
                    roles = new Dictionary<string, string> { ["subject"] = "The counter subject." },
                    executor = ApplicationQueryContract.MechanicProjectionExecutor,
                    projection = new
                    {
                        qualifiedId = ReadId,
                        version = readRecord.Version,
                        contentHash = readRecord.ContentFingerprint,
                        outputSchemaHash = output.SchemaHash
                    },
                    outputSchema = JsonDocument.Parse(output.NormalizedSchema).RootElement,
                    exposure = "model-visible",
                    status = "active"
                });
                Write(root, "content/queries/counter-query.json", queryJson);
                var query = ApplicationQueryContract.Parse(queryJson, Application);
                var queryReference = new InteractionQueryContractReference(
                    query.Executor, query.ProjectionQualifiedId, query.ProjectionVersion,
                    query.ProjectionContentHash, query.OutputSchemaHash, query.OutputSchemaJson,
                    query.Exposure, query.Roles.Keys);
                var input = schemas.Compile(workflow
                    ? CounterSchema
                    : "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{}}");
                var definition = new ApplicationReadOnlyServiceDefinition(
                    input.SchemaHash, input.NormalizedSchema, output.SchemaHash, output.NormalizedSchema,
                    [new("counter", QueryId, queryReference,
                        new Dictionary<string, string> { ["subject"] = "subject" },
                        schemas, output.NormalizedSchema)], schemas,
                    workflow
                        ? [new("setCounter", ActionId, actionRecord.Version, actionRecord.ContentFingerprint,
                            new Dictionary<string, string> { ["subject"] = "subject" })]
                        : []);
                WriteMechanic(root, "counter-service", ServiceId,
                    "{\"service\":" + definition.ToJson() + "}",
                    workflow
                        ? workflowSource ?? "ctx.services.action('setCounter',{value:ctx.input.value});return {data:{entityId:'subject',value:ctx.input.value}};"
                        : "var r=ctx.services.read('counter',{});ctx.services.progress({phase:'read'});return {data:JSON.parse(r.dataJson)};");
                _ = await ActivateAsync(previews, activation, first.ActivationFingerprint);

                var catalogs = new ActivatedApplicationCatalogProvider(
                    new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
                    new CatalogCursorCodec(Enumerable.Repeat((byte)0x51, 32).ToArray()));
                Assert.True(catalogs.TryGet(Application, out var catalog));
                var serviceSummary = catalog.EffectiveContent(new(Application,
                    Kinds: [CatalogNamespaceKinds.Mechanic], QualifiedIds: [ServiceId]))
                    .ResolvedWinners.Single().Record;
                var serviceRecord = catalog.Inspect(new(Application, serviceSummary.Collection, ServiceId));
                var selected = new SystemTaskSelectedDefinition(
                    ServiceId, serviceSummary.Version, serviceSummary.ContentFingerprint);
                var retainedDefinition = new ApplicationReadOnlyServiceDefinitionReader(schemas)
                    .ReadRetained(selected, serviceRecord);

                var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
                var active = activation.Current(Application)!;
                stateSpaces.Create(new(SpaceId, revision, active.ActivationFingerprint,
                    active.ResolutionFingerprint));
                var types = new SqliteComponentTypeRegistry(db, schemas);
                var counterType = types.Define(new(Application, Application.Value + ".counter", CounterSchema));
                var entities = new SqliteEntityComponentStore(db, types, schemas);
                await entities.CreateEntityAsync(SpaceId, "subject", "Subject");
                await entities.AddComponentAsync(new(SpaceId, "subject",
                    new(counterType.QualifiedId, counterType.Version, counterType.SchemaHash), "{\"value\":7}", 0));
                var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
                var engine = new JintMechanicEngine();
                var mapping = new ApplicationMechanicProjectionMappingResolver(catalogs, stateSpaces, types, edges);
                var projection = new ApplicationMechanicProjectionResolver(db, stateSpaces);
                var evaluator = new ApplicationMechanicEvaluator(catalogs, projection, engine);
                var readModels = new ApplicationReadModelService(
                    catalogs, activation, stateSpaces, mapping, evaluator, schemas);
                var targets = new SqliteStandingGrantTargetResolver(
                    db, applications, activation, activation, sources, extensions, namespaces, materializer);
                var policy = new SqliteStandingGrantPolicy(db, targets);
                var principal = PrivateOperatorPrincipal.Create("test", "service-fixture-operator");
                var standingReads = new StandingGrantApplicationReadModelInvocationAdapter(
                    policy, targets, stateSpaces, readModels);
                var operations = new OperationLog(db);
                var effects = new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges);
                IApplicationEcsEffectBatchBuilder? batchBuilder = revokeAfterEvaluation
                    ? new AfterBuildBatchBuilder(
                        new ApplicationEcsEffectBatchBuilder(types, entities, edges),
                        async () =>
                        {
                            await SeedGrantAsync(db, principal, [ServiceId, ActionId], 2,
                                "service-grant@2", revoked: true, addCurrent: false,
                                capabilities: [StandingGrantCapability.Read, StandingGrantCapability.Execute],
                                effectKinds: [ApplicationEcsEffectType.ComponentSet]);
                            var current = await db.Set<StandingGrantCurrentRecord>().SingleAsync();
                            current.Revision = 2;
                            await db.SaveChangesAsync();
                        })
                    : null;
                var runner = new ApplicationActionRunner(catalogs, activation, stateSpaces, types, entities, edges,
                    mapping, evaluator, effects, operations, batchBuilder);
                var transactions = new SqliteEcsWriteTransactionFactory(db);
                var actionAdapter = new ApplicationActionInvocationAdapter(
                    new PrivateHostInteractionAuthorizationPolicy(stateSpaces), stateSpaces, runner, operations,
                    null, targets, policy, transactions);
                var service = new ApplicationReadOnlyServiceInvocationAdapter(
                    catalogs, new ApplicationReadOnlyServiceDefinitionReader(schemas), standingReads,
                    schemas, engine, stateSpaces, targets, policy, actionAdapter, transactions);
                IReadOnlyList<string> exactIds = workflow
                    ? includeActionInGrant ? [ServiceId, ActionId] : [ServiceId]
                    : includeQueryInGrant ? [ServiceId, QueryId] : [ServiceId];
                await SeedGrantAsync(db, principal, exactIds, 1,
                    GrantReference, revoked: false,
                    capabilities: workflow
                        ? [StandingGrantCapability.Read, StandingGrantCapability.Execute]
                        : [StandingGrantCapability.Read],
                    effectKinds: workflow && includeActionEffectInGrant
                        ? [ApplicationEcsEffectType.ComponentSet]
                        : []);
                return new(sqlite, db, root, revision, principal, stateSpaces, entities, counterType,
                    service, service, serviceRecord, retainedDefinition);
            }
            catch
            {
                db.Dispose();
                sqlite.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, true);
                throw;
            }
        }

        public ApplicationReadOnlyServiceInvocationRequest Request(
            string commandId,
            ApplicationServiceProgressChannel? progress = null,
            string grantReference = GrantReference,
            TrustedPrincipalContext? principal = null)
        {
            var state = stateSpaces.Get(SpaceId)!;
            var host = new InteractionInvocationHost(
                principal ?? this.principal,
                revision,
                SpaceId,
                grantReference,
                commandId,
                InteractionStateRevision.From(state),
                InteractionExecutionProfile.ReadOnly,
                new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(5)));
            return new(host,
                new(serviceRecord.Summary.QualifiedId, serviceRecord.Summary.Version,
                    serviceRecord.Summary.ContentFingerprint),
                serviceDefinition,
                new Dictionary<string, string> { ["subject"] = "subject" },
                "{}",
                ExecutionLimits.ReadModel,
                progress);
        }

        public ApplicationWorkflowServiceInvocationRequest WorkflowRequest(string commandId, string inputJson)
        {
            var state = stateSpaces.Get(SpaceId)!;
            var host = new InteractionInvocationHost(
                principal, revision, SpaceId, GrantReference, commandId,
                InteractionStateRevision.From(state), InteractionExecutionProfile.Workflow,
                new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(5)));
            return new(host,
                new(serviceRecord.Summary.QualifiedId, serviceRecord.Summary.Version,
                    serviceRecord.Summary.ContentFingerprint),
                serviceDefinition,
                new Dictionary<string, string> { ["subject"] = "subject" },
                inputJson,
                ExecutionLimits.ReadModel);
        }

        public async Task RevokeAsync()
        {
            await SeedGrantAsync(Db, principal, [ServiceId, QueryId], 2,
                "service-grant@2", revoked: true, addCurrent: false);
            var current = await Db.Set<StandingGrantCurrentRecord>().SingleAsync();
            current.Revision = 2;
            await Db.SaveChangesAsync();
        }

        private static async Task<ActiveApplicationManifest> ActivateAsync(
            ApplicationPreviewService previews,
            ApplicationActivationService activation,
            string? expected)
        {
            var preview = await previews.PreviewAsync(Application);
            Assert.True(preview.IsValid, string.Join(';', preview.Problems.Select(problem => problem.Code)));
            var request = new ApplicationActivationRequest(Application, preview.PreviewFingerprint, expected);
            var token = Guid.NewGuid().ToString("N");
            var context = new ApplicationActivationContext(token, "Activate service authorization fixture.",
                ["procedure.system.use"], new AuthorizationAuditEvidence(
                    "principal." + new string('a', 64), "test", "modify", "system.private-host",
                    token, true, "PRIVATE_OPERATOR_ALLOWED"));
            await activation.PreviewAsync(request, context);
            return (await activation.ActivateAsync(request, context)).Activation;
        }

        private static async Task SeedGrantAsync(
            DantesRoleplayDbContext db,
            TrustedPrincipalContext principal,
            IReadOnlyList<string> exactIds,
            int revision,
            string reference,
            bool revoked,
            bool addCurrent = true,
            IReadOnlyList<StandingGrantCapability>? capabilities = null,
            IReadOnlyList<string>? effectKinds = null)
        {
            var operationId = $"grant-seed-{revision}";
            db.Add(new Operation { Id = operationId, Timestamp = DateTime.UtcNow, Tool = "test" });
            var grant = new StandingGrantRevision(
                reference, "service-grant", revision, new string('0', 64), principal.PrincipalId,
                Application, StandingGrantScope.StateSpace, SpaceId,
                capabilities ?? [StandingGrantCapability.Read],
                new StandingGrantDefinitionAllowance(
                    StandingGrantDefinitionMode.ExactIds,
                    exactIds.Order(StringComparer.Ordinal).ToArray(),
                    []),
                effectKinds ?? [], 16, DateTime.UtcNow.AddHours(1), revoked, operationId);
            db.Add(new StandingGrantRevisionRecord
            {
                GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
                PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
                Scope = "stateSpace", StateSpaceId = grant.StateSpaceId,
                PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
                ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
                MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc,
                Revoked = grant.Revoked, IssuedByOperationId = grant.IssuedByOperationId
            });
            if (addCurrent)
                db.Add(new StandingGrantCurrentRecord { GrantId = "service-grant", Revision = revision });
            await db.SaveChangesAsync();
        }

        private static void WriteMechanic(
            string root,
            string file,
            string id,
            string requirements,
            string source)
        {
            Write(root, $"content/mechanics/{file}.md", $$$"""
                ---
                id: {{{id}}}
                category: runtime.service.fixture
                name: {{{file}}}
                status: active
                ---

                ## Description
                Generic service authorization fixture.

                ## Requirements
                ```json
                {{{requirements}}}
                ```
                """);
            Write(root, $"content/mechanics/{file}.js", source);
        }

        private static void Write(string root, string relative, string value)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value, new UTF8Encoding(false));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            sqlite.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private sealed class Root(string root) : IAllowedSourceRootResolver
        {
            public bool TryResolve(string allowedRootId, out string canonicalPath)
            {
                canonicalPath = root;
                return allowedRootId == RootId;
            }
        }

        private sealed class AfterBuildBatchBuilder(
            IApplicationEcsEffectBatchBuilder inner,
            Func<Task> afterBuild) : IApplicationEcsEffectBatchBuilder
        {
            private bool invoked;

            public async Task<ApplicationEcsEffectBatchBuildResult> BuildAsync(
                StateSpaceView stateSpace,
                ApplicationMechanicProjectionMapping mapping,
                MechanicProjection projection,
                MechanicRequirements requirements,
                CompositionProposal proposal,
                string mechanicId,
                int mechanicVersion,
                long seed,
                CancellationToken cancellationToken = default)
            {
                var result = await inner.BuildAsync(stateSpace, mapping, projection, requirements,
                    proposal, mechanicId, mechanicVersion, seed, cancellationToken);
                if (result.Ok && !invoked)
                {
                    invoked = true;
                    await afterBuild();
                }
                return result;
            }
        }
    }
}
