using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Slice3Tests : IDisposable
{
    private const string Instance = "dnd2024.item-instance";
    private const string Backpack = "item.dnd2024.backpack.v1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(
        Path.GetTempPath(),
        $"dantesroleplay-catalog-feature-23-slice-3-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();

        if (Directory.Exists(_catalogCopy))
        {
            Directory.Delete(_catalogCopy, recursive: true);
        }
    }

    [Fact]
    public async Task Physical_items_are_created_recorded_read_and_moved_through_containment()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await ImportAsync(db, mechanics, world);
        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.item-instance.create-and-place"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.item-instance"));

        await world.CreateEntityAsync("Ari", "fixture.catalog.f23.ari");
        await world.CreateEntityAsync("Ari's camp", "fixture.catalog.f23.camp");
        var runner = CreateRunner(db, world, mechanics);

        var created = await runner.RunAsync(Request("administratively grant physical item",
            """{"itemId":"fixture.catalog.f23.pack","name":"Ari's backpack","slot":"carried"}""",
            ("definition", Backpack), ("destination", "fixture.catalog.f23.ari")));
        Assert.True(created.Ok, created.Error?.Why);
        Assert.Equal("mechanic.dnd2024.item-instance.create-and-place", created.Mechanic?.Id);
        Assert.Equal(3, created.AppliedCount);
        Assert.Collection(created.Output!.Effects,
            effect => Assert.Equal(EffectType.EntityCreate, effect.Type),
            effect => Assert.Equal(EffectType.ComponentAdd, effect.Type),
            effect => Assert.Equal(EffectType.ContainmentMove, effect.Type));
        await AssertInstanceAsync(world, "fixture.catalog.f23.pack", Backpack, "fixture.catalog.f23.ari", "carried");

        var read = await runner.RunAsync(Request("read item instance", "{}", ("item", "fixture.catalog.f23.pack")));
        Assert.True(read.Ok, read.Error?.Why);
        Assert.Empty(read.Output!.Effects);
        using (var data = JsonDocument.Parse(read.Output.Data))
        {
            Assert.Equal(Backpack, data.RootElement.GetProperty("definitionId").GetString());
            Assert.Equal("fixture.catalog.f23.ari", data.RootElement.GetProperty("containerId").GetString());
            Assert.Equal("carried", data.RootElement.GetProperty("slot").GetString());
        }

        var moved = await runner.RunAsync(Request("administratively move physical item", """{"slot":"stored"}""",
            ("item", "fixture.catalog.f23.pack"), ("destination", "fixture.catalog.f23.camp")));
        Assert.True(moved.Ok, moved.Error?.Why);
        Assert.Single(moved.Output!.Effects);
        Assert.Equal(EffectType.ContainmentMove, moved.Output.Effects[0].Type);
        await AssertInstanceAsync(world, "fixture.catalog.f23.pack", Backpack, "fixture.catalog.f23.camp", "stored");

        await world.CreateEntityAsync("Loose rope", "fixture.catalog.f23.rope");
        var recorded = await runner.RunAsync(Request("record item instance", "{}",
            ("item", "fixture.catalog.f23.rope"), ("definition", "item.dnd2024.hempen-rope-50-foot.v1")));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Single(recorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, recorded.Output.Effects[0].Type);
        await AssertInstanceAsync(world, "fixture.catalog.f23.rope", "item.dnd2024.hempen-rope-50-foot.v1", null, "");
    }

    [Fact]
    public async Task Item_instance_lifecycle_rejects_duplicates_definitions_and_invalid_create_without_partial_state()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await ImportAsync(db, mechanics, world)).Aborted);
        await world.CreateEntityAsync("Carrier", "fixture.catalog.f23.carrier");
        await world.CreateEntityAsync("Loose item", "fixture.catalog.f23.loose");
        var runner = CreateRunner(db, world, mechanics);

        var recorded = await runner.RunAsync(Request("record item instance", "{}",
            ("item", "fixture.catalog.f23.loose"), ("definition", Backpack)));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        var before = await InstanceDataAsync(world, "fixture.catalog.f23.loose");

        var duplicateRecord = await runner.RunAsync(Request("record item instance", "{}",
            ("item", "fixture.catalog.f23.loose"), ("definition", "item.dnd2024.pouch.v1")));
        Assert.False(duplicateRecord.Ok);
        Assert.Equal(before, await InstanceDataAsync(world, "fixture.catalog.f23.loose"));

        var definitionAsItem = await runner.RunAsync(Request("record item instance", "{}",
            ("item", Backpack), ("definition", Backpack)));
        Assert.False(definitionAsItem.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(Backpack))!.Components, component => component.DefinitionId == Instance);

        var invalidCreate = await runner.RunAsync(Request("administratively grant physical item",
            """{"itemId":"Bad Id","name":"Broken","slot":"carried"}""",
            ("definition", Backpack), ("destination", "fixture.catalog.f23.carrier")));
        Assert.False(invalidCreate.Ok);
        Assert.Null(await world.GetEntityAsync("Bad Id"));

        var created = await runner.RunAsync(Request("administratively grant physical item",
            """{"itemId":"fixture.catalog.f23.atomic","name":"Atomic pack","slot":"carried"}""",
            ("definition", Backpack), ("destination", "fixture.catalog.f23.carrier")));
        Assert.True(created.Ok, created.Error?.Why);
        var duplicateCreate = await runner.RunAsync(Request("administratively grant physical item",
            """{"itemId":"fixture.catalog.f23.atomic","name":"Another pack","slot":"carried"}""",
            ("definition", Backpack), ("destination", "fixture.catalog.f23.carrier")));
        Assert.False(duplicateCreate.Ok);
        await AssertInstanceAsync(world, "fixture.catalog.f23.atomic", Backpack, "fixture.catalog.f23.carrier", "carried");
    }

    private static ActionRequest Request(string intent, string input, params (string role, string id)[] roles) => new()
    {
        Intent = intent,
        Input = input,
        Seed = 17,
        RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal)
    };

    private static async Task AssertInstanceAsync(WorldStore world, string id, string definitionId, string? containerId, string slot)
    {
        var entity = await world.GetEntityAsync(id);
        Assert.NotNull(entity);
        Assert.Equal(containerId, entity!.ContainerId);
        Assert.Equal(slot, entity.ContainerSlot);
        using var data = JsonDocument.Parse(await InstanceDataAsync(world, id));
        Assert.Equal(definitionId, data.RootElement.GetProperty("definitionId").GetString());
    }

    private static async Task<string> InstanceDataAsync(WorldStore world, string id) =>
        Assert.Single((await world.GetEntityAsync(id))!.Components, component => component.DefinitionId == Instance).Data;

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private async Task<CatalogImportResult> ImportAsync(
        DantesRoleplayDbContext db,
        MechanicStore mechanics,
        WorldStore world)
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        return await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
        }
        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }
}
