using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
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
