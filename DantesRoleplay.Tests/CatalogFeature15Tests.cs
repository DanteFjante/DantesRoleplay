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

public sealed class CatalogFeature15Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-15-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Damage_type_contract_defines_the_complete_vocabulary_without_widening_weapon_profiles()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        var contract = await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.damage-types");
        Assert.NotNull(contract);
        Assert.Equal("ruleset.dnd2024.core.gameplay.damage", contract!.Category);

        var markdown = await File.ReadAllTextAsync(Path.Combine(_catalogCopy,
            "procedures", "ruleset", "dnd2024", "core", "gameplay", "damage", "procedure.mechanic.dnd2024.damage-types.md"));
        var types = new[] { "acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder" };
        var previous = -1;
        foreach (var type in types)
        {
            var index = markdown.IndexOf($"`{type}`", StringComparison.Ordinal);
            Assert.True(index > previous, type);
            previous = index;
        }
        Assert.Contains("`bludgeoning`,\n   `piercing`, and `slashing`", markdown, StringComparison.Ordinal);

        var weaponSchema = await File.ReadAllTextAsync(Path.Combine(_catalogCopy, "components", "dnd2024.weapon-profile.schema.json"));
        Assert.Contains("\"type\":{\"enum\":[\"bludgeoning\",\"piercing\",\"slashing\"]}", weaponSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"radiant\"", weaponSchema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Damage_mitigation_writer_records_canonical_complete_lists_and_rejects_bad_state()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.damage-mitigation.write"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.damage-mitigation"));

        const string subject = "fixture.catalog.f15.subject";
        await world.CreateEntityAsync("Mitigated creature", subject);
        var runner = CreateRunner(db, world, mechanics);
        var recorded = await RunAsync(runner, "record damage resistances", subject,
            """{"mode":"record","resistances":["fire","acid"],"immunities":["thunder"],"vulnerabilities":["fire","acid"]}""");
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(recorded.Output!.Effects).Type);
        AssertMitigation(Component(await world.GetEntityAsync(subject)), new[] { "acid", "fire" }, new[] { "thunder" }, new[] { "acid", "fire" });

        var all = new[] { "acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder" };
        var corrected = await RunAsync(runner, "correct damage mitigation", subject,
            JsonSerializer.Serialize(new { mode = "correct", resistances = all, immunities = all, vulnerabilities = all }));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(corrected.Output!.Effects).Type);
        AssertMitigation(Component(await world.GetEntityAsync(subject)), all, all, all);

        var before = Component(await world.GetEntityAsync(subject));
        foreach (var input in new[]
                 {
                     "{}", """{"mode":"record","resistances":[],"immunities":[],"vulnerabilities":[]}""",
                     """{"mode":"correct","resistances":["acid","acid"],"immunities":[],"vulnerabilities":[]}""",
                     """{"mode":"correct","resistances":["Fire"],"immunities":[],"vulnerabilities":[]}""",
                     """{"mode":"correct","resistances":["acid","fire"],"immunities":[],"vulnerabilities":[],"sourceRef":{}}"""
                 })
        {
            var rejected = await RunAsync(runner, "correct damage mitigation", subject, input);
            Assert.False(rejected.Ok, input);
            Assert.Equal(before, Component(await world.GetEntityAsync(subject)));
        }

        const string absent = "fixture.catalog.f15.absent";
        await world.CreateEntityAsync("Unrecorded creature", absent);
        Assert.False((await RunAsync(runner, "correct damage mitigation", absent,
            """{"mode":"correct","resistances":[],"immunities":[],"vulnerabilities":[]}""")).Ok);
        await world.SetComponentAsync(subject, "dnd2024.damage-mitigation",
            """{"resistances":["fire","acid"],"immunities":[],"vulnerabilities":[],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing"}}""");
        Assert.False((await RunAsync(runner, "correct damage mitigation", subject,
            """{"mode":"correct","resistances":[],"immunities":[],"vulnerabilities":[]}""")).Ok);
    }

    [Fact]
    public async Task Damage_mitigation_profile_reports_petrified_and_supports_srd_ordered_arithmetic()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.damage.resolve"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.damage.resolve"));

        const string defender = "fixture.catalog.f15.defender";
        await world.CreateEntityAsync("Defender", defender);
        var runner = CreateRunner(db, world, mechanics);
        var absent = await ResolveAsync(runner, defender);
        Assert.True(absent.Ok, absent.Error?.Why);
        Assert.Empty(absent.Output!.Effects);
        using (var data = JsonDocument.Parse(absent.Output.Data))
        {
            Assert.False(data.RootElement.GetProperty("mitigationKnown").GetBoolean());
            Assert.False(data.RootElement.GetProperty("conditionsKnown").GetBoolean());
            Assert.False(data.RootElement.GetProperty("petrified").GetBoolean());
        }

        var types = new[] { "acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder" };
        foreach (var type in types) Assert.Equal(23L, Mitigate(23, type, [], [], [], false).FinalAmount);
        Assert.Equal(0L, Mitigate(23, "fire", [], ["fire"], ["fire"], false).FinalAmount);
        Assert.Equal(0L, Mitigate(1, "fire", ["fire"], [], [], false).FinalAmount);
        Assert.Equal(1L, Mitigate(2, "fire", ["fire"], [], [], false).FinalAmount);
        Assert.Equal(1L, Mitigate(3, "fire", ["fire"], [], [], false).FinalAmount);
        Assert.Equal(3L, Mitigate(7, "fire", ["fire"], [], [], false).FinalAmount);
        Assert.Equal(46L, Mitigate(23, "fire", [], [], ["fire"], false).FinalAmount);
        Assert.Equal(22L, Mitigate(23, "fire", ["fire"], [], ["fire"], false).FinalAmount);
        Assert.Equal(11L, Mitigate(23, "fire", [], [], [], true).FinalAmount);
        var twiceSourced = Mitigate(23, "fire", ["fire"], [], [], true);
        Assert.Equal(11L, twiceSourced.FinalAmount);
        Assert.Equal(new[] { "component", "condition:petrified" }, twiceSourced.Reasons);

        await world.SetComponentAsync(defender, "dnd2024.damage-mitigation",
            """{"resistances":["fire"],"immunities":["acid"],"vulnerabilities":["fire"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing"}}""");
        await world.SetComponentAsync(defender, "dnd2024.conditions",
            """{"entries":[{"condition":"petrified"}],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary"}}""");
        var profile = await ResolveAsync(runner, defender);
        Assert.True(profile.Ok, profile.Error?.Why);
        using (var data = JsonDocument.Parse(profile.Output!.Data))
        {
            Assert.True(data.RootElement.GetProperty("mitigationKnown").GetBoolean());
            Assert.True(data.RootElement.GetProperty("conditionsKnown").GetBoolean());
            Assert.True(data.RootElement.GetProperty("petrified").GetBoolean());
            Assert.Equal("acid", Assert.Single(data.RootElement.GetProperty("immunities").EnumerateArray()).GetString());
            Assert.Equal("fire", Assert.Single(data.RootElement.GetProperty("resistances").EnumerateArray()).GetString());
        }

        await world.SetComponentAsync(defender, "dnd2024.conditions",
            """{"entries":[{"condition":"exhaustion","level":0}],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary"}}""");
        Assert.False((await ResolveAsync(runner, defender)).Ok);
    }

    [Fact]
    public async Task Weapon_damage_applies_the_profile_once_and_records_a_schema_valid_damage_event()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        const string subject = "fixture.catalog.f15.damage.subject";
        await world.CreateEntityAsync("Damage subject", subject);
        await world.SetComponentAsync(subject, "dnd2024.abilities", """{"str":16,"dex":10,"con":10,"int":10,"wis":10,"cha":10}""");

        const string normalTarget = "fixture.catalog.f15.damage.normal";
        await CreateTargetAsync(world, normalTarget, 100, 100);
        var unmitigated = await ApplyDamageAsync(runner, subject, normalTarget, 51);
        Assert.True(unmitigated.Ok, unmitigated.Error?.Why);
        using var normal = JsonDocument.Parse(unmitigated.Output!.Data);
        var raw = normal.RootElement.GetProperty("rawAmount").GetInt32();
        Assert.Equal(raw, normal.RootElement.GetProperty("finalAmount").GetInt32());
        Assert.Equal(100 - raw, normal.RootElement.GetProperty("afterCurrent").GetInt32());
        Assert.Equal("mechanic.dnd2024.damage.resolve", normal.RootElement.GetProperty("mitigationChildMechanicId").GetString());
        Assert.Single(unmitigated.Projection!.Children["mitigation"]);

        const string resistantTarget = "fixture.catalog.f15.damage.resistant";
        await CreateTargetAsync(world, resistantTarget, 100, 100);
        await SetMitigationAsync(world, resistantTarget, ["piercing"], [], []);
        var resistant = await ApplyDamageAsync(runner, subject, resistantTarget, 51);
        Assert.True(resistant.Ok, resistant.Error?.Why);
        using (var data = JsonDocument.Parse(resistant.Output!.Data))
        {
            Assert.Equal(raw / 2, data.RootElement.GetProperty("finalAmount").GetInt32());
            Assert.True(data.RootElement.GetProperty("mitigation").GetProperty("resistanceApplied").GetBoolean());
        }

        const string immuneTarget = "fixture.catalog.f15.damage.immune";
        await CreateTargetAsync(world, immuneTarget, 1, 12);
        await SetMitigationAsync(world, immuneTarget, [], ["piercing"], ["piercing"]);
        var immune = await ApplyDamageAsync(runner, subject, immuneTarget, 51);
        Assert.True(immune.Ok, immune.Error?.Why);
        Assert.Equal(1, immune.AppliedCount);
        using (var data = JsonDocument.Parse(immune.Output!.Data))
        {
            Assert.Equal(0, data.RootElement.GetProperty("finalAmount").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("overkill").GetInt32());
        }

        const string vulnerableTarget = "fixture.catalog.f15.damage.vulnerable";
        await CreateTargetAsync(world, vulnerableTarget, 1, 12);
        await SetMitigationAsync(world, vulnerableTarget, ["piercing"], [], ["piercing"]);
        var vulnerable = await ApplyDamageAsync(runner, subject, vulnerableTarget, 51);
        Assert.True(vulnerable.Ok, vulnerable.Error?.Why);
        using (var data = JsonDocument.Parse(vulnerable.Output!.Data))
        {
            Assert.Equal((raw / 2) * 2, data.RootElement.GetProperty("finalAmount").GetInt32());
            Assert.Equal(Math.Max(0, (raw / 2) * 2 - 1), data.RootElement.GetProperty("overkill").GetInt32());
        }

        var ledger = new EventLedger(db);
        var recorded = await ledger.FindAsync(rootOperationId: vulnerable.OperationId);
        var damageEvent = Assert.Single(recorded, item => item.TypeId == "dnd2024.damage.dealt");
        var damageDetail = await ledger.GetAsync(damageEvent.Id);
        Assert.NotNull(damageDetail);
        using (var payload = JsonDocument.Parse(damageDetail!.PayloadJson))
        {
            Assert.Equal(vulnerableTarget, payload.RootElement.GetProperty("targetId").GetString());
            Assert.Equal(subject, payload.RootElement.GetProperty("sourceId").GetString());
            Assert.Equal("piercing", payload.RootElement.GetProperty("type").GetString());
            Assert.Equal((raw / 2) * 2, payload.RootElement.GetProperty("finalAmount").GetInt32());
            Assert.Equal(Math.Max(0, (raw / 2) * 2 - 1), payload.RootElement.GetProperty("overkill").GetInt32());
        }
        Assert.Equal(4, await db.Events.CountAsync(item => item.TypeId == "dnd2024.damage.dealt"));
    }

    private static Task<ActionRunResult> RunAsync(ActionRunner runner, string intent, string subject, string input) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = intent,
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject },
            Input = input,
            Seed = 15
        });

    private static Task<ActionRunResult> ResolveAsync(ActionRunner runner, string defender) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = "inspect damage mitigation",
            RoleEntityIds = new Dictionary<string, string> { ["defender"] = defender },
            Input = "{}",
            Seed = 15
        });

    private static Task<ActionRunResult> ApplyDamageAsync(ActionRunner runner, string subject, string target, long seed) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = "apply confirmed weapon damage",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject, ["target"] = target, ["weapon"] = "weapon.dnd2024.dagger" },
            Input = """{"ability":"str","critical":false}""",
            Seed = seed
        });

    private static async Task CreateTargetAsync(WorldStore world, string id, int current, int maximum)
    {
        await world.CreateEntityAsync("Damage target", id);
        await world.SetComponentAsync(id, "dnd2024.hit-points", JsonSerializer.Serialize(new
        {
            current,
            maximum,
            sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Damage and Healing > Hit Points" }
        }));
    }

    private static Task SetMitigationAsync(WorldStore world, string id, string[] resistances, string[] immunities, string[] vulnerabilities) =>
        world.SetComponentAsync(id, "dnd2024.damage-mitigation", JsonSerializer.Serialize(new
        {
            resistances,
            immunities,
            vulnerabilities,
            sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Damage and Healing" }
        }));

    private static (long FinalAmount, string[] Reasons) Mitigate(long amount, string type, string[] resistances, string[] immunities, string[] vulnerabilities, bool petrified)
    {
        if (immunities.Contains(type, StringComparer.Ordinal)) return (0, ["component"]);
        var reasons = new List<string>();
        if (resistances.Contains(type, StringComparer.Ordinal)) reasons.Add("component");
        if (petrified) reasons.Add("condition:petrified");
        if (reasons.Count > 0) amount /= 2;
        if (vulnerabilities.Contains(type, StringComparer.Ordinal)) amount *= 2;
        return (amount, reasons.ToArray());
    }

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, events: new EventLedger(db)),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string Component(EntitySnapshot? entity) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == "dnd2024.damage-mitigation").Data;

    private static void AssertMitigation(string data, string[] resistances, string[] immunities, string[] vulnerabilities)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        Assert.Equal(resistances, root.GetProperty("resistances").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(immunities, root.GetProperty("immunities").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(vulnerabilities, root.GetProperty("vulnerabilities").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("source.dnd2024.srd-5.2.1", root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
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
