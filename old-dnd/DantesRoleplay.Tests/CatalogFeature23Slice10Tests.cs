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

public sealed class CatalogFeature23Slice10Tests : IDisposable
{
    private const string VoucherDefinition = "fixture.f23.s10.voucher.v1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-23-slice-10-catalog-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, true); }

    [Fact]
    public async Task Fixed_activity_consumes_its_stack_and_creates_only_its_declared_grant_atomically()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s10.ari");
        await CreateVoucherDefinition(world);
        var runner = Runner(db, world, mechanics);
        await GrantVoucherStack(runner, "fixture.f23.s10.vouchers", 2);

        var first = await Run(runner, "redeem item", """{"activityId":"redeem","grantItemId":"fixture.f23.s10.dagger-one"}""",
            ("item", "fixture.f23.s10.vouchers"), ("definition", VoucherDefinition), ("grantDefinition", "item.dnd2024.dagger.v1"));
        Assert.True(first.Ok, first.Error?.Why);
        Assert.Collection(first.Output!.Effects,
            effect => Assert.Equal(EffectType.ComponentSet, effect.Type),
            effect => Assert.Equal(EffectType.EntityCreate, effect.Type),
            effect => Assert.Equal(EffectType.ComponentAdd, effect.Type),
            effect => Assert.Equal(EffectType.ContainmentMove, effect.Type));
        await AssertStackCount(world, "fixture.f23.s10.vouchers", 1);
        await AssertGrant(world, "fixture.f23.s10.dagger-one");

        var second = await Run(runner, "use item activity", """{"activityId":"redeem","grantItemId":"fixture.f23.s10.dagger-two"}""",
            ("item", "fixture.f23.s10.vouchers"), ("definition", VoucherDefinition), ("grantDefinition", "item.dnd2024.dagger.v1"));
        Assert.True(second.Ok, second.Error?.Why);
        Assert.Equal(EffectType.EntityDelete, second.Output!.Effects[0].Type);
        Assert.Null(await world.GetEntityAsync("fixture.f23.s10.vouchers"));
        await AssertGrant(world, "fixture.f23.s10.dagger-two");
    }

    [Fact]
    public async Task Activity_refuses_mismatched_grant_or_direct_contents_without_partial_mutation()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        await ImportAsync(db, mechanics, world);
        await world.CreateEntityAsync("Ari", "fixture.f23.s10.ari");
        await CreateVoucherDefinition(world);
        var runner = Runner(db, world, mechanics);
        await GrantVoucherStack(runner, "fixture.f23.s10.vouchers", 1);

        var mismatched = await Run(runner, "redeem item", """{"activityId":"redeem","grantItemId":"fixture.f23.s10.invalid"}""",
            ("item", "fixture.f23.s10.vouchers"), ("definition", VoucherDefinition), ("grantDefinition", "item.dnd2024.hempen-rope-50-foot.v1"));
        Assert.False(mismatched.Ok);
        await AssertStackCount(world, "fixture.f23.s10.vouchers", 1);
        Assert.Null(await world.GetEntityAsync("fixture.f23.s10.invalid"));

        await world.CreateEntityAsync("Attached note", "fixture.f23.s10.note");
        await world.MoveAsync("fixture.f23.s10.note", "fixture.f23.s10.vouchers", "inside");
        var contained = await Run(runner, "open consumable item", """{"activityId":"redeem","grantItemId":"fixture.f23.s10.invalid"}""",
            ("item", "fixture.f23.s10.vouchers"), ("definition", VoucherDefinition), ("grantDefinition", "item.dnd2024.dagger.v1"));
        Assert.False(contained.Ok);
        await AssertStackCount(world, "fixture.f23.s10.vouchers", 1);
        Assert.NotNull(await world.GetEntityAsync("fixture.f23.s10.note"));
        Assert.Null(await world.GetEntityAsync("fixture.f23.s10.invalid"));
    }

    [Fact]
    public async Task Activity_schema_is_closed_against_arbitrary_effects()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        Assert.Contains(contents.Procedures, procedure => procedure.Id == "procedure.mechanic.dnd2024.item-activity");
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == "dnd2024.item-activity").Schema);
        using var invalid = JsonDocument.Parse("""{"activities":[{"id":"redeem","kind":"consume-and-grant-item","consumeQuantity":1,"grant":{"definitionId":"item.dnd2024.dagger.v1","name":"Dagger","slot":"carried"},"effects":[{"type":"component.set"}]}]}""");
        Assert.False(schema.Evaluate(invalid.RootElement).IsValid);
    }

    private static async Task CreateVoucherDefinition(WorldStore world)
    {
        await world.CreateEntityAsync("Fixture redemption voucher definition", VoucherDefinition);
        await world.SetComponentAsync(VoucherDefinition, "dnd2024.item-definition", """{"definitionVersion":1,"kind":"adventuring-gear","stackPolicy":"fungible","massPounds":{"numerator":0,"denominator":1},"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Equipment > Adventuring Gear"}}""");
        await world.SetComponentAsync(VoucherDefinition, "dnd2024.item-activity", """{"activities":[{"id":"redeem","kind":"consume-and-grant-item","consumeQuantity":1,"grant":{"definitionId":"item.dnd2024.dagger.v1","name":"Dagger","slot":"carried"}}]}""");
    }

    private static async Task GrantVoucherStack(ActionRunner runner, string itemId, int count)
    {
        var result = await Run(runner, "administratively grant physical item stack", JsonSerializer.Serialize(new { itemId, name = "Redemption vouchers", slot = "carried", count }), ("definition", VoucherDefinition), ("destination", "fixture.f23.s10.ari"));
        Assert.True(result.Ok, result.Error?.Why);
    }

    private static async Task AssertStackCount(WorldStore world, string itemId, int expected)
    {
        var item = await world.GetEntityAsync(itemId); Assert.NotNull(item);
        using var quantity = JsonDocument.Parse(Assert.Single(item!.Components, component => component.DefinitionId == "dnd2024.item-quantity").Data);
        Assert.Equal(expected, quantity.RootElement.GetProperty("count").GetInt32());
    }

    private static async Task AssertGrant(WorldStore world, string itemId)
    {
        var item = await world.GetEntityAsync(itemId); Assert.NotNull(item);
        Assert.Equal("fixture.f23.s10.ari", item!.ContainerId); Assert.Equal("carried", item.ContainerSlot);
        using var instance = JsonDocument.Parse(Assert.Single(item.Components, component => component.DefinitionId == "dnd2024.item-instance").Data);
        Assert.Equal("item.dnd2024.dagger.v1", instance.RootElement.GetProperty("definitionId").GetString());
        Assert.DoesNotContain(item.Components, component => component.DefinitionId == "dnd2024.item-quantity");
    }

    private static Task<ActionRunResult> Run(ActionRunner runner, string intent, string input, params (string role, string id)[] roles) => runner.RunAsync(new ActionRequest { Intent = intent, Input = input, Seed = 10, RoleEntityIds = roles.ToDictionary(pair => pair.role, pair => pair.id, StringComparer.Ordinal) });
    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) => new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
    private async Task ImportAsync(DantesRoleplayDbContext db, MechanicStore mechanics, WorldStore world) { CopyDirectory(RepositoryCatalog(), _catalogCopy); var result = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions()); Assert.False(result.Aborted); }
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); Directory.CreateDirectory(destination); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
}
