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

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature17Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-17-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Zero_hit_point_policy_is_closed_nonrandom_and_present_on_combat_fixtures()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var procedures = new ProcedureStore(db);
        var imported = await new CatalogImporter(db, mechanics, procedures, world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.zero-hit-points-policy.write"));
        Assert.NotNull(await procedures.GetAsync("procedure.mechanic.dnd2024.zero-hit-points-policy"));

        AssertPolicy(Component(await world.GetEntityAsync("creature.dnd2024.feature-10.hero"), "dnd2024.zero-hit-points-policy"), "death-saves");
        AssertPolicy(Component(await world.GetEntityAsync("creature.dnd2024.feature-10.training-target"), "dnd2024.zero-hit-points-policy"), "die-at-zero");

        const string subject = "fixture.catalog.f17.policy.subject";
        const string sibling = "fixture.catalog.f17.policy.sibling";
        await world.CreateEntityAsync("Policy subject", subject);
        await world.CreateEntityAsync("Untouched sibling", sibling);
        await world.SetComponentAsync(subject, "dnd2024.hit-points", HitPoints(4, 12));
        await world.SetComponentAsync(sibling, "dnd2024.hit-points", HitPoints(6, 12));
        var runner = CreateRunner(db, world, mechanics);
        var siblingBefore = Component(await world.GetEntityAsync(sibling), "dnd2024.hit-points");

        var recorded = await RunAsync(runner, "record death saves policy", subject, """{"mode":"record","policy":"death-saves"}""");
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(recorded.Output!.Effects).Type);
        AssertPolicy(Component(await world.GetEntityAsync(subject), "dnd2024.zero-hit-points-policy"), "death-saves");
        Assert.Equal(Component(await world.GetEntityAsync(subject), "dnd2024.hit-points"), HitPoints(4, 12));
        Assert.Equal(siblingBefore, Component(await world.GetEntityAsync(sibling), "dnd2024.hit-points"));

        var beforeInvalid = Component(await world.GetEntityAsync(subject), "dnd2024.zero-hit-points-policy");
        foreach (var input in new[]
                 {
                     "{}", "[]", "null", """{"mode":"record","policy":"character"}""",
                     """{"mode":"record","policy":"monster"}""", """{"mode":"record","policy":"Character"}""",
                     """{"mode":"record","policy":"pc"}""", """{"mode":"record","policy":""}""",
                     """{"mode":"record","policy":null}""", """{"mode":"record","policy":1}""",
                     """{"mode":"record","policy":"death-saves","sourceRef":{}}""",
                     """{"mode":"record","policy":"death-saves","effects":[]}"""
                 })
        {
            var rejected = await RunAsync(runner, "record zero hit point policy", subject, input);
            Assert.False(rejected.Ok, input);
            Assert.Equal(0, rejected.AppliedCount);
            Assert.Equal(beforeInvalid, Component(await world.GetEntityAsync(subject), "dnd2024.zero-hit-points-policy"));
            Assert.Equal(siblingBefore, Component(await world.GetEntityAsync(sibling), "dnd2024.hit-points"));
        }

        var corrected = await RunAsync(runner, "correct zero hit point policy", subject, """{"mode":"correct","policy":"die-at-zero"}""");
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(corrected.Output!.Effects).Type);
        AssertPolicy(Component(await world.GetEntityAsync(subject), "dnd2024.zero-hit-points-policy"), "die-at-zero");
        using (var data = JsonDocument.Parse(corrected.Output.Data))
        {
            Assert.Equal("death-saves", data.RootElement.GetProperty("previousPolicy").GetString());
            Assert.Equal("die-at-zero", data.RootElement.GetProperty("policy").GetString());
        }

        const string absent = "fixture.catalog.f17.policy.absent";
        await world.CreateEntityAsync("Absent policy", absent);
        Assert.False((await RunAsync(runner, "correct zero hit point policy", absent, """{"mode":"correct","policy":"death-saves"}""")).Ok);

        const string corrupt = "fixture.catalog.f17.policy.corrupt";
        await world.CreateEntityAsync("Corrupt policy", corrupt);
        await world.SetComponentAsync(corrupt, "dnd2024.zero-hit-points-policy", "{}");
        var corruptBefore = Component(await world.GetEntityAsync(corrupt), "dnd2024.zero-hit-points-policy");
        var corruptRejected = await RunAsync(runner, "correct zero hit point policy", corrupt, """{"mode":"correct","policy":"death-saves"}""");
        Assert.False(corruptRejected.Ok);
        Assert.Equal(corruptBefore, Component(await world.GetEntityAsync(corrupt), "dnd2024.zero-hit-points-policy"));
    }

    [Fact]
    public async Task Death_state_is_closed_terminal_and_can_only_end_while_not_dead()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var procedures = new ProcedureStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, procedures, world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.death-state.write"));
        Assert.NotNull(await procedures.GetAsync("procedure.mechanic.dnd2024.death-state"));

        const string subject = "fixture.catalog.f17.death-state.subject";
        const string sibling = "fixture.catalog.f17.death-state.sibling";
        await world.CreateEntityAsync("Dying subject", subject);
        await world.CreateEntityAsync("Untouched sibling", sibling);
        await world.SetComponentAsync(subject, "dnd2024.hit-points", HitPoints(0, 12));
        await world.SetComponentAsync(sibling, "dnd2024.hit-points", HitPoints(6, 12));
        var siblingBefore = Component(await world.GetEntityAsync(sibling), "dnd2024.hit-points");
        var runner = CreateRunner(db, world, mechanics);

        var begun = await RunAsync(runner, "begin death state", subject, """{"mode":"begin"}""");
        Assert.True(begun.Ok, begun.Error?.Why);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(begun.Output!.Effects).Type);
        AssertDeathState(Component(await world.GetEntityAsync(subject), "dnd2024.death-state"), 0, 0, stable: false, dead: false);

        var corrected = await RunAsync(runner, "correct death state", subject,
            """{"mode":"correct","successes":2,"failures":1,"stable":false,"dead":false}""");
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(corrected.Output!.Effects).Type);
        AssertDeathState(Component(await world.GetEntityAsync(subject), "dnd2024.death-state"), 2, 1, stable: false, dead: false);

        var beforeInvalid = Component(await world.GetEntityAsync(subject), "dnd2024.death-state");
        foreach (var input in new[]
                 {
                     "{}", """{"mode":"correct","successes":3,"failures":0,"stable":false,"dead":false}""",
                     """{"mode":"correct","successes":-1,"failures":0,"stable":false,"dead":false}""",
                     """{"mode":"correct","successes":1.5,"failures":0,"stable":false,"dead":false}""",
                     """{"mode":"correct","successes":1,"failures":0,"stable":true,"dead":false}""",
                     """{"mode":"correct","successes":0,"failures":0,"stable":true,"dead":true}""",
                     """{"mode":"correct","successes":0,"failures":0,"stable":false,"dead":false,"sourceRef":{}}""",
                     """{"mode":"correct","successes":0,"failures":0,"stable":false,"dead":false,"effects":[]}"""
                 })
        {
            var rejected = await RunAsync(runner, "correct death state", subject, input);
            Assert.False(rejected.Ok, input);
            Assert.Equal(0, rejected.AppliedCount);
            Assert.Equal(beforeInvalid, Component(await world.GetEntityAsync(subject), "dnd2024.death-state"));
            Assert.Equal(siblingBefore, Component(await world.GetEntityAsync(sibling), "dnd2024.hit-points"));
        }

        var stable = await RunAsync(runner, "correct death state", subject,
            """{"mode":"correct","successes":0,"failures":0,"stable":true,"dead":false}""");
        Assert.True(stable.Ok, stable.Error?.Why);
        AssertDeathState(Component(await world.GetEntityAsync(subject), "dnd2024.death-state"), 0, 0, stable: true, dead: false);

        var ended = await RunAsync(runner, "end death state", subject, """{"mode":"end"}""");
        Assert.True(ended.Ok, ended.Error?.Why);
        Assert.Equal(EffectType.ComponentRemove, Assert.Single(ended.Output!.Effects).Type);
        Assert.DoesNotContain((await world.GetEntityAsync(subject))!.Components, item => item.DefinitionId == "dnd2024.death-state");
        Assert.False((await RunAsync(runner, "end death state", subject, """{"mode":"end"}""")).Ok);
        Assert.False((await RunAsync(runner, "correct death state", subject,
            """{"mode":"correct","successes":0,"failures":0,"stable":false,"dead":false}""")).Ok);

        Assert.True((await RunAsync(runner, "begin death state", subject, """{"mode":"begin"}""")).Ok);
        var dead = await RunAsync(runner, "correct death state", subject,
            """{"mode":"correct","successes":0,"failures":0,"stable":false,"dead":true}""");
        Assert.True(dead.Ok, dead.Error?.Why);
        AssertDeathState(Component(await world.GetEntityAsync(subject), "dnd2024.death-state"), 0, 0, stable: false, dead: true);
        Assert.False((await RunAsync(runner, "end death state", subject, """{"mode":"end"}""")).Ok);
        Assert.False((await RunAsync(runner, "correct death state", subject,
            """{"mode":"correct","successes":0,"failures":0,"stable":false,"dead":false}""")).Ok);

        const string corrupt = "fixture.catalog.f17.death-state.corrupt";
        await world.CreateEntityAsync("Corrupt death state", corrupt);
        await world.SetComponentAsync(corrupt, "dnd2024.death-state", "{}");
        var corruptBefore = Component(await world.GetEntityAsync(corrupt), "dnd2024.death-state");
        var corruptRejected = await RunAsync(runner, "correct death state", corrupt,
            """{"mode":"correct","successes":0,"failures":0,"stable":false,"dead":false}""");
        Assert.False(corruptRejected.Ok);
        Assert.Equal(corruptBefore, Component(await world.GetEntityAsync(corrupt), "dnd2024.death-state"));
    }

    [Fact]
    public async Task Condition_guard_allows_the_normal_writer_and_denies_each_invalid_proposed_list_atomically()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var procedures = new ProcedureStore(db);
        var subscriptions = new SubscriptionStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, procedures, world, new EventTypeStore(db), subscriptions)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await procedures.GetAsync("procedure.mechanic.dnd2024.conditions.guard"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.conditions.guard"));
        Assert.NotNull(await subscriptions.GetAsync("subscription.dnd2024.conditions.guard.added"));
        Assert.NotNull(await subscriptions.GetAsync("subscription.dnd2024.conditions.guard.replaced"));

        const string subject = "fixture.catalog.f17.guard.subject";
        const string sibling = "fixture.catalog.f17.guard.sibling";
        const string writerSubject = "fixture.catalog.f17.guard.writer";
        await world.CreateEntityAsync("Guard subject", subject);
        await world.CreateEntityAsync("Untouched sibling", sibling);
        await world.CreateEntityAsync("Normal writer subject", writerSubject);
        await world.SetComponentAsync(subject, "dnd2024.hit-points", HitPoints(4, 12));
        var applier = GuardedApplier(db, world);

        var valid = await applier.ApplyAsync(
            [new Effect { Type = EffectType.ComponentAdd, EntityId = subject, DefinitionId = "dnd2024.conditions", Data = Conditions("""[{"condition":"blinded"},{"condition":"exhaustion","level":2}]""") }],
            rootOperationId: "feature-17-guard-valid");
        Assert.True(valid.Applied);
        Assert.Equal("allow", Assert.Single(valid.GuardEvaluations).Decision);
        var beforeInvalid = Component(await world.GetEntityAsync(subject), "dnd2024.conditions");

        foreach (var (entries, code) in new[]
                 {
                     ("""[{"condition":"prone"},{"condition":"blinded"}]""", "CONDITIONS_ORDER"),
                     ("""[{"condition":"blinded"},{"condition":"blinded"}]""", "CONDITIONS_DUPLICATE"),
                     ("""[{"condition":"unknown"}]""", "CONDITIONS_ENTRY"),
                     ("""[{"condition":"petrified"},{"condition":"poisoned"}]""", "CONDITIONS_INCOMPATIBLE"),
                     ("""[{"condition":"blinded","level":1}]""", "CONDITIONS_ENTRY"),
                     ("""[{"condition":"exhaustion","level":0}]""", "CONDITIONS_EXHAUSTION"),
                     ("""[{"condition":"blinded","sourceEntityId":" "}]""", "CONDITIONS_ENTRY")
                 })
        {
            var denied = await applier.ApplyAsync(
                [
                    new Effect { Type = EffectType.ComponentSet, EntityId = subject, DefinitionId = "dnd2024.conditions", Data = Conditions(entries) },
                    new Effect { Type = EffectType.ComponentAdd, EntityId = sibling, DefinitionId = "dnd2024.death-state", Data = DeathState() }
                ],
                rootOperationId: "feature-17-guard-" + code);
            Assert.True(denied.Blocked);
            Assert.Equal(code, denied.BlockCode);
            Assert.Equal(beforeInvalid, Component(await world.GetEntityAsync(subject), "dnd2024.conditions"));
            Assert.DoesNotContain((await world.GetEntityAsync(sibling))!.Components, item => item.DefinitionId == "dnd2024.death-state");
        }

        var wrongSource = await applier.ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = subject, DefinitionId = "dnd2024.conditions", Data = """{"entries":[],"sourceRef":{"sourceId":"forged","locator":"Rules Glossary"}}""" }],
            rootOperationId: "feature-17-guard-source");
        Assert.True(wrongSource.Blocked);
        Assert.Equal("CONDITIONS_SHAPE", wrongSource.BlockCode);

        var unrelated = await applier.ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = subject, DefinitionId = "dnd2024.hit-points", Data = HitPoints(3, 12) }],
            rootOperationId: "feature-17-guard-unrelated");
        Assert.True(unrelated.Applied);
        Assert.Empty(unrelated.GuardEvaluations);

        var runner = new ActionRunner(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), applier,
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
        var recorded = await RunAsync(runner, "record creature conditions", writerSubject, """{"mode":"record"}""");
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal("dnd2024.conditions", Assert.Single(recorded.Output!.Effects).DefinitionId);
        Assert.DoesNotContain("forged", (await world.GetEntityAsync(writerSubject))!.Components.Single(component => component.DefinitionId == "dnd2024.conditions").Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Damage_reaction_starts_dying_records_failures_and_applies_terminal_zero_hp_branches()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.dying.on-damage"));
        Assert.NotNull(await new SubscriptionStore(db).GetAsync("subscription.dnd2024.dying.on-damage"));
        var applier = ReactiveApplier(db, world);

        const string dying = "fixture.catalog.f17.dying";
        await PrepareTargetAsync(world, dying, 5, 10, "death-saves");
        var dropped = await ApplyDamageAsync(applier, dying, 5, 0, 5, 0, critical: false);
        Assert.True(dropped.Applied);
        AssertDeathState(Component(await world.GetEntityAsync(dying), "dnd2024.death-state"), 0, 0, stable: false, dead: false);
        Assert.Contains("unconscious", Component(await world.GetEntityAsync(dying), "dnd2024.conditions"), StringComparison.Ordinal);

        Assert.True((await ApplyDamageAsync(applier, dying, 0, 0, 2, 2, critical: false)).Applied);
        AssertDeathState(Component(await world.GetEntityAsync(dying), "dnd2024.death-state"), 0, 1, stable: false, dead: false);
        Assert.True((await ApplyDamageAsync(applier, dying, 0, 0, 1, 1, critical: true)).Applied);
        AssertDeathState(Component(await world.GetEntityAsync(dying), "dnd2024.death-state"), 0, 0, stable: false, dead: true);

        const string instant = "fixture.catalog.f17.instant";
        await PrepareTargetAsync(world, instant, 5, 10, "death-saves");
        Assert.True((await ApplyDamageAsync(applier, instant, 5, 0, 15, 10, critical: false)).Applied);
        AssertDeathState(Component(await world.GetEntityAsync(instant), "dnd2024.death-state"), 0, 0, stable: false, dead: true);
        Assert.DoesNotContain((await world.GetEntityAsync(instant))!.Components, item => item.DefinitionId == "dnd2024.conditions");

        const string monster = "fixture.catalog.f17.monster";
        await PrepareTargetAsync(world, monster, 5, 10, "die-at-zero");
        Assert.True((await ApplyDamageAsync(applier, monster, 5, 0, 5, 0, critical: false)).Applied);
        AssertDeathState(Component(await world.GetEntityAsync(monster), "dnd2024.death-state"), 0, 0, stable: false, dead: true);
        Assert.DoesNotContain((await world.GetEntityAsync(monster))!.Components, item => item.DefinitionId == "dnd2024.conditions");

        const string missingPolicy = "fixture.catalog.f17.missing-policy";
        await world.CreateEntityAsync("Missing policy", missingPolicy);
        await world.SetComponentAsync(missingPolicy, "dnd2024.hit-points", HitPoints(5, 10));
        var rejected = await ApplyDamageAsync(applier, missingPolicy, 5, 0, 5, 0, critical: false);
        Assert.True(rejected.Blocked);
        Assert.Equal(HitPoints(5, 10), Component(await world.GetEntityAsync(missingPolicy), "dnd2024.hit-points"));
    }

    private static Task<ActionRunResult> RunAsync(ActionRunner runner, string intent, string subject, string input) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = intent,
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject },
            Input = input,
            Seed = 17
        });

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, events: new EventLedger(db)),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static EffectApplier GuardedApplier(DantesRoleplayDbContext db, WorldStore world) =>
        new(db, world,
            new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
            new EventLedger(db));

    private static EffectApplier ReactiveApplier(DantesRoleplayDbContext db, WorldStore world) =>
        new(db, world,
            new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
            new EventLedger(db),
            new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)));

    private static async Task PrepareTargetAsync(WorldStore world, string id, int current, int maximum, string policy)
    {
        await world.CreateEntityAsync(id, id);
        await world.SetComponentAsync(id, "dnd2024.hit-points", HitPoints(current, maximum));
        await world.SetComponentAsync(id, "dnd2024.zero-hit-points-policy", Policy(policy));
    }

    private static Task<EffectResult> ApplyDamageAsync(EffectApplier applier, string target, int before, int after, int finalAmount, int overkill, bool critical) =>
        applier.ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = target, DefinitionId = "dnd2024.hit-points", Data = HitPoints(after, 10) }],
            rootOperationId: $"feature-17-damage-{target}-{before}-{finalAmount}-{critical}",
            declaredEvents:
            [new DeclaredEvent
            {
                Type = "dnd2024.damage.dealt", EntityIds = [target], Payload = JsonSerializer.Serialize(new
                {
                    targetId = target, sourceId = "fixture.catalog.f17.attacker", rawAmount = finalAmount, type = "slashing", finalAmount,
                    immune = false, resistanceApplied = false, vulnerabilityApplied = false, temporaryBefore = 0, temporaryAfter = 0,
                    temporaryAbsorbed = 0, beforeCurrent = before, afterCurrent = after, maximum = 10, overkill, critical,
                    sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Damage and Healing" }
                })
            }]);

    private static string HitPoints(int current, int maximum) => JsonSerializer.Serialize(new
    {
        current,
        maximum,
        sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Damage and Healing > Hit Points" }
    });

    private static string Conditions(string entries) =>
        "{\"entries\":" + entries + ",\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary\"}}";

    private static string DeathState() =>
        """{"successes":0,"failures":0,"stable":false,"dead":false,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Dropping to 0 Hit Points"}}""";

    private static string Policy(string policy) =>
        "{\"policy\":\"" + policy + "\",\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Dropping to 0 Hit Points\"}}";

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, item => item.DefinitionId == definitionId).Data;

    private static void AssertPolicy(string state, string policy)
    {
        using var data = JsonDocument.Parse(state);
        Assert.Equal(policy, data.RootElement.GetProperty("policy").GetString());
        Assert.Equal("source.dnd2024.srd-5.2.1", data.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Playing the Game > Damage and Healing > Dropping to 0 Hit Points", data.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());
    }

    private static void AssertDeathState(string state, int successes, int failures, bool stable, bool dead)
    {
        using var data = JsonDocument.Parse(state);
        Assert.Equal(successes, data.RootElement.GetProperty("successes").GetInt32());
        Assert.Equal(failures, data.RootElement.GetProperty("failures").GetInt32());
        Assert.Equal(stable, data.RootElement.GetProperty("stable").GetBoolean());
        Assert.Equal(dead, data.RootElement.GetProperty("dead").GetBoolean());
        Assert.Equal("source.dnd2024.srd-5.2.1", data.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
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

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}
