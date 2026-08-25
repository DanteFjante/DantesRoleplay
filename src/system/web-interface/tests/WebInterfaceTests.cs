using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Authorization;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.Ecs;
using DantesRoleplay.Events;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Live;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.Web.Security;
using DantesRoleplay.Web.Settings;
using DantesRoleplay.Web.Interactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DantesRoleplay.Tests;

public sealed class WebInterfaceTests
{
    [Fact]
    public void Application_conversation_surface_is_exact_and_component_has_no_control_authority()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());
        var application = builder.Build();
        application.MapDantesRoleplayWeb();

        var routes = ((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/api/applications/", StringComparison.Ordinal)
                || endpoint.RoutePattern.RawText == "/components/application-conversation.js")
            .Select(endpoint => (endpoint.RoutePattern.RawText,
                Method: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single())).ToArray();
        Assert.Equal([
            ("/components/application-conversation.js", HttpMethods.Get),
            ("/api/applications/{applicationId}/conversations/{conversationId}", HttpMethods.Get),
            ("/api/applications/{applicationId}/conversations", HttpMethods.Post),
            ("/api/applications/{applicationId}/conversations/{conversationId}/turns", HttpMethods.Post),
            ("/api/applications/{applicationId}/conversations/{conversationId}/execute", HttpMethods.Post)
        ], routes);
        Assert.Contains("customElements.define('application-conversation'", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("session-context-id", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("new CustomEvent", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("Remember this route", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.Contains("remember.checked = false", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/control", ApplicationConversationElement.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("/mcp", ApplicationConversationElement.Script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_uploads_append_revisions_and_the_active_page_is_unchanged_html()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        const string first = "<!doctype html><title>First</title><script>window.first=true</script>";
        const string second = "<!doctype html><title>Second</title><style>body{color:gold}</style>";

        var revision1 = await store.SaveAndActivateAsync("character-sheet", first);
        var revision2 = await store.SaveAndActivateAsync("character-sheet", second);
        var active = await store.GetActiveAsync("character-sheet");

        Assert.Equal(1, revision1.Revision);
        Assert.Equal(2, revision2.Revision);
        Assert.NotNull(active);
        Assert.Equal(2, active!.Revision);
        Assert.Equal(second, active.Html);
        Assert.Equal(2, await db.PageRevisions.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../page")]
    [InlineData("page/name")]
    [InlineData(" page")]
    public void Page_ids_are_route_safe(string id)
    {
        Assert.False(WebPageId.IsValid(id));
    }

    [Fact]
    public async Task Invalid_page_inputs_do_not_create_a_revision()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAndActivateAsync("../page", "<p>content</p>"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAndActivateAsync("valid-page", "   "));

        Assert.Empty(await db.Pages.ToListAsync());
        Assert.Empty(await db.PageRevisions.ToListAsync());
    }

    [Fact]
    public async Task Dynamic_entity_data_preserves_unknown_components_and_fields()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("future.unknown", "Future", "Test data.");
        var entity = await world.CreateEntityAsync("Orban", "creature.orban");
        await world.SetComponentAsync(
            entity.Id,
            "future.unknown",
            """{"resonance":7,"nested":{"answer":42}}""");
        var reader = new DynamicDataReader(world);

        var result = await reader.ReadAsync("entity", entity.Id);

        Assert.NotNull(result);
        var component = result!.Json["components"]!["future.unknown"]!;
        Assert.Equal(7, component["resonance"]!.GetValue<int>());
        Assert.Equal(42, component["nested"]!["answer"]!.GetValue<int>());
    }

    [Fact]
    public async Task Dynamic_component_data_is_returned_as_the_raw_json_object()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("inventory", "Inventory", "Test data.");
        var entity = await world.CreateEntityAsync("Orban", "creature.orban");
        await world.SetComponentAsync(
            entity.Id,
            "inventory",
            """{"items":[{"id":"lantern","quantity":1}]}""");
        var reader = new DynamicDataReader(world);

        var result = await reader.ReadAsync("inventory", entity.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Revision);
        Assert.Equal("lantern", result.Json["items"]![0]!["id"]!.GetValue<string>());
        Assert.Null(await reader.ReadAsync("missing", entity.Id));
        Assert.Null(await reader.ReadAsync("inventory", "missing.entity"));
    }

    [Fact]
    public async Task Zip_bundle_materializes_and_serves_exact_active_revision_assets()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        var reader = new WebPageBundleReader();
        var css = Encoding.UTF8.GetBytes("body { color: gold; }");
        await using var zip = CreateZip(
            ("index.html", Encoding.UTF8.GetBytes("<!doctype html><link rel=\"stylesheet\" href=\"assets/site.css\"><h1>Home</h1>")),
            ("assets/site.css", css),
            ("assets/scripts/app.js", Encoding.UTF8.GetBytes("window.ready = true;")));

        var bundle = await reader.ReadAsync(zip, zip.Length);
        var saved = await store.SaveBundleAndActivateAsync("home", bundle);
        var active = await store.GetActiveAsync("home");
        var asset = await store.GetActiveAssetAsync("home", "assets/site.css");

        Assert.Equal(1, saved.Revision);
        Assert.Equal(2, bundle.Assets.Count);
        Assert.Contains("<h1>Home</h1>", active!.Html, StringComparison.Ordinal);
        Assert.NotNull(asset);
        Assert.Equal("text/css", asset!.ContentType);
        Assert.Equal(css, asset.Content);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(css)), asset.ContentHash);
    }

    [Fact]
    public async Task Later_revision_exposes_only_its_own_assets_and_retains_old_rows()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        var reader = new WebPageBundleReader();
        await using var firstZip = CreateZip(
            ("index.html", Encoding.UTF8.GetBytes("<h1>First</h1>")),
            ("assets/old.css", Encoding.UTF8.GetBytes("body{}")));
        await using var secondZip = CreateZip(
            ("index.html", Encoding.UTF8.GetBytes("<h1>Second</h1>")),
            ("assets/new.css", Encoding.UTF8.GetBytes("main{}")));

        await store.SaveBundleAndActivateAsync("home", await reader.ReadAsync(firstZip, firstZip.Length));
        await store.SaveBundleAndActivateAsync("home", await reader.ReadAsync(secondZip, secondZip.Length));

        Assert.Null(await store.GetActiveAssetAsync("home", "assets/old.css"));
        Assert.NotNull(await store.GetActiveAssetAsync("home", "assets/new.css"));
        Assert.Equal(2, await db.PageAssets.CountAsync());
        Assert.Equal(2, (await store.GetActiveAsync("home"))!.Revision);

        await store.SaveAndActivateAsync("home", "<h1>Plain HTML</h1>");
        Assert.Null(await store.GetActiveAssetAsync("home", "assets/new.css"));
        Assert.Equal(2, await db.PageAssets.CountAsync());
    }

    [Fact]
    public async Task Zip_boundary_rejects_missing_index_unsafe_duplicates_bad_utf8_and_oversize_html()
    {
        var reader = new WebPageBundleReader();
        var cases = new[]
        {
            CreateZip(("assets/site.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("../escape.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("assets/site%2fadmin.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("assets/site.css", Encoding.UTF8.GetBytes("body{}")),
                ("assets/site.css", Encoding.UTF8.GetBytes("main{}"))),
            CreateZip(
                ("index.html", Encoding.UTF8.GetBytes("<h1>Home</h1>")),
                ("site.css", Encoding.UTF8.GetBytes("body{}"))),
            CreateZip(("index.html", [0xC3, 0x28])),
            CreateZip(("index.html", new byte[WebPageBundleLimits.MaximumHtmlBytes + 1]))
        };

        try
        {
            foreach (var zip in cases)
            {
                await Assert.ThrowsAsync<WebPageBundleException>(
                    () => reader.ReadAsync(zip, zip.Length));
            }
        }
        finally
        {
            foreach (var zip in cases)
            {
                await zip.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Persistence_failure_rolls_back_revision_assets_and_active_pointer()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        await store.SaveAndActivateAsync("home", "<h1>Stable</h1>");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER reject_web_asset
            BEFORE INSERT ON web_page_asset
            BEGIN
                SELECT RAISE(ABORT, 'forced asset failure');
            END;
            """);

        var bundle = new WebPageBundle(
            "<h1>Rejected</h1>",
            [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]);
        await Assert.ThrowsAsync<DbUpdateException>(
            () => store.SaveBundleAndActivateAsync("home", bundle));

        await using var verificationDb = CreateWebContext(connection);
        var verificationStore = new WebPageStore(verificationDb);
        var active = await verificationStore.GetActiveAsync("home");
        Assert.Equal(1, active!.Revision);
        Assert.Contains("Stable", active.Html, StringComparison.Ordinal);
        Assert.Equal(1, await verificationDb.PageRevisions.CountAsync());
        Assert.Empty(await verificationDb.PageAssets.ToListAsync());
    }

    [Fact]
    public async Task Page_drafts_copy_exact_assets_without_activation_and_exact_revisions_publish_or_roll_back()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        var css = Encoding.UTF8.GetBytes("body{color:gold}");
        await store.SaveBundleAndActivateAsync(
            "control-center",
            new WebPageBundle(
                "<h1>Stable</h1>",
                [new WebPageAssetUpload("assets/site.css", css)]));

        var draft = await store.AppendDraftAsync(
            "control-center", 1, 1, "<h1>Draft</h1>");
        var afterDraft = await store.GetSummaryAsync("control-center");
        var activeAfterDraft = await store.GetActiveAsync("control-center");

        Assert.Equal(2, draft.Summary.Revision);
        Assert.False(draft.Summary.IsActive);
        Assert.Equal(css, Assert.Single(draft.Assets).Content);
        Assert.Equal(1, afterDraft!.ActiveRevision);
        Assert.Equal(2, afterDraft.LatestRevision);
        Assert.Contains("Stable", activeAfterDraft!.Html, StringComparison.Ordinal);
        Assert.Equal("PAGE_ALREADY_ACTIVE", (await Assert.ThrowsAsync<WebPageStoreException>(() =>
            store.ActivateRevisionAsync("control-center", 1, 1))).Code);
        var staleDraft = await Assert.ThrowsAsync<WebPageStoreException>(() =>
            store.AppendDraftAsync("control-center", 1, 1, "<h1>Replay</h1>"));
        Assert.Equal("PAGE_LATEST_STALE", staleDraft.Code);

        var published = await store.ActivateRevisionAsync("control-center", 2, 1);
        Assert.Equal(2, published.ActiveRevision);
        Assert.Contains("Draft", (await store.GetActiveAsync("control-center"))!.Html, StringComparison.Ordinal);
        var staleActivation = await Assert.ThrowsAsync<WebPageStoreException>(() =>
            store.ActivateRevisionAsync("control-center", 1, 1));
        Assert.Equal("PAGE_ACTIVE_STALE", staleActivation.Code);

        var rolledBack = await store.ActivateRevisionAsync("control-center", 1, 2);
        Assert.Equal(1, rolledBack.ActiveRevision);
        Assert.Contains("Stable", (await store.GetActiveAsync("control-center"))!.Html, StringComparison.Ordinal);
        Assert.Equal(2, await db.PageRevisions.CountAsync());
        Assert.Equal(2, await db.PageAssets.CountAsync());
    }

    [Fact]
    public async Task Page_and_revision_discovery_is_bounded_stable_and_exact()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        await store.SaveAndActivateAsync("bravo", "<h1>Bravo</h1>");
        await store.SaveAndActivateAsync("alpha", "<h1>Alpha 1</h1>");
        await store.AppendDraftAsync("alpha", 1, 1, "<h1>Alpha 2</h1>");
        await store.AppendDraftAsync("alpha", 2, 2, "<h1>Alpha 3</h1>");

        var firstPages = await store.ListPageAsync(null, 1);
        Assert.Equal(["alpha"], firstPages.Pages.Select(page => page.Id));
        Assert.Equal("alpha", firstPages.NextPageId);
        Assert.Equal(["bravo"], (await store.ListPageAsync(firstPages.NextPageId, 1)).Pages.Select(page => page.Id));
        Assert.Equal("CURSOR_STALE", (await Assert.ThrowsAsync<WebPageStoreException>(
            () => store.ListPageAsync("missing", 1))).Code);

        var firstRevisions = await store.ListRevisionsAsync("alpha", null, 2);
        Assert.Equal([3, 2], firstRevisions.Revisions.Select(revision => revision.Revision));
        Assert.Equal(2, firstRevisions.NextRevision);
        Assert.Equal([1], (await store.ListRevisionsAsync("alpha", firstRevisions.NextRevision, 2))
            .Revisions.Select(revision => revision.Revision));
        Assert.Equal("CURSOR_STALE", (await Assert.ThrowsAsync<WebPageStoreException>(
            () => store.ListRevisionsAsync("alpha", 99, 1))).Code);
        Assert.Contains("Alpha 2", (await store.GetRevisionAsync("alpha", 2))!.Html, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ListPageAsync(null, 101));
    }

    [Fact]
    public async Task Draft_and_activation_persistence_failures_roll_back_their_complete_transition()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using (var setup = CreateWebContext(connection))
        {
            await setup.Database.EnsureCreatedAsync();
            await new WebPageStore(setup).SaveBundleAndActivateAsync(
                "home",
                new WebPageBundle(
                    "<h1>Stable</h1>",
                    [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]));
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_draft_asset BEFORE INSERT ON web_page_asset BEGIN SELECT RAISE(ABORT, 'forced draft failure'); END;");
        }
        await using (var failingDraft = CreateWebContext(connection))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new WebPageStore(failingDraft)
                .AppendDraftAsync("home", 1, 1, "<h1>Rejected</h1>"));
        }
        await using (var verifyDraft = CreateWebContext(connection))
        {
            Assert.Equal(1, await verifyDraft.PageRevisions.CountAsync());
            Assert.Equal(1, (await new WebPageStore(verifyDraft).GetSummaryAsync("home"))!.ActiveRevision);
            await verifyDraft.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_draft_asset;");
        }
        await using (var setupActivation = CreateWebContext(connection))
        {
            await new WebPageStore(setupActivation).AppendDraftAsync("home", 1, 1, "<h1>Draft</h1>");
            await setupActivation.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_page_activation BEFORE UPDATE ON web_page BEGIN SELECT RAISE(ABORT, 'forced activation failure'); END;");
        }
        await using (var failingActivation = CreateWebContext(connection))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new WebPageStore(failingActivation)
                .ActivateRevisionAsync("home", 2, 1));
        }
        await using var verifyActivation = CreateWebContext(connection);
        var summary = await new WebPageStore(verifyActivation).GetSummaryAsync("home");
        Assert.Equal(1, summary!.ActiveRevision);
        Assert.Equal(2, summary.LatestRevision);
    }

    [Fact]
    public async Task Control_page_editor_projects_bounded_metadata_export_and_isolated_preview()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new WebPageStore(db);
        await store.SaveBundleAndActivateAsync(
            "control-center",
            new WebPageBundle(
                "<script>fetch('/api/control/status')</script><h1>Editor</h1>",
                [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]));
        await store.SaveAndActivateAsync("home", "<h1>Home</h1>");
        var editor = new ControlPageEditor(store);

        var pages = await editor.ListPagesAsync(null, "1");
        var revisions = await editor.ListRevisionsAsync("control-center", null, null);
        var detail = await editor.GetRevisionAsync("control-center", 1);
        var bundle = await editor.ExportAsync("control-center", 1);
        var preview = await editor.PreviewHtmlAsync("control-center", 1);
        var asset = await editor.PreviewAssetAsync("control-center", 1, "site.css");

        Assert.Equal("control-center", Assert.Single(pages.Items).Id);
        Assert.Equal(1, Assert.Single(revisions.Items).Revision);
        Assert.Equal("assets/site.css", Assert.Single(detail!.Assets).Path);
        Assert.DoesNotContain("\"content\":", JsonSerializer.Serialize(detail.Assets), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fetch", preview, StringComparison.Ordinal);
        Assert.Equal("text/css", asset!.ContentType);
        using var archive = new ZipArchive(new MemoryStream(bundle!.Content), ZipArchiveMode.Read);
        Assert.Equal(["assets/site.css", "index.html"], archive.Entries.Select(entry => entry.FullName).Order());

        var response = new DefaultHttpContext().Response;
        WebInterfaceSecurity.ApplyHeaders(response);
        ControlPageEditor.ApplyPreviewHeaders(response, asset: false);
        Assert.Contains("connect-src 'none'", response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Equal("SAMEORIGIN", response.Headers.XFrameOptions);
        Assert.Equal("no-store", response.Headers.CacheControl);
        Assert.Equal("CURSOR_STALE", (await Assert.ThrowsAsync<ControlPageEditorException>(
            () => editor.ListRevisionsAsync("control-center", pages.NextCursor, "1"))).Code);

        var oversized = new DefaultHttpContext().Request;
        oversized.ContentLength = ControlPageEditor.MaximumJsonBodyBytes + 1;
        Assert.Equal("BODY_TOO_LARGE", (await Assert.ThrowsAsync<ControlPageEditorException>(
            () => ControlPageEditor.ReadBodyAsync<ControlPageDraftRequest>(oversized))).Code);
    }

    [Fact]
    public async Task Web_migrations_create_asset_storage_from_a_blank_database()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateWebContext(connection);

        await db.Database.MigrateAsync();

        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var schemaRows = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_schema WHERE type IN ('table', 'index')")
            .ToListAsync();
        Assert.Contains("web_page_asset", schemaRows);
        Assert.Contains("IX_web_page_asset_PageRevisionId_Path", schemaRows);

        var store = new WebPageStore(db);
        await store.SaveBundleAndActivateAsync(
            "home",
            new WebPageBundle(
                "<h1>Migrated</h1>",
                [new WebPageAssetUpload("assets/site.css", Encoding.UTF8.GetBytes("body{}"))]));
        Assert.NotNull(await store.GetActiveAssetAsync("home", "assets/site.css"));
    }

    [Fact]
    public async Task Change_feed_starts_invalidated_and_reports_committed_page_revision()
    {
        var connectionString = SharedMemoryConnectionString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using var observerDb = CreateWebContext(connectionString);
        await observerDb.Database.EnsureCreatedAsync();
        var feed = new SqliteWebChangeFeed(observerDb);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var changes = feed.WatchAsync(
                "home",
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromSeconds(10),
                timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(WebChangeKind.Invalidate, changes.Current.Kind);
        Assert.Equal("connected", changes.Current.Reason);
        Assert.Null(changes.Current.PageRevision);

        await using (var writerDb = CreateWebContext(connectionString))
        {
            await new WebPageStore(writerDb).SaveAndActivateAsync("home", "<h1>Live</h1>");
        }

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(WebChangeKind.Invalidate, changes.Current.Kind);
        Assert.Equal("database-commit", changes.Current.Reason);
        Assert.Equal(1, changes.Current.PageRevision);

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(WebChangeKind.PageRevision, changes.Current.Kind);
        Assert.Equal("home", changes.Current.PageId);
        Assert.Equal(1, changes.Current.PageRevision);
    }

    [Fact]
    public async Task Change_feed_does_not_report_a_rolled_back_write()
    {
        var connectionString = SharedMemoryConnectionString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var setupDb = CreateWebContext(connectionString))
        {
            await setupDb.Database.EnsureCreatedAsync();
            await new WebPageStore(setupDb).SaveAndActivateAsync("home", "<h1>Stable</h1>");
        }

        await using var observerDb = CreateWebContext(connectionString);
        var feed = new SqliteWebChangeFeed(observerDb);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await using var changes = feed.WatchAsync(
                "home",
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromSeconds(10),
                timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        Assert.True(await changes.MoveNextAsync());
        Assert.Equal(1, changes.Current.PageRevision);

        await using (var writerDb = CreateWebContext(connectionString))
        {
            await using var transaction = await writerDb.Database.BeginTransactionAsync();
            await writerDb.Database.ExecuteSqlRawAsync(
                "UPDATE web_page SET UpdatedAt = {0} WHERE Id = {1}",
                DateTime.UtcNow,
                "home");
            await transaction.RollbackAsync();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await changes.MoveNextAsync().AsTask());
    }

    [Fact]
    public void Change_events_have_standard_single_line_sse_frames()
    {
        var invalidation = WebChangeSseFormatter.Format(
            new WebChange(WebChangeKind.Invalidate, "connected", 7, "home", 2));
        var pageRevision = WebChangeSseFormatter.Format(
            new WebChange(WebChangeKind.PageRevision, "page-activated", 8, "home", 3));

        Assert.StartsWith("event: invalidate\ndata: ", invalidation, StringComparison.Ordinal);
        Assert.Contains("\"databaseVersion\":7", invalidation, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', invalidation);
        Assert.EndsWith("\n\n", invalidation, StringComparison.Ordinal);
        Assert.Contains("event: page-revision", pageRevision, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"/ui/home/index.html\"", pageRevision, StringComparison.Ordinal);
        Assert.Equal(
            ": keep-alive\n\n",
            WebChangeSseFormatter.Format(
                new WebChange(WebChangeKind.KeepAlive, "keep-alive", 8)));
    }

    [Fact]
    public void Local_web_access_fails_closed_except_for_ipv4_and_ipv6_loopback()
    {
        Assert.True(WebInterfaceSecurity.IsLoopback(IPAddress.Loopback));
        Assert.True(WebInterfaceSecurity.IsLoopback(IPAddress.IPv6Loopback));
        Assert.False(WebInterfaceSecurity.IsLoopback(IPAddress.Parse("192.0.2.10")));
        Assert.False(WebInterfaceSecurity.IsLoopback(null));
    }

    [Fact]
    public void Local_web_responses_receive_the_closed_trusted_content_policy()
    {
        var context = new DefaultHttpContext();

        WebInterfaceSecurity.ApplyHeaders(context.Response);

        Assert.Equal(WebInterfaceSecurity.ContentSecurityPolicy, context.Response.Headers.ContentSecurityPolicy);
        Assert.Contains("connect-src 'self'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
    }

    [Fact]
    public void Direct_loopback_access_remains_local_without_remote_configuration()
    {
        var context = RequestContext("localhost", IPAddress.Loopback);
        var policy = AccessPolicy(new WebRemoteAccessOptions());

        var decision = policy.Evaluate(context);

        Assert.True(decision.Allowed);
        Assert.Equal(WebAccessMode.Local, decision.Mode);
        Assert.Null(decision.Login);
        Assert.True(WebAccessPolicy.CreatePrincipal(decision).Identity!.IsAuthenticated);
    }

    [Fact]
    public void Exact_tailscale_host_and_allowed_login_resolve_remote_identity()
    {
        var context = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "operator@example.com";
        var policy = AccessPolicy(new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "ROLEPLAY.example.ts.net.",
            AllowedLogins = ["operator@example.com"]
        });

        var decision = policy.Evaluate(context);
        var principal = WebAccessPolicy.CreatePrincipal(decision);

        Assert.True(decision.Allowed);
        Assert.Equal(WebAccessMode.Tailscale, decision.Mode);
        Assert.Equal("operator@example.com", decision.Login);
        Assert.Equal(WebAccessPolicy.TailscaleAuthenticationType, principal.Identity!.AuthenticationType);
        Assert.Equal("operator@example.com", principal.Identity.Name);
    }

    [Fact]
    public void Trusted_web_identity_becomes_a_stable_opaque_authorization_principal()
    {
        var first = WebTrustedPrincipalContextFactory.Create(new(
            true, WebAccessMode.Tailscale, "Operator@Example.com"));
        var second = WebTrustedPrincipalContextFactory.Create(new(
            true, WebAccessMode.Tailscale, "operator@example.com"));
        var local = WebTrustedPrincipalContextFactory.Create(new(true, WebAccessMode.Local));

        Assert.True(first.Verified);
        Assert.Equal(first.PrincipalId, second.PrincipalId);
        Assert.StartsWith("principal.", first.PrincipalId, StringComparison.Ordinal);
        Assert.DoesNotContain("operator", first.PrincipalId, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(first.PrincipalId, local.PrincipalId);
        Assert.Equal("tailscale-serve", first.AuthenticationMethod);
        Assert.Equal("local-loopback", local.AuthenticationMethod);
    }

    [Fact]
    public void Private_operator_guard_maps_http_reads_and_modifications_without_caller_authority()
    {
        var guard = OperatorGuard(new WebRemoteAccessOptions());
        var read = RequestContext("localhost", IPAddress.Loopback);
        read.Request.Method = HttpMethods.Get;
        var modify = RequestContext("localhost", IPAddress.Loopback);
        modify.Request.Method = HttpMethods.Put;

        var readDecision = guard.Evaluate(read);
        var modifyDecision = guard.Evaluate(modify);

        Assert.True(readDecision.Allowed);
        Assert.True(modifyDecision.Allowed);
        Assert.Equal("read", readDecision.Evidence.Capability);
        Assert.Equal("modify", modifyDecision.Evidence.Capability);
        Assert.Equal(PrivateOperatorAuthorizationPolicy.PrivateHostScope, readDecision.Evidence.Scope);
        Assert.DoesNotContain("local", readDecision.Evidence.PrincipalReference, StringComparison.OrdinalIgnoreCase);
        Assert.True(readDecision.Principal!.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Control_reads_and_local_same_origin_json_changes_use_the_server_selected_capability()
    {
        var guard = ControlGuard(new WebRemoteAccessOptions());
        var read = RequestContext("localhost:6217", IPAddress.Loopback);
        read.Request.Method = HttpMethods.Get;
        read.Request.Scheme = Uri.UriSchemeHttp;
        read.Request.Headers["X-Control-Capability"] = "control.codex.approve";
        var write = RequestContext("localhost:6217", IPAddress.IPv6Loopback);
        write.Request.Method = HttpMethods.Put;
        write.Request.Scheme = Uri.UriSchemeHttp;
        write.Request.ContentType = "application/json; charset=utf-8";
        write.Request.Headers.Origin = "http://localhost:6217";
        write.Request.Headers["X-Control-Capability"] = "control.codex.approve";

        var readDecision = guard.Evaluate(
            read,
            PrivateOperatorCapability.ControlRead,
            mutation: false);
        var writeDecision = guard.Evaluate(
            write,
            PrivateOperatorCapability.ControlPagesWrite,
            mutation: true);

        Assert.True(readDecision.Allowed);
        Assert.Equal("control.read", readDecision.Evidence.Capability);
        Assert.True(writeDecision.Allowed);
        Assert.Equal("control.pages.write", writeDecision.Evidence.Capability);
    }

    [Fact]
    public void Tailscale_control_change_requires_the_exact_https_public_origin()
    {
        var options = new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = ["operator@example.com"]
        };
        var accepted = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        accepted.Request.Method = HttpMethods.Post;
        accepted.Request.Scheme = Uri.UriSchemeHttp;
        accepted.Request.ContentType = "application/json";
        accepted.Request.Headers.Origin = "https://roleplay.example.ts.net";
        accepted.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "operator@example.com";
        var wrongScheme = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        wrongScheme.Request.Method = HttpMethods.Post;
        wrongScheme.Request.Scheme = Uri.UriSchemeHttp;
        wrongScheme.Request.ContentType = "application/json";
        wrongScheme.Request.Headers.Origin = "http://roleplay.example.ts.net";
        wrongScheme.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "operator@example.com";

        var acceptedDecision = ControlGuard(options).Evaluate(
            accepted,
            PrivateOperatorCapability.ControlAiMessage,
            mutation: true);
        var deniedDecision = ControlGuard(options).Evaluate(
            wrongScheme,
            PrivateOperatorCapability.ControlAiMessage,
            mutation: true);

        Assert.True(acceptedDecision.Allowed);
        Assert.Equal("control.ai.message", acceptedDecision.Evidence.Capability);
        Assert.False(deniedDecision.Allowed);
        Assert.Equal("CONTROL_ORIGIN_DENIED", deniedDecision.ErrorCode);
    }

    [Theory]
    [InlineData("localhost:6217", null, "application/json", 403, "CONTROL_ORIGIN_REQUIRED")]
    [InlineData("localhost:6217", "null", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("localhost:6217", "http://localhost:6218", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("localhost:6217", "https://localhost:6217", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("localhost:6217", "http://localhost:6217/path", "application/json", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("evil.example:6217", "http://evil.example:6217", "application/json", 403, "CONTROL_HOST_DENIED")]
    [InlineData("localhost:6217", "http://localhost:6217", "text/plain", 415, "CONTROL_JSON_REQUIRED")]
    public void Invalid_control_mutation_inputs_fail_before_owner_invocation(
        string host,
        string? origin,
        string contentType,
        int statusCode,
        string errorCode)
    {
        var context = RequestContext(host, IPAddress.Loopback);
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = Uri.UriSchemeHttp;
        context.Request.ContentType = contentType;
        if (origin is not null)
            context.Request.Headers.Origin = origin;

        var decision = ControlGuard(new WebRemoteAccessOptions()).Evaluate(
            context,
            PrivateOperatorCapability.ControlSettingsWrite,
            mutation: true);

        Assert.False(decision.Allowed);
        Assert.Equal(statusCode, decision.StatusCode);
        Assert.Equal(errorCode, decision.ErrorCode);
    }

    [Fact]
    public void Multiple_origins_and_non_control_capabilities_fail_closed()
    {
        var multiple = RequestContext("localhost:6217", IPAddress.Loopback);
        multiple.Request.Method = HttpMethods.Put;
        multiple.Request.Scheme = Uri.UriSchemeHttp;
        multiple.Request.ContentType = "application/json";
        multiple.Request.Headers.Origin = new Microsoft.Extensions.Primitives.StringValues(
            ["http://localhost:6217", "http://localhost:6217"]);
        var wrongCapability = RequestContext("localhost:6217", IPAddress.Loopback);
        wrongCapability.Request.Method = HttpMethods.Put;
        wrongCapability.Request.Scheme = Uri.UriSchemeHttp;
        wrongCapability.Request.ContentType = "application/json";
        wrongCapability.Request.Headers.Origin = "http://localhost:6217";

        var multipleDecision = ControlGuard(new WebRemoteAccessOptions()).Evaluate(
            multiple,
            PrivateOperatorCapability.ControlSettingsWrite,
            mutation: true);
        var capabilityDecision = ControlGuard(new WebRemoteAccessOptions()).Evaluate(
            wrongCapability,
            PrivateOperatorCapability.Modify,
            mutation: true);

        Assert.False(multipleDecision.Allowed);
        Assert.Equal("CONTROL_ORIGIN_REQUIRED", multipleDecision.ErrorCode);
        Assert.False(capabilityDecision.Allowed);
        Assert.Equal("CONTROL_CAPABILITY_DENIED", capabilityDecision.ErrorCode);
    }

    [Fact]
    public async Task Rejected_control_request_never_invokes_the_handler()
    {
        var context = RequestContext("localhost:6217", IPAddress.Loopback);
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = Uri.UriSchemeHttp;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Origin = "https://forwarded.example:6217";
        context.Request.Headers["X-Forwarded-Host"] = "forwarded.example:6217";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        var filter = new WebControlRequestFilter(ControlGuard(new WebRemoteAccessOptions()));
        var invoked = false;

        await filter.InvokeAsync(
            new TestFilterContext(context),
            _ =>
            {
                invoked = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            },
            PrivateOperatorCapability.ControlPagesWrite,
            mutation: true);

        Assert.False(invoked);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task Accepted_control_request_invokes_the_handler_once_with_an_authenticated_principal()
    {
        var context = RequestContext("127.0.0.1:6217", IPAddress.Loopback);
        context.Request.Method = HttpMethods.Put;
        context.Request.Scheme = Uri.UriSchemeHttp;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Origin = "http://127.0.0.1:6217";
        var filter = new WebControlRequestFilter(ControlGuard(new WebRemoteAccessOptions()));
        var invocations = 0;

        await filter.InvokeAsync(
            new TestFilterContext(context),
            _ =>
            {
                invocations++;
                return ValueTask.FromResult<object?>(Results.Ok());
            },
            PrivateOperatorCapability.ControlPagesWrite,
            mutation: true);

        Assert.Equal(1, invocations);
        Assert.True(context.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Control_route_helpers_are_closed_under_the_control_prefix()
    {
        var builder = WebApplication.CreateBuilder();
        var application = builder.Build();
        application.MapDantesRoleplayControlGet(
            "/status",
            new Func<IResult>(() => Results.Ok()));
        application.MapDantesRoleplayControlPost(
            "/messages",
            PrivateOperatorCapability.ControlAiMessage,
            new Func<IResult>(() => Results.Ok()));

        var patterns = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/api/control/status", patterns);
        Assert.Contains("/api/control/messages", patterns);
        Assert.Throws<ArgumentException>(() => application.MapDantesRoleplayControlGet(
            "/api/escape",
            new Func<IResult>(() => Results.Ok())));
        Assert.Throws<ArgumentOutOfRangeException>(() => application.MapDantesRoleplayControlPut(
            "/settings",
            PrivateOperatorCapability.Modify,
            new Func<IResult>(() => Results.Ok())));
    }

    [Fact]
    public void Control_center_status_is_bounded_and_derives_only_authenticated_access_identity()
    {
        var local = ControlCenterStatus.Create(WebAccessPolicy.CreatePrincipal(new(
            true,
            WebAccessMode.Local)));
        var tailscale = ControlCenterStatus.Create(WebAccessPolicy.CreatePrincipal(new(
            true,
            WebAccessMode.Tailscale,
            "operator@example.com")));
        var response = new DefaultHttpContext().Response;

        ControlCenterStatus.ApplyCacheHeaders(response);

        Assert.Equal("ready", local.Status);
        Assert.Equal("local", local.Access.Mode);
        Assert.Null(local.Access.Login);
        Assert.Equal("tailscale", tailscale.Access.Mode);
        Assert.Equal("operator@example.com", tailscale.Access.Login);
        Assert.Collection(
            local.Panels,
            panel =>
            {
                Assert.Equal("server-settings", panel.Id);
                Assert.Equal("ready", panel.State);
                Assert.False(string.IsNullOrWhiteSpace(panel.Message));
            },
            panel => AssertPanel(panel, "effect-history"),
            panel =>
            {
                Assert.Equal("assistant", panel.Id);
                Assert.Equal("ready", panel.State);
                Assert.False(string.IsNullOrWhiteSpace(panel.Message));
            },
            panel => AssertPanel(panel, "ecs-explorer"),
            panel =>
            {
                Assert.Equal("site-editor", panel.Id);
                Assert.Equal("ready", panel.State);
                Assert.False(string.IsNullOrWhiteSpace(panel.Message));
            });
        Assert.Equal(ControlCenterStatus.CacheControl, response.Headers.CacheControl);
    }

    [Fact]
    public void Control_center_shell_keeps_one_workspace_inside_closed_persistent_navigation()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));

        Assert.Contains("class=\"app-shell\"", html, StringComparison.Ordinal);
        Assert.Contains("<aside class=\"sidebar\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"workspace-title\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/settings\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/effects\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/assistants\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/applications\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#/site-editor\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-current", html, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"hashchange\"", html, StringComparison.Ordinal);
        Assert.Contains("history.replaceState", html, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 760px)", html, StringComparison.Ordinal);

        Assert.Equal(1, html.Split("<server-settings-panel", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<effect-history-panel", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<assistant-panel", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<ecs-explorer", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, html.Split("<site-editor", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Site_editor_saves_changed_html_as_a_draft_before_previewing_it()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));

        Assert.Contains("Save & preview draft", html, StringComparison.Ordinal);
        Assert.Contains("saveDraft(page, revision, textarea.value, body, state, true)", html,
            StringComparison.Ordinal);
        Assert.Contains("async saveDraft(page, baseRevision, html, body, state, previewAfterSave = false)", html,
            StringComparison.Ordinal);
        Assert.Contains("await this.selectRevision(current, detail.summary.revision, body)", html,
            StringComparison.Ordinal);
        Assert.Contains("this.preview(page.id, detail.summary.revision, body)", html, StringComparison.Ordinal);
        Assert.Contains("Preview saved revision", html, StringComparison.Ordinal);
        Assert.Contains("frame.setAttribute(\"sandbox\", \"allow-scripts\")", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_navigation_deep_links_into_the_existing_structure_owner_without_embedding_pages()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var routerStart = html.IndexOf("const workspaceRoutes", StringComparison.Ordinal);
        var panelStart = html.IndexOf("class EcsExplorerPanel", StringComparison.Ordinal);
        var panelEnd = html.IndexOf("class SiteEditorPanel", panelStart, StringComparison.Ordinal);

        Assert.True(routerStart > 0);
        Assert.True(panelStart > routerStart);
        var router = html[routerStart..panelStart];
        var panel = html[panelStart..panelEnd];
        Assert.Contains("#/applications/", router, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent(applicationId)", router, StringComparison.Ordinal);
        Assert.Contains("navigateToApplication(application.id)", panel, StringComparison.Ordinal);
        Assert.Contains("openApplicationFromRoute", panel, StringComparison.Ordinal);
        Assert.Contains("showApplicationListFromRoute", panel, StringComparison.Ordinal);
        Assert.Contains("decodeURIComponent", router, StringComparison.Ordinal);
        Assert.Contains("applicationId.length > 200", router, StringComparison.Ordinal);
        Assert.Contains("panelId: \"ecs-explorer\"", router, StringComparison.Ordinal);
        Assert.DoesNotContain("createElement(\"iframe\")", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("postMessage", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_setting_provider_freezes_allowlist_sources_and_inactive_runtime()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Knowledge:Completion:Enabled"] = "false",
                ["Knowledge:Completion:Endpoint"] = "http://127.0.0.1:11435",
                ["Knowledge:Completion:Timeout"] = "00:02:00"
            })
            .Build();

        var catalog = new ConfiguredHostSettingDefinitionProvider(configuration).GetCatalog();

        Assert.Equal(HostSettingRuntimeState.NotRegistered, catalog.Runtime.State);
        Assert.Equal(
        [
            "local-completion.enabled",
            "local-completion.endpoint",
            "local-completion.model",
            "local-completion.profile",
            "local-completion.max-output-tokens",
            "local-completion.timeout-seconds",
            "local-completion.max-concurrent-requests"
        ], catalog.Definitions.Select(definition => definition.Key));
        Assert.All(catalog.Definitions, definition =>
        {
            Assert.Equal(HostSettingSensitivity.PublicValue, definition.Sensitivity);
            Assert.Equal(HostSettingMutability.RestartRequired, definition.Mutability);
            Assert.Equal(HostSettingDisruption.HostRestart, definition.Disruption);
            Assert.Null(definition.EffectiveValue);
            Assert.Null(definition.PendingValue);
            Assert.False(definition.RestartRequired);
        });
        var enabled = catalog.Definitions[0];
        Assert.True(enabled.Configured);
        Assert.Equal("configuration", enabled.Source);
        Assert.False(enabled.Value!.Value.GetBoolean());
        Assert.Equal("default", catalog.Definitions[2].Source);
        Assert.Equal("qwen3:8b", catalog.Definitions[2].Value!.Value.GetString());
        Assert.Equal(120, catalog.Definitions[5].Value!.Value.GetInt32());
        Assert.Equal(8192, catalog.Definitions[4].Schema.GetProperty("maximum").GetInt32());
    }

    [Theory]
    [InlineData("Knowledge:Completion:Enabled", "sometimes")]
    [InlineData("Knowledge:Completion:Endpoint", "https://example.com")]
    [InlineData("Knowledge:Completion:MaxOutputTokens", "63")]
    [InlineData("Knowledge:Completion:Timeout", "00:00:00.500")]
    public void Host_setting_provider_rejects_invalid_startup_values(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => new ConfiguredHostSettingDefinitionProvider(configuration));
    }

    [Fact]
    public void Host_setting_provider_normalizes_and_applies_override_heads_without_activating_a_provider()
    {
        var provider = new ConfiguredHostSettingDefinitionProvider(new ConfigurationBuilder().Build());
        var normalized = provider.NormalizeOverride(
            "local-completion.profile", JsonSerializer.SerializeToElement("  focused  "));
        Assert.Equal("focused", normalized.GetString());
        Assert.Throws<InvalidOperationException>(() => provider.NormalizeOverride(
            "local-completion.max-concurrent-requests", JsonSerializer.SerializeToElement(9)));

        provider.ApplyStartupOverrides(new Dictionary<string, JsonElement?>
        {
            ["local-completion.profile"] = normalized,
            ["local-completion.enabled"] = null
        });

        var catalog = provider.GetCatalog();
        var profile = catalog.Definitions.Single(item => item.Key == "local-completion.profile");
        Assert.Equal("override", profile.Source);
        Assert.Equal("focused", profile.Value!.Value.GetString());
        Assert.False(profile.RestartRequired);
        Assert.Equal(HostSettingRuntimeState.NotRegistered, catalog.Runtime.State);
        Assert.Equal("default", catalog.Definitions.Single(item => item.Key == "local-completion.enabled").Source);
    }

    [Fact]
    public void Host_setting_provider_projects_applied_values_into_the_fixed_assistant_provider()
    {
        var provider = new ConfiguredHostSettingDefinitionProvider(new ConfigurationBuilder().Build());
        provider.ApplyStartupOverrides(new Dictionary<string, JsonElement?>
        {
            ["local-completion.enabled"] = JsonSerializer.SerializeToElement(true),
            ["local-completion.endpoint"] = JsonSerializer.SerializeToElement("http://127.0.0.1:11435/"),
            ["local-completion.model"] = JsonSerializer.SerializeToElement("fixture-model"),
            ["local-completion.profile"] = JsonSerializer.SerializeToElement("fixture-profile"),
            ["local-completion.max-output-tokens"] = JsonSerializer.SerializeToElement(512),
            ["local-completion.timeout-seconds"] = JsonSerializer.SerializeToElement(45),
            ["local-completion.max-concurrent-requests"] = JsonSerializer.SerializeToElement(2)
        });

        var options = provider.CreateCompletionOptions();
        provider.MarkProviderRegistered();

        Assert.True(options.Enabled);
        Assert.Equal(new Uri("http://127.0.0.1:11435/"), options.Endpoint);
        Assert.Equal("fixture-model", options.Model);
        Assert.Equal("fixture-profile", options.Profile);
        Assert.Equal(512, options.MaxOutputTokens);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Timeout);
        Assert.Equal(2, options.MaxConcurrentRequests);
        Assert.Equal(
            [AssistantConversationService.TaskClass, InteractionPlannerProtocol.TaskClass],
            options.AllowedTaskClasses.Order(StringComparer.Ordinal));
        Assert.Equal(HostSettingRuntimeState.Ready, provider.GetCatalog().Runtime.State);
        Assert.All(provider.GetCatalog().Definitions, definition => Assert.Equal(definition.Value, definition.EffectiveValue));
    }

    [Fact]
    public void Host_setting_projection_redacts_configured_only_values_and_rejects_bad_catalogs()
    {
        var schema = JsonDocument.Parse("""{"type":"string"}""").RootElement.Clone();
        var secret = JsonSerializer.SerializeToElement("do-not-return");
        var definition = new HostSettingDefinition(
            "private.token",
            "Private token",
            "A configured-only test definition.",
            HostSettingSensitivity.ConfiguredOnly,
            HostSettingMutability.ReadOnly,
            HostSettingDisruption.None,
            "configuration",
            true,
            secret,
            secret,
            secret,
            true,
            schema);
        var explorer = new ControlSettingsExplorer(new StaticHostSettingProvider(new(
            new(HostSettingRuntimeState.Ready, "Ready."),
            [definition])));

        var summary = Assert.Single(explorer.List().Items);
        Assert.Equal("configured-only", summary.Sensitivity);
        Assert.True(summary.Configured);
        Assert.Null(summary.Value);
        Assert.Null(summary.EffectiveValue);
        Assert.Null(summary.PendingValue);
        Assert.Equal("string", explorer.Get("private.token")!.Schema.GetProperty("type").GetString());
        Assert.Null(explorer.Get("unknown.key"));
        Assert.Equal("INVALID_SETTING_KEY", Assert.Throws<ControlSettingsException>(
            () => explorer.Get("../private")).Code);

        var duplicates = new ControlSettingsExplorer(new StaticHostSettingProvider(new(
            new(HostSettingRuntimeState.Ready, "Ready."),
            [definition, definition])));
        Assert.Equal("HOST_SETTING_CATALOG_INVALID", Assert.Throws<ControlSettingsException>(
            duplicates.List).Code);
    }

    [Fact]
    public void Server_settings_panel_stages_versioned_changes_and_never_restarts_the_host()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var start = html.IndexOf("class ServerSettingsPanel", StringComparison.Ordinal);
        var end = html.IndexOf("class EffectHistoryPanel", start, StringComparison.Ordinal);
        var panel = html[start..end];

        Assert.Contains("/api/control/settings", panel, StringComparison.Ordinal);
        Assert.Contains("/api/control/settings/", panel, StringComparison.Ordinal);
        Assert.Contains("Stage change", panel, StringComparison.Ordinal);
        Assert.Contains("Reset to startup value", panel, StringComparison.Ordinal);
        Assert.Contains("/versions", panel, StringComparison.Ordinal);
        Assert.Contains("/rollback", panel, StringComparison.Ordinal);
        Assert.Contains("POST", panel, StringComparison.Ordinal);
        Assert.Contains("PUT", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("restart()", panel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Web_route_map_includes_the_versioned_settings_control_endpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb(
            "Data Source=:memory:",
            new ConfigurationBuilder().Build());
        var application = builder.Build();

        application.MapDantesRoleplayWeb();
        Assert.Equal(
            HostSettingRuntimeState.Unavailable,
            ((IHostSettingDefinitionProvider)application.Services.GetService(
                typeof(IHostSettingDefinitionProvider))!)
                .GetCatalog().Runtime.State);

        var endpoints = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(candidate => candidate.RoutePattern.RawText!.StartsWith("/api/control/", StringComparison.Ordinal))
            .ToList();
        var patterns = endpoints.Select(endpoint => endpoint.RoutePattern.RawText).ToList();

        Assert.Equal(
        [
            "/api/control/status",
            "/api/control/settings",
            "/api/control/settings/{key}",
            "/api/control/settings/{key}/versions",
            "/api/control/settings/{key}",
            "/api/control/settings/{key}/reset",
            "/api/control/settings/{key}/rollback",
            "/api/control/assistants/local/status",
            "/api/control/assistants/codex/status",
            "/api/control/conversations",
            "/api/control/conversations/{conversationId}",
            "/api/control/conversations",
            "/api/control/conversations/{conversationId}/turns",
            "/api/control/conversations/{conversationId}/turns/{turnId}/cancel",
            "/api/control/conversations/{conversationId}/turns/{turnId}/approvals/{approvalId}",
            "/api/control/effects",
            "/api/control/effects/{eventId}",
            "/api/control/structure/applications",
            "/api/control/structure/applications/{applicationId}",
            "/api/control/structure/applications/{applicationId}/state-spaces",
            "/api/control/structure/applications/{applicationId}/component-types",
            "/api/control/structure/component-types/{qualifiedId}/versions/{version:int}",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities/{entityId}",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components",
            "/api/control/structure/state-spaces/{stateSpaceId}/entities/{entityId}/components/{qualifiedTypeId}",
            "/api/control/structure/applications/{applicationId}/catalog",
            "/api/control/structure/applications/{applicationId}/catalog/browse",
            "/api/control/structure/applications/{applicationId}/catalog/search",
            "/api/control/structure/applications/{applicationId}/catalog/records/{qualifiedId}",
            "/api/control/pages",
            "/api/control/pages/{pageId}",
            "/api/control/pages/{pageId}/revisions",
            "/api/control/pages/{pageId}/revisions/{revision:int}",
            "/api/control/pages/{pageId}/revisions/{revision:int}/bundle",
            "/api/control/pages/{pageId}/revisions/{revision:int}/preview/index.html",
            "/api/control/pages/{pageId}/revisions/{revision:int}/preview/assets/{**path}",
            "/api/control/pages/{pageId}/drafts",
            "/api/control/pages/{pageId}/active"
        ], patterns);
        Assert.All(endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Single() == HttpMethods.Get),
            endpoint => Assert.Equal(
                [HttpMethods.Get], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.Equal(
            [HttpMethods.Put],
            endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/control/settings/{key}" &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single() == HttpMethods.Put)
                .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.All(endpoints.Where(endpoint => endpoint.RoutePattern.RawText is
            "/api/control/settings/{key}/reset" or "/api/control/settings/{key}/rollback"),
            endpoint => Assert.Equal(
                [HttpMethods.Post], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.All(endpoints.Where(endpoint => endpoint.RoutePattern.RawText is
            "/api/control/conversations" or "/api/control/conversations/{conversationId}/turns" or
            "/api/control/conversations/{conversationId}/turns/{turnId}/cancel" or
            "/api/control/conversations/{conversationId}/turns/{turnId}/approvals/{approvalId}" &&
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single() == HttpMethods.Post),
            endpoint => Assert.Equal(
                [HttpMethods.Post], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.Equal(
            [HttpMethods.Post],
            endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/control/pages/{pageId}/drafts")
                .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Equal(
            [HttpMethods.Put],
            endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "/api/control/pages/{pageId}/active")
                .Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
    }

    [Fact]
    public void Web_route_map_includes_a_read_only_root_home_entry()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDantesRoleplayWeb(
            "Data Source=:memory:",
            new ConfigurationBuilder().Build());
        var application = builder.Build();

        application.MapDantesRoleplayWeb();

        var endpoints = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        var root = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/");

        Assert.Equal([HttpMethods.Get], root.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.DoesNotContain(endpoints, endpoint => endpoint.RoutePattern.RawText == "/mcp");
    }

    [Fact]
    public void Home_and_site_editor_expose_direct_page_navigation()
    {
        var root = RepositoryRoot();
        var home = File.ReadAllText(Path.Combine(
            root, "src", "system", "web-interface", "examples", "home.html"));
        var controlCenter = File.ReadAllText(Path.Combine(
            root, "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var endpoints = File.ReadAllText(Path.Combine(
            root, "src", "system", "web-interface", "DantesRoleplay.Web", "Http", "WebInterfaceEndpoints.cs"));

        Assert.Contains("private const string HomePageId = \"home\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("GetPageAsync(HomePageId", endpoints, StringComparison.Ordinal);
        Assert.Contains("href=\"/ui/control-center/index.html\"", home, StringComparison.Ordinal);
        Assert.Contains("Open control center", home, StringComparison.Ordinal);
        Assert.Contains("livePageLink(item.id)", controlCenter, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent(pageId)", controlCenter, StringComparison.Ordinal);
        Assert.Contains("Open live page", controlCenter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assistant_panel_keeps_codex_approvals_one_request_turn_scoped_and_closed()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "system", "web-interface", "examples", "control-center", "index.html"));
        var start = html.IndexOf("class AssistantPanel", StringComparison.Ordinal);
        var end = html.IndexOf("class EcsExplorerPanel", start, StringComparison.Ordinal);
        var panel = html[start..end];

        Assert.Contains("/api/control/assistants/\" + providerName + \"/status", panel, StringComparison.Ordinal);
        Assert.Contains("/api/control/conversations", panel, StringComparison.Ordinal);
        Assert.Contains("providerName === \"codex\"", panel, StringComparison.Ordinal);
        Assert.Contains("response.body.getReader()", panel, StringComparison.Ordinal);
        Assert.Contains("new TextDecoder()", panel, StringComparison.Ordinal);
        Assert.Contains("Cancel active Codex turn", panel, StringComparison.Ordinal);
        Assert.Contains("read-only/no-network baseline", panel, StringComparison.Ordinal);
        Assert.Contains("model \" + provider.model", panel, StringComparison.Ordinal);
        Assert.Contains("New conversations use the host-selected model", panel, StringComparison.Ordinal);
        Assert.Contains("Accept once", panel, StringComparison.Ordinal);
        Assert.Contains("Decline", panel, StringComparison.Ordinal);
        Assert.Contains("Cancel turn", panel, StringComparison.Ordinal);
        Assert.Contains("/approvals/", panel, StringComparison.Ordinal);
        Assert.Contains("There is no session-wide approval", panel, StringComparison.Ordinal);
        Assert.Contains("expectedRevision", panel, StringComparison.Ordinal);
        Assert.Contains("decision", panel, StringComparison.Ordinal);
        Assert.Contains("crypto.randomUUID", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("systemPrompt", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("responseSchema", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptForSession", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("workspaceWrite", panel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dangerFullAccess", panel, StringComparison.OrdinalIgnoreCase);

        var unknownField = new DefaultHttpContext();
        unknownField.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"provider\":\"local\",\"message\":\"hello\",\"idempotencyKey\":\"web:1\",\"prompt\":\"forbidden\"}"));
        var invalid = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlAssistantExplorer.ReadBodyAsync<AssistantConversationCreate>(unknownField.Request));
        Assert.Equal("ASSISTANT_BODY_INVALID", invalid.Code);

        var approvalField = new DefaultHttpContext();
        approvalField.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "{\"expectedRevision\":1,\"decision\":\"accept\",\"scope\":\"session\"}"));
        var invalidApproval = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlAssistantExplorer.ReadBodyAsync<CodexApprovalDecisionInput>(approvalField.Request));
        Assert.Equal("ASSISTANT_BODY_INVALID", invalidApproval.Code);

        var oversized = new DefaultHttpContext();
        oversized.Request.ContentLength = 16 * 1024 + 1;
        var tooLarge = await Assert.ThrowsAsync<ControlAssistantException>(() =>
            ControlAssistantExplorer.ReadBodyAsync<AssistantConversationCreate>(oversized.Request));
        Assert.Equal("ASSISTANT_BODY_TOO_LARGE", tooLarge.Code);
    }

    [Fact]
    public async Task Structure_explorer_reads_exact_application_ecs_and_schema_evidence_without_publishing_catalog_files()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var applicationId = ApplicationIdentifier.Parse("fixture-app");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(applicationId, "Fixture", "Explorer fixture", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("fixture-space", revision, new string('A', 64)));
        var validator = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, validator);
        var stats = types.Define(new(applicationId, "fixture-app.stats", "{\"type\":\"object\"}"));
        var entities = new SqliteEntityComponentStore(db, types, validator);
        await entities.CreateEntityAsync("fixture-space", "hero", "Hero");
        await entities.AddComponentAsync(new(
            "fixture-space", "hero",
            new(stats.QualifiedId, stats.Version, stats.SchemaHash),
            "{\"health\":12}", 0));
        var changesBeforeReads = await SqliteTotalChangesAsync(db);
        var explorer = new ControlStructureExplorer(
            applications, stateSpaces, types, entities, new EmptyPublicApplicationCatalogProvider());

        var appPage = explorer.ListApplications(null, "1");
        var app = explorer.GetApplication("fixture-app");
        var spaces = explorer.ListStateSpaces("fixture-app", null, null);
        var typePage = explorer.ListComponentTypes("fixture-app", null, null);
        var schema = explorer.GetComponentType("fixture-app.stats", 1);
        var entityPage = await explorer.ListEntitiesAsync("fixture-space", null, null);
        var componentPage = await explorer.ListComponentsAsync("fixture-space", "hero", null, null);
        var component = await explorer.GetComponentAsync("fixture-space", "hero", "fixture-app.stats");
        var catalog = explorer.GetCatalog("fixture-app");

        Assert.Equal("fixture-app", Assert.Single(appPage.Items).Id);
        Assert.Equal("Explorer fixture", app!.Description);
        Assert.Equal("fixture-space", Assert.Single(spaces.Items).StateSpaceId);
        Assert.Equal(stats.SchemaHash, Assert.Single(typePage.Items).SchemaHash);
        Assert.Equal("{\"type\":\"object\"}", schema!.SchemaJson);
        Assert.Equal("hero", Assert.Single(entityPage.Items).EntityId);
        Assert.Equal("fixture-app.stats", Assert.Single(componentPage.Items).QualifiedTypeId);
        Assert.Equal("{\"health\":12}", component!.ValueJson);
        Assert.Equal(stats.SchemaHash, component.SchemaHash);
        Assert.Equal("unavailable", catalog.Status);
        Assert.Empty(catalog.Collections);
        Assert.Null(explorer.GetComponentType("fixture-app.stats", 2));
        Assert.Equal("STATE_SPACE_UNKNOWN", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.GetEntityAsync("missing-space", "hero"))).Code);
        Assert.Equal("ENTITY_UNKNOWN", (await Assert.ThrowsAsync<ControlStructureException>(
            () => explorer.ListComponentsAsync("fixture-space", "missing", null, null))).Code);
        Assert.Equal(changesBeforeReads, await SqliteTotalChangesAsync(db));
    }

    [Fact]
    public void Structure_explorer_uses_only_the_explicit_public_catalog_provider()
    {
        using var fixture = new SqliteFixture();
        using var db = fixture.CreateContext();
        var applicationId = ApplicationIdentifier.Parse("fixture-app");
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(applicationId, "Fixture", "", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var entities = new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator());
        var emptyExplorer = new ControlStructureExplorer(
            applications, stateSpaces, types, entities,
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
            {
                [applicationId] = new EmptyCatalogNavigator()
            }));

        Assert.Equal("empty", emptyExplorer.GetCatalog("fixture-app").Status);

        const string content = "{\"kind\":\"contract\"}";
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var manifest = CatalogNavigationManifest.Create(
            applicationId,
            new string('A', 64),
            "catalog-v1",
            [new("contracts", "Contracts", "Public contracts")],
            [new("contracts", "", "Contracts", "Public contracts", CatalogDescriptionStatus.Authored)],
            [new(
                "contracts", "contract", "fixture-app.contract.hero", "Hero", "Hero contract",
                [], ["hero"], "", "active", 1, content, contentHash, "fixture", "catalog/hero.json")]);
        var navigator = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(new byte[32]));
        var explorer = new ControlStructureExplorer(
            applications, stateSpaces, types, entities,
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
            {
                [applicationId] = navigator
            }));

        var overview = explorer.GetCatalog("fixture-app");
        var browse = explorer.BrowseCatalog("fixture-app", "contracts", null, null, null);
        var search = explorer.SearchCatalog("fixture-app", "hero", null, null, [], [], null, null);
        var record = explorer.InspectCatalog("fixture-app", "contracts", "fixture-app.contract.hero");

        Assert.Equal("available", overview.Status);
        Assert.Equal("contracts", Assert.Single(overview.Collections).Id);
        Assert.Equal("fixture-app.contract.hero", Assert.Single(browse.Entries).Record!.QualifiedId);
        Assert.Equal("fixture-app.contract.hero", Assert.Single(search.Records).Record.QualifiedId);
        Assert.Equal(content, record.ContentJson);
        Assert.Equal("CURSOR_INVALID", Assert.Throws<ControlStructureException>(() => explorer.BrowseCatalog(
            "fixture-app", "contracts", null, new string('x', ControlStructureExplorer.MaximumCursorLength + 1), null)).Code);
    }

    [Fact]
    public void Structure_explorer_cursors_are_bounded_and_scope_bound()
    {
        var applications = new InMemoryApplicationRegistry();
        applications.Register(new(ApplicationIdentifier.Parse("alpha-app"), "Alpha", "", []));
        applications.Register(new(ApplicationIdentifier.Parse("bravo-app"), "Bravo", "", []));
        using var fixture = new SqliteFixture();
        using var db = fixture.CreateContext();
        var explorer = new ControlStructureExplorer(
            applications,
            new SqliteStateSpaceRegistry(db, applications),
            new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()),
            new SqliteEntityComponentStore(db, new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()), new BoundedJsonSchemaValidator()),
            new EmptyPublicApplicationCatalogProvider());

        var first = explorer.ListApplications(null, "1");
        Assert.NotNull(first.NextCursor);
        Assert.Equal("bravo-app", Assert.Single(explorer.ListApplications(first.NextCursor, "1").Items).Id);
        Assert.Equal("INVALID_LIMIT", Assert.Throws<ControlStructureException>(
            () => explorer.ListApplications(null, "101")).Code);
        Assert.Equal("CURSOR_INVALID", Assert.Throws<ControlStructureException>(
            () => explorer.ListApplications("not-a-cursor", "1")).Code);
        Assert.Equal("CURSOR_STALE", Assert.Throws<ControlStructureException>(
            () => explorer.ListApplications(first.NextCursor, "2")).Code);
    }

    [Fact]
    public async Task Committed_effect_history_groups_summaries_and_returns_exact_operation_context()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var timestamp = DateTime.UtcNow;
        db.Operations.Add(new Operation
        {
            Id = "operation-history",
            Timestamp = timestamp,
            Tool = "commit",
            Summary = "Created a record",
            Success = true,
            GuardEvidenceJson = "{\"accepted\":true}"
        });
        db.Events.Add(new EventRecord
        {
            Id = "event-history",
            TypeId = "world.entity.created",
            TypeVersion = 1,
            Scope = "world",
            PayloadJson = "{\"beforeJson\":null,\"afterJson\":{\"id\":\"hero\"}}",
            Timestamp = timestamp,
            CorrelationId = "operation-history",
            RootOperationId = "operation-history",
            Sequence = 0,
            Entities = [new EventEntity { EventId = "event-history", EntityId = "hero", Ordinal = 0 }]
        });
        await db.SaveChangesAsync();
        var history = new CommittedEffectHistory(new EventLedger(db), new OperationLog(db));

        var page = await history.ListAsync(null, null, null, null, "25");
        var detail = await history.GetAsync("event-history");

        var group = Assert.Single(page.Groups);
        Assert.Equal("operation-history", group.RootOperationId);
        Assert.Equal("event-history", Assert.Single(group.Events).Id);
        Assert.NotNull(detail);
        Assert.Contains("beforeJson", detail!.Event.PayloadJson, StringComparison.Ordinal);
        Assert.Equal("operation-history", detail.Operation!.Id);
        Assert.DoesNotContain("ProjectionJson", JsonSerializer.Serialize(detail));
    }

    [Fact]
    public async Task Committed_effect_history_rejects_invalid_bounds_and_missing_event()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var history = new CommittedEffectHistory(new EventLedger(db), new OperationLog(db));

        var limit = await Assert.ThrowsAsync<CommittedEffectHistoryException>(
            () => history.ListAsync(null, null, null, null, "101"));
        var cursor = await Assert.ThrowsAsync<CommittedEffectHistoryException>(
            () => history.ListAsync(null, null, null, "not a cursor", null));

        Assert.Equal("INVALID_LIMIT", limit.Code);
        Assert.Equal("INVALID_CURSOR", cursor.Code);
        Assert.Null(await history.GetAsync("missing-event"));
        Assert.Empty(await db.Events.ToListAsync());
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Denied_private_identity_never_invokes_the_web_handler()
    {
        var context = RequestContext("roleplay.example.ts.net", IPAddress.Loopback);
        context.Request.Method = HttpMethods.Put;
        context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = "intruder@example.com";
        var filter = new WebInterfaceSecurityFilter(OperatorGuard(new WebRemoteAccessOptions
        {
            Enabled = true,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = ["operator@example.com"]
        }));
        var invoked = false;

        await filter.InvokeAsync(new TestFilterContext(context), _ =>
        {
            invoked = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.False(invoked);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Theory]
    [InlineData(false, "roleplay.example.ts.net", "operator@example.com", "REMOTE_ACCESS_DENIED")]
    [InlineData(true, "other.example.ts.net", "operator@example.com", "REMOTE_ACCESS_DENIED")]
    [InlineData(true, "roleplay.example.ts.net", null, "REMOTE_IDENTITY_REQUIRED")]
    [InlineData(true, "roleplay.example.ts.net", "intruder@example.com", "REMOTE_ACCESS_DENIED")]
    public void Remote_access_fails_closed_for_disabled_host_missing_or_denied_identity(
        bool enabled,
        string host,
        string? login,
        string errorCode)
    {
        var context = RequestContext(host, IPAddress.IPv6Loopback);
        if (login is not null)
        {
            context.Request.Headers[WebAccessPolicy.TailscaleLoginHeader] = login;
        }

        var decision = AccessPolicy(new WebRemoteAccessOptions
        {
            Enabled = enabled,
            TailscaleHost = "roleplay.example.ts.net",
            AllowedLogins = ["operator@example.com"]
        }).Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Equal(errorCode, decision.ErrorCode);
    }

    [Fact]
    public void Remote_route_boundary_includes_only_the_web_surface()
    {
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/ui/home"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/pages/home"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/data/entity/hero"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/changes"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/session"));
        Assert.True(WebAccessPolicy.IsAllowedRemotePath("/api/control/status"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/mcp"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/api/pages-other"));
        Assert.False(WebAccessPolicy.IsAllowedRemotePath("/api/control-other"));
    }

    [Fact]
    public async Task Direct_html_reader_accepts_the_limit_and_rejects_oversize_or_invalid_utf8()
    {
        var reader = new WebHtmlReader();
        var maximum = Enumerable.Repeat((byte)'a', WebPageBundleLimits.MaximumHtmlBytes).ToArray();
        await using var valid = new MemoryStream(maximum);

        var html = await reader.ReadAsync(valid, valid.Length);

        Assert.Equal(WebPageBundleLimits.MaximumHtmlBytes, html.Length);
        await Assert.ThrowsAsync<WebHtmlUploadException>(() => reader.ReadAsync(
            Stream.Null,
            WebPageBundleLimits.MaximumHtmlBytes + 1L));
        await using var invalid = new MemoryStream([0xC3, 0x28]);
        var exception = await Assert.ThrowsAsync<WebHtmlUploadException>(
            () => reader.ReadAsync(invalid, invalid.Length));
        Assert.Equal("INVALID_HTML_ENCODING", exception.Code);
    }

    private static WebContentDbContext CreateWebContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(connection)
            .Options;
        return new WebContentDbContext(options);
    }

    private static async Task<long> SqliteTotalChangesAsync(DantesRoleplayDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT total_changes();";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static WebContentDbContext CreateWebContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new WebContentDbContext(options);
    }

    private static string SharedMemoryConnectionString() =>
        $"Data Source=web-change-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static WebAccessPolicy AccessPolicy(WebRemoteAccessOptions options) =>
        new(Options.Create(options));

    private static WebPrivateOperatorGuard OperatorGuard(WebRemoteAccessOptions options) =>
        new(AccessPolicy(options), new PrivateOperatorAuthorizationPolicy());

    private static WebControlRequestGuard ControlGuard(WebRemoteAccessOptions options) =>
        new(OperatorGuard(options));

    private static void AssertPanel(ControlCenterPanelStatus panel, string id)
    {
        Assert.Equal(id, panel.Id);
        Assert.Equal("unavailable", panel.State);
        Assert.False(string.IsNullOrWhiteSpace(panel.Message));
        Assert.True(panel.Message.Length <= 128);
    }

    private static DefaultHttpContext RequestContext(string host, IPAddress address)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = address;
        return context;
    }

    private sealed class TestFilterContext(HttpContext context) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = context;
        public override IList<object?> Arguments { get; } = [];
        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }

    private sealed class EmptyCatalogNavigator : ICatalogNavigator
    {
        public IReadOnlyList<CatalogCollectionSummary> ListCollections(ApplicationIdentifier applicationId) => [];
        public CatalogBrowseResult Browse(CatalogBrowseRequest request) => throw new NotSupportedException();
        public CatalogSearchResult Search(CatalogSearchRequest request) => throw new NotSupportedException();
        public CatalogRecordView Inspect(CatalogRecordRequest request) => throw new NotSupportedException();
    }

    private sealed class StaticHostSettingProvider(HostSettingCatalog catalog)
        : IHostSettingDefinitionProvider
    {
        public HostSettingCatalog GetCatalog() => catalog;
        public JsonElement NormalizeOverride(string key, JsonElement value) =>
            throw new KeyNotFoundException(key);
        public void ApplyStartupOverrides(IReadOnlyDictionary<string, JsonElement?> overrides) =>
            throw new InvalidOperationException();
    }

    private static MemoryStream CreateZip(params (string Path, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Path, CompressionLevel.Fastest);
                using var target = zipEntry.Open();
                target.Write(entry.Content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
