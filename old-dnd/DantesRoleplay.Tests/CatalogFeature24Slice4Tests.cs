using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature24Slice4Tests : IDisposable
{
    private const string TrainingSource = "Rules Glossary > Armor Class and Armor Training";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-24-slice-4-catalog-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Imported_catalog_derives_default_armor_and_trained_shield_armor_class_without_legacy_fallback()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.armor-class.read"));
        var legacy = await mechanics.GetAsync("mechanic.dnd2024.armor-class.write");
        Assert.NotNull(legacy); Assert.NotEqual(MechanicStatus.Active, legacy!.Status);
        var runner = Runner(db, world, mechanics);

        await CreateCreatureAsync(world, "fixture.catalog.f24.s4.default", 14);
        await world.SetComponentAsync("fixture.catalog.f24.s4.default", "dnd2024.armor-class", """{"value":99,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > D20 Tests > Attack Rolls > Armor Class"}}""");
        await AssertArmorClassAsync(runner, "fixture.catalog.f24.s4.default", 12, "default", 2, 0);

        await CreateCreatureAsync(world, "fixture.catalog.f24.s4.light", 14);
        await AddEquipmentAsync(world, "fixture.catalog.f24.s4.light", "leather", "item.dnd2024.leather-armor.v1", "worn");
        await AssertArmorClassAsync(runner, "fixture.catalog.f24.s4.light", 13, "light", 2, 0);

        await CreateCreatureAsync(world, "fixture.catalog.f24.s4.medium", 16);
        await AddEquipmentAsync(world, "fixture.catalog.f24.s4.medium", "half-plate", "item.dnd2024.half-plate-armor.v1", "worn");
        await AssertArmorClassAsync(runner, "fixture.catalog.f24.s4.medium", 17, "medium", 2, 0);

        await CreateCreatureAsync(world, "fixture.catalog.f24.s4.heavy", 18);
        await AddEquipmentAsync(world, "fixture.catalog.f24.s4.heavy", "chain-mail", "item.dnd2024.chain-mail.v1", "worn");
        await AssertArmorClassAsync(runner, "fixture.catalog.f24.s4.heavy", 16, "heavy", 0, 0);

        await CreateCreatureAsync(world, "fixture.catalog.f24.s4.shield-trained", 10, ["shield"]);
        await AddEquipmentAsync(world, "fixture.catalog.f24.s4.shield-trained", "shield", "item.dnd2024.shield.v1", "held");
        await AssertArmorClassAsync(runner, "fixture.catalog.f24.s4.shield-trained", 12, "default", 0, 2);

        await CreateCreatureAsync(world, "fixture.catalog.f24.s4.shield-untrained", 10, []);
        await AddEquipmentAsync(world, "fixture.catalog.f24.s4.shield-untrained", "shield", "item.dnd2024.shield.v1", "held");
        await AssertArmorClassAsync(runner, "fixture.catalog.f24.s4.shield-untrained", 10, "default", 0, 0);

        await CreateCreatureAsync(world, "fixture.catalog.f24.s4.shield-unknown", 10);
        await AddEquipmentAsync(world, "fixture.catalog.f24.s4.shield-unknown", "shield", "item.dnd2024.shield.v1", "held");
        Assert.False((await ReadAsync(runner, "fixture.catalog.f24.s4.shield-unknown")).Ok);
        Assert.False((await Run(runner, "read derived armor class", """{"armorClass":20}""", "fixture.catalog.f24.s4.default")).Ok);
    }

    private static async Task CreateCreatureAsync(WorldStore world, string id, int dexterity, string[]? training = null)
    {
        await world.CreateEntityAsync("Armor Class fixture", id);
        await world.SetComponentAsync(id, "dnd2024.abilities", $$"""{"str":10,"dex":{{dexterity}},"con":10,"int":10,"wis":10,"cha":10}""");
        if (training is not null) await world.SetComponentAsync(id, "dnd2024.armor-training", JsonSerializer.Serialize(new { categories = training, sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = TrainingSource } }));
    }

    private static async Task AddEquipmentAsync(WorldStore world, string holder, string key, string definitionId, string state)
    {
        var itemId = holder + "." + key;
        await world.CreateEntityAsync("Armor fixture item", itemId);
        await world.SetComponentAsync(itemId, "dnd2024.item-instance", JsonSerializer.Serialize(new { definitionId }));
        await world.SetComponentAsync(itemId, "dnd2024.equipment-state", JsonSerializer.Serialize(new { state }));
        await world.MoveAsync(itemId, holder, "equipped");
    }

    private static async Task AssertArmorClassAsync(ActionRunner runner, string subject, int expected, string baseKind, int dexApplied, int shieldBonus)
    {
        var result = await ReadAsync(runner, subject);
        Assert.True(result.Ok, result.Error?.Why); Assert.Empty(result.Output!.Effects);
        using var data = JsonDocument.Parse(result.Output.Data);
        Assert.Equal(expected, data.RootElement.GetProperty("armorClass").GetInt32());
        Assert.Equal(baseKind, data.RootElement.GetProperty("base").GetProperty("kind").GetString());
        Assert.Equal(dexApplied, data.RootElement.GetProperty("base").GetProperty("dexterityModifierApplied").GetInt32());
        Assert.Equal(shieldBonus, data.RootElement.GetProperty("shield").GetProperty("bonusApplied").GetInt32());
    }

    private static Task<ActionRunResult> ReadAsync(ActionRunner runner, string subject) => Run(runner, "read derived armor class", "{}", subject);
    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, string subject) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 24, RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject } });
    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world) { CopyDirectory(RepositoryCatalog(), _catalogCopy); var result = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions()); Assert.False(result.Aborted); }
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
