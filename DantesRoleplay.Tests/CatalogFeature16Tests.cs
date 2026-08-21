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

public sealed class CatalogFeature16Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-16-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Temporary_hit_points_are_positive_nonstacking_and_never_heal()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.temporary-hit-points.write"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.temporary-hit-points"));

        const string subject = "fixture.catalog.f16.subject";
        await world.CreateEntityAsync("Temporary buffer subject", subject);
        await world.SetComponentAsync(subject, "dnd2024.hit-points", HitPoints(4, 12));
        var runner = CreateRunner(db, world, mechanics);
        var hitPointsBefore = Component(await world.GetEntityAsync(subject), "dnd2024.hit-points");

        var granted = await RunAsync(runner, "grant temporary hit points", subject, """{"mode":"grant","amount":8}""");
        Assert.True(granted.Ok, granted.Error?.Why);
        Assert.Equal(1, granted.AppliedCount);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(granted.Output!.Effects).Type);
        AssertTemporary(Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points"), 8);
        Assert.Equal(hitPointsBefore, Component(await world.GetEntityAsync(subject), "dnd2024.hit-points"));

        var kept = await RunAsync(runner, "grant temp hp", subject, """{"mode":"grant","amount":5,"onExisting":"keep"}""");
        Assert.True(kept.Ok, kept.Error?.Why);
        Assert.Equal(0, kept.AppliedCount);
        Assert.Empty(kept.Output!.Effects);
        using (var data = JsonDocument.Parse(kept.Output.Data))
        {
            Assert.True(data.RootElement.GetProperty("kept").GetBoolean());
            Assert.Equal(5, data.RootElement.GetProperty("discardedAmount").GetInt32());
        }
        AssertTemporary(Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points"), 8);

        var replaced = await RunAsync(runner, "replace temporary hit points", subject, """{"mode":"grant","amount":5,"onExisting":"replace"}""");
        Assert.True(replaced.Ok, replaced.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(replaced.Output!.Effects).Type);
        AssertTemporary(Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points"), 5);
        Assert.Equal(hitPointsBefore, Component(await world.GetEntityAsync(subject), "dnd2024.hit-points"));

        var beforeInvalid = Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points");
        foreach (var input in new[]
                 {
                     "{}", """{"mode":"grant","amount":0,"onExisting":"replace"}""",
                     """{"mode":"grant","amount":1.5,"onExisting":"replace"}""",
                     """{"mode":"grant","amount":4}""",
                     """{"mode":"grant","amount":4,"onExisting":"discard"}""",
                     """{"mode":"grant","amount":4,"onExisting":"replace","sourceRef":{}}""",
                     """{"mode":"expire","amount":1}"""
                 })
        {
            var rejected = await RunAsync(runner, "grant temporary hit points", subject, input);
            Assert.False(rejected.Ok, input);
            Assert.Equal(beforeInvalid, Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points"));
            Assert.Equal(hitPointsBefore, Component(await world.GetEntityAsync(subject), "dnd2024.hit-points"));
        }

        var expired = await RunAsync(runner, "expire temporary hit points", subject, """{"mode":"expire"}""");
        Assert.True(expired.Ok, expired.Error?.Why);
        Assert.Equal(EffectType.ComponentRemove, Assert.Single(expired.Output!.Effects).Type);
        Assert.DoesNotContain((await world.GetEntityAsync(subject))!.Components, item => item.DefinitionId == "dnd2024.temporary-hit-points");
        Assert.False((await RunAsync(runner, "expire temporary hit points", subject, """{"mode":"expire"}""")).Ok);

        var maximum = await RunAsync(runner, "grant temporary hit points", subject, """{"mode":"grant","amount":9007199254740991}""");
        Assert.True(maximum.Ok, maximum.Error?.Why);
        AssertTemporary(Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points"), 9007199254740991);
    }

    [Fact]
    public async Task Healing_clamps_without_touching_the_buffer_and_records_its_event()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.healing.apply"));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.healing"));

        const string subject = "fixture.catalog.f16.healing";
        await world.CreateEntityAsync("Healing subject", subject);
        await world.SetComponentAsync(subject, "dnd2024.hit-points", HitPoints(3, 10));
        await world.SetComponentAsync(subject, "dnd2024.temporary-hit-points", Temporary(8));
        var runner = CreateRunner(db, world, mechanics);
        var bufferBefore = Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points");

        var healed = await RunAsync(runner, "heal the character", subject, """{"amount":4}""");
        Assert.True(healed.Ok, healed.Error?.Why);
        Assert.Equal(1, healed.AppliedCount);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(healed.Output!.Effects).Type);
        using (var data = JsonDocument.Parse(healed.Output.Data))
        {
            Assert.Equal(4, data.RootElement.GetProperty("appliedAmount").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("lostToMaximum").GetInt32());
            Assert.Equal(7, data.RootElement.GetProperty("afterCurrent").GetInt32());
        }
        Assert.Equal(7, HitPointCurrent(Component(await world.GetEntityAsync(subject), "dnd2024.hit-points")));
        Assert.Equal(bufferBefore, Component(await world.GetEntityAsync(subject), "dnd2024.temporary-hit-points"));
        await AssertHealingEventAsync(db, healed.OperationId, subject, requested: 4, applied: 4, lost: 0, before: 3, after: 7, maximum: 10);

        await world.SetComponentAsync(subject, "dnd2024.hit-points", HitPoints(3, 10));
        var capped = await RunAsync(runner, "restore hit points", subject, """{"amount":20}""");
        Assert.True(capped.Ok, capped.Error?.Why);
        Assert.Equal(10, HitPointCurrent(Component(await world.GetEntityAsync(subject), "dnd2024.hit-points")));
        await AssertHealingEventAsync(db, capped.OperationId, subject, requested: 20, applied: 7, lost: 13, before: 3, after: 10, maximum: 10);

        var fullBefore = Component(await world.GetEntityAsync(subject), "dnd2024.hit-points");
        var full = await RunAsync(runner, "apply healing", subject, """{"amount":1}""");
        Assert.True(full.Ok, full.Error?.Why);
        Assert.Equal(fullBefore, Component(await world.GetEntityAsync(subject), "dnd2024.hit-points"));
        await AssertHealingEventAsync(db, full.OperationId, subject, requested: 1, applied: 0, lost: 1, before: 10, after: 10, maximum: 10);

        var eventCount = await db.Events.CountAsync(item => item.TypeId == "dnd2024.healing.received");
        foreach (var input in new[] { "{}", """{"amount":0}""", """{"amount":-1}""", """{"amount":1.5}""", """{"amount":9007199254740992}""", """{"amount":1,"maximum":10}""" })
        {
            var rejected = await RunAsync(runner, "heal the character", subject, input);
            Assert.False(rejected.Ok, input);
            Assert.Equal(fullBefore, Component(await world.GetEntityAsync(subject), "dnd2024.hit-points"));
        }
        Assert.Equal(eventCount, await db.Events.CountAsync(item => item.TypeId == "dnd2024.healing.received"));
    }

    [Fact]
    public async Task Weapon_damage_spends_temporary_hit_points_before_hit_points_and_records_the_split()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        const string subject = "fixture.catalog.f16.damage.subject";
        await world.CreateEntityAsync("Damage subject", subject);
        await world.SetComponentAsync(subject, "dnd2024.abilities", """{"str":16,"dex":10,"con":10,"int":10,"wis":10,"cha":10}""");

        const string noBuffer = "fixture.catalog.f16.damage.none";
        await CreateDamageTargetAsync(world, noBuffer, 100, 100);
        var baseline = await ApplyWeaponDamageAsync(runner, subject, noBuffer);
        Assert.True(baseline.Ok, baseline.Error?.Why);
        using var baselineData = JsonDocument.Parse(baseline.Output!.Data);
        var damage = baselineData.RootElement.GetProperty("finalAmount").GetInt32();
        Assert.True(damage > 0);
        Assert.Equal(0, baselineData.RootElement.GetProperty("temporaryBefore").GetInt32());
        Assert.Equal(0, baselineData.RootElement.GetProperty("temporaryAfter").GetInt32());
        Assert.Equal(0, baselineData.RootElement.GetProperty("temporaryAbsorbed").GetInt32());
        Assert.Equal(100 - damage, HitPointCurrent(Component(await world.GetEntityAsync(noBuffer), "dnd2024.hit-points")));
        Assert.Equal(EffectType.ComponentSet, Assert.Single(baseline.Output.Effects).Type);

        const string partial = "fixture.catalog.f16.damage.partial";
        await CreateDamageTargetAsync(world, partial, 100, 100);
        await world.SetComponentAsync(partial, "dnd2024.temporary-hit-points", Temporary(damage - 1));
        var partlyAbsorbed = await ApplyWeaponDamageAsync(runner, subject, partial);
        Assert.True(partlyAbsorbed.Ok, partlyAbsorbed.Error?.Why);
        Assert.Equal(new[] { EffectType.ComponentRemove, EffectType.ComponentSet }, partlyAbsorbed.Output!.Effects.Select(effect => effect.Type));
        Assert.DoesNotContain((await world.GetEntityAsync(partial))!.Components, item => item.DefinitionId == "dnd2024.temporary-hit-points");
        Assert.Equal(99, HitPointCurrent(Component(await world.GetEntityAsync(partial), "dnd2024.hit-points")));
        using (var data = JsonDocument.Parse(partlyAbsorbed.Output.Data))
        {
            Assert.Equal(damage - 1, data.RootElement.GetProperty("temporaryBefore").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("temporaryAfter").GetInt32());
            Assert.Equal(damage - 1, data.RootElement.GetProperty("temporaryAbsorbed").GetInt32());
            Assert.Equal(1, data.RootElement.GetProperty("toHitPoints").GetInt32());
        }
        await AssertDamageEventAsync(db, partlyAbsorbed.OperationId, partial, damage - 1, 0, damage - 1, overkill: 0);

        const string exhausted = "fixture.catalog.f16.damage.exhausted";
        await CreateDamageTargetAsync(world, exhausted, 100, 100);
        await world.SetComponentAsync(exhausted, "dnd2024.temporary-hit-points", Temporary(damage));
        var fullyAbsorbed = await ApplyWeaponDamageAsync(runner, subject, exhausted);
        Assert.True(fullyAbsorbed.Ok, fullyAbsorbed.Error?.Why);
        Assert.Equal(new[] { EffectType.ComponentRemove, EffectType.ComponentSet }, fullyAbsorbed.Output!.Effects.Select(effect => effect.Type));
        var exhaustedState = await world.GetEntityAsync(exhausted);
        Assert.DoesNotContain(exhaustedState!.Components, item => item.DefinitionId == "dnd2024.temporary-hit-points");
        Assert.Equal(100, HitPointCurrent(Component(exhaustedState, "dnd2024.hit-points")));

        const string retained = "fixture.catalog.f16.damage.retained";
        await CreateDamageTargetAsync(world, retained, 100, 100);
        await world.SetComponentAsync(retained, "dnd2024.temporary-hit-points", Temporary(damage + 1));
        var retainedBuffer = await ApplyWeaponDamageAsync(runner, subject, retained);
        Assert.True(retainedBuffer.Ok, retainedBuffer.Error?.Why);
        Assert.Equal(new[] { EffectType.ComponentSet, EffectType.ComponentSet }, retainedBuffer.Output!.Effects.Select(effect => effect.Type));
        AssertTemporary(Component(await world.GetEntityAsync(retained), "dnd2024.temporary-hit-points"), 1);
        Assert.Equal(100, HitPointCurrent(Component(await world.GetEntityAsync(retained), "dnd2024.hit-points")));

        const string overkill = "fixture.catalog.f16.damage.overkill";
        await CreateDamageTargetAsync(world, overkill, 1, 10);
        await world.SetComponentAsync(overkill, "dnd2024.temporary-hit-points", Temporary(1));
        var overkilled = await ApplyWeaponDamageAsync(runner, subject, overkill);
        Assert.True(overkilled.Ok, overkilled.Error?.Why);
        using (var data = JsonDocument.Parse(overkilled.Output!.Data))
        {
            Assert.Equal(Math.Max(0, damage - 2), data.RootElement.GetProperty("overkill").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("afterCurrent").GetInt32());
        }

        const string corrupt = "fixture.catalog.f16.damage.corrupt";
        await CreateDamageTargetAsync(world, corrupt, 10, 10);
        await world.SetComponentAsync(corrupt, "dnd2024.temporary-hit-points", "{}");
        var corruptHitPoints = Component(await world.GetEntityAsync(corrupt), "dnd2024.hit-points");
        var damageEventCount = await db.Events.CountAsync(item => item.TypeId == "dnd2024.damage.dealt");
        var rejected = await ApplyWeaponDamageAsync(runner, subject, corrupt);
        Assert.False(rejected.Ok);
        Assert.Equal(corruptHitPoints, Component(await world.GetEntityAsync(corrupt), "dnd2024.hit-points"));
        Assert.Equal(damageEventCount, await db.Events.CountAsync(item => item.TypeId == "dnd2024.damage.dealt"));
    }

    private static Task<ActionRunResult> RunAsync(ActionRunner runner, string intent, string subject, string input) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = intent,
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject },
            Input = input,
            Seed = 16
        });

    private static Task<ActionRunResult> ApplyWeaponDamageAsync(ActionRunner runner, string subject, string target) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = "apply confirmed weapon damage",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject, ["target"] = target, ["weapon"] = "weapon.dnd2024.dagger" },
            Input = """{"ability":"str","critical":false}""",
            Seed = 51
        });

    private static async Task CreateDamageTargetAsync(WorldStore world, string id, int current, int maximum)
    {
        await world.CreateEntityAsync("Damage target", id);
        await world.SetComponentAsync(id, "dnd2024.hit-points", HitPoints(current, maximum));
    }

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, events: new EventLedger(db)),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string HitPoints(int current, int maximum) => JsonSerializer.Serialize(new
    {
        current,
        maximum,
        sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Damage and Healing > Hit Points" }
    });

    private static string Temporary(int amount) => JsonSerializer.Serialize(new
    {
        amount,
        sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Damage and Healing > Temporary Hit Points" }
    });

    private static int HitPointCurrent(string state)
    {
        using var data = JsonDocument.Parse(state);
        return data.RootElement.GetProperty("current").GetInt32();
    }

    private static async Task AssertHealingEventAsync(DantesRoleplayDbContext db, string? operationId, string target, int requested, int applied, int lost, int before, int after, int maximum)
    {
        Assert.NotNull(operationId);
        var ledger = new EventLedger(db);
        var summary = Assert.Single(await ledger.FindAsync(rootOperationId: operationId), item => item.TypeId == "dnd2024.healing.received");
        var detail = await ledger.GetAsync(summary.Id);
        Assert.NotNull(detail);
        using var payload = JsonDocument.Parse(detail!.PayloadJson);
        Assert.Equal(target, payload.RootElement.GetProperty("targetId").GetString());
        Assert.Equal(requested, payload.RootElement.GetProperty("requestedAmount").GetInt32());
        Assert.Equal(applied, payload.RootElement.GetProperty("appliedAmount").GetInt32());
        Assert.Equal(lost, payload.RootElement.GetProperty("lostToMaximum").GetInt32());
        Assert.Equal(before, payload.RootElement.GetProperty("beforeCurrent").GetInt32());
        Assert.Equal(after, payload.RootElement.GetProperty("afterCurrent").GetInt32());
        Assert.Equal(maximum, payload.RootElement.GetProperty("maximum").GetInt32());
    }

    private static async Task AssertDamageEventAsync(DantesRoleplayDbContext db, string? operationId, string target, int temporaryBefore, int temporaryAfter, int temporaryAbsorbed, int overkill)
    {
        Assert.NotNull(operationId);
        var ledger = new EventLedger(db);
        var summary = Assert.Single(await ledger.FindAsync(rootOperationId: operationId), item => item.TypeId == "dnd2024.damage.dealt");
        var detail = await ledger.GetAsync(summary.Id);
        Assert.NotNull(detail);
        using var payload = JsonDocument.Parse(detail!.PayloadJson);
        Assert.Equal(target, payload.RootElement.GetProperty("targetId").GetString());
        Assert.Equal(temporaryBefore, payload.RootElement.GetProperty("temporaryBefore").GetInt32());
        Assert.Equal(temporaryAfter, payload.RootElement.GetProperty("temporaryAfter").GetInt32());
        Assert.Equal(temporaryAbsorbed, payload.RootElement.GetProperty("temporaryAbsorbed").GetInt32());
        Assert.Equal(overkill, payload.RootElement.GetProperty("overkill").GetInt32());
    }

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, item => item.DefinitionId == definitionId).Data;

    private static void AssertTemporary(string state, long amount)
    {
        using var data = JsonDocument.Parse(state);
        Assert.Equal(amount, data.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("source.dnd2024.srd-5.2.1", data.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Playing the Game > Damage and Healing > Temporary Hit Points", data.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());
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
