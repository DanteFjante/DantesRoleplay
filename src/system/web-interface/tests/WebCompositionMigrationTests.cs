using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.Tests;

public sealed class WebCompositionMigrationTests
{
    private const string DurableAssetMigration = "20260907023758_DurableWebAssetContent";
    private const string ResourceIdentityPrevious = "20260911175529_RetainedWebCompositionDrafts";

    [Fact]
    public async Task Resource_identity_upgrade_preserves_pages_and_rejects_immutable_retained_identity_downgrade()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.GetService<IMigrator>().MigrateAsync(ResourceIdentityPrevious);
        var created = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO web_page (Id, ActiveRevision, UpdatedAt) VALUES ({"page"}, 0, {created})");

        await db.Database.MigrateAsync();
        Assert.Equal(0, (await db.Pages.SingleAsync(page => page.Id == "page")).ActiveRevision);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO web_page_resource_identity
                (ContentPageId, QualifiedTargetId, OwnerApplicationId, SourceOperationId, CreatedAtUtc)
            VALUES ('page', 'target.page', 'application.page', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', CURRENT_TIMESTAMP)
            """);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE web_page_resource_identity SET OwnerApplicationId = 'other' WHERE ContentPageId = 'page'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM web_page_resource_identity WHERE ContentPageId = 'page'"));
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.Pages.Remove(await db.Pages.SingleAsync(page => page.Id == "page"));
            await db.SaveChangesAsync();
        });
        var rejected = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(ResourceIdentityPrevious));
        Assert.Contains("retained_web_page_resource_identity_prevents_downgrade", rejected.Message);
        Assert.Equal("target.page", await db.Database.SqlQueryRaw<string>(
            "SELECT QualifiedTargetId AS Value FROM web_page_resource_identity").SingleAsync());
    }

    [Fact]
    public async Task Forward_migration_preserves_legacy_html_revisions_assets_and_active_pointer_while_supporting_compositions_and_inert_drafts()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.GetService<IMigrator>().MigrateAsync(DurableAssetMigration);

        var legacyBytes = Encoding.UTF8.GetBytes("retained legacy asset bytes");
        var legacyHash = Convert.ToHexString(SHA256.HashData(legacyBytes));
        var created = DateTime.UtcNow;
        await SeedLegacyPageAsync(db, "legacy", 2, created, legacyBytes, legacyHash);

        await db.Database.MigrateAsync();

        var legacy = await db.Pages.SingleAsync(page => page.Id == "legacy");
        Assert.Equal(2, legacy.ActiveRevision);
        var revisions = await db.PageRevisions.Where(row => row.PageId == "legacy")
            .OrderBy(row => row.Revision).ToArrayAsync();
        Assert.Equal(["<p>legacy one</p>", "<p>legacy two</p>"], revisions.Select(row => row.Html));
        Assert.All(revisions, row =>
        {
            Assert.Equal("html", row.ContentFormat);
            Assert.Null(row.CompositionJson);
            Assert.Null(row.CompositionHash);
        });
        Assert.Equal(legacyBytes, (await db.PageAssetContents.SingleAsync()).Content);

        const string composition = "{\"kind\":\"document\",\"body\":[]}";
        var compositionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composition)));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO web_page (Id, ActiveRevision, UpdatedAt)
            VALUES ({"composition"}, 1, {created}), ({"inert"}, 0, {created});
            INSERT INTO web_page_revision (PageId, Revision, Html, ContentFormat, CompositionJson, CompositionHash, CreatedAt)
            VALUES ({"composition"}, 1, {""}, {"composition-v1"}, {composition}, {compositionHash}, {created}),
                   ({"inert"}, 1, {""}, {"composition-v1"}, {composition}, {compositionHash}, {created});
            """);

