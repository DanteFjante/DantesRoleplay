using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature39Tests : IDisposable
{
    private const string Definition = "dnd2024.heroic-inspiration", Profile = "dnd2024.character.profile";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-39-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Heroic_inspiration_is_one_profile_gated_presence_instance()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.heroic-inspiration.grant"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.heroic-inspiration"));

        const string character = "fixture.catalog.f39.character", creature = "fixture.catalog.f39.creature";
        await world.CreateEntityAsync("Character", character);
        await world.SetComponentAsync(character, Profile, "{}");
        await world.SetComponentAsync(character, "dnd2024.abilities", "{\"str\":10,\"dex\":10,\"con\":10,\"int\":10,\"wis\":10,\"cha\":10}");
        await world.CreateEntityAsync("Creature", creature);
        var runner = Runner(db, world, mechanics);
        var abilitiesBefore = Component(await world.GetEntityAsync(character), "dnd2024.abilities");

        var granted = await RunAsync(runner, "grant heroic inspiration", character, "{}");
        Assert.True(granted.Ok, granted.Error?.Why);
        Assert.Equal(1, granted.AppliedCount);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(granted.Output!.Effects).Type);
        Assert.Equal("{}", Component(await world.GetEntityAsync(character), Definition));
        Assert.Equal(abilitiesBefore, Component(await world.GetEntityAsync(character), "dnd2024.abilities"));
        using (var data = JsonDocument.Parse(granted.Output.Data))
        {
            Assert.False(data.RootElement.GetProperty("heldBefore").GetBoolean());
            Assert.True(data.RootElement.GetProperty("heldAfter").GetBoolean());
            Assert.Equal("source.dnd2024.srd-5.2.1", data.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        }

        var beforeDuplicate = Component(await world.GetEntityAsync(character), Definition);
        foreach (var input in new[] { "{}", "{\"mode\":\"grant\"}", "{\"source\":\"human\"}", "{\"count\":1}", "{\"recipient\":\"other\"}", "{\"die\":20}", "[]" })
        {
            var rejected = await RunAsync(runner, "grant heroic inspiration", character, input);
            Assert.False(rejected.Ok, input);
            Assert.Equal(beforeDuplicate, Component(await world.GetEntityAsync(character), Definition));
            Assert.Equal(abilitiesBefore, Component(await world.GetEntityAsync(character), "dnd2024.abilities"));
        }

        var ineligible = await RunAsync(runner, "grant heroic inspiration", creature, "{}");
        Assert.False(ineligible.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(creature))!.Components, component => component.DefinitionId == Definition);
    }

    [Fact]
    public async Task Heroic_inspiration_rejects_corrupt_profile_or_held_state_without_repairing_it()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = Runner(db, world, mechanics);

        const string badProfile = "fixture.catalog.f39.bad-profile", badState = "fixture.catalog.f39.bad-state";
        await world.CreateEntityAsync("Bad profile", badProfile); await world.SetComponentAsync(badProfile, Profile, "{\"unknown\":true}");
        await world.CreateEntityAsync("Bad state", badState); await world.SetComponentAsync(badState, Profile, "{}"); await world.SetComponentAsync(badState, Definition, "{\"available\":true}");

        Assert.False((await RunAsync(runner, "grant heroic inspiration", badProfile, "{}")).Ok);
        Assert.Equal("{\"unknown\":true}", Component(await world.GetEntityAsync(badProfile), Profile));
        Assert.False((await RunAsync(runner, "grant heroic inspiration", badState, "{}")).Ok);
        Assert.Equal("{\"available\":true}", Component(await world.GetEntityAsync(badState), Definition));
    }

    [Fact]
    public async Task Heroic_inspiration_schema_is_an_empty_presence_marker_with_no_reroll_surface()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Definition).Schema);
        using var valid = JsonDocument.Parse("{}");
        using var falseValue = JsonDocument.Parse("{\"available\":false}");
        using var count = JsonDocument.Parse("{\"count\":1}");
        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(falseValue.RootElement).IsValid);
        Assert.False(schema.Evaluate(count.RootElement).IsValid);
        Assert.DoesNotContain(contents.Mechanics, mechanic => mechanic.Id.Contains("heroic-inspiration.reroll", StringComparison.Ordinal));
    }

    private static Task<ActionRunResult> RunAsync(ActionRunner runner, string intent, string subject, string input) =>
        runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject }, Input = input, Seed = 39 });

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string Component(EntitySnapshot? entity, string definition) => Assert.Single(entity!.Components, component => component.DefinitionId == definition).Data;
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
