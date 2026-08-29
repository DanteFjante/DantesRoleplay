using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Slice9Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-slice-9-catalog-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Currency_value_is_derived_from_nested_physical_stacks_without_effects()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s9.ari");
        var runner = Runner(db, world, mechanics);

        var empty = await Run(runner, "count carried currency", "{}", ("root", "fixture.f23.s9.ari"));
        Assert.True(empty.Ok, empty.Error?.Why);
        Assert.Empty(empty.Output!.Effects);
        using (var emptyData = JsonDocument.Parse(empty.Output.Data))
        {
            Assert.Equal(0, emptyData.RootElement.GetProperty("coinCount").GetInt32());
            Assert.Equal(0, emptyData.RootElement.GetProperty("copperValue").GetInt32());
            Assert.Empty(emptyData.RootElement.GetProperty("denominations").EnumerateArray());
        }

        await GrantItem(runner, "fixture.f23.s9.pouch", "Pouch", "item.dnd2024.pouch.v1", "fixture.f23.s9.ari");
        await GrantStack(runner, "fixture.f23.s9.copper", "Copper pieces", "currency.dnd2024.copper-piece.v1", "fixture.f23.s9.pouch", 4);
        await GrantStack(runner, "fixture.f23.s9.silver", "Silver pieces", "currency.dnd2024.silver-piece.v1", "fixture.f23.s9.pouch", 3);
        await GrantStack(runner, "fixture.f23.s9.electrum", "Electrum pieces", "currency.dnd2024.electrum-piece.v1", "fixture.f23.s9.ari", 1);
        await GrantStack(runner, "fixture.f23.s9.gold", "Gold pieces", "currency.dnd2024.gold-piece.v1", "fixture.f23.s9.ari", 2);

        var read = await Run(runner, "read physical coin value", "{}", ("root", "fixture.f23.s9.ari"));
        Assert.True(read.Ok, read.Error?.Why);
        Assert.Empty(read.Output!.Effects);
        using var data = JsonDocument.Parse(read.Output.Data);
        Assert.Equal("fixture.f23.s9.ari", data.RootElement.GetProperty("rootId").GetString());
        Assert.Equal(10, data.RootElement.GetProperty("coinCount").GetInt32());
        Assert.Equal(284, data.RootElement.GetProperty("copperValue").GetInt32());
        Assert.Equal(4, data.RootElement.GetProperty("boundedDepth").GetInt32());
        Assert.Equal(
            ["cp", "sp", "ep", "gp"],
            data.RootElement.GetProperty("denominations").EnumerateArray().Select(row => row.GetProperty("denomination").GetString()));
        Assert.Equal(4, data.RootElement.GetProperty("denominations")[0].GetProperty("count").GetInt32());
        Assert.Equal(30, data.RootElement.GetProperty("denominations")[1].GetProperty("totalCopperValue").GetInt32());
        Assert.Equal(200, data.RootElement.GetProperty("denominations")[3].GetProperty("totalCopperValue").GetInt32());
    }

    [Fact]
    public async Task Currency_reader_refuses_an_unquantified_currency_instance_without_creating_a_balance()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s9.ari");
        var runner = Runner(db, world, mechanics);

        await GrantItem(runner, "fixture.f23.s9.invalid-copper", "Invalid copper", "currency.dnd2024.copper-piece.v1", "fixture.f23.s9.ari");
        var refused = await Run(runner, "inspect coin stacks", "{}", ("root", "fixture.f23.s9.ari"));
        Assert.False(refused.Ok);
        var item = await world.GetEntityAsync("fixture.f23.s9.invalid-copper");
        Assert.NotNull(item);
        Assert.Equal("fixture.f23.s9.ari", item!.ContainerId);
        Assert.DoesNotContain(item.Components, component => component.DefinitionId == "dnd2024.item-quantity");
    }

    private static async Task GrantItem(ActionRunner runner, string itemId, string name, string definition, string destination)
    {
        var result = await Run(runner, "administratively grant physical item", JsonSerializer.Serialize(new { itemId, name, slot = "carried" }), ("definition", definition), ("destination", destination));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static async Task GrantStack(ActionRunner runner, string itemId, string name, string definition, string destination, int count)
    {
        var result = await Run(runner, "administratively grant physical item stack", JsonSerializer.Serialize(new { itemId, name, slot = "carried", count }), ("definition", definition), ("destination", destination));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 9, RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal) });
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world) { CopyDirectory(RepositoryCatalog(), _catalogCopy); var result = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions()); Assert.False(result.Aborted); }
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
