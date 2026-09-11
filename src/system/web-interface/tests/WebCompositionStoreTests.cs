using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class WebCompositionStoreTests
{
    [Fact]
    public async Task First_composition_draft_is_inert_and_retains_canonical_payload_and_assets()
    {
        var fixture = await Storage.CreateAsync();
        await using var db = fixture.Open();
        var store = new WebPageStore(db);
        var draft = await store.AppendBundleDraftAsync("preview", 0, Composition("first", [1, 2]));
        Assert.Equal(1, draft.Summary.Revision);
        Assert.False(draft.Summary.IsActive);
        Assert.Equal(WebPageContentFormat.Composition, draft.ContentFormat);
        Assert.Equal(string.Empty, draft.Html);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(draft.CompositionJson!))), draft.CompositionHash);
        Assert.Equal(draft.CompositionHash, draft.Summary.ContentHash);
        Assert.Equal(draft.CompositionHash, draft.Summary.CompositionHash);
        Assert.Equal(0, (await store.GetSummaryAsync("preview"))!.ActiveRevision);
        Assert.Null(await store.GetActiveAsync("preview"));
        Assert.Null(await store.GetActiveAssetAsync("preview", "assets/logo.bin"));
        Assert.Equal(new byte[] { 1, 2 }, (await store.GetRevisionAssetAsync("preview", 1, "assets/logo.bin"))!.Content);
        await using var reopened = fixture.Open();
        var retained = await new WebPageStore(reopened).GetRevisionAsync("preview", 1);
        Assert.Equal(draft.CompositionJson, retained!.CompositionJson);
        Assert.Equal(draft.CompositionHash, retained.CompositionHash);
        Assert.Equal(WebPageContentFormat.Composition, retained.Summary.ContentFormat);
    }

    [Fact]
    public async Task Historical_asset_reads_stay_on_the_requested_revision_and_never_fall_back()
    {
        var fixture = await Storage.CreateAsync();
        await using var db = fixture.Open();
        var store = new WebPageStore(db);
        var first = await store.AppendBundleDraftAsync("preview", 0, Composition("first", [1]));
        var second = await store.AppendBundleDraftAsync("preview", 1, Composition("second", [2]));
        Assert.NotEqual(first.CompositionHash, second.CompositionHash);
        Assert.Equal(2, second.Summary.Revision);
        Assert.Equal(new byte[] { 1 }, (await store.GetRevisionAssetAsync("preview", 1, "assets/logo.bin"))!.Content);
        Assert.Equal(new byte[] { 2 }, (await store.GetRevisionAssetAsync("preview", 2, "assets/logo.bin"))!.Content);
        Assert.Null(await store.GetRevisionAssetAsync("preview", 3, "assets/logo.bin"));
        Assert.Null(await store.GetRevisionAssetAsync("preview", 1, "assets/missing.bin"));
        Assert.Null(await store.GetRevisionAssetAsync("preview", 1, "assets/../logo.bin"));
        Assert.Null(await store.GetRevisionAssetAsync("preview", 1, "composition.json"));
        var revisions = await store.ListRevisionsAsync("preview", null, 10);
        Assert.Equal([2, 1], revisions.Revisions.Select(revision => revision.Revision));
        Assert.All(revisions.Revisions, revision => Assert.False(revision.IsActive));
    }

    [Fact]
    public async Task Composition_cannot_activate_through_either_legacy_entry_and_prior_html_stays_usable()
    {
        var fixture = await Storage.CreateAsync();
        await using var db = fixture.Open();
        var store = new WebPageStore(db);
        await AssertCode("COMPOSITION_ACTIVATION_UNAVAILABLE", () => store.SaveBundleAndActivateAsync("new", Composition("new", [1])));
        Assert.Null(await store.GetSummaryAsync("new"));
        const string html = "<h1>Previously published</h1>";
        await store.SaveBundleAndActivateAsync("page", new(html, [new("assets/old.bin", [4])]));
        var candidate = await store.AppendBundleDraftAsync("page", 1, Composition("candidate", [8]));
        await AssertCode("COMPOSITION_ACTIVATION_UNAVAILABLE", () => store.ActivateRevisionAsync("page", candidate.Summary.Revision, 1));
        Assert.Equal(html, (await store.GetActiveAsync("page"))!.Html);
        Assert.Equal(new byte[] { 4 }, (await store.GetActiveAssetAsync("page", "assets/old.bin"))!.Content);
        Assert.Null(await store.GetActiveAssetAsync("page", "assets/logo.bin"));
        await AssertCode("PAGE_FORMAT_MISMATCH", () => store.AppendDraftAsync("page", 2, 2, "<p>replace</p>"));
        Assert.Equal(2, (await store.GetSummaryAsync("page"))!.LatestRevision);
        Assert.Null((await store.GetActiveAsync("page"))!.CompositionJson);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))),
            (await store.GetRevisionAsync("page", 1))!.Summary.ContentHash);
    }

    [Fact]
    public async Task A_first_html_draft_can_activate_from_explicit_absent_active_state()
    {
        var fixture = await Storage.CreateAsync();
        await using var db = fixture.Open();
        var store = new WebPageStore(db);
        await store.AppendBundleDraftAsync("page", 0, new("<p>draft</p>", []));
        Assert.Null(await store.GetActiveAsync("page"));
        await store.ActivateRevisionAsync("page", 1, 0);
        Assert.Equal("<p>draft</p>", (await store.GetActiveAsync("page"))!.Html);
        await AssertCode("PAGE_LATEST_STALE", () => store.AppendBundleDraftAsync("page", 0, new("<p>stale</p>", [])));
        await AssertCode("PAGE_UNKNOWN", () => store.AppendBundleDraftAsync("absent", 1, new("<p>stale</p>", [])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Competing_writers_compare_after_reservation_and_commit_exactly_one_draft(int expectedLatest)
    {
        var fixture = await Storage.CreateAsync();
        if (expectedLatest == 1)
        {
            await using var seed = fixture.Open();
            await new WebPageStore(seed).AppendBundleDraftAsync("page", 0, Composition("seed", [0]));
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Attempt(string generation, byte[] bytes)
        {
            await start.Task;
            await using var context = fixture.Open();
            try
            {
                await new WebPageStore(context).AppendBundleDraftAsync("page", expectedLatest, Composition(generation, bytes));
                return "saved";
            }
            catch (WebPageStoreException exception) { return exception.Code; }
        }
        var first = Task.Run(() => Attempt("one", [1]));
        var second = Task.Run(() => Attempt("two", [2]));
        start.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, outcome => outcome == "saved");
        Assert.Single(outcomes, outcome => outcome == "PAGE_LATEST_STALE");
        await using var verify = fixture.Open();
        Assert.Equal(expectedLatest + 1, await verify.PageRevisions.CountAsync());
        Assert.Equal(expectedLatest + 1, await verify.PageAssets.CountAsync());
        Assert.Equal(0, (await new WebPageStore(verify).GetSummaryAsync("page"))!.ActiveRevision);
    }

    [Fact]
    public async Task Invalid_payload_or_asset_inventory_never_creates_a_page_or_blob()
    {
        var fixture = await Storage.CreateAsync();
        await using var db = fixture.Open();
        var store = new WebPageStore(db);
        var badHash = Composition("first", [1]) with { CompositionHash = new string('0', 64) };
        Assert.Equal("COMPOSITION_HASH_MISMATCH", (await Assert.ThrowsAsync<WebPageBundleException>(() =>
            store.AppendBundleDraftAsync("page", 0, badHash))).Code);
        var missingAsset = Composition("first", [1]) with { Assets = [] };
        Assert.Equal("MISSING_ASSET", (await Assert.ThrowsAsync<WebPageBundleException>(() =>
            store.AppendBundleDraftAsync("page", 0, missingAsset))).Code);
        await Assert.ThrowsAsync<WebPageBundleException>(() => store.AppendBundleDraftAsync("page", 0,
            new("<p>mixed</p>", []) { CompositionJson = "{}" }));
        Assert.Empty(await db.Pages.ToListAsync());
        Assert.Empty(await db.PageAssetContents.ToListAsync());
    }

    [Fact]
    public async Task Failed_first_draft_rolls_back_page_revision_and_blob_together()
    {
        var fixture = await Storage.CreateAsync();
        await using (var setup = fixture.Open())
            await setup.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_asset BEFORE INSERT ON web_page_asset BEGIN SELECT RAISE(ABORT, 'fixture'); END;");
        await using (var failing = fixture.Open())
            await Assert.ThrowsAsync<DbUpdateException>(() => new WebPageStore(failing).AppendBundleDraftAsync("page", 0, Composition("first", [1])));
        await using var verify = fixture.Open();
        Assert.Empty(await verify.Pages.ToListAsync());
        Assert.Empty(await verify.PageRevisions.ToListAsync());
        Assert.Empty(await verify.PageAssetContents.ToListAsync());
    }

    [Fact]
    public async Task Content_constraints_reject_invalid_json_or_oversize_utf8_and_accept_absent_active()
    {
        var fixture = await Storage.CreateAsync();
        await using var db = fixture.Open();
        db.Pages.Add(new() { Id = "page", ActiveRevision = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        foreach (var json in new[] { "invalid", "\"" + new string('é', 530_000) + "\"" })
        {
            var hash = new string('A', 64);
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO web_page_revision (PageId, Revision, Html, ContentFormat, CompositionJson, CompositionHash, CreatedAt)
                VALUES ('page', 1, '', 'composition-v1', {json}, {hash}, '2026-09-11')
                """));
        }
        Assert.Empty(await db.PageRevisions.ToListAsync());
    }

    private static WebPageBundle Composition(string generation, byte[] asset) => new(string.Empty, [new("assets/logo.bin", asset)])
    {
        ContentFormat = WebPageContentFormat.Composition,
        CompositionJson = "{\"root\":{\"kind\":\"asset\",\"path\":\"assets/logo.bin\"},\"generation\":\"" + generation + "\",\"components\":[],\"formatVersion\":1}"
    };

    private static async Task AssertCode(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<WebPageStoreException>(action)).Code);

    private sealed record Storage(string ConnectionString)
    {
        public WebContentDbContext Open() => new(new DbContextOptionsBuilder<WebContentDbContext>().UseSqlite(ConnectionString).Options);
        public static async Task<Storage> CreateAsync()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("The test worktree root was not found.");
            var directory = Path.Combine(root.FullName, ".tmp", "composition-store-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var fixture = new Storage(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(directory, "content.db"), Pooling = false, DefaultTimeout = 10 }.ToString());
            await using var db = fixture.Open();
            await db.Database.MigrateAsync(); // Real coordinator migration, isolated file-backed storage per fixture.
            return fixture;
        }
    }
}
