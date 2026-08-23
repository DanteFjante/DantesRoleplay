using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature14Tests : IDisposable
{
    private const string Conditions = "dnd2024.conditions";
    private const string Budget = "dnd2024.turn-budget";
    private const string Encounter = "encounter.dnd2024.feature-10.training";
    private const string Hero = "creature.dnd2024.feature-10.hero";
    private const string Target = "creature.dnd2024.feature-10.training-target";
    private const string Dagger = "weapon.dnd2024.dagger";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-14-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Exhaustion_is_leveled_recoverable_and_announces_only_the_lethal_threshold()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(
            db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        const string subject = "fixture.catalog.f14.subject";
        const string source = "fixture.catalog.f14.source";
        await world.CreateEntityAsync("Exhausted creature", subject);
        await world.CreateEntityAsync("Unrelated source", source);
        var runner = CreateRunner(db, world, mechanics);

        Assert.True((await RunAsync(runner, "record creature conditions", subject, """{"mode":"record"}""")).Ok);
        for (var expected = 1; expected <= 6; expected++)
        {
            var result = await RunAsync(runner, "exhaust the character", subject, """{"mode":"exhaust","levels":1}""");
            Assert.True(result.Ok, result.Error?.Why);
            using var data = JsonDocument.Parse(result.Output!.Data);
            Assert.Equal(expected - 1, data.RootElement.GetProperty("previousLevel").GetInt32());
            Assert.Equal(expected, data.RootElement.GetProperty("newLevel").GetInt32());
            Assert.Equal(expected == 6, data.RootElement.GetProperty("lethalEventDeclared").GetBoolean());
            Assert.Equal(expected, ExhaustionLevel(await world.GetEntityAsync(subject)));
        }

        var lethal = Assert.Single(await db.Events.Where(e => e.TypeId == "dnd2024.exhaustion.reached-lethal").ToListAsync());
        using (var payload = JsonDocument.Parse(lethal.PayloadJson))
        {
            Assert.Equal(subject, payload.RootElement.GetProperty("creatureId").GetString());
            Assert.Equal(6, payload.RootElement.GetProperty("level").GetInt32());
            Assert.Equal("Rules Glossary > Exhaustion", payload.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());
        }

        var compatibility = await RunAsync(runner, "inspect condition-derived d20 effects", subject, "{}");
        Assert.True(compatibility.Ok, compatibility.Error?.Why);
        using (var data = JsonDocument.Parse(compatibility.Output!.Data))
        {
            Assert.Equal(6, data.RootElement.GetProperty("exhaustionLevel").GetInt32());
            var modifier = Assert.Single(data.RootElement.GetProperty("derivedModifiers").EnumerateArray());
            Assert.Equal("condition:exhaustion (level 6)", modifier.GetProperty("source").GetString());
            Assert.Equal(-12, modifier.GetProperty("value").GetInt32());
        }

        var recovered = await RunAsync(runner, "recover a level of exhaustion", subject, """{"mode":"recover","levels":6}""");
        Assert.True(recovered.Ok, recovered.Error?.Why);
        Assert.Null(ExhaustionLevel(await world.GetEntityAsync(subject)));
        Assert.Single(await db.Events.Where(e => e.TypeId == "dnd2024.exhaustion.reached-lethal").ToListAsync());
        var notExhausted = await RunAsync(runner, "recover a level of exhaustion", subject, """{"mode":"recover","levels":1}""");
        Assert.False(notExhausted.Ok);
        Assert.Contains("not exhausted", notExhausted.Error?.Why, StringComparison.Ordinal);

        Assert.True((await RunAsync(runner, "exhaust the character", subject, """{"mode":"exhaust","levels":5}""")).Ok);
        Assert.True((await RunAsync(runner, "exhaust the character", subject, """{"mode":"exhaust","levels":1}""")).Ok);
        Assert.Equal(2, await db.Events.CountAsync(e => e.TypeId == "dnd2024.exhaustion.reached-lethal"));

        var before = ConditionData(await world.GetEntityAsync(subject));
        foreach (var input in new[]
                 {
                     """{"mode":"exhaust","levels":1}""", """{"mode":"recover","levels":7}""",
                     """{"mode":"recover","levels":1.5}""", """{"mode":"exhaust","levels":0}""",
                     """{"mode":"apply","conditions":["exhaustion"]}"""
                 })
        {
            var rejected = await RunAsync(runner, "exhaust the character", subject, input);
            Assert.False(rejected.Ok, input);
            Assert.Equal(before, ConditionData(await world.GetEntityAsync(subject)));
        }

        var sourced = await RunAsync(runner, "exhaust the character", subject, """{"mode":"exhaust","levels":1}""", source);
        Assert.False(sourced.Ok);
        Assert.Equal(before, ConditionData(await world.GetEntityAsync(subject)));
    }

    [Fact]
    public async Task Exhaustion_rejects_missing_or_corrupt_condition_state_without_effects()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);

        const string subject = "fixture.catalog.f14.corrupt";
        await world.CreateEntityAsync("Corrupt creature", subject);
        var runner = CreateRunner(db, world, mechanics);

        Assert.False((await RunAsync(runner, "exhaust the character", subject, """{"mode":"exhaust","levels":1}""")).Ok);
        Assert.False((await RunAsync(runner, "recover a level of exhaustion", subject, """{"mode":"recover","levels":1}""")).Ok);
        Assert.True((await RunAsync(runner, "record creature conditions", subject, """{"mode":"record"}""")).Ok);
        await world.SetComponentAsync(subject, Conditions, """{"entries":[{"condition":"exhaustion","level":0}],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary"}}""");
        var corrupt = ConditionData(await world.GetEntityAsync(subject));

        foreach (var intent in new[] { "exhaust the character", "recover a level of exhaustion" })
        {
            var input = intent.StartsWith("recover", StringComparison.Ordinal)
                ? """{"mode":"recover","levels":1}"""
                : """{"mode":"exhaust","levels":1}""";
            var rejected = await RunAsync(runner, intent, subject, input);
            Assert.False(rejected.Ok);
            Assert.Equal(corrupt, ConditionData(await world.GetEntityAsync(subject)));
            Assert.Empty(rejected.Output?.Effects ?? []);
        }
    }

    [Fact]
    public async Task Exhaustion_preserves_the_existing_source_instance_capacity()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);

        const string subject = "fixture.catalog.f14.capacity";
        await world.CreateEntityAsync("Capacity creature", subject);
        var entries = Enumerable.Range(0, 100)
            .Select(index => new { condition = "blinded", sourceEntityId = $"fixture.source.{index:D3}" })
            .ToArray();
        var state = JsonSerializer.Serialize(new
        {
            entries,
            sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Rules Glossary" }
        });
        await world.SetComponentAsync(subject, Conditions, state);
        var runner = CreateRunner(db, world, mechanics);

        var rejected = await RunAsync(runner, "exhaust the character", subject, """{"mode":"exhaust","levels":1}""");
        Assert.False(rejected.Ok);
        Assert.Contains("more than 100", rejected.Error?.Why, StringComparison.Ordinal);
        Assert.Equal(state, ConditionData(await world.GetEntityAsync(subject)));
        Assert.Empty(await db.Events.Where(e => e.TypeId == "dnd2024.exhaustion.reached-lethal").ToListAsync());
    }

    [Fact]
    public async Task Exhaustion_applies_one_flat_penalty_to_each_d20_test_owner()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var abilityBefore = await D20Async(runner, "perception check", """{"ability":"wis","skill":"perception","dc":99}""", 141, ("subject", Hero));
        var saveBefore = await D20Async(runner, "make a saving throw", """{"ability":"wis","dc":99}""", 142, ("subject", Hero));
        var attackBefore = await D20Async(runner, "attack target with dagger", """{"ability":"dex"}""", 143, ("subject", Hero), ("target", Target), ("weapon", Dagger));
        var initiativeBefore = await D20Async(runner, "roll initiative", "{}", 144, ("subject", Hero));
        Assert.True((await RunAsync(runner, "record creature conditions", Hero, """{"mode":"record"}""")).Ok);
        Assert.True((await RunAsync(runner, "exhaust the character", Hero, """{"mode":"exhaust","levels":3}""")).Ok);
        Assert.True((await RunAsync(runner, "record creature conditions", Target, """{"mode":"record"}""")).Ok);
        Assert.True((await RunAsync(runner, "exhaust the character", Target, """{"mode":"exhaust","levels":6}""")).Ok);

        var abilityAfter = await D20Async(runner, "perception check", """{"ability":"wis","skill":"perception","dc":99}""", 141, ("subject", Hero));
        var saveAfter = await D20Async(runner, "make a saving throw", """{"ability":"wis","dc":99}""", 142, ("subject", Hero));
        var attackAfter = await D20Async(runner, "attack target with dagger", """{"ability":"dex"}""", 143, ("subject", Hero), ("target", Target), ("weapon", Dagger));
        var initiativeAfter = await D20Async(runner, "roll initiative", "{}", 144, ("subject", Hero));

        AssertPenalty(abilityBefore, abilityAfter, "total", -6);
        AssertPenalty(saveBefore, saveAfter, "total", -6);
        AssertPenalty(attackBefore, attackAfter, "total", -6);
        AssertPenalty(initiativeBefore, initiativeAfter, "initiative", -6);

        Assert.True((await RunAsync(runner, "apply the paralyzed condition", Hero, """{"mode":"apply","conditions":["paralyzed"]}""")).Ok);
        var automatic = await D20Async(runner, "make a saving throw", """{"ability":"str","dc":0}""", 145, ("subject", Hero));
        Assert.True(automatic.Ok, automatic.Error?.Why);
        using (var automaticData = JsonDocument.Parse(automatic.Output!.Data))
        {
            Assert.Equal("automatic-failure", automaticData.RootElement.GetProperty("resolution").GetString());
            Assert.Equal(JsonValueKind.Null, automaticData.RootElement.GetProperty("total").ValueKind);
            Assert.DoesNotContain(automaticData.RootElement.GetProperty("modifiers").EnumerateArray(),
                item => item.GetProperty("source").GetString() == "condition:exhaustion (level 3)");
        }

        ActionRunResult? naturalTwenty = null;
        for (var seed = 1; seed <= 200 && naturalTwenty is null; seed++)
        {
            var candidate = await D20Async(runner, "attack target with dagger", """{"ability":"dex"}""", seed,
                ("subject", Hero), ("target", Target), ("weapon", Dagger));
            using var candidateData = JsonDocument.Parse(candidate.Output!.Data);
            if (candidateData.RootElement.GetProperty("roll").GetInt32() == 20) naturalTwenty = candidate;
        }
        Assert.NotNull(naturalTwenty);
        using (var naturalData = JsonDocument.Parse(naturalTwenty!.Output!.Data))
        {
            Assert.True(naturalData.RootElement.GetProperty("hit").GetBoolean());
            Assert.True(naturalData.RootElement.GetProperty("critical").GetBoolean());
            Assert.Equal("natural-20", naturalData.RootElement.GetProperty("hitReason").GetString());
        }
    }

    [Fact]
    public async Task Exhaustion_reduces_only_the_newly_restored_turn_budget_movement()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var initiative = await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 100
        });
        Assert.True(initiative.Ok, initiative.Error?.Why);
        Assert.True((await RunAsync(runner, "record creature conditions", Hero, """{"mode":"record"}""")).Ok);
        Assert.True((await RunAsync(runner, "exhaust the character", Hero, """{"mode":"exhaust","levels":1}""")).Ok);

        // A corrupt non-active condition record is reported but does not prevent Hero's first turn.
        await world.SetComponentAsync(Target, Conditions, """{"entries":[],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary"}}""");
        await SetCorruptConditionAsync(db, Target);
        var started = await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        });
        Assert.True(started.Ok, started.Error?.Why);
        Assert.Equal(25, BudgetRemaining(await world.GetEntityAsync(Hero)));
        Assert.Equal(30, BudgetRemaining(await world.GetEntityAsync(Target)));
        using (var data = JsonDocument.Parse(started.Output!.Data))
        {
            Assert.Equal(30, data.RootElement.GetProperty("movementMaximumFeet").GetInt32());
            Assert.Equal(5, data.RootElement.GetProperty("movementReductionFeet").GetInt32());
            Assert.Equal(25, data.RootElement.GetProperty("movementRemainingFeet").GetInt32());
        }

        foreach (var (participant, level, remaining) in new[]
                 {
                     (Target, 2, 20), (Hero, 3, 15), (Target, 4, 10), (Hero, 5, 5), (Target, 6, 0)
                 })
        {
            await world.SetComponentAsync(participant, Conditions, ExhaustionCondition(level));
            var advanced = await runner.RunAsync(new ActionRequest
            {
                Intent = "advance encounter turn",
                RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
                Input = "{}",
                Seed = 712
            });
            Assert.True(advanced.Ok, advanced.Error?.Why);
            Assert.Equal(remaining, BudgetRemaining(await world.GetEntityAsync(participant)));
            using var data = JsonDocument.Parse(advanced.Output!.Data);
            Assert.Equal(30, data.RootElement.GetProperty("movementMaximumFeet").GetInt32());
            Assert.Equal(level * 5, data.RootElement.GetProperty("movementReductionFeet").GetInt32());
            Assert.Equal(remaining, data.RootElement.GetProperty("movementRemainingFeet").GetInt32());
        }

        await SetCorruptConditionAsync(db, Hero);
        var stateBefore = Component(await world.GetEntityAsync(Encounter), "dnd2024.encounter-turn-state");
        var rejected = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        });
        Assert.False(rejected.Ok);
        Assert.Contains("invalid condition state", rejected.Error?.Why, StringComparison.Ordinal);
        Assert.Equal(stateBefore, Component(await world.GetEntityAsync(Encounter), "dnd2024.encounter-turn-state"));
    }

    private static Task<ActionRunResult> RunAsync(ActionRunner runner, string intent, string subject, string input, string? source = null)
    {
        var roles = new Dictionary<string, string> { ["subject"] = subject };
        if (source is not null) roles["source"] = source;
        return runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = roles, Input = input, Seed = 714 });
    }

    private static Task<ActionRunResult> D20Async(
        ActionRunner runner,
        string intent,
        string input,
        long seed,
        params (string Role, string Id)[] roles) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = intent,
            Input = input,
            Seed = seed,
            RoleEntityIds = roles.ToDictionary(pair => pair.Role, pair => pair.Id, StringComparer.Ordinal)
        });

    private static void AssertPenalty(ActionRunResult before, ActionRunResult after, string totalField, int penalty)
    {
        Assert.True(before.Ok, before.Error?.Why);
        Assert.True(after.Ok, after.Error?.Why);
        using var oldData = JsonDocument.Parse(before.Output!.Data);
        using var newData = JsonDocument.Parse(after.Output!.Data);
        Assert.Equal(oldData.RootElement.GetProperty(totalField).GetInt32() + penalty, newData.RootElement.GetProperty(totalField).GetInt32());
        Assert.Equal(oldData.RootElement.GetProperty("rollMode").GetString(), newData.RootElement.GetProperty("rollMode").GetString());
        Assert.True(JsonElement.DeepEquals(oldData.RootElement.GetProperty("rolls"), newData.RootElement.GetProperty("rolls")));
        Assert.True(JsonElement.DeepEquals(oldData.RootElement.GetProperty("roll"), newData.RootElement.GetProperty("roll")));
        var modifier = Assert.Single(newData.RootElement.GetProperty("modifiers").EnumerateArray(),
            item => item.GetProperty("source").GetString() == "condition:exhaustion (level 3)");
        Assert.Equal(penalty, modifier.GetProperty("value").GetInt32());
    }

    private static int? ExhaustionLevel(EntitySnapshot? entity)
    {
        using var document = JsonDocument.Parse(ConditionData(entity));
        var exhaustion = document.RootElement.GetProperty("entries")
            .EnumerateArray().SingleOrDefault(entry => entry.GetProperty("condition").GetString() == "exhaustion");
        return exhaustion.ValueKind == JsonValueKind.Undefined ? null : exhaustion.GetProperty("level").GetInt32();
    }

    private static string ConditionData(EntitySnapshot? entity) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == Conditions).Data;

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;

    private static int BudgetRemaining(EntitySnapshot? entity)
    {
        using var document = JsonDocument.Parse(Component(entity, Budget));
        return document.RootElement.GetProperty("movementRemainingFeet").GetInt32();
    }

    private static async Task SetCorruptConditionAsync(DantesRoleplayDbContext db, string entityId)
    {
        var component = await db.Components.SingleAsync(candidate => candidate.EntityId == entityId && candidate.DefinitionId == Conditions);
        component.Data = "{";
        await db.SaveChangesAsync();
    }

    private static string ExhaustionCondition(int level) =>
        JsonSerializer.Serialize(new
        {
            entries = new[] { new { condition = "exhaustion", level } },
            sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Rules Glossary" }
        });

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
            new EffectApplier(db, world,
                new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)), new EventLedger(db)), new OperationLog(db),
            new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
        }
        throw new DirectoryNotFoundException();
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}
