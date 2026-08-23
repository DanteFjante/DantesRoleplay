using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature20TacticalMeleeTests : IDisposable
{
    private const string Hero = "creature.dnd2024.feature-10.hero";
    private const string Target = "creature.dnd2024.feature-10.training-target";
    private const string Encounter = "encounter.dnd2024.feature-10.training";
    private const string Dagger = "weapon.dnd2024.dagger";
    private const string Shortbow = "weapon.dnd2024.shortbow";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-20-tactical-melee-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Tactical_melee_requires_admission_before_composing_the_existing_weapon_attack()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        async Task<ActionRunResult> Run(string intent, Dictionary<string, string> roles, string input, long seed = 20) =>
            await runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = roles, Input = input, Seed = seed });

        Assert.True((await Run("record creature size", new() { ["creature"] = Hero }, "{\"size\":\"medium\"}")).Ok);
        Assert.True((await Run("record creature size", new() { ["creature"] = Target }, "{\"size\":\"medium\"}")).Ok);
        Assert.True((await Run("record encounter space", new() { ["encounter"] = Encounter }, "{\"mode\":\"record\",\"widthSquares\":6,\"heightSquares\":4,\"blockedCells\":[],\"difficultCells\":[]}")).Ok);
        Assert.True((await Run("record base melee reach", new() { ["subject"] = Hero }, "{\"mode\":\"record\",\"feet\":5}")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"mode\":\"record\",\"anchorX\":0,\"anchorY\":0}")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Target, ["encounter"] = Encounter }, "{\"mode\":\"record\",\"anchorX\":4,\"anchorY\":0}")).Ok);

        const string tacticalInput = "{\"kind\":\"melee\",\"attack\":{\"ability\":\"dex\"}}";
        var legal = await Run("make tactical melee attack", new() { ["attacker"] = Hero, ["target"] = Target, ["weapon"] = Dagger, ["encounter"] = Encounter }, tacticalInput, 77);
        Assert.True(legal.Ok, legal.Error?.Why);
        Assert.Equal("mechanic.dnd2024.tactical-melee.attack", legal.Mechanic?.Id);
        Assert.Empty(legal.Output!.Effects);
        using (var data = JsonDocument.Parse(legal.Output.Data))
        {
            Assert.Equal("tactical-melee-attack", data.RootElement.GetProperty("test").GetString());
            Assert.Equal("mechanic.dnd2024.tactical-melee.admit", data.RootElement.GetProperty("admissionChild").GetProperty("mechanicId").GetString());
            Assert.Equal("mechanic.dnd2024.weapon-attack", data.RootElement.GetProperty("attackChild").GetProperty("mechanicId").GetString());
            Assert.Equal("weapon-attack", data.RootElement.GetProperty("attack").GetProperty("test").GetString());
        }
        var replay = await Run("make tactical melee attack", new() { ["attacker"] = Hero, ["target"] = Target, ["weapon"] = Dagger, ["encounter"] = Encounter }, tacticalInput, 77);
        Assert.True(replay.Ok, replay.Error?.Why);
        Assert.Equal(legal.Output.Data, replay.Output!.Data);

        var before = Component(await world.GetEntityAsync(Target), "dnd2024.encounter-position");
        Assert.True((await Run("correct encounter participant position", new() { ["subject"] = Target, ["encounter"] = Encounter }, "{\"mode\":\"correct\",\"anchorX\":6,\"anchorY\":0}")).Ok);
        var afterMove = Component(await world.GetEntityAsync(Target), "dnd2024.encounter-position");
        var outOfReach = await Run("make tactical melee attack", new() { ["attacker"] = Hero, ["target"] = Target, ["weapon"] = Dagger, ["encounter"] = Encounter }, tacticalInput);
        Assert.False(outOfReach.Ok);
        Assert.False(outOfReach.Output!.HasData);
        Assert.Empty(outOfReach.Output.Effects);
        Assert.NotEqual(before, afterMove);
        Assert.Equal(afterMove, Component(await world.GetEntityAsync(Target), "dnd2024.encounter-position"));

        Assert.True((await Run("correct encounter participant position", new() { ["subject"] = Target, ["encounter"] = Encounter }, "{\"mode\":\"correct\",\"anchorX\":4,\"anchorY\":0}")).Ok);
        var ranged = await Run("make tactical melee attack", new() { ["attacker"] = Hero, ["target"] = Target, ["weapon"] = Shortbow, ["encounter"] = Encounter }, tacticalInput);
        Assert.False(ranged.Ok);
        Assert.False(ranged.Output!.HasData);
        Assert.Empty(ranged.Output.Effects);
        var wrongKind = await Run("make tactical melee attack", new() { ["attacker"] = Hero, ["target"] = Target, ["weapon"] = Dagger, ["encounter"] = Encounter }, "{\"kind\":\"ranged\",\"attack\":{\"ability\":\"dex\"}}");
        Assert.False(wrongKind.Ok);
        Assert.False(wrongKind.Output!.HasData);
        Assert.Empty(wrongKind.Output.Effects);

        var direct = await Run("attack with weapon", new() { ["subject"] = Hero, ["target"] = Target, ["weapon"] = Dagger }, "{\"ability\":\"dex\"}", 77);
        Assert.True(direct.Ok, direct.Error?.Why);
        Assert.Equal("mechanic.dnd2024.weapon-attack", direct.Mechanic?.Id);
    }

    private async Task<(WorldStore World, MechanicStore Mechanics, DantesRoleplayDbContext Db)> ImportAsync()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        return (world, mechanics, db);
    }

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
        }
        throw new DirectoryNotFoundException();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }
}
