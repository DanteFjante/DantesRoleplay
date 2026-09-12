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
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.World;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string PageCounterProjectionId = "demo.runtime.page.counter-projection";
    private const string PageCounterQueryId = "demo.runtime.page.counter-query";
    private const string PageCounterActionId = "demo.runtime.page.set-counter";

    [Fact]
    public async Task Published_composition_invokes_the_active_jint_action_and_rejects_revoked_authority()
    {
        Directory.CreateDirectory(root);
        await using var db = new DantesRoleplayDbContext(
            new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(root, "composition-kernel.db"),
                    Pooling = false
                }.ToString()).Options);
        await db.Database.MigrateAsync();
        var data = await PureRuntimeFixtureAsync(db,
            "return { data: { count: ctx.input.count + 7 } };");
        await AllowPurePublicationAsync(db, execute: true);
        db.ChangeTracker.Clear();
        var policy = new SqliteStandingGrantPolicy(db, data.Setup.Resolver);
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
            new ActivatedApplicationCatalogMaterializer(data.Setup.Applications,
                data.Setup.Activation, data.Setup.Sources, data.Setup.Roots, data.Setup.Extensions),
            new CatalogCursorCodec(new byte[32]), data.Setup.Activation);
        var manuals = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(catalogs), policy, data.Setup.Resolver,
            data.Setup.Activation, ["system"]);
        var authoring = new SqliteApplicationAuthoringService(db, data.Setup.Applications,
            data.Setup.Activation, data.Setup.Activation, data.Setup.Sources, policy,
            data.Setup.Resolver, new OperationLog(db), PureRuntimeValidator(db, data.Setup), manuals);
        var validated = await authoring.ValidateAsync(new ApplicationCandidateValidationRequest(
            data.Candidate, [new(data.Definition, "{\"count\":5}", "{\"count\":12}")]),
            PureRuntimeHost(data.Setup, operations: 3));
        Assert.Equal(InteractionInvocationResultTag.Committed, validated.Tag);
        var activated = await authoring.ActivateAsync(PureRuntimeHost(data.Setup),
            new(data.Candidate, validated.Receipt!.OperationId));
        Assert.Equal(InteractionInvocationResultTag.Committed, activated.Tag);

        var active = data.Setup.Activation.Current(Application)!;
        Assert.True(catalogs.TryGet(Application, out var catalog));
        var mechanic = catalog.Inspect(new(Application, Application.Value,
            data.Definition.DefinitionId)).Summary;
        await using var page = await PublishedActionPageAsync(db, data.Setup, active,
            mechanic.QualifiedId);
        var schemas = new BoundedJsonSchemaValidator();
        var coordinator = new CompositionPageBindingCoordinator(catalogs,
            new ApplicationQueryRoleBindingResolver(schemas), schemas, data.Setup.Resolver,
            policy, new SqliteStandingGrantReadCandidateReader(db),
            new CompositionQueryMaterializer(new UnusedCompositionReadAdapter()),
            PureActionAdapter(db, data.Setup, catalogs));
        var principal = PureRuntimeHost(data.Setup).Principal;
        var pageHost = InteractionInvocationHost.ForApplication(principal,
            page.Selection.Publication.ApplicationRevision, "grant@1", "page-render",
            InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

        var rendered = await coordinator.RenderAsync(page.Selection, page.Document,
            pageHost, default);
        var result = await coordinator.InvokeActionAsync(page.Selection, page.Document,
            principal, "calculate", "page-action", "{\"count\":5}",
            DateTime.UtcNow.AddMinutes(1), default);

        Assert.True(rendered.IsSuccess,
            string.Join("; ", rendered.Errors.Select(value => value.Code)));
        Assert.Contains("/ui/dynamic/actions/", rendered.Html, StringComparison.Ordinal);
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("{\"count\":12}", result.DataJson);

        await RevokeGrantAsync(db);
        db.ChangeTracker.Clear();
        var denied = await coordinator.InvokeActionAsync(page.Selection, page.Document,
            principal, "calculate", "page-action-revoked", "{\"count\":5}",
            DateTime.UtcNow.AddMinutes(1), default);
        Assert.Equal(InteractionInvocationResultTag.Failed, denied.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", denied.Code);
    }

    [Fact]
    public async Task Published_composition_commits_stateful_action_then_fresh_query_reads_it_for_the_authorized_audience()
    {
        Directory.CreateDirectory(root);
        await using var db = new DantesRoleplayDbContext(
            new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(root, "composition-stateful-kernel.db"),
                    Pooling = false
                }.ToString()).Options);
        await db.Database.MigrateAsync();
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration("demo.runtime.page", "human-domain-label",
            "Runtime page fixture namespace.", [CatalogNamespaceKinds.Mechanic, CatalogNamespaceKinds.Query,
                CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        WritePageMechanic("counter-projection", PageCounterProjectionId,
            "{\"roles\":{\"subject\":{\"components\":[\"runtime.page.counter\"]}}}",
            "var c=JSON.parse(ctx.roles.subject.components['runtime.page.counter']);return {data:{value:c.value}};");
        WritePageMechanic("set-counter", PageCounterActionId,
            "{\"roles\":{\"subject\":{\"components\":[\"runtime.page.counter\"]}},\"inputSchema\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}}",
            "return {effects:[{type:'component.set',entityId:ctx.roles.subject.id,definitionId:'runtime.page.counter',data:JSON.stringify({value:ctx.input.value})}]};");
        await ActivateAsync(setup);

        var schemas = new BoundedJsonSchemaValidator();
        const string outputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}";
        var compiledOutput = schemas.Compile(outputSchema);
        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
            setup.Sources, setup.Roots, setup.Extensions);
        var projectionRecord = materializer.Build(Application).Records
            .Single(value => value.QualifiedId == PageCounterProjectionId);
        WritePageQuery(JsonSerializer.Serialize(new
        {
            id = PageCounterQueryId,
            category = "runtime.page.fixture",
            name = "Page counter",
            description = "Reads the counter on the selected page entity.",
            matches = new[] { "read page counter" },
            roles = new Dictionary<string, string> { ["subject"] = "The selected page entity." },
            roleBindings = new Dictionary<string, object> { ["subject"] = new { source = "route-entity" } },
            executor = ApplicationQueryContract.MechanicProjectionExecutor,
            projection = new
            {
                qualifiedId = PageCounterProjectionId,
                version = projectionRecord.Version,
                contentHash = projectionRecord.ContentFingerprint,
                outputSchemaHash = compiledOutput.SchemaHash
            },
            outputSchema = JsonDocument.Parse(compiledOutput.NormalizedSchema).RootElement,
            exposure = "binding-only",
            status = "active"
        }));
        var prior = setup.Activation.Current(Application)!;
        await ActivateAsync(setup, prior.ActivationFingerprint);
        var active = setup.Activation.Current(Application)!;
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
            new CatalogCursorCodec(new byte[32]), setup.Activation);
        Assert.True(catalogs.TryGet(Application, out var catalog));
        _ = catalog.Inspect(new(Application, Application.Value, PageCounterActionId)).Summary;

        var revision = new ApplicationRevision(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []);
        var spaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        const string stateSpaceId = "publication:counter";
        spaces.Create(new(stateSpaceId, revision, active.ActivationFingerprint,
            active.ResolutionFingerprint, EcsStateSpaceScope.ApplicationPublication));
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var counterType = types.Define(new(Application, "demo.runtime.page.counter", outputSchema));
        setup.Namespaces.Register(new CatalogNamespaceRegistration("system", "human-domain-label",
            "System fixture namespace.", [CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        setup.Namespaces.Register(new CatalogNamespaceRegistration("system.web", "human-domain-label",
            "System web fixture namespace.", [CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var pageType = types.Define(new(ApplicationIdentifier.System, WebPageComponentTypes.Page,
            await File.ReadAllTextAsync(Path.Combine(repository!.FullName, "catalog", "components",
                "system", "web", "page.schema.json"))));
        var entities = new SqliteEntityComponentStore(db, types, schemas, new SqliteEcsRoleConstraintValidator(db));
        const string entityId = "web-page:counter";
        await entities.CreateEntityAsync(stateSpaceId, entityId, "Counter");
        await entities.AddComponentAsync(new(stateSpaceId, entityId,
            new(counterType.QualifiedId, counterType.Version, counterType.SchemaHash), "{\"value\":7}", 0));

        var webPath = Path.Combine(root, "page-counter.db");
        await using var web = new WebContentDbContext(new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = webPath, Pooling = false }.ToString()).Options);
        await web.Database.MigrateAsync();
        var content = new WebPageStore(web);
        const string contentId = "counter-content";
        var composition = JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            generation = "retained",
            queries = new[] { new { name = "counter", query = PageCounterQueryId, input = new { } } },
            actions = new[] { new { name = "set", mechanic = PageCounterActionId } },
            components = Array.Empty<object>(),
            root = new
            {
                kind = "element", tag = "main", children = new object[]
                {
                    new { kind = "value", path = "counter.value" },
                    new { kind = "element", tag = "button", action = "set", children = new[] { new { kind = "text", text = "Set" } } }
                }
            }
        });
        await content.SaveBundleAndActivateAsync(contentId, new WebPageBundle("<p>Legacy</p>", []));
        await content.AppendBundleDraftAsync(contentId, 1, new WebPageBundle("", [])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = composition
        });
        await entities.AddComponentAsync(new(stateSpaceId, entityId,
            new(pageType.QualifiedId, pageType.Version, pageType.SchemaHash),
            JsonSerializer.Serialize(new
            {
                title = "Counter", navigationLabel = "Counter", slug = "counter", order = 0,
                visibility = "public", activeContentReference = new { pageId = contentId }
            }), 0));
        var constraints = new SqliteEcsRoleConstraintValidator(db);
        var transactions = new SqliteEcsWriteTransactionFactory(db);
        var publication = new WebPagePublicationService(setup.Applications, spaces, types, entities,
            new WorldStore(db), content, web, transactions, new(), NullLogger<WebPagePublicationService>.Instance,
            setup.Activation, constraints);
        await publication.CompareExchangeContentReferenceAsync(
            await publication.SelectDraftAsync(Application, entityId, 2));

        var principal = TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test");
        const string grantReference = "page-stateful@1";
        await SeedPageStatefulGrantAsync(db, principal, stateSpaceId, grantReference,
            [PageCounterQueryId, PageCounterActionId]);
        db.ChangeTracker.Clear();
        var targets = new SqliteStandingGrantTargetResolver(db, setup.Applications, setup.Activation,
            setup.Activation, setup.Sources, setup.Extensions, setup.Namespaces, materializer);
        var policy = new SqliteStandingGrantPolicy(db, targets);
        var edges = new SqliteStateSpaceEdgeStore(db, spaces);
        var projection = new ApplicationMechanicProjectionResolver(db, spaces);
        var mapping = new ApplicationMechanicProjectionMappingResolver(catalogs, spaces, types, edges);
        var evaluator = new ApplicationMechanicEvaluator(catalogs, projection, new JintMechanicEngine());
        var reads = new ApplicationReadModelService(catalogs, setup.Activation, spaces, mapping, evaluator, schemas);
        var standingReads = new StandingGrantApplicationReadModelInvocationAdapter(policy, targets, spaces, reads);
        var operations = new OperationLog(db);
        var effects = new ApplicationEcsEffectApplier(db, entities, spaces, operations, edges);
        var runner = new ApplicationActionRunner(catalogs, setup.Activation, spaces, types, entities, edges,
            mapping, evaluator, effects, operations, new ApplicationEcsEffectBatchBuilder(types, entities, edges));
        var actionAdapter = new ApplicationActionInvocationAdapter(
            new PrivateHostInteractionAuthorizationPolicy(spaces), spaces, runner, operations,
            null, targets, policy, transactions);
        var coordinator = new CompositionPageBindingCoordinator(catalogs,
            new ApplicationQueryRoleBindingResolver(schemas), schemas, targets, policy,
            new SqliteStandingGrantReadCandidateReader(db), new CompositionQueryMaterializer(standingReads),
            actionAdapter, actionAdapter);

        var selected = await publication.SelectPublishedAsync(Application, entityId);
        var retained = await publication.RevalidateSelectionAsync(selected);
        var document = new WebCompositionParser().Parse(retained.CompositionJson!,
            retained.Assets.Select(value => value.Path)).Document!;
        var before = await coordinator.RenderAsync(selected, document,
            PageHost(selected, principal, "page-before"), default);
        var committed = await coordinator.InvokeActionAsync(selected, document, principal,
            "set", "page-set", "{\"value\":11}", DateTime.UtcNow.AddMinutes(1), default);
        var refreshedSelection = await publication.SelectPublishedAsync(Application, entityId);
        var after = await coordinator.RenderAsync(refreshedSelection, document,
            PageHost(refreshedSelection, principal, "page-after"), default);

        Assert.True(before.IsSuccess, string.Join("; ", before.Errors.Select(value => value.Code)));
        Assert.Contains(">7<", before.Html, StringComparison.Ordinal);
        Assert.Equal(InteractionInvocationResultTag.Committed, committed.Tag);
        Assert.True(after.IsSuccess, string.Join("; ", after.Errors.Select(value => value.Code)));
        Assert.Contains(">11<", after.Html, StringComparison.Ordinal);
        Assert.Equal(2, (await entities.GetComponentAsync(
            stateSpaceId, entityId, counterType.QualifiedId))!.Revision);

        var wrongAudience = TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('b', 64), "test");
        var wrongRead = await coordinator.RenderAsync(refreshedSelection, document,
            PageHost(refreshedSelection, wrongAudience, "page-wrong-read"), default);
        var wrong = await coordinator.InvokeActionAsync(refreshedSelection, document, wrongAudience,
            "set", "page-wrong", "{\"value\":13}", DateTime.UtcNow.AddMinutes(1), default);
        Assert.Equal("COMPOSITION_QUERY_NOT_AUTHORIZED", Assert.Single(wrongRead.Errors).Code);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", wrong.Code);
        await RevokePageStatefulGrantAsync(db, principal, stateSpaceId);
        db.ChangeTracker.Clear();
        var revokedRead = await coordinator.RenderAsync(refreshedSelection, document,
            PageHost(refreshedSelection, principal, "page-revoked-read"), default);
        var revoked = await coordinator.InvokeActionAsync(refreshedSelection, document, principal,
            "set", "page-revoked", "{\"value\":17}", DateTime.UtcNow.AddMinutes(1), default);
        Assert.Equal("COMPOSITION_QUERY_NOT_AUTHORIZED", Assert.Single(revokedRead.Errors).Code);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", revoked.Code);
        Assert.Contains("\"value\":11", (await entities.GetComponentAsync(
            stateSpaceId, entityId, counterType.QualifiedId))!.ValueJson);
    }

    private void WritePageMechanic(string file, string id, string requirements, string source)
    {
        var markdown = Path.Combine(root, "content", "mechanics", file + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(markdown)!);
        File.WriteAllText(markdown, $$$$"""
            ---
            id: {{{{id}}}}
            category: runtime.page.fixture
            name: {{{{file}}}}
            scope: action
            status: active
            ---

            ## Description
            Generic runtime page fixture.

            ## Requirements
            ```json
            {{{{requirements}}}}
            ```
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "content", "mechanics", file + ".js"),
            source, new UTF8Encoding(false));
    }

    private void WritePageQuery(string json)
    {
        var path = Path.Combine(root, "content", "queries", "counter-query.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private static InteractionInvocationHost PageHost(
        WebPagePublicationSelection selection,
        TrustedPrincipalContext principal,
        string commandId) =>
        new(principal, selection.Publication.ApplicationRevision, selection.Publication.StateSpaceId,
            "page-stateful@1", commandId, InteractionStateRevision.From(selection.Publication),
            InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1)));

    private static async Task SeedPageStatefulGrantAsync(
        DantesRoleplayDbContext db,
        TrustedPrincipalContext principal,
        string stateSpaceId,
        string reference,
        IReadOnlyList<string> exactIds)
    {
        var grant = new StandingGrantRevision(reference, "page-stateful", 1, new string('0', 64),
            principal.PrincipalId, Application, StandingGrantScope.StateSpace, stateSpaceId,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ExactIds, exactIds.Order(StringComparer.Ordinal).ToArray(), []),
            [ApplicationEcsEffectType.ComponentSet], 8, DateTime.UtcNow.AddMinutes(10), false,
            "page-stateful-grant");
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(ToRecord(grant));
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = grant.Revision });
        await db.SaveChangesAsync();
    }

    private static async Task RevokePageStatefulGrantAsync(
        DantesRoleplayDbContext db,
        TrustedPrincipalContext principal,
        string stateSpaceId)
    {
        var revoked = new StandingGrantRevision("page-stateful@2", "page-stateful", 2, new string('0', 64),
            principal.PrincipalId, Application, StandingGrantScope.StateSpace, stateSpaceId,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ExactIds,
                new[] { PageCounterActionId, PageCounterQueryId }.Order(StringComparer.Ordinal).ToArray(), []),
            [ApplicationEcsEffectType.ComponentSet], 8, DateTime.UtcNow.AddMinutes(10), true,
            "page-stateful-revoke");
        db.Add(new Operation { Id = revoked.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(ToRecord(revoked));
        (await db.Set<StandingGrantCurrentRecord>().SingleAsync(value => value.GrantId == revoked.GrantId)).Revision = 2;
        await db.SaveChangesAsync();
    }

    private static StandingGrantRevisionRecord ToRecord(StandingGrantRevision grant) => new()
    {
        GrantId = grant.GrantId,
        Revision = grant.Revision,
        GrantReference = grant.GrantReference,
        PrincipalReference = grant.PrincipalReference,
        ApplicationId = grant.ApplicationId.Value,
        Scope = "stateSpace",
        StateSpaceId = grant.StateSpaceId,
        PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
        ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
        Revoked = grant.Revoked,
        ExpiresAtUtc = grant.ExpiresAtUtc,
        MaximumOperations = grant.MaximumOperations,
        IssuedByOperationId = grant.IssuedByOperationId
    };

    private async Task<PublishedActionPage> PublishedActionPageAsync(
        DantesRoleplayDbContext db,
        SetupState setup,
        ActiveApplicationManifest active,
        string mechanicId)
    {
        var database = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "composition-publication.db"),
            Pooling = false
        }.ToString();
        var web = new WebContentDbContext(new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(database).Options);
        await web.Database.MigrateAsync();
        var spaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        const string publicationSpace = "publication:dynamic";
        spaces.Create(new StateSpaceBinding(publicationSpace,
            setup.Applications.Get(Application)!, active.ActivationFingerprint,
            active.ResolutionFingerprint, EcsStateSpaceScope.ApplicationPublication));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        setup.Namespaces.Register(new CatalogNamespaceRegistration("system", "system-domain-label",
            "System fixture namespace.", [CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed system fixture."));
        setup.Namespaces.Register(new CatalogNamespaceRegistration("system.web", "system-domain-label",
            "System web fixture namespace.", [CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed system web fixture."));
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md")))
            repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("The test worktree root was not found.");
        var pageType = types.Define(new(ApplicationIdentifier.System, WebPageComponentTypes.Page,
            await File.ReadAllTextAsync(Path.Combine(repository.FullName, "catalog", "components",
                "system", "web", "page.schema.json"))));
        var constraints = new SqliteEcsRoleConstraintValidator(db);
        var entities = new SqliteEntityComponentStore(db, types, schemas, constraints);
        const string entityId = "web-page:dynamic";
        await entities.CreateEntityAsync(publicationSpace, entityId, "Dynamic");
        var content = new WebPageStore(web);
        const string pageId = "dynamic-content";
        var composition = JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            generation = "retained",
            actions = new[] { new { name = "calculate", mechanic = mechanicId } },
            components = Array.Empty<object>(),
            root = new
            {
                kind = "element",
                tag = "button",
                action = "calculate",
                children = Array.Empty<object>()
            }
        });
        await content.SaveBundleAndActivateAsync(pageId, new WebPageBundle("<p>Legacy</p>", []));
        await content.AppendBundleDraftAsync(pageId, 1, new WebPageBundle("", [])
        {
            ContentFormat = WebPageContentFormat.Composition,
            CompositionJson = composition
        });
        await entities.AddComponentAsync(new(publicationSpace, entityId,
            new(pageType.QualifiedId, pageType.Version, pageType.SchemaHash),
            JsonSerializer.Serialize(new
            {
                title = "Dynamic",
                navigationLabel = "Dynamic",
                slug = "dynamic",
                order = 0,
                visibility = "public",
                activeContentReference = new { pageId }
            }), 0));
        var transactions = new SqliteEcsWriteTransactionFactory(db);
        var publication = new WebPagePublicationService(setup.Applications, spaces, types,
            entities, new WorldStore(db), content, web, transactions, new(),
            NullLogger<WebPagePublicationService>.Instance, setup.Activation, constraints);
        var draft = await publication.SelectDraftAsync(Application, entityId, 2);
        await publication.CompareExchangeContentReferenceAsync(draft);
        var selection = await publication.SelectPublishedAsync(Application, entityId);
        var selected = await publication.RevalidateSelectionAsync(selection);
        var parsed = new WebCompositionParser().Parse(selected.CompositionJson!,
            selected.Assets.Select(value => value.Path));
        Assert.True(parsed.IsValid,
            string.Join("; ", parsed.Errors.Select(value => value.Code)));
        return new(web, selection, parsed.Document!);
    }

    private sealed record PublishedActionPage(
        WebContentDbContext Web,
        WebPagePublicationSelection Selection,
        WebCompositionDocument Document) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Web.DisposeAsync();
    }

    private sealed class UnusedCompositionReadAdapter : IApplicationReadModelInvocationAdapter
    {
        public Task<InteractionInvocationResult> ReadAsync(
            ApplicationReadModelInvocationRequest request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
                "The action-only composition must not dispatch a query.");
    }
}
