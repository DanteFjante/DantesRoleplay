using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Slice8Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-slice-8-catalog-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Directly_possessed_eligible_items_equip_read_unequip_and_then_transfer()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s8.ari");
        await world.CreateEntityAsync("Borin", "fixture.f23.s8.borin");
        var runner = Runner(db, world, mechanics);

        await GrantItem(runner, "fixture.f23.s8.dagger", "Dagger", "item.dnd2024.dagger.v1", "fixture.f23.s8.ari");
        var equipped = await Run(runner, "hold item", """{"state":"held"}""", ("item", "fixture.f23.s8.dagger"), ("holder", "fixture.f23.s8.ari"));
        Assert.True(equipped.Ok, equipped.Error?.Why);
        Assert.Equal("mechanic.dnd2024.item.equip", equipped.Mechanic!.Id);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(equipped.Output!.Effects).Type);
        await AssertStateAsync(world, "fixture.f23.s8.dagger", "held", "fixture.f23.s8.ari");

        var read = await Run(runner, "read equipped item", "{}", ("item", "fixture.f23.s8.dagger"));
        Assert.True(read.Ok, read.Error?.Why);
        Assert.Empty(read.Output!.Effects);
        using (var data = JsonDocument.Parse(read.Output.Data))
        {
            Assert.Equal("held", data.RootElement.GetProperty("state").GetString());
            Assert.Equal("item.dnd2024.dagger.v1", data.RootElement.GetProperty("definitionId").GetString());
            Assert.Equal("fixture.f23.s8.ari", data.RootElement.GetProperty("containerId").GetString());
        }

        var blockedTransfer = await Run(runner, "give item", """{"slot":"carried"}""",
            ("item", "fixture.f23.s8.dagger"), ("source", "fixture.f23.s8.ari"), ("destination", "fixture.f23.s8.borin"));
        Assert.False(blockedTransfer.Ok);
        await AssertStateAsync(world, "fixture.f23.s8.dagger", "held", "fixture.f23.s8.ari");

        var unequipped = await Run(runner, "unequip item", "{}", ("item", "fixture.f23.s8.dagger"), ("holder", "fixture.f23.s8.ari"));
        Assert.True(unequipped.Ok, unequipped.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(unequipped.Output!.Effects).Type);
        await AssertStateAsync(world, "fixture.f23.s8.dagger", "unequipped", "fixture.f23.s8.ari");

        var transferred = await Run(runner, "give item", """{"slot":"carried"}""",
            ("item", "fixture.f23.s8.dagger"), ("source", "fixture.f23.s8.ari"), ("destination", "fixture.f23.s8.borin"));
        Assert.True(transferred.Ok, transferred.Error?.Why);
        await AssertStateAsync(world, "fixture.f23.s8.dagger", "unequipped", "fixture.f23.s8.borin");

        await GrantItem(runner, "fixture.f23.s8.backpack", "Backpack", "item.dnd2024.backpack.v1", "fixture.f23.s8.ari");
        var worn = await Run(runner, "wear item", """{"state":"worn"}""", ("item", "fixture.f23.s8.backpack"), ("holder", "fixture.f23.s8.ari"));
        Assert.True(worn.Ok, worn.Error?.Why);
        await AssertStateAsync(world, "fixture.f23.s8.backpack", "worn", "fixture.f23.s8.ari");
    }

    [Fact]
    public async Task Equipment_refuses_inaccessible_ineligible_stacked_and_wrong_holder_items_without_mutation()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s8.ari");
        await world.CreateEntityAsync("Borin", "fixture.f23.s8.borin");
        var runner = Runner(db, world, mechanics);

        await GrantItem(runner, "fixture.f23.s8.dagger", "Dagger", "item.dnd2024.dagger.v1", "fixture.f23.s8.ari");
        var wrongMode = await Run(runner, "equip item", """{"state":"worn"}""", ("item", "fixture.f23.s8.dagger"), ("holder", "fixture.f23.s8.ari"));
        Assert.False(wrongMode.Ok);
        Assert.False(await HasStateAsync(world, "fixture.f23.s8.dagger"));

        var wrongHolder = await Run(runner, "equip item", """{"state":"held"}""", ("item", "fixture.f23.s8.dagger"), ("holder", "fixture.f23.s8.borin"));
        Assert.False(wrongHolder.Ok);
        Assert.False(await HasStateAsync(world, "fixture.f23.s8.dagger"));

        await GrantItem(runner, "fixture.f23.s8.pouch", "Pouch", "item.dnd2024.pouch.v1", "fixture.f23.s8.ari");
        await GrantItem(runner, "fixture.f23.s8.nested-dagger", "Nested dagger", "item.dnd2024.dagger.v1", "fixture.f23.s8.pouch");
        var nested = await Run(runner, "hold item", """{"state":"held"}""", ("item", "fixture.f23.s8.nested-dagger"), ("holder", "fixture.f23.s8.ari"));
        Assert.False(nested.Ok);
        Assert.False(await HasStateAsync(world, "fixture.f23.s8.nested-dagger"));

        await GrantStack(runner, "fixture.f23.s8.coins", "Copper", "currency.dnd2024.copper-piece.v1", "fixture.f23.s8.ari", 2);
        var stacked = await Run(runner, "equip item", """{"state":"held"}""", ("item", "fixture.f23.s8.coins"), ("holder", "fixture.f23.s8.ari"));
        Assert.False(stacked.Ok);
        Assert.False(await HasStateAsync(world, "fixture.f23.s8.coins"));

        var unequipWrongHolder = await Run(runner, "unequip item", "{}", ("item", "fixture.f23.s8.dagger"), ("holder", "fixture.f23.s8.borin"));
        Assert.False(unequipWrongHolder.Ok);
        Assert.False(await HasStateAsync(world, "fixture.f23.s8.dagger"));
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

    private static async Task AssertStateAsync(WorldStore world, string id, string state, string containerId)
    {
        var entity = await world.GetEntityAsync(id);
        Assert.NotNull(entity); Assert.Equal(containerId, entity!.ContainerId);
        using var data = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == "dnd2024.equipment-state").Data);
        Assert.Equal(state, data.RootElement.GetProperty("state").GetString());
    }

    private static async Task<bool> HasStateAsync(WorldStore world, string id) => (await world.GetEntityAsync(id))!.Components.Any(component => component.DefinitionId == "dnd2024.equipment-state");
    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world) { CopyDirectory(RepositoryCatalog(), _catalogCopy); var result = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions()); Assert.False(result.Aborted); }
    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 8, RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal) });
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
