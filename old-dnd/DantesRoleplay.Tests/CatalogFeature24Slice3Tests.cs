using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature24Slice3Tests : IDisposable
{
    private const string Subject = "fixture.catalog.f24.s3.subject";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-24-slice-3-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true);
    }

    [Fact]
    public async Task Imported_catalog_reads_only_one_valid_direct_worn_armor_and_held_shield()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.armor-equipment.read"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.armor-equipment"));

        await world.CreateEntityAsync("Armor fixture", Subject);
        await world.SetComponentAsync(Subject, "dnd2024.armor-class", """{"value":14,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > D20 Tests > Attack Rolls > Armor Class"}}""");
        var protectedBefore = await ProtectedStateAsync(world, Subject);
        var runner = Runner(db, world, mechanics);

        var empty = await Read(runner, Subject);
        Assert.True(empty.Ok, empty.Error?.Why);
        Assert.Empty(empty.Output!.Effects);
        AssertSelection(empty.Output.Data, null, null);

        await GrantItem(runner, "fixture.catalog.f24.s3.chain", "Chain Mail", "item.dnd2024.chain-mail.v1", Subject);
        await GrantItem(runner, "fixture.catalog.f24.s3.shield", "Shield", "item.dnd2024.shield.v1", Subject);
        await GrantItem(runner, "fixture.catalog.f24.s3.dagger", "Dagger", "item.dnd2024.dagger.v1", Subject);
        Assert.True((await Run(runner, "wear item", """{"state":"worn"}""", ("item", "fixture.catalog.f24.s3.chain"), ("holder", Subject))).Ok);
        Assert.True((await Run(runner, "hold item", """{"state":"held"}""", ("item", "fixture.catalog.f24.s3.shield"), ("holder", Subject))).Ok);

        var selected = await Read(runner, Subject);
        Assert.True(selected.Ok, selected.Error?.Why);
        Assert.Equal("mechanic.dnd2024.armor-equipment.read", selected.Mechanic!.Id);
        Assert.Equal(0, selected.AppliedCount);
        Assert.Empty(selected.Output!.Effects);
        AssertSelection(selected.Output.Data, "fixture.catalog.f24.s3.chain", "fixture.catalog.f24.s3.shield");
        Assert.Equal(protectedBefore, await ProtectedStateAsync(world, Subject));

        Assert.True((await Run(runner, "unequip item", "{}", ("item", "fixture.catalog.f24.s3.chain"), ("holder", Subject))).Ok);
        Assert.True((await Run(runner, "unequip item", "{}", ("item", "fixture.catalog.f24.s3.shield"), ("holder", Subject))).Ok);
        var unequipped = await Read(runner, Subject);
        Assert.True(unequipped.Ok, unequipped.Error?.Why);
        AssertSelection(unequipped.Output!.Data, null, null);
    }

    [Fact]
    public async Task Reader_fails_closed_for_invalid_or_duplicate_direct_equipment_and_ignores_nested_equipment()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        var runner = Runner(db, world, mechanics);

        const string duplicate = "fixture.catalog.f24.s3.duplicate";
        await world.CreateEntityAsync("Duplicate armor fixture", duplicate);
        await GrantItem(runner, "fixture.catalog.f24.s3.duplicate.chain", "Chain Mail", "item.dnd2024.chain-mail.v1", duplicate);
        await GrantItem(runner, "fixture.catalog.f24.s3.duplicate.leather", "Leather", "item.dnd2024.leather-armor.v1", duplicate);
        Assert.True((await Run(runner, "wear item", """{"state":"worn"}""", ("item", "fixture.catalog.f24.s3.duplicate.chain"), ("holder", duplicate))).Ok);
        Assert.True((await Run(runner, "wear item", """{"state":"worn"}""", ("item", "fixture.catalog.f24.s3.duplicate.leather"), ("holder", duplicate))).Ok);
        Assert.False((await Read(runner, duplicate)).Ok);

        const string missing = "fixture.catalog.f24.s3.missing";
        await world.CreateEntityAsync("Missing state fixture", missing);
        await GrantItem(runner, "fixture.catalog.f24.s3.missing.chain", "Chain Mail", "item.dnd2024.chain-mail.v1", missing);
        Assert.False((await Read(runner, missing)).Ok);

        const string wrongMode = "fixture.catalog.f24.s3.wrong-mode";
        await world.CreateEntityAsync("Wrong mode fixture", wrongMode);
        await GrantItem(runner, "fixture.catalog.f24.s3.wrong-mode.shield", "Shield", "item.dnd2024.shield.v1", wrongMode);
        await world.SetComponentAsync("fixture.catalog.f24.s3.wrong-mode.shield", "dnd2024.equipment-state", """{"state":"worn"}""");
        Assert.False((await Read(runner, wrongMode)).Ok);

        const string stacked = "fixture.catalog.f24.s3.stacked";
        await world.CreateEntityAsync("Stacked armor fixture", stacked);
        await GrantItem(runner, "fixture.catalog.f24.s3.stacked.chain", "Chain Mail", "item.dnd2024.chain-mail.v1", stacked);
        await world.SetComponentAsync("fixture.catalog.f24.s3.stacked.chain", "dnd2024.equipment-state", """{"state":"worn"}""");
        await world.SetComponentAsync("fixture.catalog.f24.s3.stacked.chain", "dnd2024.item-quantity", """{"count":1,"stackKey":"item.dnd2024.chain-mail.v1"}""");
        Assert.False((await Read(runner, stacked)).Ok);

        const string nested = "fixture.catalog.f24.s3.nested";
        await world.CreateEntityAsync("Nested armor fixture", nested);
        await GrantItem(runner, "fixture.catalog.f24.s3.nested.pouch", "Pouch", "item.dnd2024.pouch.v1", nested);
        await GrantItem(runner, "fixture.catalog.f24.s3.nested.chain", "Chain Mail", "item.dnd2024.chain-mail.v1", "fixture.catalog.f24.s3.nested.pouch");
        await world.SetComponentAsync("fixture.catalog.f24.s3.nested.chain", "dnd2024.equipment-state", """{"state":"worn"}""");
        var nestedRead = await Read(runner, nested);
        Assert.True(nestedRead.Ok, nestedRead.Error?.Why);
        AssertSelection(nestedRead.Output!.Data, null, null);

        var badInput = await Run(runner, "read equipped armor and shield", """{"armor":"item.dnd2024.chain-mail.v1"}""", ("subject", nested));
        Assert.False(badInput.Ok);
    }

    private static async Task GrantItem(ActionRunner runner, string itemId, string name, string definition, string destination)
    {
        var result = await Run(runner, "administratively grant physical item", JsonSerializer.Serialize(new { itemId, name, slot = "carried" }), ("definition", definition), ("destination", destination));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static Task<ActionRunResult> Read(ActionRunner runner, string subject) => Run(runner, "read equipped armor and shield", "{}", ("subject", subject));
    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 24, RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal) });

    private static void AssertSelection(string data, string? armorId, string? shieldId)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        Assert.Equal("armor-equipment-read", root.GetProperty("test").GetString());
        AssertSelection(root.GetProperty("armor"), armorId, "armor", "worn");
        AssertSelection(root.GetProperty("shield"), shieldId, "shield", "held");
    }

    private static void AssertSelection(JsonElement selection, string? expectedId, string expectedKind, string expectedState)
    {
        if (expectedId is null)
        {
            Assert.Equal(JsonValueKind.Null, selection.ValueKind);
            return;
        }
        Assert.Equal(expectedId, selection.GetProperty("itemId").GetString());
        Assert.Equal(expectedState, selection.GetProperty("state").GetString());
        Assert.Equal("source.dnd2024.srd-5.2.1", selection.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Equipment > Armor", selection.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Equal(expectedKind == "shield" ? "shield" : "heavy", selection.GetProperty("armorProfile").GetProperty("category").GetString());
    }

    private static async Task<Dictionary<string, string>> ProtectedStateAsync(WorldStore world, string id) =>
        (await world.GetEntityAsync(id))!.Components.Where(component => component.DefinitionId == "dnd2024.armor-class")
            .ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal);

    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world)
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
    }

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
