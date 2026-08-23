using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 7 Slice 1 imports file-authored canonical weapon profiles before testing the only
/// writer that may create or correct one. Feature 8 consumes these facts; it must not invent them.
/// </summary>
public sealed class CatalogFeature7Tests : IDisposable
{
    private const string Profile = "dnd2024.weapon-profile";
    private const string Proficiencies = "dnd2024.weapon-proficiencies";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private const string SourceLocator = "Equipment > Weapons";
    private const string ProficiencySourceLocator = "Equipment > Weapons > Weapon Proficiency";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-7-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();

        if (Directory.Exists(_catalogCopy))
        {
            Directory.Delete(_catalogCopy, recursive: true);
        }
    }

    [Fact]
    public async Task Imported_catalog_records_corrects_and_guards_canonical_weapon_profiles()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.True(imported.ManifestUpdated);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.weapon-profile.write"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.weapon-profile"));

        await AssertProfileAsync(world, "weapon.dnd2024.dagger", "simple", "melee", ["str", "dex"], 1, 4, "piercing");
        await AssertProfileAsync(world, "weapon.dnd2024.shortbow", "simple", "ranged", ["dex"], 1, 6, "piercing", 80, 320);
        await AssertProfileAsync(world, "weapon.dnd2024.battleaxe", "martial", "melee", ["str"], 1, 8, "slashing");

        const string subject = "fixture.catalog.f7.weapon";
        await world.CreateEntityAsync("Disposable weapon profile", subject);
        var runner = CreateRunner(db, world, mechanics);

        var recorded = await runner.RunAsync(Request(
            "record weapon profile",
            """{"mode":"record","category":"simple","kind":"melee","attackAbilities":["str","dex"],"damage":{"count":1,"faces":4,"type":"piercing"},"propertyTags":["finesse","light","thrown"],"thrownRangeFeet":{"normal":20,"long":60},"mastery":"nick"}"""));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.weapon-profile.write", recorded.Mechanic?.Id);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Single(recorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, recorded.Output.Effects[0].Type);
        await AssertProfileAsync(world, subject, "simple", "melee", ["str", "dex"], 1, 4, "piercing");

        var duplicate = await runner.RunAsync(Request(
            "record weapon profile",
            """{"mode":"record","category":"simple","kind":"melee","attackAbilities":["str","dex"],"damage":{"count":1,"faces":4,"type":"piercing"},"propertyTags":["finesse","light","thrown"],"thrownRangeFeet":{"normal":20,"long":60},"mastery":"nick"}"""));
        Assert.False(duplicate.Ok);
        await AssertProfileAsync(world, subject, "simple", "melee", ["str", "dex"], 1, 4, "piercing");

        var corrected = await runner.RunAsync(Request(
            "correct weapon profile",
            """{"mode":"correct","category":"martial","kind":"melee","attackAbilities":["str"],"damage":{"count":1,"faces":8,"type":"slashing"},"propertyTags":["versatile"],"versatileDamage":{"count":1,"faces":10,"type":"slashing"},"mastery":"topple"}"""));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Single(corrected.Output!.Effects);
        Assert.Equal(EffectType.ComponentSet, corrected.Output.Effects[0].Type);
        using (var result = JsonDocument.Parse(corrected.Output.Data))
        {
            Assert.Equal("simple", result.RootElement.GetProperty("previous").GetProperty("category").GetString());
            Assert.Equal("piercing", result.RootElement.GetProperty("previous").GetProperty("damage").GetProperty("type").GetString());
        }
        await AssertProfileAsync(world, subject, "martial", "melee", ["str"], 1, 8, "slashing");

        const string rangedId = "fixture.catalog.f7.ranged";
        await world.CreateEntityAsync("Ranged weapon profile", rangedId);
        var ranged = await runner.RunAsync(Request(
            "record ranged weapon profile",
            """{"mode":"record","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"},"rangeFeet":{"normal":80,"long":320},"propertyTags":["ammunition","two-handed"],"ammunitionType":"arrow","mastery":"vex"}""",
            rangedId));
        Assert.True(ranged.Ok, ranged.Error?.Why);
        await AssertProfileAsync(world, rangedId, "simple", "ranged", ["dex"], 1, 6, "piercing", 80, 320);

        var rangedBefore = Assert.Single((await world.GetEntityAsync(rangedId))!.Components,
            component => component.DefinitionId == Profile).Data;
        foreach (var invalidRange in new[]
                 {
                     """{"mode":"correct","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"}}""",
                     """{"mode":"correct","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"},"rangeFeet":{"normal":0,"long":320}}""",
                     """{"mode":"correct","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"},"rangeFeet":{"normal":82,"long":320}}""",
                     """{"mode":"correct","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"},"rangeFeet":{"normal":325,"long":320}}""",
                     """{"mode":"correct","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"},"rangeFeet":{"normal":80,"long":320,"extra":1}}"""
                 })
        {
            var rejected = await runner.RunAsync(Request("correct ranged weapon profile", invalidRange, rangedId));
            Assert.False(rejected.Ok, invalidRange);
            Assert.Equal(rangedBefore, Assert.Single((await world.GetEntityAsync(rangedId))!.Components,
                component => component.DefinitionId == Profile).Data);
        }

        foreach (var invalid in new[]
                 {
                     "null",
                     "[]",
                     "\"profile\"",
                     "{}",
                     "{\"mode\":\"record\",\"category\":\"Simple\",\"kind\":\"melee\",\"attackAbilities\":[\"str\"],\"damage\":{\"count\":1,\"faces\":8,\"type\":\"slashing\"}}",
                     "{\"mode\":\"record\",\"category\":\"martial\",\"kind\":\"melee\",\"attackAbilities\":[\"dex\",\"str\"],\"damage\":{\"count\":1,\"faces\":8,\"type\":\"slashing\"}}",
                     "{\"mode\":\"record\",\"category\":\"martial\",\"kind\":\"melee\",\"attackAbilities\":[\"str\",\"str\"],\"damage\":{\"count\":1,\"faces\":8,\"type\":\"slashing\"}}",
                     "{\"mode\":\"record\",\"category\":\"martial\",\"kind\":\"melee\",\"attackAbilities\":[\"str\"],\"damage\":{\"count\":0,\"faces\":8,\"type\":\"slashing\"}}",
                     "{\"mode\":\"record\",\"category\":\"martial\",\"kind\":\"melee\",\"attackAbilities\":[\"str\"],\"damage\":{\"count\":1,\"faces\":20,\"type\":\"slashing\"}}",
                     "{\"mode\":\"record\",\"category\":\"martial\",\"kind\":\"melee\",\"attackAbilities\":[\"str\"],\"damage\":{\"count\":1,\"faces\":8,\"type\":\"fire\"}}",
                     "{\"mode\":\"record\",\"category\":\"simple\",\"kind\":\"melee\",\"attackAbilities\":[\"str\",\"dex\"],\"damage\":{\"count\":1,\"faces\":4,\"type\":\"piercing\"},\"propertyTags\":[\"light\",\"finesse\",\"thrown\"],\"thrownRangeFeet\":{\"normal\":20,\"long\":60},\"mastery\":\"nick\"}",
                     "{\"mode\":\"correct\",\"category\":\"martial\",\"kind\":\"melee\",\"attackAbilities\":[\"str\"],\"damage\":{\"count\":1,\"faces\":8,\"type\":\"slashing\"},\"range\":5}"
                 })
        {
            var rejected = await runner.RunAsync(Request("set weapon profile", invalid));
            Assert.False(rejected.Ok, invalid);
            await AssertProfileAsync(world, subject, "martial", "melee", ["str"], 1, 8, "slashing");
        }

        const string absentId = "fixture.catalog.f7.absent";
        await world.CreateEntityAsync("Absent weapon profile", absentId);
        var absentCorrection = await runner.RunAsync(Request(
            "correct weapon profile",
            """{"mode":"correct","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"}}""",
            absentId));
        Assert.False(absentCorrection.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(absentId))!.Components, component => component.DefinitionId == Profile);

        const string corruptId = "fixture.catalog.f7.corrupt";
        const string corruptData = "{\"category\":\"simple\",\"kind\":\"melee\",\"attackAbilities\":[\"dex\",\"str\"],\"damage\":{\"count\":1,\"faces\":4,\"type\":\"piercing\"},\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Equipment > Weapons\"}}";
        await world.CreateEntityAsync("Corrupt weapon profile", corruptId);
        await world.SetComponentAsync(corruptId, Profile, corruptData);
        var beforeCorruptCorrection = Assert.Single((await world.GetEntityAsync(corruptId))!.Components,
            component => component.DefinitionId == Profile).Data;
        var corruptCorrection = await runner.RunAsync(Request(
            "correct weapon profile",
            """{"mode":"correct","category":"simple","kind":"ranged","attackAbilities":["dex"],"damage":{"count":1,"faces":6,"type":"piercing"}}""",
            corruptId));
        Assert.False(corruptCorrection.Ok);
        var corrupt = await world.GetEntityAsync(corruptId);
        Assert.Equal(beforeCorruptCorrection, Assert.Single(corrupt!.Components, component => component.DefinitionId == Profile).Data);

        const string replayId = "fixture.catalog.f7.replay";
        await world.CreateEntityAsync("Replay weapon profile", replayId);
        var replay = await runner.RunAsync(Request(
            "record weapon profile",
            """{"mode":"record","category":"martial","kind":"melee","attackAbilities":["str"],"damage":{"count":1,"faces":8,"type":"slashing"},"propertyTags":["versatile"],"versatileDamage":{"count":1,"faces":10,"type":"slashing"},"mastery":"topple"}""",
            replayId));
        Assert.True(replay.Ok, replay.Error?.Why);
        var original = Assert.Single((await world.GetEntityAsync(subject))!.Components, component => component.DefinitionId == Profile).Data;
        var replayed = Assert.Single((await world.GetEntityAsync(replayId))!.Components, component => component.DefinitionId == Profile).Data;
        Assert.Equal(original, replayed);
    }

    [Fact]
    public async Task Imported_catalog_records_corrects_and_guards_weapon_category_proficiencies()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.weapon-proficiencies.write"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.weapon-proficiencies"));
        await AssertProfileAsync(world, "weapon.dnd2024.dagger", "simple", "melee", ["str", "dex"], 1, 4, "piercing");

        const string subject = "fixture.catalog.f7.proficiency.subject";
        await world.CreateEntityAsync("Weapon proficiency fixture", subject);
        await world.SetComponentAsync(subject, "dnd2024.armor-class", """{"value":14,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > D20 Tests > Attack Rolls > Armor Class"}}""");
        await world.SetComponentAsync(subject, "dnd2024.hit-points", """{"current":12,"maximum":12,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Hit Points"}}""");
        await world.SetComponentAsync(subject, "dnd2024.character-level", """{"level":5,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Creation > Character Advancement"}}""");
        var protectedBefore = await SupportingStateAsync(world, subject);
        var runner = CreateRunner(db, world, mechanics);

        var recorded = await runner.RunAsync(SubjectRequest(
            "record weapon proficiencies",
            """{"mode":"record","categories":["simple"]}"""));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.weapon-proficiencies.write", recorded.Mechanic?.Id);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Single(recorded.Output!.Effects);
        Assert.Equal(EffectType.ComponentAdd, recorded.Output.Effects[0].Type);
        await AssertProficienciesAsync(world, subject, ["simple"]);
        Assert.Equal(protectedBefore, await SupportingStateAsync(world, subject));

        var corrected = await runner.RunAsync(SubjectRequest(
            "correct weapon proficiencies",
            """{"mode":"correct","categories":["simple","martial"]}"""));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Single(corrected.Output!.Effects);
        Assert.Equal(EffectType.ComponentSet, corrected.Output.Effects[0].Type);
        using (var result = JsonDocument.Parse(corrected.Output.Data))
        {
            Assert.Equal(["simple"], result.RootElement.GetProperty("previousCategories")
                .EnumerateArray().Select(item => item.GetString()!).ToArray());
        }
        await AssertProficienciesAsync(world, subject, ["simple", "martial"]);
        Assert.Equal(protectedBefore, await SupportingStateAsync(world, subject));

        foreach (var (id, categories) in new[]
                 {
                     ("fixture.catalog.f7.proficiency.empty", "[]"),
                     ("fixture.catalog.f7.proficiency.martial", "[\"martial\"]"),
                     ("fixture.catalog.f7.proficiency.both", "[\"simple\",\"martial\"]")
                 })
        {
            await world.CreateEntityAsync("Weapon proficiency boundary fixture", id);
            var recordedCategory = await runner.RunAsync(SubjectRequest(
                "record weapon proficiencies",
                $$"""{"mode":"record","categories":{{categories}}}""",
                id));
            Assert.True(recordedCategory.Ok, recordedCategory.Error?.Why);
        }
        await AssertProficienciesAsync(world, "fixture.catalog.f7.proficiency.empty", []);
        await AssertProficienciesAsync(world, "fixture.catalog.f7.proficiency.martial", ["martial"]);
        await AssertProficienciesAsync(world, "fixture.catalog.f7.proficiency.both", ["simple", "martial"]);

        var duplicate = await runner.RunAsync(SubjectRequest(
            "record weapon proficiencies",
            """{"mode":"record","categories":["simple"]}"""));
        Assert.False(duplicate.Ok);
        await AssertProficienciesAsync(world, subject, ["simple", "martial"]);

        foreach (var invalid in new[]
                 {
                     "null",
                     "[]",
                     "\"proficiencies\"",
                     "{}",
                     "{\"mode\":\"record\",\"categories\":[\"Simple\"]}",
                     "{\"mode\":\"record\",\"categories\":[\"other\"]}",
                     "{\"mode\":\"record\",\"categories\":[1]}",
                     "{\"mode\":\"record\",\"categories\":[\"simple\",\"simple\"]}",
                     "{\"mode\":\"record\",\"categories\":[\"martial\",\"simple\"]}",
                     "{\"mode\":\"record\",\"categories\":\"simple\"}",
                     "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"sourceRef\":{}}",
                     "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"class\":\"fighter\"}",
                     "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"proficiencyBonus\":3}",
                     "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"weapon\":\"weapon.dnd2024.dagger\"}",
                     "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"attack\":true}",
                     "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"damage\":4}",
                     "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"effects\":[]}"
                 })
        {
            var rejected = await runner.RunAsync(SubjectRequest("set weapon proficiencies", invalid));
            Assert.False(rejected.Ok, invalid);
            await AssertProficienciesAsync(world, subject, ["simple", "martial"]);
            Assert.Equal(protectedBefore, await SupportingStateAsync(world, subject));
        }

        const string absentId = "fixture.catalog.f7.proficiency.absent";
        await world.CreateEntityAsync("Absent weapon proficiency", absentId);
        var absentCorrection = await runner.RunAsync(SubjectRequest(
            "correct weapon proficiencies",
            """{"mode":"correct","categories":[]}""",
            absentId));
        Assert.False(absentCorrection.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(absentId))!.Components,
            component => component.DefinitionId == Proficiencies);

        const string corruptId = "fixture.catalog.f7.proficiency.corrupt";
        await world.CreateEntityAsync("Corrupt weapon proficiency", corruptId);
        await world.SetComponentAsync(corruptId, Proficiencies,
            """{"categories":["martial","simple"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Weapons > Weapon Proficiency"}}""");
        var beforeCorruptCorrection = Assert.Single((await world.GetEntityAsync(corruptId))!.Components,
            component => component.DefinitionId == Proficiencies).Data;
        var corruptCorrection = await runner.RunAsync(SubjectRequest(
            "correct weapon proficiencies",
            """{"mode":"correct","categories":["simple"]}""",
            corruptId));
        Assert.False(corruptCorrection.Ok);
        Assert.Equal(beforeCorruptCorrection, Assert.Single((await world.GetEntityAsync(corruptId))!.Components,
            component => component.DefinitionId == Proficiencies).Data);

        const string replayId = "fixture.catalog.f7.proficiency.replay";
        await world.CreateEntityAsync("Replay weapon proficiency", replayId);
        var replay = await runner.RunAsync(SubjectRequest(
            "record weapon proficiencies",
            """{"mode":"record","categories":["simple","martial"]}""",
            replayId));
        Assert.True(replay.Ok, replay.Error?.Why);
        var original = Assert.Single((await world.GetEntityAsync(subject))!.Components,
            component => component.DefinitionId == Proficiencies).Data;
        var replayed = Assert.Single((await world.GetEntityAsync(replayId))!.Components,
            component => component.DefinitionId == Proficiencies).Data;
        Assert.Equal(original, replayed);
    }

    private static ActionRequest Request(string intent, string input, string weapon = "fixture.catalog.f7.weapon") => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["weapon"] = weapon },
        Input = input,
        Seed = 1
    };

    private static ActionRequest SubjectRequest(
        string intent,
        string input,
        string subject = "fixture.catalog.f7.proficiency.subject") => new()
    {
        Intent = intent,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject },
        Input = input,
        Seed = 1
    };

    private static async Task AssertProfileAsync(
        WorldStore world,
        string entityId,
        string category,
        string kind,
        string[] abilities,
        long count,
        int faces,
        string damageType,
        int? normalRange = null,
        int? longRange = null)
    {
        var entity = await world.GetEntityAsync(entityId);
        var component = Assert.Single(entity!.Components, component => component.DefinitionId == Profile);
        using var document = JsonDocument.Parse(component.Data);
        var root = document.RootElement;

        Assert.Equal(category, root.GetProperty("category").GetString());
        Assert.Equal(kind, root.GetProperty("kind").GetString());
        Assert.Equal(abilities, root.GetProperty("attackAbilities").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(count, root.GetProperty("damage").GetProperty("count").GetInt64());
        Assert.Equal(faces, root.GetProperty("damage").GetProperty("faces").GetInt32());
        Assert.Equal(damageType, root.GetProperty("damage").GetProperty("type").GetString());
        Assert.Equal(SourceId, root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal(SourceLocator, root.GetProperty("sourceRef").GetProperty("locator").GetString());
        if (normalRange is null || longRange is null)
        {
            Assert.False(root.TryGetProperty("rangeFeet", out _));
        }
        else
        {
            Assert.Equal(normalRange.Value, root.GetProperty("rangeFeet").GetProperty("normal").GetInt32());
            Assert.Equal(longRange.Value, root.GetProperty("rangeFeet").GetProperty("long").GetInt32());
        }
        Assert.True(root.TryGetProperty("propertyTags", out _));
        Assert.True(root.TryGetProperty("mastery", out _));
    }

    private static async Task AssertProficienciesAsync(WorldStore world, string entityId, string[] expected)
    {
        var entity = await world.GetEntityAsync(entityId);
        var component = Assert.Single(entity!.Components, component => component.DefinitionId == Proficiencies);
        using var document = JsonDocument.Parse(component.Data);
        var root = document.RootElement;

        Assert.Equal(expected, root.GetProperty("categories").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(SourceId, root.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal(ProficiencySourceLocator, root.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Equal(2, root.EnumerateObject().Count());
    }

    private static async Task<Dictionary<string, string>> SupportingStateAsync(WorldStore world, string entityId) =>
        (await world.GetEntityAsync(entityId))!.Components
        .Where(component => component.DefinitionId is "dnd2024.armor-class" or "dnd2024.hit-points" or "dnd2024.character-level")
        .ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal);

    private static ActionRunner CreateRunner(
        DantesRoleplayDbContext db,
        WorldStore world,
        MechanicStore mechanics) =>
        new(
            db,
            mechanics,
            new ProjectionResolver(db),
            new JintMechanicEngine(),
            new EffectApplier(db, world),
            new OperationLog(db),
            new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var catalog = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(catalog))
            {
                return Path.GetDirectoryName(catalog)!;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }
}
