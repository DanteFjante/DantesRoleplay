using System.Text.Json.Nodes;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class WebInterfaceTests
{
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

    private static WebContentDbContext CreateWebContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<WebContentDbContext>()
            .UseSqlite(connection)
            .Options;
        return new WebContentDbContext(options);
    }
}
