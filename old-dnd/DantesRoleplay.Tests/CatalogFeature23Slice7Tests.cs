using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Slice7Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-slice-7-catalog-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Transfer_admits_whole_items_and_preserves_stack_quantity()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s7.ari");
        await world.CreateEntityAsync("Borin", "fixture.f23.s7.borin");
        var runner = Runner(db, world, mechanics);

        await GrantItem(runner, "fixture.f23.s7.pouch", "Pouch", "item.dnd2024.pouch.v1", "fixture.f23.s7.ari");
        await GrantItem(runner, "fixture.f23.s7.rope", "Rope", "item.dnd2024.hempen-rope-50-foot.v1", "fixture.f23.s7.ari");
        var stowed = await Run(runner, "stow item", """{"slot":"inside"}""",
            ("item", "fixture.f23.s7.rope"), ("source", "fixture.f23.s7.ari"), ("destination", "fixture.f23.s7.pouch"));
        Assert.True(stowed.Ok, stowed.Error?.Why);
        Assert.Equal("mechanic.dnd2024.item.transfer", stowed.Mechanic!.Id);
        Assert.Equal(EffectType.ContainmentMove, Assert.Single(stowed.Output!.Effects).Type);
        Assert.Equal("fixture.f23.s7.pouch", (await world.GetEntityAsync("fixture.f23.s7.rope"))!.ContainerId);

        await GrantStack(runner, "fixture.f23.s7.coins", "Copper", "currency.dnd2024.copper-piece.v1", "fixture.f23.s7.ari", 3);
        var transferred = await Run(runner, "transfer physical item", """{"slot":"inside"}""",
            ("item", "fixture.f23.s7.coins"), ("source", "fixture.f23.s7.ari"), ("destination", "fixture.f23.s7.pouch"));
        Assert.True(transferred.Ok, transferred.Error?.Why);
        await AssertStackAsync(world, "fixture.f23.s7.coins", 3, "fixture.f23.s7.pouch");

        var split = await Run(runner, "split stack", """{"itemId":"fixture.f23.s7.coins-split","name":"Split copper","count":1}""",
            ("source", "fixture.f23.s7.coins"), ("definition", "currency.dnd2024.copper-piece.v1"));
        Assert.True(split.Ok, split.Error?.Why);
        await AssertStackAsync(world, "fixture.f23.s7.coins", 2, "fixture.f23.s7.pouch");
        await AssertStackAsync(world, "fixture.f23.s7.coins-split", 1, "fixture.f23.s7.pouch");
        var merged = await Run(runner, "merge stacks", "{}",
            ("source", "fixture.f23.s7.coins-split"), ("target", "fixture.f23.s7.coins"), ("definition", "currency.dnd2024.copper-piece.v1"));
        Assert.True(merged.Ok, merged.Error?.Why);
        await AssertStackAsync(world, "fixture.f23.s7.coins", 3, "fixture.f23.s7.pouch");

        var rootTransfer = await Run(runner, "give item", """{"slot":"carried"}""",
            ("item", "fixture.f23.s7.rope"), ("source", "fixture.f23.s7.pouch"), ("destination", "fixture.f23.s7.borin"));
        Assert.True(rootTransfer.Ok, rootTransfer.Error?.Why);
        Assert.Equal("fixture.f23.s7.borin", (await world.GetEntityAsync("fixture.f23.s7.rope"))!.ContainerId);
    }

    [Fact]
    public async Task Transfer_rejects_bad_custody_cycles_permissions_and_capacity_without_mutation()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s7.ari");
        await world.CreateEntityAsync("Borin", "fixture.f23.s7.borin");
        var runner = Runner(db, world, mechanics);

        await GrantItem(runner, "fixture.f23.s7.pack", "Backpack", "item.dnd2024.backpack.v1", "fixture.f23.s7.ari");
        await GrantItem(runner, "fixture.f23.s7.pouch", "Pouch", "item.dnd2024.pouch.v1", "fixture.f23.s7.pack");
        var cycle = await Run(runner, "move item", """{"slot":"inside"}""",
            ("item", "fixture.f23.s7.pack"), ("source", "fixture.f23.s7.ari"), ("destination", "fixture.f23.s7.pouch"));
        Assert.False(cycle.Ok);
        Assert.Equal("fixture.f23.s7.ari", (await world.GetEntityAsync("fixture.f23.s7.pack"))!.ContainerId);

        await GrantItem(runner, "fixture.f23.s7.dagger", "Dagger", "item.dnd2024.dagger.v1", "fixture.f23.s7.ari");
        var sourceMismatch = await Run(runner, "take item", """{"slot":"carried"}""",
            ("item", "fixture.f23.s7.dagger"), ("source", "fixture.f23.s7.borin"), ("destination", "fixture.f23.s7.borin"));
        Assert.False(sourceMismatch.Ok);
        Assert.Equal("fixture.f23.s7.ari", (await world.GetEntityAsync("fixture.f23.s7.dagger"))!.ContainerId);

        await GrantItem(runner, "fixture.f23.s7.quiver", "Quiver", "item.dnd2024.quiver.v1", "fixture.f23.s7.ari");
        var wrongKind = await Run(runner, "transfer physical item", """{"slot":"inside"}""",
            ("item", "fixture.f23.s7.dagger"), ("source", "fixture.f23.s7.ari"), ("destination", "fixture.f23.s7.quiver"));
        Assert.False(wrongKind.Ok);
        Assert.Equal("fixture.f23.s7.ari", (await world.GetEntityAsync("fixture.f23.s7.dagger"))!.ContainerId);

        await CreateAmmunitionFixtureAsync(world);
        await CreateFixtureStackAsync(world, "fixture.f23.s7.arrows-a", "Arrows", 20, "fixture.f23.s7.quiver");
        await CreateFixtureStackAsync(world, "fixture.f23.s7.arrows-b", "More arrows", 1, "fixture.f23.s7.ari");
        var countExceeded = await Run(runner, "transfer physical item", """{"slot":"inside"}""",
            ("item", "fixture.f23.s7.arrows-b"), ("source", "fixture.f23.s7.ari"), ("destination", "fixture.f23.s7.quiver"));
        Assert.False(countExceeded.Ok);
        await AssertStackAsync(world, "fixture.f23.s7.arrows-b", 1, "fixture.f23.s7.ari");
    }

    private static async Task GrantItem(ActionRunner runner, string itemId, string name, string definition, string destination)
    {
        var result = await Run(runner, "administratively grant physical item", JsonSerializer.Serialize(new { itemId, name, slot = "carried" }),
            ("definition", definition), ("destination", destination));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static async Task GrantStack(ActionRunner runner, string itemId, string name, string definition, string destination, int count)
    {
        var result = await Run(runner, "administratively grant physical item stack", JsonSerializer.Serialize(new { itemId, name, slot = "carried", count }),
            ("definition", definition), ("destination", destination));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static async Task CreateAmmunitionFixtureAsync(WorldStore world)
    {
        await world.CreateEntityAsync("Fixture arrow definition", "fixture.f23.s7.arrow.v1");
        await world.SetComponentAsync("fixture.f23.s7.arrow.v1", "dnd2024.item-definition", """{"definitionVersion":1,"kind":"ammunition","stackPolicy":"fungible","massPounds":{"numerator":1,"denominator":20},"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons"}}""");
    }

    private static async Task CreateFixtureStackAsync(WorldStore world, string id, string name, int count, string destination)
    {
        await world.CreateEntityAsync(name, id);
        await world.SetComponentAsync(id, "dnd2024.item-instance", """{"definitionId":"fixture.f23.s7.arrow.v1"}""");
        await world.SetComponentAsync(id, "dnd2024.item-quantity", JsonSerializer.Serialize(new { count, stackKey = "fixture.f23.s7.arrow.v1" }));
        await world.MoveAsync(id, destination, "inside");
    }

    private static async Task AssertStackAsync(WorldStore world, string id, int count, string destination)
    {
        var entity = await world.GetEntityAsync(id);
        Assert.NotNull(entity);
        Assert.Equal(destination, entity!.ContainerId);
        using var quantity = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == "dnd2024.item-quantity").Data);
        Assert.Equal(count, quantity.RootElement.GetProperty("count").GetInt32());
    }

    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world)
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
    }

    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) =>
        runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 7, RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal) });
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
