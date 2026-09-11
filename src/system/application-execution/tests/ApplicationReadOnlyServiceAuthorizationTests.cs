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
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
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
            this.serviceRecord = serviceRecord;
            this.serviceDefinition = serviceDefinition;
        }

        public DantesRoleplayDbContext Db { get; }
        public SqliteEntityComponentStore Entities { get; }
        public RegisteredComponentTypeVersion CounterType { get; }
        public IApplicationReadOnlyServiceInvocationAdapter Service { get; }

        public static async Task<Fixture> CreateAsync(bool includeQueryInGrant = true)
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
                var first = await ActivateAsync(previews, activation, expected: null);
                var materializer = new ActivatedApplicationCatalogMaterializer(
                    applications, activation, sources, roots, extensions);
                var readRecord = materializer.Build(Application).Records.Single(record => record.QualifiedId == ReadId);

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
                var input = schemas.Compile(
                    "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{}}");
                var definition = new ApplicationReadOnlyServiceDefinition(
                    input.SchemaHash, input.NormalizedSchema, output.SchemaHash, output.NormalizedSchema,
                    [new("counter", QueryId, queryReference,
                        new Dictionary<string, string> { ["subject"] = "subject" },
                        schemas, output.NormalizedSchema)], schemas);
                WriteMechanic(root, "counter-service", ServiceId,
                    "{\"service\":" + definition.ToJson() + "}",
                    "var r=ctx.services.read('counter',{});ctx.services.progress({phase:'read'});return {data:JSON.parse(r.dataJson)};");
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
                    db, applications, activation, activation, sources, extensions, namespaces);
                var policy = new SqliteStandingGrantPolicy(db, targets);
                var standingReads = new StandingGrantApplicationReadModelInvocationAdapter(
                    policy, targets, stateSpaces, readModels);
                var service = new ApplicationReadOnlyServiceInvocationAdapter(
                    catalogs, new ApplicationReadOnlyServiceDefinitionReader(schemas), standingReads,
                    schemas, engine, stateSpaces, targets, policy);
                var principal = PrivateOperatorPrincipal.Create("test", "service-fixture-operator");
                await SeedGrantAsync(db, principal, includeQueryInGrant ? [ServiceId, QueryId] : [ServiceId], 1,
                    GrantReference, revoked: false);
                return new(sqlite, db, root, revision, principal, stateSpaces, entities, counterType,
                    service, serviceRecord, retainedDefinition);
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
            bool addCurrent = true)
        {
            var operationId = $"grant-seed-{revision}";
            db.Add(new Operation { Id = operationId, Timestamp = DateTime.UtcNow, Tool = "test" });
            var grant = new StandingGrantRevision(
                reference, "service-grant", revision, new string('0', 64), principal.PrincipalId,
                Application, StandingGrantScope.StateSpace, SpaceId, [StandingGrantCapability.Read],
                new StandingGrantDefinitionAllowance(
                    StandingGrantDefinitionMode.ExactIds,
                    exactIds.Order(StringComparer.Ordinal).ToArray(),
                    []),
                [], 16, DateTime.UtcNow.AddHours(1), revoked, operationId);
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
    }
}
