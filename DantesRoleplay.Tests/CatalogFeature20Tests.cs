using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature20Tests : IDisposable
{
    private const string Speed = "dnd2024.speed";
    private const string Budget = "dnd2024.turn-budget";
    private const string Order = "dnd2024.encounter-initiative-order";
    private const string Hero = "creature.dnd2024.feature-10.hero";
    private const string Target = "creature.dnd2024.feature-10.training-target";
    private const string Encounter = "encounter.dnd2024.feature-10.training";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-20-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_records_and_corrects_closed_source_backed_Speed()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.speed"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.speed.write"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.speed.read"));
        const string subject = "fixture.catalog.f20.speed";
        await world.CreateEntityAsync("Speed subject", subject);
        var runner = Runner(db, world, mechanics);

        var recorded = await runner.RunAsync(Request("record creature speed", subject, SpeedInput("record", 25, 0, 0, 0, 0)));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(recorded.Output!.Effects).Type);
        AssertSpeed(Component(await world.GetEntityAsync(subject), Speed), 25, 0, 0, 0, 0);

        var corrected = await runner.RunAsync(Request("correct creature speed", subject, SpeedInput("correct", 35, 0, 5, 0, 15)));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(corrected.Output!.Effects).Type);
        AssertSpeed(Component(await world.GetEntityAsync(subject), Speed), 35, 0, 5, 0, 15);
        var before = Component(await world.GetEntityAsync(subject), Speed);

        foreach (var input in new[]
                 {
                     SpeedInput("record", 30, 0, 0, 0, 0),
                     SpeedInput("correct", 0, 0, 0, 0, 0),
                     SpeedInput("correct", 30, 0, 7, 0, 0),
                     """{"mode":"correct","walkFeet":30,"burrowFeet":0,"climbFeet":0,"flyFeet":0,"swimFeet":0,"sourceRef":{}}"""
                 })
        {
            var rejected = await runner.RunAsync(Request("correct creature speed", subject, input));
            Assert.False(rejected.Ok, input);
            Assert.Equal(before, Component(await world.GetEntityAsync(subject), Speed));
        }
    }

    [Fact]
    public async Task Turn_lifecycle_refreshes_remaining_movement_from_each_active_creature_walk_Speed()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        Assert.True((await runner.RunAsync(Request("correct creature speed", Hero, SpeedInput("correct", 25, 0, 0, 0, 0)))).Ok);
        Assert.True((await runner.RunAsync(Request("correct creature speed", Target, SpeedInput("correct", 35, 0, 0, 0, 0)))).Ok);
        Assert.True((await runner.RunAsync(Request("correct turn budget", Hero, BudgetInput("correct", false, false, false, false, 0)))).Ok);
        Assert.True((await runner.RunAsync(Request("correct turn budget", Target, BudgetInput("correct", false, false, false, false, 0)))).Ok);
        Assert.True((await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 20
        })).Ok);

        var started = await runner.RunAsync(TurnRequest("start encounter turns"));
        Assert.True(started.Ok, started.Error?.Why);
        var first = JsonDocument.Parse(started.Output!.Data).RootElement.GetProperty("activeParticipantId").GetString();
        Assert.NotNull(first);
        Assert.Equal(first == Hero ? 25 : 0, Remaining(await world.GetEntityAsync(Hero)));
        Assert.Equal(first == Target ? 35 : 0, Remaining(await world.GetEntityAsync(Target)));

        var advanced = await runner.RunAsync(TurnRequest("advance encounter turn"));
        Assert.True(advanced.Ok, advanced.Error?.Why);
        using var data = JsonDocument.Parse(advanced.Output!.Data);
        var next = data.RootElement.GetProperty("activeParticipantId").GetString();
        Assert.NotNull(next);
        Assert.NotEqual(first, next);
        Assert.Equal(next == Hero ? 25 : 35, data.RootElement.GetProperty("walkFeet").GetInt32());
        Assert.Equal(25, Remaining(await world.GetEntityAsync(Hero)));
        Assert.Equal(35, Remaining(await world.GetEntityAsync(Target)));
    }

    [Fact]
    public async Task Missing_or_corrupt_Speed_rejects_refresh_and_normal_movement_without_mutation()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        Assert.True((await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 20
        })).Ok);
        var active = JsonDocument.Parse(Component(await world.GetEntityAsync(Encounter), Order)).RootElement.GetProperty("order")[0].GetProperty("participantId").GetString();
        Assert.NotNull(active);
        Assert.True((await new EffectApplier(db, world).ApplyAsync([new Effect { Type = EffectType.ComponentRemove, EntityId = active!, DefinitionId = Speed }])).Applied);
        var heroBudget = Component(await world.GetEntityAsync(active), Budget);
        var start = await runner.RunAsync(TurnRequest("start encounter turns"));
        Assert.False(start.Ok);
        Assert.Equal(heroBudget, Component(await world.GetEntityAsync(active), Budget));
        Assert.DoesNotContain((await world.GetEntityAsync(Encounter))!.Components, component => component.DefinitionId == "dnd2024.encounter-turn-state");

        await world.SetComponentAsync(active, Speed, """{"walkFeet":30}""");
        await world.SetComponentAsync(Encounter, "dnd2024.encounter-turn-state", """{"status":"active","round":1,"turnIndex":0,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Combat > The Order of Combat"}}""");
        var move = await runner.RunAsync(new ActionRequest
        {
            Intent = "move 5 feet",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = active, ["encounter"] = Encounter },
            Input = """{"resource":"movement","feet":5}""",
            Seed = 20
        });
        Assert.False(move.Ok);
        Assert.Equal(heroBudget, Component(await world.GetEntityAsync(active), Budget));
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

    private static ActionRequest Request(string intent, string subject, string input) => new()
    {
        Intent = intent, Input = input, Seed = 20, RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject }
    };

    private static ActionRequest TurnRequest(string intent) => new()
    {
        Intent = intent, Input = "{}", Seed = 20, RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter }
    };

    private static string SpeedInput(string mode, int walk, int burrow, int climb, int fly, int swim) =>
        JsonSerializer.Serialize(new { mode, walkFeet = walk, burrowFeet = burrow, climbFeet = climb, flyFeet = fly, swimFeet = swim });

    private static string BudgetInput(string mode, bool action, bool bonusAction, bool reaction, bool freeInteraction, int remaining) =>
        JsonSerializer.Serialize(new { mode, action, bonusAction, reaction, freeInteraction, movementRemainingFeet = remaining });

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;

    private static int Remaining(EntitySnapshot? entity) =>
        JsonDocument.Parse(Component(entity, Budget)).RootElement.GetProperty("movementRemainingFeet").GetInt32();

    private static void AssertSpeed(string data, int walk, int burrow, int climb, int fly, int swim)
    {
        using var document = JsonDocument.Parse(data);
        var speed = document.RootElement;
        Assert.Equal(6, speed.EnumerateObject().Count());
        Assert.Equal(walk, speed.GetProperty("walkFeet").GetInt32());
        Assert.Equal(burrow, speed.GetProperty("burrowFeet").GetInt32());
        Assert.Equal(climb, speed.GetProperty("climbFeet").GetInt32());
        Assert.Equal(fly, speed.GetProperty("flyFeet").GetInt32());
        Assert.Equal(swim, speed.GetProperty("swimFeet").GetInt32());
        Assert.Equal("Rules Glossary > Speed", speed.GetProperty("sourceRef").GetProperty("locator").GetString());
    }

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
