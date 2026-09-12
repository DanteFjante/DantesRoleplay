using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.World;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string QueryId = "demo.runtime.query.page-summary";
    private const string QueryPath = "content/queries/page-summary.json";
    private const string QueryProjectionOne = "demo.runtime.projection.page-summary-one";
    private const string QueryProjectionTwo = "demo.runtime.projection.page-summary-two";
    private const string QueryOutputSchema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"label\"],\"properties\":{\"label\":{\"type\":\"string\"}}}";

    [Fact]
    public async Task Reviewed_query_publication_refreshes_page_and_incompatible_candidate_preserves_it()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"query-publication-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new DantesRoleplayDbContext(
                new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                    .UseSqlite("Filename=" + databasePath).Options);
            await db.Database.MigrateAsync();
            var setup = Setup(db);
            RegisterQueryNamespaces(setup);
            var firstProjection = WriteQueryProjection("page-summary-one", QueryProjectionOne, "old");
            var secondProjection = WriteQueryProjection("page-summary-two", QueryProjectionTwo, "new");
            WriteQuery(QueryText(QueryProjectionOne, firstProjection, QueryOutputSchema, "Initial page summary."));
            await ActivateAsync(setup);
            await SeedQueryAuthoringGrantAsync(db);
            db.ChangeTracker.Clear();

            var before = setup.Activation.Current(Application)!;
            var materializer = new ActivatedApplicationCatalogMaterializer(
                    setup.Applications, setup.Activation, setup.Sources, setup.Roots, setup.Extensions)
                .UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(),
                    new ActivatedApplicationCatalogCacheAuthority());
            var catalogs = new ActivatedApplicationCatalogProvider(
                new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
                new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)), setup.Activation);
            var features = new InteractionFeatureRetriever(catalogs,
                namespaces: setup.Namespaces, changes: setup.Activation);
            var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
            var schemas = new BoundedJsonSchemaValidator();
            var manuals = new InteractionManualContextService(new ProcedureStore(db), features,
                policy, setup.Resolver, setup.Activation, ["system"]);
            var queryClosure = new ApplicationCandidateQueryClosureReader(
                setup.Activation, setup.Activation, setup.Resolver, catalogs, schemas);
            var gate = new SystemTaskApplicationValidationGate(db, setup.Applications,
                setup.Activation, setup.Resolver, policy, TimeProvider.System,
                pureClosures: null, manuals, features, [queryClosure]);
            var reviews = new SystemTaskApplicationValidationService(db, gate, TimeProvider.System);
            var reviewed = new ApplicationCandidateReviewedQueryUpdateReader(
                db, setup.Applications, setup.Activation, setup.Resolver, gate);
            var authoring = new SqliteApplicationAuthoringService(db, setup.Applications,
                setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver,
                new OperationLog(db), preparation: null, manuals, reviewedPureUpdates: null,
                reviewedQueryUpdates: reviewed);
            var gateway = new ApplicationCandidateCapabilityGateway(
                CandidateCatalog(db, setup, authoring, reviews));
            var principal = TrustedPrincipalContext.VerifiedPrincipal(
                "principal." + new string('a', 64), "test");

            await using var page = await PublishedQueryPageAsync(db, setup, before);
            await SeedQueryPageGrantAsync(db, page.StateSpaceId);
            db.ChangeTracker.Clear();
            var initialSelection = await page.Publication.SelectPublishedAsync(Application, page.EntityId);
            var initialRender = await RenderQueryPageAsync(db, setup, catalogs, policy,
                schemas, page, initialSelection, principal, "query-page-before");
            Assert.True(initialRender.IsSuccess,
                string.Join("; ", initialRender.Errors.Select(value => value.Code)));
            Assert.Equal("<p>old</p>", initialRender.Html);

            const string writeKey = "query-publication-write";
            var written = await WriteQueryCandidateAsync(gateway, principal, before,
                QueryText(QueryProjectionTwo, secondProjection, QueryOutputSchema,
                    "Reviewed page summary."), writeKey);
            var candidate = await CandidateAsync(db, written.OperationId!);
            await CompleteQueryReviewAsync(db, setup, gateway, gate, reviews,
                principal, candidate, writeKey);

            var validated = await ValidateQueryAsync(gateway, principal, candidate,
                "query-publication-validate");
            Assert.True(validated.Ok, validated.Error?.Code + ": " + validated.Error?.Message);
            var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
                .SingleAsync(value => value.OperationId == validated.OperationId);
            Assert.Equal("valid", validation.Outcome);
            Assert.Equal(ApplicationCandidateReviewedQueryUpdateValidation.PreparationVersion,
                validation.PreparationVersion);

            var activationArguments = JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                validationOperationId = validated.OperationId
            });
            var activated = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateActivate, activationArguments,
                "query-publication-activate", "website");
            Assert.True(activated.Ok, activated.Error?.Code + ": " + activated.Error?.Message);
            var current = setup.Activation.Current(Application)!;
            Assert.Equal(before.ActivationRevision + 1, current.ActivationRevision);
            var replayed = await gateway.InvokeAsync(principal, Application,
                SystemCapabilityIds.ApplicationCandidateActivate, activationArguments,
                "query-publication-activate", "website");
            Assert.True(replayed.Ok, replayed.Error?.Code + ": " + replayed.Error?.Message);
            Assert.Equal(activated.OperationId, replayed.OperationId);
            Assert.Equal(current.ActivationFingerprint,
                setup.Activation.Current(Application)!.ActivationFingerprint);

            var refreshedSelection = await page.Publication.SelectPublishedAsync(Application, page.EntityId);
            Assert.Equal(current.ResolutionFingerprint,
                refreshedSelection.Publication.ResolutionFingerprint);
            var refreshed = await RenderQueryPageAsync(db, setup, catalogs, policy,
                schemas, page, refreshedSelection, principal, "query-page-after");
            Assert.True(refreshed.IsSuccess,
                string.Join("; ", refreshed.Errors.Select(value => value.Code)));
            Assert.Equal("<p>new</p>", refreshed.Html);

            var incompatibleSchema =
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"renamed\"],\"properties\":{\"renamed\":{\"type\":\"string\"}}}";
            var incompatibleCandidates = new[]
            {
                (Name: "schema", Text: QueryText(QueryProjectionTwo, secondProjection,
                    incompatibleSchema, "Incompatible output schema.")),
                (Name: "role", Text: QueryText(QueryProjectionTwo, secondProjection,
                    QueryOutputSchema, "Incompatible role contract.", additionalRole: true)),
                (Name: "dependency", Text: QueryText(QueryProjectionTwo, new string('A', 64),
                    QueryOutputSchema, "Unavailable projection dependency."))
            };
            foreach (var incompatible in incompatibleCandidates)
            {
                var prefix = "query-publication-incompatible-" + incompatible.Name;
                var rejectedWrite = await WriteQueryCandidateAsync(gateway, principal, current,
                    incompatible.Text, prefix + "-write");
                var rejectedCandidate = await CandidateAsync(db, rejectedWrite.OperationId!);
                var rejectedValidation = await ValidateQueryAsync(gateway, principal,
                    rejectedCandidate, prefix + "-validate");
                Assert.True(rejectedValidation.Ok,
                    rejectedValidation.Error?.Code + ": " + rejectedValidation.Error?.Message);
                var rejectedRow = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
                    .SingleAsync(value => value.OperationId == rejectedValidation.OperationId);
                Assert.NotEqual("valid", rejectedRow.Outcome);
                var rejectedActivation = await gateway.InvokeAsync(principal, Application,
                    SystemCapabilityIds.ApplicationCandidateActivate,
                    JsonSerializer.Serialize(new
                    {
                        applicationId = Application.Value,
                        candidateId = rejectedCandidate.CandidateId,
                        revision = rejectedCandidate.Revision,
                        contentFingerprint = rejectedCandidate.ContentFingerprint,
                        validationOperationId = rejectedValidation.OperationId
                    }), prefix + "-activate", "website");
                Assert.False(rejectedActivation.Ok);
                Assert.Equal(current.ActivationFingerprint,
                    setup.Activation.Current(Application)!.ActivationFingerprint);
                var retainedPage = await RenderQueryPageAsync(db, setup, catalogs, policy,
                    schemas, page, refreshedSelection, principal,
                    "query-page-retained-" + incompatible.Name);
                Assert.True(retainedPage.IsSuccess,
                    string.Join("; ", retainedPage.Errors.Select(value => value.Code)));
                Assert.Equal("<p>new</p>", retainedPage.Html);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static async Task<ApplicationCandidateCapabilityInvocationResult> WriteQueryCandidateAsync(
        ApplicationCandidateCapabilityGateway gateway, TrustedPrincipalContext principal,
        ActiveApplicationManifest basis, string text, string idempotencyKey)
    {
        var result = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateWrite,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = (string?)null,
                expectedCandidateRevision = 0,
                expectedActiveFingerprint = basis.ActivationFingerprint,
                origin = "runtime",
                synchronizationEvidenceReference = (string?)null,
                newImplementationReason = "Reuse the existing query contract with a reviewed projection.",
                documents = new[]
                {
                    new
                    {
                        logicalIdentity = "file:" + QueryPath,
                        sourceId = "catalog",
                        relativePath = QueryPath,
                        mediaType = "application/json",
                        text
                    }
                }
            }), idempotencyKey, "website");
        Assert.True(result.Ok, result.Error?.Code + ": " + result.Error?.Message);
        return result;
    }

    private static async Task<ApplicationCandidateReference> CandidateAsync(
        DantesRoleplayDbContext db, string operationId)
    {
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .SingleAsync(value => value.SourceOperationId == operationId);
        return new(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
    }

    private static async Task CompleteQueryReviewAsync(
        DantesRoleplayDbContext db, SetupState setup,
        ApplicationCandidateCapabilityGateway gateway,
        SystemTaskApplicationValidationGate gate,
        SystemTaskApplicationValidationService reviews,
        TrustedPrincipalContext principal,
        ApplicationCandidateReference candidate,
        string writeKey)
    {
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .SingleAsync(value => value.CandidateId == candidate.CandidateId);
        var submitted = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateReviewSubmit,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                authoringOperationId = row.SourceOperationId,
                authoringCommandId = GatewayCommandId(
                    principal, SystemCapabilityIds.ApplicationCandidateWrite, writeKey)
            }), "query-publication-review", "website");
        Assert.True(submitted.Ok, submitted.Error?.Code + ": " + submitted.Error?.Message);

        SystemTaskValidationAuthority prepared;
        await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(
            db, TimeProvider.System, false, default))
            prepared = await gate.CheckAsync(PureReviewHost(setup, "query-review-provider"),
                candidate, true);
        Assert.Null(SystemTaskApplicationValidationGate.ExecutionPrerequisite(prepared));
        var closure = Assert.IsType<ApplicationCandidateQueryReviewClosureEvidence>(
            prepared.ReviewClosure);
        Assert.Equal(QueryProjectionTwo, Assert.Single(closure.Dependencies).DefinitionId);
        var provider = new RetainedReviewProvider(prepared.ReviewInput!, "extendExisting");
        var invoker = new SystemInnerWorkerValidationInvoker(
            new AiService([provider]), TimeProvider.System);
        var services = new ServiceCollection().AddSingleton(db).AddSingleton(gate)
            .AddSingleton<TimeProvider>(TimeProvider.System).BuildServiceProvider();
        await using (services)
        {
            var lifecycles = new SystemTaskAiInvocationLifecycleFactory(
                services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            Assert.True(await reviews.RunNextAsync("query-publication-reviewer", invoker,
                ReviewerConfiguration(), lifecycles));
        }
    }

    private static Task<ApplicationCandidateCapabilityInvocationResult> ValidateQueryAsync(
        ApplicationCandidateCapabilityGateway gateway, TrustedPrincipalContext principal,
        ApplicationCandidateReference candidate, string idempotencyKey) => gateway.InvokeAsync(
        principal, Application, SystemCapabilityIds.ApplicationCandidateValidate,
        JsonSerializer.Serialize(new
        {
            applicationId = Application.Value,
            candidateId = candidate.CandidateId,
            revision = candidate.Revision,
            contentFingerprint = candidate.ContentFingerprint,
            samples = Array.Empty<object>()
        }), idempotencyKey, "codex");

    private void RegisterQueryNamespaces(SetupState setup)
    {
        setup.Namespaces.Register(new CatalogNamespaceRegistration("demo.runtime.query", "human-domain-label",
            "Reviewed fixture queries.", [CatalogNamespaceKinds.Query],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed query publication fixture."));
        setup.Namespaces.Register(new CatalogNamespaceRegistration("demo.runtime.projection", "human-domain-label",
            "Reviewed fixture projections.", [CatalogNamespaceKinds.Mechanic],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed query projection fixture."));
    }

    private string WriteQueryProjection(string file, string id, string label)
    {
        var relative = "content/mechanics/" + file + ".md";
        var sourceRelative = "content/mechanics/" + file + ".js";
        var markdown = """
            ---
            id: __ID__
            category: runtime.query.projection
            name: __NAME__
            status: active
            ---

            ## Description
            Project one generic page label.

            ## Requirements
            ```json
            {"roles":{"subject":{"components":[]}}}
            ```
            """.Replace("__ID__", id, StringComparison.Ordinal)
                .Replace("__NAME__", file, StringComparison.Ordinal);
        var source = $"return {{ data: {{ label: '{label}' }} }};";
        WriteFile(relative, markdown);
        WriteFile(sourceRelative, source);
        var parsed = MechanicFile.Parse(markdown, relative, source);
        return ApplicationCatalogRecordContent.Fingerprint(
            ApplicationCatalogRecordContent.MechanicJson(parsed));
    }

    private void WriteQuery(string content) => WriteFile(QueryPath, content);

    private void WriteFile(string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string QueryText(string projectionId, string projectionHash,
        string outputSchema, string description, bool additionalRole = false)
    {
        var schemas = new BoundedJsonSchemaValidator();
        var compiled = schemas.Compile(outputSchema);
        Assert.True(compiled.IsAccepted);
        var roles = new Dictionary<string, string>
        {
            ["subject"] = "The server-selected page entity."
        };
        var roleBindings = new Dictionary<string, object>
        {
            ["subject"] = new { source = "route-entity" }
        };
        if (additionalRole)
        {
            roles["viewer"] = "An additional route entity.";
            roleBindings["viewer"] = new { source = "route-entity" };
        }
        return JsonSerializer.Serialize(new
        {
            id = QueryId,
            category = "runtime.query.page",
            name = "Page summary",
            description,
            matches = new[] { "show the page summary" },
            roles,
            roleBindings,
            executor = ApplicationQueryContract.MechanicProjectionExecutor,
            projection = new
            {
                qualifiedId = projectionId,
                version = 1,
                contentHash = projectionHash,
                outputSchemaHash = compiled.SchemaHash
            },
            outputSchema = JsonDocument.Parse(compiled.NormalizedSchema).RootElement,
            exposure = "binding-only",
            status = "active"
        });
    }

    private static async Task SeedQueryAuthoringGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("grant@1", "grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.Application, null,
            [StandingGrantCapability.Author, StandingGrantCapability.Read,
                StandingGrantCapability.Validate, StandingGrantCapability.Activate],
            new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.runtime.query", true, [CatalogNamespaceKinds.Query]),
                    new("demo.runtime.projection", true, [CatalogNamespaceKinds.Mechanic])]),
            [], 16, DateTime.UtcNow.AddMinutes(10), false, "query-publication-grant");
        await PersistGrantAsync(db, grant, "application");
    }

    private static async Task SeedQueryPageGrantAsync(
        DantesRoleplayDbContext db, string stateSpaceId)
    {
        var grant = new StandingGrantRevision("query-page@1", "query-page", 1,
            new string('0', 64), "principal." + new string('a', 64), Application,
            StandingGrantScope.StateSpace, stateSpaceId, [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ExactIds, [QueryId], []), [], 2,
            DateTime.UtcNow.AddMinutes(10), false, "query-page-grant");
        await PersistGrantAsync(db, grant, "stateSpace");
    }

    private async Task<PublishedQueryPage> PublishedQueryPageAsync(
        DantesRoleplayDbContext db, SetupState setup, ActiveApplicationManifest active)
    {
        var web = new WebContentDbContext(new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite("Filename=" + Path.Combine(root, "query-page.db")).Options);
        await web.Database.MigrateAsync();
        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        const string stateSpaceId = "publication:query";
        stateSpaces.Create(new(stateSpaceId, setup.Applications.Get(Application)!,
            active.ActivationFingerprint, active.ResolutionFingerprint,
            EcsStateSpaceScope.ApplicationPublication));
        setup.Namespaces.Register(new CatalogNamespaceRegistration("system", "system-domain-label",
            "System fixture namespace.", [CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed query page fixture."));
        setup.Namespaces.Register(new CatalogNamespaceRegistration("system.web", "system-domain-label",
            "System web fixture namespace.", [CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed query page fixture."));
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var pageType = types.Define(new(ApplicationIdentifier.System, WebPageComponentTypes.Page,
            await File.ReadAllTextAsync(Path.Combine(repository!.FullName, "catalog", "components",
                "system", "web", "page.schema.json"))));
        var constraints = new SqliteEcsRoleConstraintValidator(db);
        var entities = new SqliteEntityComponentStore(db, types,
            new BoundedJsonSchemaValidator(), constraints);
        const string entityId = "web-page:query";
        await entities.CreateEntityAsync(stateSpaceId, entityId, "Query page");
        var content = new WebPageStore(web);
        const string pageId = "query-page-content";
        var composition = JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            generation = "retained",
            queries = new[] { new { name = "summary", query = QueryId } },
            components = Array.Empty<object>(),
            root = new
            {
                kind = "element",
                tag = "p",
                children = new[] { new { kind = "value", path = "summary.label" } }
            }
        });
        await content.SaveBundleAndActivateAsync(pageId, new("<p>Legacy</p>", []));
        await content.AppendBundleDraftAsync(pageId, 1, new("", [])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = composition
        });
        await entities.AddComponentAsync(new(stateSpaceId, entityId,
            new(pageType.QualifiedId, pageType.Version, pageType.SchemaHash),
            JsonSerializer.Serialize(new
            {
                title = "Query page", navigationLabel = "Query page", slug = "query",
                order = 0, visibility = "public", activeContentReference = new { pageId }
            }), 0));
        var publication = new WebPagePublicationService(setup.Applications, stateSpaces, types,
            entities, new WorldStore(db), content, web,
            new SqliteEcsWriteTransactionFactory(db), new(),
            NullLogger<WebPagePublicationService>.Instance, setup.Activation, constraints);
        var draft = await publication.SelectDraftAsync(Application, entityId, 2);
        await publication.CompareExchangeContentReferenceAsync(draft);
        var selected = await publication.SelectPublishedAsync(Application, entityId);
        var retained = await publication.RevalidateSelectionAsync(selected);
        var parsed = new WebCompositionParser().Parse(retained.CompositionJson!,
            retained.Assets.Select(value => value.Path));
        Assert.True(parsed.IsValid,
            string.Join("; ", parsed.Errors.Select(value => value.Code)));
        return new(web, publication, stateSpaceId, entityId, parsed.Document!);
    }

    private static async Task<WebCompositionRenderResult> RenderQueryPageAsync(
        DantesRoleplayDbContext db, SetupState setup,
        ActivatedApplicationCatalogProvider catalogs, SqliteStandingGrantPolicy policy,
        BoundedJsonSchemaValidator schemas, PublishedQueryPage page,
        WebPagePublicationSelection selection, TrustedPrincipalContext principal, string command)
    {
        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        var mapping = new ApplicationMechanicProjectionMappingResolver(
            catalogs, stateSpaces, types, edges);
        var evaluator = new ApplicationMechanicEvaluator(catalogs,
            new ApplicationMechanicProjectionResolver(db, stateSpaces),
            new JintMechanicEngine());
        var reads = new ApplicationReadModelService(catalogs, setup.Activation,
            stateSpaces, mapping, evaluator, schemas);
        var standing = new StandingGrantApplicationReadModelInvocationAdapter(
            policy, setup.Resolver, stateSpaces, reads);
        var coordinator = new CompositionPageBindingCoordinator(catalogs,
            new ApplicationQueryRoleBindingResolver(schemas), schemas, setup.Resolver,
            policy, new SqliteStandingGrantReadCandidateReader(db),
            new CompositionQueryMaterializer(standing), new UnusedQueryPageActionAdapter());
        var host = InteractionInvocationHost.ForApplication(principal,
            selection.Publication.ApplicationRevision, "query-page@1", command,
            InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1)));
        return await coordinator.RenderAsync(selection, page.Document, host, default);
    }

    private sealed record PublishedQueryPage(
        WebContentDbContext Web,
        WebPagePublicationService Publication,
        string StateSpaceId,
        string EntityId,
        WebCompositionDocument Document) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Web.DisposeAsync();
    }

    private sealed class UnusedQueryPageActionAdapter : IApplicationActionInvocationAdapter
    {
        public Task<InteractionInvocationResult> ExecuteAsync(
            ApplicationActionInvocationRequest request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
            "The query-only page must not dispatch an action.");
    }
}
