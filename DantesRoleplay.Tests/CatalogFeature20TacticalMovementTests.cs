using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature20TacticalMovementTests : IDisposable
{
    private const string Hero = "creature.dnd2024.feature-10.hero";
    private const string Target = "creature.dnd2024.feature-10.training-target";
    private const string Encounter = "encounter.dnd2024.feature-10.training";
    private const string Position = "dnd2024.encounter-position";
    private const string Budget = "dnd2024.turn-budget";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-20-tactical-movement-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Tactical_movement_derives_cost_and_atomically_updates_position_and_budget()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        async Task<ActionRunResult> Run(string intent, Dictionary<string, string> roles, string input, long seed = 20) =>
            await runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = roles, Input = input, Seed = seed });

        Assert.True((await Run("record creature size", new() { ["creature"] = Hero }, "{\"size\":\"medium\"}")).Ok);
        Assert.True((await Run("record creature size", new() { ["creature"] = Target }, "{\"size\":\"medium\"}")).Ok);
        Assert.True((await Run("record encounter space", new() { ["encounter"] = Encounter }, "{\"mode\":\"record\",\"widthSquares\":10,\"heightSquares\":4,\"blockedCells\":[],\"difficultCells\":[]}")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"mode\":\"record\",\"anchorX\":0,\"anchorY\":0}")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Target, ["encounter"] = Encounter }, "{\"mode\":\"record\",\"anchorX\":16,\"anchorY\":0}")).Ok);
        Assert.True((await Run("set the encounter initiative order", new() { ["encounter"] = Encounter }, JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }))).Ok);
        var started = await Run("start encounter turns", new() { ["encounter"] = Encounter }, "{}");
        Assert.True(started.Ok, started.Error?.Why);
        using (var startedData = JsonDocument.Parse(started.Output!.Data))
        {
            if (startedData.RootElement.GetProperty("activeParticipantId").GetString() != Hero)
                Assert.True((await Run("advance encounter turn", new() { ["encounter"] = Encounter }, "{}")).Ok);
        }

        var beforeOnePosition = Component(await world.GetEntityAsync(Hero), Position);
        var beforeOneBudget = Component(await world.GetEntityAsync(Hero), Budget);
        var one = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[{\"dx\":1,\"dy\":0}]}", 91);
        Assert.True(one.Ok, one.Error?.Why);
        Assert.Equal("mechanic.dnd2024.tactical-move.execute", one.Mechanic?.Id);
        Assert.Equal(2, Anchor(await world.GetEntityAsync(Hero), "anchorX"));
        Assert.Equal(25, Remaining(await world.GetEntityAsync(Hero)));
        Assert.Equal(2, one.Output!.Effects.Count);
        using (var oneData = JsonDocument.Parse(one.Output.Data))
        {
            Assert.Equal("tactical-move", oneData.RootElement.GetProperty("test").GetString());
            Assert.Equal(5, oneData.RootElement.GetProperty("feet").GetInt32());
            Assert.Equal("mechanic.dnd2024.turn-budget.spend", oneData.RootElement.GetProperty("budgetChild").GetProperty("mechanicId").GetString());
        }
        await world.SetComponentAsync(Hero, Position, beforeOnePosition);
        await world.SetComponentAsync(Hero, Budget, beforeOneBudget);
        var replay = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[{\"dx\":1,\"dy\":0}]}", 91);
        Assert.True(replay.Ok, replay.Error?.Why);
        Assert.Equal(one.Output.Data, replay.Output!.Data);

        const string twoEast = "{\"path\":[{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0}]}";
        var multiple = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, twoEast, 92);
        Assert.True(multiple.Ok, multiple.Error?.Why);
        Assert.Equal(6, Anchor(await world.GetEntityAsync(Hero), "anchorX"));
        Assert.Equal(15, Remaining(await world.GetEntityAsync(Hero)));
        var exact = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0}]}", 93);
        Assert.True(exact.Ok, exact.Error?.Why);
        Assert.Equal(12, Anchor(await world.GetEntityAsync(Hero), "anchorX"));
        Assert.Equal(0, Remaining(await world.GetEntityAsync(Hero)));
        var replayState = Component(await world.GetEntityAsync(Hero), Position);
        var replayBudget = Component(await world.GetEntityAsync(Hero), Budget);
        var insufficient = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[{\"dx\":1,\"dy\":0}]}", 94);
        Assert.False(insufficient.Ok);
        Assert.Equal(replayState, Component(await world.GetEntityAsync(Hero), Position));
        Assert.Equal(replayBudget, Component(await world.GetEntityAsync(Hero), Budget));

        var empty = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[]}");
        Assert.False(empty.Ok);
        Assert.Equal(replayState, Component(await world.GetEntityAsync(Hero), Position));
        Assert.Equal(replayBudget, Component(await world.GetEntityAsync(Hero), Budget));
    }

    [Fact]
    public async Task Tactical_movement_rejects_bounds_blocked_corner_and_occupied_paths_unchanged()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        async Task<ActionRunResult> Run(string intent, Dictionary<string, string> roles, string input) =>
            await runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = roles, Input = input, Seed = 20 });

        Assert.True((await Run("record creature size", new() { ["creature"] = Hero }, "{\"size\":\"medium\"}")).Ok);
        Assert.True((await Run("record creature size", new() { ["creature"] = Target }, "{\"size\":\"medium\"}")).Ok);
        Assert.True((await Run("record encounter space", new() { ["encounter"] = Encounter }, "{\"mode\":\"record\",\"widthSquares\":10,\"heightSquares\":4,\"blockedCells\":[],\"difficultCells\":[]}")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"mode\":\"record\",\"anchorX\":0,\"anchorY\":0}")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Target, ["encounter"] = Encounter }, "{\"mode\":\"record\",\"anchorX\":16,\"anchorY\":0}")).Ok);
        Assert.True((await Run("set the encounter initiative order", new() { ["encounter"] = Encounter }, JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }))).Ok);
        var started = await Run("start encounter turns", new() { ["encounter"] = Encounter }, "{}");
        using (var startedData = JsonDocument.Parse(started.Output!.Data))
        {
            if (startedData.RootElement.GetProperty("activeParticipantId").GetString() != Hero)
                Assert.True((await Run("advance encounter turn", new() { ["encounter"] = Encounter }, "{}")).Ok);
        }
        var position = Component(await world.GetEntityAsync(Hero), Position);
        var budget = Component(await world.GetEntityAsync(Hero), Budget);
        foreach (var input in new[]
                 {
                     "{\"path\":[{\"dx\":-1,\"dy\":0}]}",
                     "{\"path\":[{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0}]}",
                     "{\"path\":[{\"dx\":0,\"dy\":0}]}",
                     "{\"path\":[{\"dx\":1,\"dy\":0}],\"feet\":5}"
                 })
        {
            Assert.False((await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, input)).Ok, input);
            Assert.Equal(position, Component(await world.GetEntityAsync(Hero), Position));
            Assert.Equal(budget, Component(await world.GetEntityAsync(Hero), Budget));
        }

        var occupied = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0},{\"dx\":1,\"dy\":0}]}");
        Assert.False(occupied.Ok);
        Assert.Equal(position, Component(await world.GetEntityAsync(Hero), Position));

        Assert.True((await Run("correct encounter space", new() { ["encounter"] = Encounter }, "{\"mode\":\"correct\",\"widthSquares\":10,\"heightSquares\":4,\"blockedCells\":[{\"x\":1,\"y\":0},{\"x\":0,\"y\":1}],\"difficultCells\":[]}")).Ok);
        var corner = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[{\"dx\":1,\"dy\":1}]}");
        Assert.False(corner.Ok);
        Assert.Equal(position, Component(await world.GetEntityAsync(Hero), Position));
        Assert.Equal(budget, Component(await world.GetEntityAsync(Hero), Budget));

        Assert.True((await Run("advance encounter turn", new() { ["encounter"] = Encounter }, "{}")).Ok);
        var offTurn = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, "{\"path\":[{\"dx\":1,\"dy\":0}]}");
        Assert.False(offTurn.Ok);
        Assert.Equal(position, Component(await world.GetEntityAsync(Hero), Position));
        Assert.Equal(budget, Component(await world.GetEntityAsync(Hero), Budget));
    }

    [Fact]
    public async Task Tactical_movement_charges_difficult_terrain_and_admits_only_documented_passage()
    {
        var (world, mechanics, db) = await ImportAsync();
        await using var _ = db;
        var runner = Runner(db, world, mechanics);
        async Task<ActionRunResult> Run(string intent, Dictionary<string, string> roles, string input) =>
            await runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = roles, Input = input, Seed = 42 });

        Assert.True((await Run("record creature size", new() { ["creature"] = Hero }, """{"size":"medium"}""")).Ok);
        Assert.True((await Run("record creature size", new() { ["creature"] = Target }, """{"size":"medium"}""")).Ok);
        Assert.True((await Run("record encounter space", new() { ["encounter"] = Encounter }, """{"mode":"record","widthSquares":10,"heightSquares":4,"blockedCells":[],"difficultCells":[{"x":1,"y":0}]}""")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Hero, ["encounter"] = Encounter }, """{"mode":"record","anchorX":0,"anchorY":0}""")).Ok);
        Assert.True((await Run("place encounter participant", new() { ["subject"] = Target, ["encounter"] = Encounter }, """{"mode":"record","anchorX":16,"anchorY":0}""")).Ok);
        Assert.True((await Run("set the encounter initiative order", new() { ["encounter"] = Encounter }, JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }))).Ok);
        var started = await Run("start encounter turns", new() { ["encounter"] = Encounter }, "{}");
        using (var data = JsonDocument.Parse(started.Output!.Data))
        {
            if (data.RootElement.GetProperty("activeParticipantId").GetString() != Hero)
                Assert.True((await Run("advance encounter turn", new() { ["encounter"] = Encounter }, "{}")).Ok);
        }

        var difficult = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, """{"path":[{"dx":1,"dy":0}]}""");
        Assert.True(difficult.Ok, difficult.Error?.Why);
        using (var data = JsonDocument.Parse(difficult.Output!.Data))
        {
            Assert.Equal(10, data.RootElement.GetProperty("feet").GetInt32());
            Assert.Equal(10, Assert.Single(data.RootElement.GetProperty("stepCostsFeet").EnumerateArray()).GetInt32());
        }
        Assert.Equal(20, Remaining(await world.GetEntityAsync(Hero)));

        const string startPosition = """{"encounterId":"encounter.dnd2024.feature-10.training","anchorX":0,"anchorY":0,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Playing on a Grid > Creature Size"}}""";
        const string startBudget = """{"action":true,"bonusAction":true,"reaction":true,"freeInteraction":true,"movementRemainingFeet":30,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn"}}""";
        const string targetAtFirstStep = """{"encounterId":"encounter.dnd2024.feature-10.training","anchorX":2,"anchorY":0,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Playing on a Grid > Creature Size"}}""";
        await world.SetComponentAsync(Hero, Position, startPosition);
        await world.SetComponentAsync(Hero, Budget, startBudget);
        await world.SetComponentAsync(Target, Position, targetAtFirstStep);
        Assert.True((await Run("correct encounter space", new() { ["encounter"] = Encounter }, """{"mode":"correct","widthSquares":10,"heightSquares":4,"blockedCells":[],"difficultCells":[]}""")).Ok);
        Assert.True((await Run("correct encounter sides", new() { ["encounter"] = Encounter }, """{"mode":"correct","assignments":[{"participantId":"creature.dnd2024.feature-10.hero","sideId":"side.party"},{"participantId":"creature.dnd2024.feature-10.training-target","sideId":"side.party"}],"hostilePairs":[]}""")).Ok);
        var ally = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, """{"path":[{"dx":1,"dy":0},{"dx":1,"dy":0}]}""");
        Assert.True(ally.Ok, ally.Error?.Why);
        Assert.Equal(20, Remaining(await world.GetEntityAsync(Hero)));
        using (var data = JsonDocument.Parse(ally.Output!.Data))
            Assert.All(data.RootElement.GetProperty("stepCostsFeet").EnumerateArray(), cost => Assert.Equal(5, cost.GetInt32()));

        await world.SetComponentAsync(Hero, Position, startPosition);
        await world.SetComponentAsync(Hero, Budget, startBudget);
        Assert.True((await Run("correct encounter sides", new() { ["encounter"] = Encounter }, """{"mode":"correct","assignments":[{"participantId":"creature.dnd2024.feature-10.hero","sideId":"side.party"},{"participantId":"creature.dnd2024.feature-10.training-target","sideId":"side.training-opposition"}],"hostilePairs":[{"firstSideId":"side.party","secondSideId":"side.training-opposition"}]}""")).Ok);
        var enemy = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, """{"path":[{"dx":1,"dy":0},{"dx":1,"dy":0}]}""");
        Assert.False(enemy.Ok);
        Assert.Equal(0, Anchor(await world.GetEntityAsync(Hero), "anchorX"));
        Assert.Equal(30, Remaining(await world.GetEntityAsync(Hero)));

        await world.SetComponentAsync(Target, "dnd2024.creature-size", """{"size":"tiny"}""");
        var tiny = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, """{"path":[{"dx":1,"dy":0},{"dx":1,"dy":0}]}""");
        Assert.True(tiny.Ok, tiny.Error?.Why);
        using (var data = JsonDocument.Parse(tiny.Output!.Data))
            Assert.Equal(new[] { 5, 5 }, data.RootElement.GetProperty("stepCostsFeet").EnumerateArray().Select(item => item.GetInt32()));

        await world.SetComponentAsync(Hero, Position, startPosition);
        await world.SetComponentAsync(Hero, Budget, startBudget);
        await world.SetComponentAsync(Hero, "dnd2024.creature-size", """{"size":"tiny"}""");
        await world.SetComponentAsync(Target, "dnd2024.creature-size", """{"size":"large"}""");
        var sizeDifference = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, """{"path":[{"dx":1,"dy":0},{"dx":1,"dy":0},{"dx":1,"dy":0}]}""");
        Assert.True(sizeDifference.Ok, sizeDifference.Error?.Why);
        using (var data = JsonDocument.Parse(sizeDifference.Output!.Data))
            Assert.Equal(new[] { 10, 10, 5 }, data.RootElement.GetProperty("stepCostsFeet").EnumerateArray().Select(item => item.GetInt32()));

        await world.SetComponentAsync(Hero, Position, startPosition);
        await world.SetComponentAsync(Hero, Budget, startBudget);
        await world.SetComponentAsync(Hero, "dnd2024.creature-size", """{"size":"medium"}""");
        await world.SetComponentAsync(Target, "dnd2024.creature-size", """{"size":"medium"}""");
        Assert.True((await Run("record creature conditions", new() { ["subject"] = Target }, """{"mode":"record"}""")).Ok);
        Assert.True((await Run("apply the incapacitated condition", new() { ["subject"] = Target }, """{"mode":"apply","conditions":["incapacitated"]}""")).Ok);
        var incapacitated = await Run("move tactically", new() { ["subject"] = Hero, ["encounter"] = Encounter }, """{"path":[{"dx":1,"dy":0},{"dx":1,"dy":0}]}""");
        Assert.True(incapacitated.Ok, incapacitated.Error?.Why);
        Assert.Equal(15, Remaining(await world.GetEntityAsync(Hero)));
        using (var data = JsonDocument.Parse(incapacitated.Output!.Data))
            Assert.Equal(new[] { 10, 5 }, data.RootElement.GetProperty("stepCostsFeet").EnumerateArray().Select(item => item.GetInt32()));
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

    private static int Anchor(EntitySnapshot? entity, string field) =>
        JsonDocument.Parse(Component(entity, Position)).RootElement.GetProperty(field).GetInt32();

    private static int Remaining(EntitySnapshot? entity) =>
        JsonDocument.Parse(Component(entity, Budget)).RootElement.GetProperty("movementRemainingFeet").GetInt32();

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
