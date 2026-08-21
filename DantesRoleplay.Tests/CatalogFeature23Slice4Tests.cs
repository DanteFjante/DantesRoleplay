using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature23Slice4Tests : IDisposable
{
    private const string Instance = "dnd2024.item-instance";
    private const string Quantity = "dnd2024.item-quantity";
    private const string Copper = "currency.dnd2024.copper-piece.v1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-slice-4-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Fungible_stacks_create_split_merge_and_consume_without_losing_count()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Carrier", "fixture.catalog.f23.carrier");
        var runner = CreateRunner(db, world, mechanics);

        var created = await runner.RunAsync(Request("administratively grant physical item stack",
            """{"itemId":"fixture.catalog.f23.copper-a","name":"Copper pieces","slot":"pouch","count":10}""",
            ("definition", Copper), ("destination", "fixture.catalog.f23.carrier")));
        Assert.True(created.Ok, created.Error?.Why);
        Assert.Collection(created.Output!.Effects,
            effect => Assert.Equal(EffectType.EntityCreate, effect.Type),
            effect => Assert.Equal(EffectType.ComponentAdd, effect.Type),
            effect => Assert.Equal(EffectType.ComponentAdd, effect.Type),
            effect => Assert.Equal(EffectType.ContainmentMove, effect.Type));
        await AssertStackAsync(world, "fixture.catalog.f23.copper-a", 10, "fixture.catalog.f23.carrier", "pouch");

        var split = await runner.RunAsync(Request("split stack",
            """{"itemId":"fixture.catalog.f23.copper-b","name":"Split copper pieces","count":3}""",
            ("source", "fixture.catalog.f23.copper-a"), ("definition", Copper)));
        Assert.True(split.Ok, split.Error?.Why);
        Assert.Equal(5, split.AppliedCount);
        await AssertStackAsync(world, "fixture.catalog.f23.copper-a", 7, "fixture.catalog.f23.carrier", "pouch");
        await AssertStackAsync(world, "fixture.catalog.f23.copper-b", 3, "fixture.catalog.f23.carrier", "pouch");

        var merged = await runner.RunAsync(Request("merge stacks", "{}",
            ("source", "fixture.catalog.f23.copper-b"), ("target", "fixture.catalog.f23.copper-a"), ("definition", Copper)));
        Assert.True(merged.Ok, merged.Error?.Why);
        Assert.Collection(merged.Output!.Effects,
            effect => Assert.Equal(EffectType.ComponentSet, effect.Type),
            effect => Assert.Equal(EffectType.EntityDelete, effect.Type));
        await AssertStackAsync(world, "fixture.catalog.f23.copper-a", 10, "fixture.catalog.f23.carrier", "pouch");
        Assert.Null(await world.GetEntityAsync("fixture.catalog.f23.copper-b"));

        var partial = await runner.RunAsync(Request("consume items", """{"count":4}""",
            ("item", "fixture.catalog.f23.copper-a"), ("definition", Copper)));
        Assert.True(partial.Ok, partial.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(partial.Output!.Effects).Type);
        await AssertStackAsync(world, "fixture.catalog.f23.copper-a", 6, "fixture.catalog.f23.carrier", "pouch");

        var final = await runner.RunAsync(Request("consume item stack", """{"count":6}""",
            ("item", "fixture.catalog.f23.copper-a"), ("definition", Copper)));
        Assert.True(final.Ok, final.Error?.Why);
        Assert.Equal(EffectType.EntityDelete, Assert.Single(final.Output!.Effects).Type);
        Assert.Null(await world.GetEntityAsync("fixture.catalog.f23.copper-a"));
    }

    [Fact]
    public async Task Stack_operations_reject_incompatible_or_invalid_changes_without_partial_state()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("First carrier", "fixture.catalog.f23.first");
        await world.CreateEntityAsync("Second carrier", "fixture.catalog.f23.second");
        await world.CreateEntityAsync("Loose copper", "fixture.catalog.f23.loose");
        await world.CreateEntityAsync("Loose dagger", "fixture.catalog.f23.dagger");
        var runner = CreateRunner(db, world, mechanics);

        var recorded = await runner.RunAsync(Request("record item instance", "{}",
            ("item", "fixture.catalog.f23.loose"), ("definition", Copper)));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        var stackRecorded = await runner.RunAsync(Request("record item stack", """{"count":2}""",
            ("item", "fixture.catalog.f23.loose"), ("definition", Copper)));
        Assert.True(stackRecorded.Ok, stackRecorded.Error?.Why);
        await AssertStackAsync(world, "fixture.catalog.f23.loose", 2, null, "");

        var daggerInstance = await runner.RunAsync(Request("record item instance", "{}",
            ("item", "fixture.catalog.f23.dagger"), ("definition", "item.dnd2024.dagger.v1")));
        Assert.True(daggerInstance.Ok, daggerInstance.Error?.Why);
        var separateRejected = await runner.RunAsync(Request("record item stack", """{"count":2}""",
            ("item", "fixture.catalog.f23.dagger"), ("definition", "item.dnd2024.dagger.v1")));
        Assert.False(separateRejected.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync("fixture.catalog.f23.dagger"))!.Components, component => component.DefinitionId == Quantity);

        var first = await CreateStackAsync(runner, "fixture.catalog.f23.first-stack", "First copper", "fixture.catalog.f23.first", 5);
        var second = await CreateStackAsync(runner, "fixture.catalog.f23.second-stack", "Second copper", "fixture.catalog.f23.second", 4);
        Assert.True(first.Ok, first.Error?.Why);
        Assert.True(second.Ok, second.Error?.Why);
        var mismatchedContainer = await runner.RunAsync(Request("merge stacks", "{}",
            ("source", "fixture.catalog.f23.first-stack"), ("target", "fixture.catalog.f23.second-stack"), ("definition", Copper)));
        Assert.False(mismatchedContainer.Ok);
        await AssertStackAsync(world, "fixture.catalog.f23.first-stack", 5, "fixture.catalog.f23.first", "carried");
        await AssertStackAsync(world, "fixture.catalog.f23.second-stack", 4, "fixture.catalog.f23.second", "carried");

        var tooLarge = await runner.RunAsync(Request("consume items", """{"count":6}""",
            ("item", "fixture.catalog.f23.first-stack"), ("definition", Copper)));
        Assert.False(tooLarge.Ok);
        await AssertStackAsync(world, "fixture.catalog.f23.first-stack", 5, "fixture.catalog.f23.first", "carried");

        await world.CreateEntityAsync("Contained child", "fixture.catalog.f23.child");
        await world.MoveAsync("fixture.catalog.f23.child", "fixture.catalog.f23.first-stack", "inside");
        var contentRejected = await runner.RunAsync(Request("consume items", """{"count":5}""",
            ("item", "fixture.catalog.f23.first-stack"), ("definition", Copper)));
        Assert.False(contentRejected.Ok);
        await AssertStackAsync(world, "fixture.catalog.f23.first-stack", 5, "fixture.catalog.f23.first", "carried");
        Assert.NotNull(await world.GetEntityAsync("fixture.catalog.f23.child"));
    }

    [Fact]
    public async Task Quantity_schema_rejects_zero_and_non_versioned_stack_key()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        Assert.Contains(contents.Procedures, procedure => procedure.Id == "procedure.mechanic.dnd2024.item-quantity");
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Quantity).Schema);
        using var zero = JsonDocument.Parse("""{"count":0,"stackKey":"currency.dnd2024.copper-piece.v1"}""");
        using var noVersion = JsonDocument.Parse("""{"count":1,"stackKey":"currency.dnd2024.copper-piece"}""");
        Assert.False(schema.Evaluate(zero.RootElement).IsValid);
        Assert.False(schema.Evaluate(noVersion.RootElement).IsValid);
    }

    private static async Task<ActionRunResult> CreateStackAsync(ActionRunner runner, string id, string name, string destination, int count) =>
        await runner.RunAsync(Request("administratively create physical item stack", JsonSerializer.Serialize(new { itemId = id, name, slot = "carried", count }),
            ("definition", Copper), ("destination", destination)));

    private static ActionRequest Request(string intent, string input, params (string role, string id)[] roles) => new()
    {
        Intent = intent,
        Input = input,
        Seed = 23,
        RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal)
    };

    private static async Task AssertStackAsync(WorldStore world, string id, int count, string? containerId, string slot)
    {
        var entity = await world.GetEntityAsync(id);
        Assert.NotNull(entity);
        Assert.Equal(containerId, entity!.ContainerId);
        Assert.Equal(slot, entity.ContainerSlot);
        using var instance = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Instance).Data);
        using var quantity = JsonDocument.Parse(Assert.Single(entity.Components, component => component.DefinitionId == Quantity).Data);
        Assert.Equal(Copper, instance.RootElement.GetProperty("definitionId").GetString());
        Assert.Equal(Copper, quantity.RootElement.GetProperty("stackKey").GetString());
        Assert.Equal(count, quantity.RootElement.GetProperty("count").GetInt32());
    }

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world)
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var result = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(result.Aborted);
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
        }
        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }
}
