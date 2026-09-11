using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.MCPServer;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DantesRoleplay.Tests;

public sealed class WebPagePublicationSelectionTests
{
    [Fact]
    public async Task Legacy_publication_requires_an_explicit_draft_and_never_substitutes_active_assets()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Error("WEB_CONTENT_UNPINNED", () => fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId));
        var selected = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, 2, Composition("newer", [3]));
        Assert.Equal(2, (await fixture.Publication.RevalidateSelectionAsync(selected)).Summary.Revision);
        Assert.Equal(new byte[] { 2 }, (await fixture.Publication.ReadSelectedAssetAsync(selected, "assets/icon.bin"))!.Content);
        var rendered = await fixture.Publication.RenderLiteralSelectionAsync(selected);
        Assert.True(rendered.IsSuccess);
        Assert.Contains("/ui/example/content/example-content/revisions/2/assets/icon.bin", rendered.Html);
        Assert.Null(await fixture.Publication.ReadSelectedAssetAsync(selected, "assets/missing.bin"));
        Assert.Equal(1, (await fixture.Content.GetActiveAsync(Fixture.ContentId))!.Revision);
        Assert.Equal(new byte[] { 1 }, (await fixture.Content.GetActiveAssetAsync(Fixture.ContentId, "assets/icon.bin"))!.Content);
        await Error("WEB_CONTENT_REVISION_UNKNOWN", () => fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 500));
    }

    [Fact]
    public async Task Literal_retained_components_and_slots_render_without_constructing_query_or_action_authority()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string literal = """
            {"formatVersion":1,"generation":"literal","components":[
              {"id":"frame","revision":"1","template":{"kind":"element","tag":"section","children":[{"kind":"slot","name":"body"}]}}],
             "root":{"kind":"component","id":"frame","revision":"1","slots":{"body":[{"kind":"text","text":"Retained & safe"}]}}}
            """;
        await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, 2,
            new(string.Empty, []) { ContentFormat = WebPageContentFormat.Composition, CompositionJson = literal });
        var selected = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 3);
        Assert.Equal("<section>Retained &amp; safe</section>", (await fixture.Publication.RenderLiteralSelectionAsync(selected)).Html);
        var latest = 3;
        foreach (var declarations in new[] { "\"queries\":[{\"name\":\"records\"}],", "\"actions\":[{\"name\":\"save\"}]," })
        {
            await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, latest,
                new(string.Empty, []) { ContentFormat = WebPageContentFormat.Composition, CompositionJson = "{" + declarations + literal[1..] });
            selected = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, ++latest);
            var rendered = await fixture.Publication.RenderLiteralSelectionAsync(selected);
            Assert.Null(rendered.Html);
            Assert.Equal("COMPOSITION_BINDINGS_UNAVAILABLE", Assert.Single(rendered.Errors).Code);
        }
    }

    [Fact]
    public async Task Selection_rechecks_metadata_entity_lifecycle_and_publication_binding()
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeMetadata = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        await fixture.Administration.UpdateMetadataAsync(fixture.ApplicationId, Fixture.EntityId,
            new(beforeMetadata.PageComponent.Revision, "Updated", "Page", "example", 0, "public"));
        await Error("WEB_PAGE_SELECTION_STALE", () => fixture.Publication.RevalidateSelectionAsync(beforeMetadata));
        await Error("WEB_PAGE_SELECTION_STALE", () => fixture.Publication.CompareExchangeContentReferenceAsync(beforeMetadata));

        var beforeDisable = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        await fixture.Lifecycle.SetEntityEnabledAsync(Fixture.PublicationSpace, Fixture.EntityId, false, beforeDisable.Entity.Revision);
        await Error("WEB_PAGE_UNKNOWN", () => fixture.Publication.RevalidateSelectionAsync(beforeDisable));
        var disabled = await fixture.Lifecycle.GetEntityAsync(Fixture.PublicationSpace, Fixture.EntityId);
        await fixture.Lifecycle.SetEntityEnabledAsync(Fixture.PublicationSpace, Fixture.EntityId, true, disabled!.Entity.Revision);

        var beforeRebind = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        await fixture.Data.Database.ExecuteSqlRawAsync("UPDATE system_state_space SET BindingRevision = BindingRevision + 1 WHERE Id = 'publication:example';");
        await Error("WEB_PAGE_SELECTION_STALE", () => fixture.Publication.RevalidateSelectionAsync(beforeRebind));
        Assert.Equal(1, (await fixture.Content.GetSummaryAsync(Fixture.ContentId))!.ActiveRevision);
    }

    [Fact]
    public async Task Retained_payload_corruption_is_detected_before_returning_selected_bytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        await fixture.Web.Database.ExecuteSqlRawAsync("UPDATE web_page_asset_content SET Content = X'99';");
        await Error("CONTENT_REFERENCE_INVALID_ASSET", () => fixture.Publication.ReadSelectedAssetAsync(selected, "assets/icon.bin"));
    }

    [Fact]
    public async Task Pin_CAS_uses_retained_content_preserves_metadata_and_reports_separate_compatibility_pointer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        var written = await fixture.Publication.CompareExchangeContentReferenceAsync(draft);
        Assert.Equal(draft.PageComponent.Revision + 1, written.PageComponentRevision);
        var published = await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId);
        Assert.Equal(draft.Content, published.Content);
        Assert.False(published.IsDraft);
        var compatibility = await fixture.Publication.ReadCompatibilityPointerAsync(published);
        Assert.True(compatibility.RequiresReconciliation);
        Assert.Equal(1, compatibility.ActiveRevision);
        Assert.Equal(2, compatibility.PinnedRevision);

        await fixture.Administration.UpdateMetadataAsync(fixture.ApplicationId, Fixture.EntityId,
            new(published.PageComponent.Revision, "Changed title", "Page", "example", 1, "public"));
        published = await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId);
        Assert.Equal(draft.Content, published.Content);
        Assert.Equal(new byte[] { 2 }, (await fixture.Publication.ReadSelectedAssetAsync(published, "assets/icon.bin"))!.Content);
        Assert.Equal(1, (await fixture.Content.GetActiveAsync(Fixture.ContentId))!.Revision);

        // Existing public discovery cannot route this pin to the unrelated legacy active HTML.
        var discovery = new WebPublicationDiscovery(fixture.Applications, fixture.Spaces, fixture.Entities,
            fixture.Content, fixture.Lifecycle);
        var diagnostic = await discovery.GetApplicationAsync(fixture.ApplicationId, diagnostics: true);
        Assert.Contains(diagnostic!.Evidence!, value => value.Code == "PAGE_PERMISSIONED_CONTENT_UNAVAILABLE");
        Assert.NotEqual("ready", (await discovery.ResolvePageRouteAsync("example")).Status);
    }

    [Fact]
    public async Task Legacy_publish_and_activation_cannot_mutate_a_pinned_pages_compatibility_pointer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2));
        // Draft authoring and reads remain allowed; revision 3 is valid HTML so the old format
        // guard alone cannot prevent an incorrect compatibility-pointer activation.
        var draft = await fixture.Administration.AppendBundleDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2,
            new("<p>Next HTML draft</p>", [new("assets/icon.bin", [3])]));
        Assert.Equal(3, draft.Summary.Revision);
        Assert.NotNull(await fixture.Administration.GetRevisionAsync(fixture.ApplicationId, Fixture.EntityId, 3));
        var before = await fixture.Content.GetSummaryAsync(Fixture.ContentId);
        var pinned = await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId);

        var publish = await Assert.ThrowsAsync<WebPageAdministrationException>(() =>
            fixture.Administration.PublishBundleAsync(fixture.ApplicationId, Fixture.EntityId,
                new("<p>Must not be retained or activated</p>", [new("assets/extra.bin", [4])])));
        Assert.Equal("WEB_PINNED_PUBLICATION_UNAVAILABLE", publish.Code);
        var activation = await Assert.ThrowsAsync<WebPageAdministrationException>(() =>
            fixture.Administration.ActivateRevisionAsync(fixture.ApplicationId, Fixture.EntityId, new(1, 3)));
        Assert.Equal("WEB_PINNED_PUBLICATION_UNAVAILABLE", activation.Code);

        Assert.Equal(before, await fixture.Content.GetSummaryAsync(Fixture.ContentId));
        Assert.Null(await fixture.Content.GetRevisionAsync(Fixture.ContentId, 4));
        Assert.Equal(pinned.Content, (await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId)).Content);
        Assert.Equal(new byte[] { 1 }, (await fixture.Content.GetActiveAssetAsync(Fixture.ContentId, "assets/icon.bin"))!.Content);
        Assert.Equal(new byte[] { 2 }, (await fixture.Publication.ReadSelectedAssetAsync(pinned, "assets/icon.bin"))!.Content);
    }

    [Fact]
    public async Task Legacy_readiness_cannot_report_a_pinned_page_current_even_when_all_revision_numbers_agree()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, 2, new("<p>Current HTML</p>", []));
        await fixture.Content.ActivateRevisionAsync(Fixture.ContentId, 3, 1);
        await fixture.Administration.SetIndexAsync(fixture.ApplicationId, Fixture.EntityId, new(true));

        // Only the page owner is under test. Other readiness owners are absent; this does not
        // demonstrate whole-application readiness or any permissioned composition transport.
        var readiness = new ApplicationReadinessService(fixture.Data, fixture.Applications,
            null!, null!, fixture.Publication, fixture.Content, null!, null!, null!, null!);
        var before = await readiness.ReadAsync(fixture.ApplicationId.Value);
        Assert.Equal("WEB_INDEX_PAGE_CURRENT", Assert.Single(before.Checks, check => check.Name == "web-page-release").Code);

        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 3));
        var pin = await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId);
        var summary = (await fixture.Content.GetSummaryAsync(Fixture.ContentId))!;
        Assert.Equal(pin.Content.Revision, summary.ActiveRevision);
        Assert.Equal(summary.ActiveRevision, summary.LatestRevision);
        Assert.Null(await fixture.Publication.FindIndexAsync(fixture.ApplicationId));
        Assert.Null(await fixture.Publication.FindBySlugAsync("example"));
        var after = await readiness.ReadAsync(fixture.ApplicationId.Value);
        var pageCheck = Assert.Single(after.Checks, check => check.Name == "web-page-release");
        Assert.Equal("WEB_INDEX_PAGE_UNAVAILABLE", pageCheck.Code);
        Assert.NotEqual("ready", pageCheck.Status);
    }

    [Fact]
    public async Task Failed_second_publication_keeps_the_prior_ECS_pin_and_retained_assets()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2));
        await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, 2, Composition("next", [3]));
        var candidate = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 3);
        await fixture.Administration.UpdateMetadataAsync(fixture.ApplicationId, Fixture.EntityId,
            new(candidate.PageComponent.Revision, "Concurrent title", "Page", "example", 0, "public"));
        await Error("WEB_PAGE_SELECTION_STALE", () => fixture.Publication.CompareExchangeContentReferenceAsync(candidate));
        var current = await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId);
        Assert.Equal(2, current.Content.Revision);
        Assert.Equal(new byte[] { 2 }, (await fixture.Publication.ReadSelectedAssetAsync(current, "assets/icon.bin"))!.Content);
        Assert.Equal(3, (await fixture.Content.GetSummaryAsync(Fixture.ContentId))!.LatestRevision);
    }

    [Fact]
    public async Task Separate_connections_competing_for_one_publication_revision_commit_only_one_pin()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, 2, Composition("competitor", [3]));
        await using var peerData = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(fixture.Data.Database.GetConnectionString()!).Options);
        await using var peerWeb = new WebContentDbContext(new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(fixture.Web.Database.GetConnectionString()!).Options);
        var applications = new SqliteApplicationRegistry(peerData);
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(peerData, schemas);
        var entities = new SqliteEntityComponentStore(peerData, types, schemas, new SqliteEcsRoleConstraintValidator(peerData));
        var peer = new WebPagePublicationService(applications, new SqliteStateSpaceRegistry(peerData, applications),
            types, entities, new WorldStore(peerData), new WebPageStore(peerWeb), peerWeb,
            new SqliteEcsWriteTransactionFactory(peerData), new(), NullLogger<WebPagePublicationService>.Instance,
            publicationConstraints: new SqliteEcsRoleConstraintValidator(peerData));
        var first = await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 2);
        var second = await peer.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 3);
        Assert.Equal(first.PageComponent.Revision, second.PageComponent.Revision);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(WebPagePublicationPinResult? Result, Exception? Failure)> Attempt(
            WebPagePublicationService owner, WebPagePublicationSelection selection)
        {
            await start.Task;
            try { return (await owner.CompareExchangeContentReferenceAsync(selection), null); }
            catch (Exception exception) { return (null, exception); }
        }
        var one = Task.Run(() => Attempt(fixture.Publication, first));
        var two = Task.Run(() => Attempt(peer, second));
        start.SetResult();
        var outcomes = await Task.WhenAll(one, two);
        var winner = Assert.Single(outcomes, value => value.Result is not null).Result!;
        var loser = Assert.Single(outcomes, value => value.Failure is not null).Failure!;
        Assert.Equal("WEB_PAGE_SELECTION_STALE", Assert.IsType<WebPageStoreException>(loser).Code);
        var current = await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId);
        Assert.Equal(winner.Content, current.Content);
        Assert.Equal(first.PageComponent.Revision + 1, current.PageComponent.Revision);
        Assert.Equal(new byte[] { (byte)winner.Content.Revision!.Value },
            (await fixture.Publication.ReadSelectedAssetAsync(current, "assets/icon.bin"))!.Content);
        Assert.NotNull(await fixture.Content.GetRevisionAsync(Fixture.ContentId, 2));
        Assert.NotNull(await fixture.Content.GetRevisionAsync(Fixture.ContentId, 3));
    }

    private static WebPageBundle Composition(string generation, byte[] bytes) => new(string.Empty, [new("assets/icon.bin", bytes)])
    {
        ContentFormat = WebPageContentFormat.Composition,
        CompositionJson = JsonSerializer.Serialize(new
        {
            formatVersion = 1, generation, components = Array.Empty<object>(),
            root = new { kind = "asset", path = "assets/icon.bin" }
        })
    };

    private static async Task Error(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<WebPageStoreException>(action)).Code);

    internal sealed class Fixture : IAsyncDisposable
    {
        public const string EntityId = "web-page:example", ContentId = "example-content", PublicationSpace = "publication:example";
        public ApplicationIdentifier ApplicationId { get; } = ApplicationIdentifier.Parse("example");
        public required DantesRoleplayDbContext Data { get; init; }
        public required WebContentDbContext Web { get; init; }
        public required SqliteApplicationRegistry Applications { get; init; }
        public required SqliteStateSpaceRegistry Spaces { get; init; }
        public required SqliteEntityComponentStore Entities { get; init; }
        public required SqliteEcsLifecycleStore Lifecycle { get; init; }
        public required WebPageStore Content { get; init; }
        public required WebPagePublicationService Publication { get; init; }
        public required WebPageAdministration Administration { get; init; }
        public required SqliteEcsWriteTransactionFactory Transactions { get; init; }
        public required SqliteEcsRoleConstraintValidator Constraints { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("The test worktree root was not found.");
            var directory = Path.Combine(root.FullName, ".tmp", "publication-selection-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string Connection(string name) => new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(directory, name), Pooling = false }.ToString();
            var data = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(Connection("kernel.db")).Options);
            var web = new WebContentDbContext(new DbContextOptionsBuilder<WebContentDbContext>().UseSqlite(Connection("web.db")).Options);
            await data.Database.MigrateAsync();
            await web.Database.MigrateAsync();
            var applications = new SqliteApplicationRegistry(data);
            var application = applications.Register(new(ApplicationIdentifier.Parse("example"), "Example", "Generic page fixture.", []));
            var spaces = new SqliteStateSpaceRegistry(data, applications);
            spaces.Create(new StateSpaceBinding(PublicationSpace, application, application.Fingerprint,
                application.Fingerprint, EcsStateSpaceScope.ApplicationPublication));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(data, schemas);
            var pageType = types.Define(new(ApplicationIdentifier.System, WebPageComponentTypes.Page,
                await File.ReadAllTextAsync(Path.Combine(root.FullName, "catalog", "components", "system", "web", "page.schema.json"))));
            types.Define(new(ApplicationIdentifier.System, WebPageComponentTypes.IndexPage,
                await File.ReadAllTextAsync(Path.Combine(root.FullName, "catalog", "components", "system", "web", "index-page.schema.json"))));
            var constraints = new SqliteEcsRoleConstraintValidator(data);
            var entities = new SqliteEntityComponentStore(data, types, schemas, constraints);
            var lifecycle = new SqliteEcsLifecycleStore(data, constraints);
            var content = new WebPageStore(web);
            await content.SaveBundleAndActivateAsync(ContentId, new("<p>Legacy active</p>", [new("assets/icon.bin", [1])]));
            await content.AppendBundleDraftAsync(ContentId, 1, Composition("selected", [2]));
            await entities.CreateEntityAsync(PublicationSpace, EntityId, "Example");
            await entities.AddComponentAsync(new(PublicationSpace, EntityId,
                new(pageType.QualifiedId, pageType.Version, pageType.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    title = "Example", navigationLabel = "Page", slug = "example", order = 0, visibility = "public",
                    activeContentReference = new { pageId = ContentId }
                }), 0));
            var transactions = new SqliteEcsWriteTransactionFactory(data);
            var publication = new WebPagePublicationService(applications, spaces, types, entities,
                new WorldStore(data), content, web, transactions, new(), NullLogger<WebPagePublicationService>.Instance,
                publicationConstraints: constraints);
            return new()
            {
                Data = data, Web = web, Applications = applications, Spaces = spaces, Entities = entities,
                Lifecycle = lifecycle, Content = content, Publication = publication, Transactions = transactions, Constraints = constraints,
                Administration = new(applications, spaces, types, entities, lifecycle, transactions, content, publication)
            };
        }

        public async ValueTask DisposeAsync() { await Web.DisposeAsync(); await Data.DisposeAsync(); }
    }
}
