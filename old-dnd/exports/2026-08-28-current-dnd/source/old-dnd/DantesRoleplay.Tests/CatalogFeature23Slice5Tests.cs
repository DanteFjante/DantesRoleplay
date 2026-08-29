using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Slice5Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-slice-5-catalog-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Burden_is_exact_for_nested_physical_items_and_refuses_non_item_contents()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        await world.CreateEntityAsync("Ari", "fixture.f23.ari");
        var runner = Runner(db, world, mechanics);
        Assert.True((await Run(runner, "administratively grant physical item", """{"itemId":"fixture.f23.pack","name":"Pack","slot":"carried"}""", ("definition", "item.dnd2024.backpack.v1"), ("destination", "fixture.f23.ari"))).Ok);
        Assert.True((await Run(runner, "administratively grant physical item", """{"itemId":"fixture.f23.pouch","name":"Pouch","slot":"inside"}""", ("definition", "item.dnd2024.pouch.v1"), ("destination", "fixture.f23.pack"))).Ok);
        Assert.True((await Run(runner, "administratively grant physical item stack", """{"itemId":"fixture.f23.coins","name":"Copper","slot":"inside","count":50}""", ("definition", "currency.dnd2024.copper-piece.v1"), ("destination", "fixture.f23.pouch"))).Ok);
        Assert.True((await Run(runner, "administratively grant physical item", """{"itemId":"fixture.f23.rope","name":"Rope","slot":"carried"}""", ("definition", "item.dnd2024.hempen-rope-50-foot.v1"), ("destination", "fixture.f23.ari"))).Ok);
        var transferred = await Run(runner, "transfer physical item", """{"slot":"inside"}""", ("item", "fixture.f23.rope"), ("source", "fixture.f23.ari"), ("destination", "fixture.f23.pouch"));
        Assert.True(transferred.Ok, transferred.Error?.Why);
        Assert.Equal(EffectType.ContainmentMove, Assert.Single(transferred.Output!.Effects).Type);
        Assert.Equal("fixture.f23.pouch", (await world.GetEntityAsync("fixture.f23.rope"))!.ContainerId);
        Assert.True((await Run(runner, "administratively grant physical item", """{"itemId":"fixture.f23.dagger","name":"Dagger","slot":"carried"}""", ("definition", "item.dnd2024.dagger.v1"), ("destination", "fixture.f23.ari"))).Ok);
        var rejectedTransfer = await Run(runner, "transfer physical item", """{"slot":"inside"}""", ("item", "fixture.f23.dagger"), ("source", "fixture.f23.ari"), ("destination", "fixture.f23.pouch"));
        Assert.False(rejectedTransfer.Ok);
        Assert.Equal("fixture.f23.ari", (await world.GetEntityAsync("fixture.f23.dagger"))!.ContainerId);

        var read = await Run(runner, "derive nested physical mass", "{}", ("root", "fixture.f23.ari"));
        Assert.True(read.Ok, read.Error?.Why);
        Assert.Empty(read.Output!.Effects);
        using (var data = JsonDocument.Parse(read.Output.Data))
        {
            var mass = data.RootElement.GetProperty("massPounds");
            Assert.Equal(13, mass.GetProperty("numerator").GetInt32());
            Assert.Equal(1, mass.GetProperty("denominator").GetInt32());
            Assert.Equal(5, data.RootElement.GetProperty("items").GetArrayLength());
        }

        await world.CreateEntityAsync("Unmeasured crate", "fixture.f23.unknown");
        await world.MoveAsync("fixture.f23.unknown", "fixture.f23.pack", "inside");
        var refused = await Run(runner, "derive nested physical mass", "{}", ("root", "fixture.f23.ari"));
        Assert.False(refused.Ok);
    }

    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 5, RoleEntityIds = roles.ToDictionary(x => x.role, x => x.id, StringComparer.Ordinal) });
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
