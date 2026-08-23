using DantesRoleplay.DataAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class WorldStoreTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static async Task<WorldStore> StoreWithDefinitionsAsync(
        DantesRoleplayDbContext db,
        params string[] definitionIds)
    {
        var store = new WorldStore(db);

        foreach (var id in definitionIds)
        {
            await store.DefineComponentAsync(id, id, $"Test definition {id}.");
        }

        return store;
    }

    [Fact]
    public async Task An_entity_starts_with_nothing_attached()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);

        var created = await store.CreateEntityAsync("Orban");

        Assert.NotEmpty(created.Id);
        Assert.Equal("Orban", created.Name);
        Assert.Empty(created.Components);
        Assert.Null(created.ContainerId);
    }

    [Fact]
    public async Task A_new_game_concept_is_a_row_not_a_schema_change()
    {
        await using var db = _fixture.CreateContext();
        var store = await StoreWithDefinitionsAsync(db, "stats");
        var orban = await store.CreateEntityAsync("Orban");

        // "Resonance" is a stat nobody anticipated. No migration, no C# change.
        await store.SetComponentAsync(orban.Id, "stats", """{"strength":12,"resonance":4}""");

        var snapshot = await store.GetEntityAsync(orban.Id);

        Assert.NotNull(snapshot);
        var stats = Assert.Single(snapshot!.Components);
        Assert.Equal("stats", stats.DefinitionId);
        Assert.Contains("resonance", stats.Data);
    }

    [Fact]
    public async Task Attaching_an_undeclared_component_fails_loudly()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);
        var orban = await store.CreateEntityAsync("Orban");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SetComponentAsync(orban.Id, "stats", "{}"));

        Assert.Contains("Define it first", error.Message);
    }

    [Fact]
    public async Task Set_replaces_and_merge_only_touches_the_keys_it_was_given()
    {
        await using var db = _fixture.CreateContext();
        var store = await StoreWithDefinitionsAsync(db, "stats");
        var orban = await store.CreateEntityAsync("Orban");

        await store.SetComponentAsync(orban.Id, "stats", """{"strength":12,"luck":3}""");

        var merged = await store.MergeComponentAsync(orban.Id, "stats", """{"luck":5}""");
        Assert.Contains("\"strength\":12", merged.Data);
        Assert.Contains("\"luck\":5", merged.Data);

        // Set discards what it was not sent. This is the distinction that bit TravelRoleplay.
        var replaced = await store.SetComponentAsync(orban.Id, "stats", """{"luck":5}""");
        Assert.DoesNotContain("strength", replaced.Data);
    }

    [Fact]
    public async Task Component_data_must_be_a_json_object()
    {
        await using var db = _fixture.CreateContext();
        var store = await StoreWithDefinitionsAsync(db, "stats");
        var orban = await store.CreateEntityAsync("Orban");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SetComponentAsync(orban.Id, "stats", "[1,2,3]"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SetComponentAsync(orban.Id, "stats", "not json"));
    }

    [Fact]
    public async Task An_entity_carries_one_component_per_definition()
    {
        await using var db = _fixture.CreateContext();
        var store = await StoreWithDefinitionsAsync(db, "stats");
        var orban = await store.CreateEntityAsync("Orban");

        await store.SetComponentAsync(orban.Id, "stats", """{"strength":12}""");
        var second = await store.SetComponentAsync(orban.Id, "stats", """{"strength":14}""");

        Assert.Equal(2, second.Revision);

        var snapshot = await store.GetEntityAsync(orban.Id);
        Assert.Single(snapshot!.Components);
    }

    [Fact]
    public async Task Entities_can_be_found_by_the_components_they_carry()
    {
        await using var db = _fixture.CreateContext();
        var store = await StoreWithDefinitionsAsync(db, "position", "stats");

        var orban = await store.CreateEntityAsync("Orban");
        var rock = await store.CreateEntityAsync("A rock");

        await store.SetComponentAsync(orban.Id, "position", """{"x":1}""");
        await store.SetComponentAsync(orban.Id, "stats", """{"strength":12}""");
        await store.SetComponentAsync(rock.Id, "position", """{"x":2}""");

        Assert.Equal(2, (await store.FindEntitiesAsync(withDefinitionId: "position")).Count);
        Assert.Single(await store.FindEntitiesAsync(withDefinitionId: "stats"));
        Assert.Single(await store.FindEntitiesAsync(nameQuery: "rock"));
    }

    [Fact]
    public async Task A_thing_is_in_at_most_one_place()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);

        var tavern = await store.CreateEntityAsync("The Tavern");
        var road = await store.CreateEntityAsync("The Road");
        var orban = await store.CreateEntityAsync("Orban");

        await store.MoveAsync(orban.Id, tavern.Id);
        await store.MoveAsync(orban.Id, road.Id, slot: "standing-in");

        Assert.Empty(await store.GetContentsAsync(tavern.Id));

        var onRoad = Assert.Single(await store.GetContentsAsync(road.Id));
        Assert.Equal("Orban", onRoad.Name);
        Assert.Equal("standing-in", onRoad.Slot);

        var snapshot = await store.GetEntityAsync(orban.Id);
        Assert.Equal(road.Id, snapshot!.ContainerId);
    }

    [Fact]
    public async Task Moving_to_nowhere_removes_containment()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);

        var bag = await store.CreateEntityAsync("Bag");
        var coin = await store.CreateEntityAsync("Coin");

        await store.MoveAsync(coin.Id, bag.Id);
        await store.MoveAsync(coin.Id, null);

        Assert.Empty(await store.GetContentsAsync(bag.Id));
        Assert.Null((await store.GetEntityAsync(coin.Id))!.ContainerId);
    }

    [Fact]
    public async Task Containment_cycles_are_refused()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);

        var bag = await store.CreateEntityAsync("Bag");
        var box = await store.CreateEntityAsync("Box");
        var pouch = await store.CreateEntityAsync("Pouch");

        await store.MoveAsync(box.Id, bag.Id);
        await store.MoveAsync(pouch.Id, box.Id);

        // Direct.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveAsync(bag.Id, bag.Id));

        // Indirect: bag -> pouch -> box -> bag.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveAsync(bag.Id, pouch.Id));
    }

    [Fact]
    public async Task Relationships_are_many_and_directed()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);

        var orban = await store.CreateEntityAsync("Orban");
        var sol = await store.CreateEntityAsync("Sol");

        await store.RelateAsync(orban.Id, sol.Id, "owes-a-debt-to", """{"amount":20}""");
        await store.RelateAsync(orban.Id, sol.Id, "is-loyal-to");

        var outgoing = await store.GetRelationshipsAsync(orban.Id, includeIncoming: false);
        Assert.Equal(2, outgoing.Count);

        var solSide = await store.GetRelationshipsAsync(sol.Id);
        Assert.Equal(2, solSide.Count);

        Assert.True(await store.UnrelateAsync(orban.Id, sol.Id, "is-loyal-to"));
        Assert.False(await store.UnrelateAsync(orban.Id, sol.Id, "is-loyal-to"));
        Assert.Single(await store.GetRelationshipsAsync(orban.Id, includeIncoming: false));
    }

    [Fact]
    public async Task Relating_the_same_pair_and_kind_updates_rather_than_duplicating()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);

        var orban = await store.CreateEntityAsync("Orban");
        var sol = await store.CreateEntityAsync("Sol");

        await store.RelateAsync(orban.Id, sol.Id, "owes-a-debt-to", """{"amount":20}""");
        var updated = await store.RelateAsync(orban.Id, sol.Id, "owes-a-debt-to", """{"amount":5}""");

        Assert.Contains("\"amount\":5", updated.Data);
        Assert.Single(await store.GetRelationshipsAsync(orban.Id, includeIncoming: false));
    }

    [Fact]
    public async Task Deleting_an_entity_is_soft_and_hides_it_from_reads()
    {
        await using var db = _fixture.CreateContext();
        var store = new WorldStore(db);

        var orban = await store.CreateEntityAsync("Orban");

        Assert.True(await store.DeleteEntityAsync(orban.Id));
        Assert.False(await store.DeleteEntityAsync(orban.Id));

        Assert.Null(await store.GetEntityAsync(orban.Id));
        Assert.Empty(await store.FindEntitiesAsync(nameQuery: "Orban"));
    }

    [Fact]
    public async Task Definitions_report_how_widely_they_are_used()
    {
        await using var db = _fixture.CreateContext();
        var store = await StoreWithDefinitionsAsync(db, "position");

        var a = await store.CreateEntityAsync("A");
        var b = await store.CreateEntityAsync("B");
        await store.SetComponentAsync(a.Id, "position", "{}");
        await store.SetComponentAsync(b.Id, "position", "{}");

        var definitions = await store.GetDefinitionsAsync();

        var position = Assert.Single(definitions);
        Assert.Equal(2, position.UsageCount);
    }

    [Fact]
    public async Task Several_entities_can_be_materialised_in_one_call()
    {
        await using var db = _fixture.CreateContext();
        var store = await StoreWithDefinitionsAsync(db, "stats");

        var orban = await store.CreateEntityAsync("Orban");
        var sol = await store.CreateEntityAsync("Sol");
        await store.SetComponentAsync(orban.Id, "stats", """{"strength":12}""");

        // This is the call a mechanic's declared requirements become (ARCHITECTURE.md §3.6).
        var snapshots = await store.GetEntitiesAsync([orban.Id, sol.Id, "does-not-exist"]);

        Assert.Equal(2, snapshots.Count);
    }
}
