using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Slice11Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-slice-11-catalog-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Inventory_read_is_bounded_effect_free_and_preserves_existing_consumer_seams()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s11.ari");
        await world.SetComponentAsync("fixture.f23.s11.ari", "dnd2024.abilities", """{"str":10,"dex":10,"con":10,"int":10,"wis":10,"cha":10}""");
        await world.SetComponentAsync("fixture.f23.s11.ari", "dnd2024.creature-size", """{"size":"medium"}""");
        var runner = Runner(db, world, mechanics);

        await GrantItem(runner, "fixture.f23.s11.pack", "Pack", "item.dnd2024.backpack.v1", "fixture.f23.s11.ari", "carried");
        await GrantItem(runner, "fixture.f23.s11.pouch", "Pouch", "item.dnd2024.pouch.v1", "fixture.f23.s11.pack", "inside");
        await GrantStack(runner, "fixture.f23.s11.coins", "Copper", "currency.dnd2024.copper-piece.v1", "fixture.f23.s11.pouch", "inside", 50);
        await GrantItem(runner, "fixture.f23.s11.dagger", "Dagger", "item.dnd2024.dagger.v1", "fixture.f23.s11.ari", "carried");
        var equip = await Run(runner, "hold item", """{"state":"held"}""", ("item", "fixture.f23.s11.dagger"), ("holder", "fixture.f23.s11.ari"));
        Assert.True(equip.Ok, equip.Error?.Why);

        var currency = await Run(runner, "read physical coin value", "{}", ("root", "fixture.f23.s11.ari"));
        Assert.True(currency.Ok, currency.Error?.Why);
        using (var currencyData = JsonDocument.Parse(currency.Output!.Data)) Assert.Equal(50, currencyData.RootElement.GetProperty("copperValue").GetInt32());

        var carrying = await Run(runner, "derive carrying capacity", "{}", ("creature", "fixture.f23.s11.ari"));
        Assert.True(carrying.Ok, carrying.Error?.Why);
        using (var carryingData = JsonDocument.Parse(carrying.Output!.Data)) Assert.True(carryingData.RootElement.GetProperty("withinCarryingCapacity").GetBoolean());

        var equipped = await Run(runner, "read equipped item", "{}", ("item", "fixture.f23.s11.dagger"));
        Assert.True(equipped.Ok, equipped.Error?.Why);
        using (var equipmentData = JsonDocument.Parse(equipped.Output!.Data)) Assert.Equal("held", equipmentData.RootElement.GetProperty("state").GetString());

        await world.CreateEntityAsync("Unclassified letter", "fixture.f23.s11.letter");
        await world.MoveAsync("fixture.f23.s11.letter", "fixture.f23.s11.ari", "carried");
        var inventory = await Run(runner, "inspect inventory", "{}", ("root", "fixture.f23.s11.ari"));
        Assert.True(inventory.Ok, inventory.Error?.Why);
        Assert.Empty(inventory.Output!.Effects);
        using var data = JsonDocument.Parse(inventory.Output.Data);
        Assert.Equal("fixture.f23.s11.ari", data.RootElement.GetProperty("rootId").GetString());
        Assert.Equal(4, data.RootElement.GetProperty("contentsDepth").GetInt32());
        Assert.True(data.RootElement.GetProperty("mayOmitDeeperContents").GetBoolean());
        var items = data.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Contains(items, item => item.GetProperty("itemId").GetString() == "fixture.f23.s11.pack" && item.GetProperty("depth").GetInt32() == 1);
        Assert.Contains(items, item => item.GetProperty("itemId").GetString() == "fixture.f23.s11.pouch" && item.GetProperty("depth").GetInt32() == 2);
        Assert.Contains(items, item => item.GetProperty("itemId").GetString() == "fixture.f23.s11.coins" && item.GetProperty("quantity").GetInt32() == 50 && item.GetProperty("depth").GetInt32() == 3);
        Assert.Contains(items, item => item.GetProperty("itemId").GetString() == "fixture.f23.s11.dagger" && item.GetProperty("equipmentState").GetString() == "held");
        var unclassified = Assert.Single(data.RootElement.GetProperty("unclassifiedContents").EnumerateArray());
        Assert.Equal("fixture.f23.s11.letter", unclassified.GetProperty("entityId").GetString());
    }

    private static async Task GrantItem(ActionRunner runner, string itemId, string name, string definition, string destination, string slot)
    {
        var result = await Run(runner, "administratively grant physical item", JsonSerializer.Serialize(new { itemId, name, slot }), ("definition", definition), ("destination", destination));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static async Task GrantStack(ActionRunner runner, string itemId, string name, string definition, string destination, string slot, int count)
    {
        var result = await Run(runner, "administratively grant physical item stack", JsonSerializer.Serialize(new { itemId, name, slot, count }), ("definition", definition), ("destination", destination));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 11, RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal) });
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world) { CopyDirectory(RepositoryCatalog(), _catalogCopy); var result = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions()); Assert.False(result.Aborted); }
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