        var compositionRevision = await db.PageRevisions.SingleAsync(row => row.PageId == "composition");
        Assert.Equal("composition-v1", compositionRevision.ContentFormat);
        Assert.Equal(composition, compositionRevision.CompositionJson);
        Assert.Equal(compositionHash, compositionRevision.CompositionHash);
        Assert.Equal(0, (await db.Pages.SingleAsync(page => page.Id == "inert")).ActiveRevision);
    }

    [Theory]
    [InlineData("composition", 1, "composition-v1")]
    [InlineData("inert", 0, "html")]
    public async Task Downgrade_refuses_retained_compositions_or_inert_drafts_without_losing_evidence(
        string pageId,
        int activeRevision,
        string contentFormat)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.MigrateAsync();

        var created = DateTime.UtcNow;
        const string composition = "{\"kind\":\"document\",\"body\":[]}";
        var compositionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composition)));
        var html = contentFormat == "html" ? "<p>inert legacy draft</p>" : "";
        var json = contentFormat == "html" ? null : composition;
        var hash = contentFormat == "html" ? null : compositionHash;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO web_page (Id, ActiveRevision, UpdatedAt)
            VALUES ({pageId}, {activeRevision}, {created});
            INSERT INTO web_page_revision (PageId, Revision, Html, ContentFormat, CompositionJson, CompositionHash, CreatedAt)
            VALUES ({pageId}, 1, {html}, {contentFormat}, {json}, {hash}, {created});
            """);

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(DurableAssetMigration));
        Assert.Contains("composition_or_inert_draft_prevents_downgrade", exception.Message, StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        Assert.Equal(activeRevision, (await db.Pages.SingleAsync(page => page.Id == pageId)).ActiveRevision);
        var retained = await db.PageRevisions.SingleAsync(row => row.PageId == pageId);
        Assert.Equal(contentFormat, retained.ContentFormat);
        Assert.Equal(html, retained.Html);
        Assert.Equal(json, retained.CompositionJson);
        Assert.Equal(hash, retained.CompositionHash);
        Assert.Contains("20260911175529_RetainedWebCompositionDrafts", await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Legacy_only_content_round_trips_through_downgrade_and_upgrade()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.MigrateAsync();

        var bytes = Encoding.UTF8.GetBytes("legacy asset survives downgrade");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await SeedLegacyPageAsync(db, "legacy", 2, DateTime.UtcNow, bytes, hash);

        await db.GetService<IMigrator>().MigrateAsync(DurableAssetMigration);
        await db.Database.MigrateAsync();

        Assert.Equal(2, (await db.Pages.SingleAsync(page => page.Id == "legacy")).ActiveRevision);
        Assert.Equal(["<p>legacy one</p>", "<p>legacy two</p>"],
            await db.PageRevisions.Where(row => row.PageId == "legacy").OrderBy(row => row.Revision)
                .Select(row => row.Html).ToArrayAsync());
        Assert.Equal(bytes, (await db.PageAssetContents.SingleAsync()).Content);
    }

    private static WebContentDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<WebContentDbContext>().UseSqlite(connection).Options);

    private static async Task SeedLegacyPageAsync(
        WebContentDbContext db,
        string pageId,
        int activeRevision,
        DateTime created,
        byte[] bytes,
        string hash)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO web_page (Id, ActiveRevision, UpdatedAt)
            VALUES ({pageId}, {activeRevision}, {created});
            INSERT INTO web_page_revision (Id, PageId, Revision, Html, CreatedAt)
            VALUES (1, {pageId}, 1, {"<p>legacy one</p>"}, {created}),
                   (2, {pageId}, 2, {"<p>legacy two</p>"}, {created});
            INSERT INTO web_page_asset_content (ContentHash, Content)
            VALUES ({hash}, {bytes});
            INSERT INTO web_page_asset (PageRevisionId, Path, ContentType, ContentHash)
            VALUES (1, {"assets/one.css"}, {"text/css"}, {hash}),
                   (2, {"assets/two.css"}, {"text/css"}, {hash});
            """);
    }
}
