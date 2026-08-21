using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature13Tests : IDisposable
{
    private const string Conditions = "dnd2024.conditions";
    private const string Hero = "creature.dnd2024.feature-10.hero";
    private const string Target = "creature.dnd2024.feature-10.training-target";
    private const string Dagger = "weapon.dnd2024.dagger";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-13-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Conditions_writer_records_source_scoped_instances_and_applies_petrified_compatibility()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.conditions"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.conditions.write"));

        const string subject = "fixture.catalog.f13.subject";
        const string sourceOne = "fixture.catalog.f13.source-one";
        const string sourceTwo = "fixture.catalog.f13.source-two";
        await world.CreateEntityAsync("Condition subject", subject);
        await world.CreateEntityAsync("First condition source", sourceOne);
        await world.CreateEntityAsync("Second condition source", sourceTwo);
        var runner = CreateRunner(db, world, mechanics);

        var record = await RunAsync(runner, "record creature conditions", subject, """{"mode":"record"}""");
        Assert.True(record.Ok, record.Error?.Why);
        Assert.Equal("mechanic.dnd2024.conditions.write", record.Mechanic?.Id);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(record.Output!.Effects).Type);
        AssertEntries(Component(await world.GetEntityAsync(subject)), []);

        Assert.True((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "poisoned"))).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "charmed"), sourceOne)).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "charmed"), sourceTwo)).Ok);
        AssertEntries(Component(await world.GetEntityAsync(subject)), [("charmed", sourceOne), ("charmed", sourceTwo), ("poisoned", null)]);

        var duplicate = await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "charmed"), sourceOne);
        Assert.False(duplicate.Ok);
        AssertEntries(Component(await world.GetEntityAsync(subject)), [("charmed", sourceOne), ("charmed", sourceTwo), ("poisoned", null)]);

        var petrified = await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "petrified"));
        Assert.True(petrified.Ok, petrified.Error?.Why);
        using (var result = JsonDocument.Parse(petrified.Output!.Data))
        {
            Assert.Equal("poisoned", result.RootElement.GetProperty("removedPoisoned")[0].GetProperty("condition").GetString());
        }
        AssertEntries(Component(await world.GetEntityAsync(subject)), [("charmed", sourceOne), ("charmed", sourceTwo), ("petrified", null)]);
        Assert.False((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "poisoned"))).Ok);

        Assert.True((await RunAsync(runner, "clear the prone condition", subject, Input("clear", "charmed"), sourceOne)).Ok);
        AssertEntries(Component(await world.GetEntityAsync(subject)), [("charmed", sourceTwo), ("petrified", null)]);
        Assert.True((await RunAsync(runner, "clear the prone condition", subject, Input("clear", "petrified"))).Ok);
        AssertEntries(Component(await world.GetEntityAsync(subject)), [("charmed", sourceTwo)]);
    }

    [Fact]
    public async Task Conditions_writer_rejects_closed_invalid_missing_and_source_unsafe_requests_without_changes()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        const string subject = "fixture.catalog.f13.reject";
        const string sibling = "fixture.catalog.f13.sibling";
        const string source = "fixture.catalog.f13.source";
        await world.CreateEntityAsync("Condition subject", subject);
        await world.CreateEntityAsync("Untouched sibling", sibling);
        await world.CreateEntityAsync("Condition source", source);
        var runner = CreateRunner(db, world, mechanics);

        Assert.False((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "poisoned"))).Ok);
        Assert.True((await RunAsync(runner, "record creature conditions", subject, """{"mode":"record"}""")).Ok);
        var before = Component(await world.GetEntityAsync(subject));
        var siblingBefore = (await world.GetEntityAsync(sibling))!.Components.ToArray();

        foreach (var input in new[]
                 {
                     """{"mode":"apply","conditions":[]}""", """{"mode":"apply","conditions":["Poisoned"]}""",
                     """{"mode":"apply","conditions":["exhaustion"]}""", """{"mode":"apply","conditions":["poisoned","poisoned"]}""",
                     """{"mode":"apply","conditions":"poisoned"}""", """{"mode":"apply","conditions":["poisoned"],"sourceEntityId":"forged"}""",
                     """{"mode":"clear","conditions":["poisoned"]}""", """{"mode":"record","entries":[]}"""
                 })
        {
            Assert.False((await RunAsync(runner, "apply the poisoned condition", subject, input)).Ok, input);
            Assert.Equal(before, Component(await world.GetEntityAsync(subject)));
            Assert.Empty((await world.GetEntityAsync(sibling))!.Components.Except(siblingBefore));
        }

        Assert.False((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "charmed"))).Ok);
        Assert.False((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "grappled"), subject)).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "prone", "blinded"))).Ok);
        AssertEntries(Component(await world.GetEntityAsync(subject)), [("blinded", null), ("prone", null)]);

        await world.SetComponentAsync(subject, Conditions, """{"entries":[{"condition":"poisoned"},{"condition":"petrified"}],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary"}}""");
        var corrupt = Component(await world.GetEntityAsync(subject));
        Assert.False((await RunAsync(runner, "apply the poisoned condition", subject, Input("apply", "prone"), source)).Ok);
        Assert.Equal(corrupt, Component(await world.GetEntityAsync(subject)));
    }

    [Fact]
    public async Task Condition_state_effects_make_poisoned_ability_checks_disadvantaged_without_caller_forgery()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.d20-test.state-effects"));
        var runner = CreateRunner(db, world, mechanics);
        const string checkInput = """{"ability":"wis","skill":"perception","dc":12}""";

        var absent = await AbilityCheckAsync(runner, checkInput, 913);
        Assert.True(absent.Ok, absent.Error?.Why);
        using (var absentData = JsonDocument.Parse(absent.Output!.Data))
        {
            Assert.False(absentData.RootElement.GetProperty("conditionsKnown").GetBoolean());
            Assert.Equal("normal", absentData.RootElement.GetProperty("rollMode").GetString());
            Assert.Empty(absentData.RootElement.GetProperty("derivedCircumstances").EnumerateArray());
        }

        Assert.True((await RunAsync(runner, "record creature conditions", Hero, """{"mode":"record"}""")).Ok);
        var knownEmpty = await AbilityCheckAsync(runner, checkInput, 913);
        Assert.True(knownEmpty.Ok, knownEmpty.Error?.Why);
        using (var oldData = JsonDocument.Parse(absent.Output!.Data))
        using (var newData = JsonDocument.Parse(knownEmpty.Output!.Data))
        {
            foreach (var field in new[] { "rollMode", "rolls", "roll", "total", "succeeded", "modifiers", "rollCircumstances" })
                Assert.True(JsonElement.DeepEquals(oldData.RootElement.GetProperty(field), newData.RootElement.GetProperty(field)), field);
            Assert.True(newData.RootElement.GetProperty("conditionsKnown").GetBoolean());
        }

        Assert.True((await RunAsync(runner, "apply the poisoned condition", Hero, Input("apply", "poisoned"))).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Hero, Input("apply", "charmed"), Target)).Ok);
        var resolved = await runner.RunAsync(new ActionRequest
        {
            Intent = "inspect condition-derived d20 effects",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero },
            Input = "{}",
            Seed = 913
        });
        Assert.True(resolved.Ok, resolved.Error?.Why);
        Assert.Empty(resolved.Output!.Effects);
        using (var stateData = JsonDocument.Parse(resolved.Output.Data))
        {
            Assert.True(stateData.RootElement.GetProperty("conditionsKnown").GetBoolean());
            Assert.Equal(Target, stateData.RootElement.GetProperty("sourcesByCondition").GetProperty("charmed")[0].GetString());
            Assert.Equal("condition:poisoned", stateData.RootElement.GetProperty("byTest").GetProperty("abilityCheck")[0].GetProperty("source").GetString());
        }

        var poisoned = await AbilityCheckAsync(runner, checkInput, 913);
        Assert.True(poisoned.Ok, poisoned.Error?.Why);
        Assert.Single(poisoned.Projection!.Children["stateEffects"]);
        using (var data = JsonDocument.Parse(poisoned.Output!.Data))
        {
            Assert.Equal("disadvantage", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("condition:poisoned", data.RootElement.GetProperty("derivedCircumstances")[0].GetProperty("source").GetString());
            Assert.Equal(2, data.RootElement.GetProperty("rolls").GetArrayLength());
        }

        var cancelled = await AbilityCheckAsync(runner, """{"ability":"wis","skill":"perception","dc":12,"rollCircumstances":[{"kind":"advantage","source":"help"}]}""", 913);
        Assert.True(cancelled.Ok, cancelled.Error?.Why);
        using (var data = JsonDocument.Parse(cancelled.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(1, data.RootElement.GetProperty("rolls").GetArrayLength());
        }
        var stacked = await AbilityCheckAsync(runner, """{"ability":"wis","skill":"perception","dc":12,"rollCircumstances":[{"kind":"disadvantage","source":"fog"}]}""", 913);
        Assert.True(stacked.Ok, stacked.Error?.Why);
        using (var data = JsonDocument.Parse(stacked.Output!.Data)) Assert.Equal("disadvantage", data.RootElement.GetProperty("rollMode").GetString());

        var forged = await AbilityCheckAsync(runner, """{"ability":"wis","skill":"perception","dc":12,"rollCircumstances":[{"kind":"advantage","source":"condition:invisible"}]}""", 913);
        Assert.False(forged.Ok);
        Assert.Empty(forged.Output?.Effects ?? []);
    }

    [Fact]
    public async Task Condition_state_effects_apply_restrained_and_automatic_saving_throw_rules_without_rolling()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var absent = await SavingThrowAsync(runner, """{"ability":"dex","dc":0}""", 914);
        Assert.True(absent.Ok, absent.Error?.Why);
        using (var data = JsonDocument.Parse(absent.Output!.Data))
        {
            Assert.False(data.RootElement.GetProperty("conditionsKnown").GetBoolean());
            Assert.Equal("rolled", data.RootElement.GetProperty("resolution").GetString());
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
        }

        Assert.True((await RunAsync(runner, "record creature conditions", Hero, """{"mode":"record"}""")).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Hero, Input("apply", "restrained"))).Ok);
        var restrainedDex = await SavingThrowAsync(runner, """{"ability":"dex","dc":0}""", 914);
        Assert.True(restrainedDex.Ok, restrainedDex.Error?.Why);
        using (var data = JsonDocument.Parse(restrainedDex.Output!.Data))
        {
            Assert.Equal("disadvantage", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("condition:restrained", data.RootElement.GetProperty("derivedCircumstances")[0].GetProperty("source").GetString());
            Assert.Equal(2, data.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("automaticFailure").ValueKind);
        }
        var restrainedWis = await SavingThrowAsync(runner, """{"ability":"wis","dc":0}""", 914);
        Assert.True(restrainedWis.Ok, restrainedWis.Error?.Why);
        using (var data = JsonDocument.Parse(restrainedWis.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Empty(data.RootElement.GetProperty("derivedCircumstances").EnumerateArray());
        }

        Assert.True((await RunAsync(runner, "clear the prone condition", Hero, Input("clear", "restrained"))).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Hero, Input("apply", "paralyzed", "petrified"))).Ok);
        var automatic = await SavingThrowAsync(runner, """{"ability":"str","dc":0,"voluntaryFailure":true}""", 914);
        Assert.True(automatic.Ok, automatic.Error?.Why);
        Assert.Empty(automatic.Output!.Effects);
        using (var data = JsonDocument.Parse(automatic.Output.Data))
        {
            Assert.Equal("automatic-failure", data.RootElement.GetProperty("resolution").GetString());
            Assert.Equal("condition:paralyzed", data.RootElement.GetProperty("automaticFailure").GetString());
            Assert.True(data.RootElement.GetProperty("voluntaryFailure").GetBoolean());
            Assert.Equal(0, data.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("roll").ValueKind);
            Assert.False(data.RootElement.GetProperty("succeeded").GetBoolean());
        }
        var unaffected = await SavingThrowAsync(runner, """{"ability":"wis","dc":0}""", 914);
        Assert.True(unaffected.Ok, unaffected.Error?.Why);
        using (var data = JsonDocument.Parse(unaffected.Output!.Data))
        {
            Assert.Equal("rolled", data.RootElement.GetProperty("resolution").GetString());
            Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("automaticFailure").ValueKind);
            Assert.True(data.RootElement.GetProperty("succeeded").GetBoolean());
        }
        var forged = await SavingThrowAsync(runner, """{"ability":"wis","dc":0,"rollCircumstances":[{"kind":"advantage","source":"condition:invisible"}]}""", 914);
        Assert.False(forged.Ok);
    }

    [Fact]
    public async Task Weapon_attacks_merge_attacker_and_target_condition_effects_without_changing_hit_rules()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var absent = await WeaponAttackAsync(runner, """{"ability":"dex"}""", 915);
        Assert.True(absent.Ok, absent.Error?.Why);
        using (var data = JsonDocument.Parse(absent.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.False(data.RootElement.GetProperty("attackerConditionsKnown").GetBoolean());
            Assert.False(data.RootElement.GetProperty("targetConditionsKnown").GetBoolean());
        }

        Assert.True((await RunAsync(runner, "record creature conditions", Hero, """{"mode":"record"}""")).Ok);
        Assert.True((await RunAsync(runner, "record creature conditions", Target, """{"mode":"record"}""")).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Hero, Input("apply", "blinded"))).Ok);
        var blindedAttacker = await WeaponAttackAsync(runner, """{"ability":"dex"}""", 915);
        Assert.True(blindedAttacker.Ok, blindedAttacker.Error?.Why);
        using (var data = JsonDocument.Parse(blindedAttacker.Output!.Data))
        {
            Assert.Equal("disadvantage", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("condition:blinded", data.RootElement.GetProperty("attackerDerivedCircumstances")[0].GetProperty("source").GetString());
        }
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Target, Input("apply", "blinded"))).Ok);
        var blindedBoth = await WeaponAttackAsync(runner, """{"ability":"dex"}""", 915);
        Assert.True(blindedBoth.Ok, blindedBoth.Error?.Why);
        using (var data = JsonDocument.Parse(blindedBoth.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("condition:blinded", data.RootElement.GetProperty("targetDerivedCircumstances")[0].GetProperty("source").GetString());
            Assert.Equal(1, data.RootElement.GetProperty("rolls").GetArrayLength());
        }

        Assert.True((await RunAsync(runner, "clear the prone condition", Hero, Input("clear", "blinded"))).Ok);
        Assert.True((await RunAsync(runner, "clear the prone condition", Target, Input("clear", "blinded"))).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Hero, Input("apply", "invisible"))).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Target, Input("apply", "invisible"))).Ok);
        var invisibleBoth = await WeaponAttackAsync(runner, """{"ability":"dex"}""", 915);
        Assert.True(invisibleBoth.Ok, invisibleBoth.Error?.Why);
        using (var data = JsonDocument.Parse(invisibleBoth.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal("condition:invisible", data.RootElement.GetProperty("attackerDerivedCircumstances")[0].GetProperty("source").GetString());
            Assert.Equal("condition:invisible", data.RootElement.GetProperty("targetDerivedCircumstances")[0].GetProperty("source").GetString());
        }

        Assert.True((await RunAsync(runner, "clear the prone condition", Hero, Input("clear", "invisible"))).Ok);
        Assert.True((await RunAsync(runner, "clear the prone condition", Target, Input("clear", "invisible"))).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Hero, Input("apply", "poisoned", "prone", "restrained"))).Ok);
        Assert.True((await RunAsync(runner, "apply the poisoned condition", Target, Input("apply", "paralyzed"))).Ok);
        var merged = await WeaponAttackAsync(runner, """{"ability":"dex"}""", 915);
        Assert.True(merged.Ok, merged.Error?.Why);
        using (var data = JsonDocument.Parse(merged.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(3, data.RootElement.GetProperty("attackerDerivedCircumstances").GetArrayLength());
            Assert.Equal("condition:paralyzed", data.RootElement.GetProperty("targetDerivedCircumstances")[0].GetProperty("source").GetString());
            Assert.True(data.RootElement.GetProperty("attackerConditionsKnown").GetBoolean());
            Assert.True(data.RootElement.GetProperty("targetConditionsKnown").GetBoolean());
        }
        var forged = await WeaponAttackAsync(runner, """{"ability":"dex","rollCircumstances":[{"kind":"advantage","source":"condition:invisible"}]}""", 915);
        Assert.False(forged.Ok);
    }

    [Fact]
    public async Task Initiative_merges_condition_effects_without_changing_encounter_order_rules()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var absent = await InitiativeAsync(runner, "{}", 916);
        Assert.True(absent.Ok, absent.Error?.Why);
        using (var data = JsonDocument.Parse(absent.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.False(data.RootElement.GetProperty("conditionsKnown").GetBoolean());
            Assert.Empty(data.RootElement.GetProperty("derivedCircumstances").EnumerateArray());
        }

        Assert.True((await RunAsync(runner, "record creature conditions", Hero, """{"mode":"record"}""")).Ok);
        var knownEmpty = await InitiativeAsync(runner, "{}", 916);
        Assert.True(knownEmpty.Ok, knownEmpty.Error?.Why);
        using (var absentData = JsonDocument.Parse(absent.Output!.Data))
        using (var knownData = JsonDocument.Parse(knownEmpty.Output!.Data))
        {
            foreach (var field in new[] { "rollMode", "rolls", "roll", "initiative", "modifiers", "rollCircumstances" })
                Assert.True(JsonElement.DeepEquals(absentData.RootElement.GetProperty(field), knownData.RootElement.GetProperty(field)), field);
            Assert.True(knownData.RootElement.GetProperty("conditionsKnown").GetBoolean());
        }

        Assert.True((await RunAsync(runner, "apply the stunned condition", Hero, Input("apply", "stunned"))).Ok);
        var stunned = await InitiativeAsync(runner, "{}", 916);
        Assert.True(stunned.Ok, stunned.Error?.Why);
        using (var data = JsonDocument.Parse(stunned.Output!.Data))
        {
            Assert.Equal("disadvantage", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(2, data.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal("condition:incapacitated", data.RootElement.GetProperty("derivedCircumstances")[0].GetProperty("source").GetString());
        }

        Assert.True((await RunAsync(runner, "apply the invisible condition", Hero, Input("apply", "invisible"))).Ok);
        var cancelled = await InitiativeAsync(runner, "{}", 916);
        Assert.True(cancelled.Ok, cancelled.Error?.Why);
        using (var data = JsonDocument.Parse(cancelled.Output!.Data))
        {
            Assert.Equal("normal", data.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(1, data.RootElement.GetProperty("rolls").GetArrayLength());
            Assert.Equal(2, data.RootElement.GetProperty("derivedCircumstances").GetArrayLength());
            Assert.Equal("condition:incapacitated", data.RootElement.GetProperty("derivedCircumstances")[0].GetProperty("source").GetString());
            Assert.Equal("condition:invisible", data.RootElement.GetProperty("derivedCircumstances")[1].GetProperty("source").GetString());
        }

        var forged = await InitiativeAsync(runner, """{"rollCircumstances":[{"kind":"advantage","source":"condition:invisible"}]}""", 916);
        Assert.False(forged.Ok);
        Assert.Empty(forged.Output?.Effects ?? []);
    }

    [Fact]
    public async Task Conditions_prohibit_only_their_turn_budget_resources_and_take_precedence_over_exhaustion()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);
        var initiative = await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = "encounter.dnd2024.feature-10.training" },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 100
        });
        Assert.True(initiative.Ok, initiative.Error?.Why);
        Assert.True((await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = "encounter.dnd2024.feature-10.training" },
            Input = "{}",
            Seed = 917
        })).Ok);

        Assert.True((await RunAsync(runner, "record creature conditions", Hero, """{"mode":"record"}""")).Ok);
        Assert.True((await RunAsync(runner, "apply the incapacitated condition", Hero, Input("apply", "incapacitated"))).Ok);
        var before = Component(await world.GetEntityAsync(Hero));
        foreach (var resource in new[] { "action", "bonusAction", "reaction" })
        {
            var prohibited = await SpendAsync(runner, Hero, JsonSerializer.Serialize(new { resource }));
            Assert.False(prohibited.Ok);
            Assert.Contains("condition:incapacitated", prohibited.Error?.Why ?? string.Empty);
            Assert.Empty(prohibited.Output?.Effects ?? []);
            Assert.Equal(before, Component(await world.GetEntityAsync(Hero)));
        }

        var interaction = await SpendAsync(runner, Hero, """{"resource":"freeInteraction"}""");
        Assert.True(interaction.Ok, interaction.Error?.Why);
        Assert.True((await RunAsync(runner, "clear the incapacitated condition", Hero, Input("clear", "incapacitated"))).Ok);
        var restoredAction = await SpendAsync(runner, Hero, """{"resource":"action"}""");
        Assert.True(restoredAction.Ok, restoredAction.Error?.Why);
        Assert.True((await RunAsync(runner, "apply the incapacitated condition", Hero, Input("apply", "incapacitated"))).Ok);
        var exhaustedAction = await SpendAsync(runner, Hero, """{"resource":"action"}""");
        Assert.False(exhaustedAction.Ok);
        Assert.Contains("condition:incapacitated", exhaustedAction.Error?.Why ?? string.Empty);

        Assert.True((await RunAsync(runner, "clear the incapacitated condition", Hero, Input("clear", "incapacitated"))).Ok);
        Assert.True((await RunAsync(runner, "apply the stunned condition", Hero, Input("apply", "stunned"))).Ok);
        var implied = await SpendAsync(runner, Hero, """{"resource":"reaction"}""");
        Assert.False(implied.Ok);
        Assert.Contains("condition:incapacitated", implied.Error?.Why ?? string.Empty);
        Assert.True((await RunAsync(runner, "clear the stunned condition", Hero, Input("clear", "stunned"))).Ok);

        foreach (var condition in new[] { "grappled", "paralyzed", "petrified", "restrained", "stunned", "unconscious" })
        {
            Assert.True((await RunAsync(runner, "apply the condition", Hero, Input("apply", condition), condition == "grappled" ? Target : null)).Ok, condition);
            var movementBefore = Component(await world.GetEntityAsync(Hero));
            var prohibited = await SpendAsync(runner, Hero, """{"resource":"movement","feet":5}""");
            Assert.False(prohibited.Ok, condition);
            Assert.Contains("condition:" + condition, prohibited.Error?.Why ?? string.Empty);
            Assert.Empty(prohibited.Output?.Effects ?? []);
            Assert.Equal(movementBefore, Component(await world.GetEntityAsync(Hero)));
            Assert.True((await RunAsync(runner, "clear the condition", Hero, Input("clear", condition))).Ok, condition);
        }
    }

    private static string Input(string mode, params string[] conditions) => JsonSerializer.Serialize(new { mode, conditions });

    private static Task<ActionRunResult> RunAsync(ActionRunner runner, string intent, string subject, string input, string? source = null)
    {
        var roles = new Dictionary<string, string> { ["subject"] = subject };
        if (source is not null) roles["source"] = source;
        return runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = roles, Input = input, Seed = 713 });
    }

    private static Task<ActionRunResult> AbilityCheckAsync(ActionRunner runner, string input, long seed) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = "perception check",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero },
            Input = input,
            Seed = seed
        });

    private static Task<ActionRunResult> SavingThrowAsync(ActionRunner runner, string input, long seed) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = "make a saving throw",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero },
            Input = input,
            Seed = seed
        });

    private static Task<ActionRunResult> WeaponAttackAsync(ActionRunner runner, string input, long seed) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = "attack target with dagger",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero, ["target"] = Target, ["weapon"] = Dagger },
            Input = input,
            Seed = seed
        });

    private static Task<ActionRunResult> InitiativeAsync(ActionRunner runner, string input, long seed) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = "roll initiative",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = Hero },
            Input = input,
            Seed = seed
        });

    private static Task<ActionRunResult> SpendAsync(ActionRunner runner, string subject, string input)
    {
        using var document = JsonDocument.Parse(input);
        var intent = document.RootElement.GetProperty("resource").GetString() switch
        {
            "action" => "spend my action",
            "bonusAction" => "use my bonus action",
            "reaction" => "use my reaction",
            "freeInteraction" => "use my free interaction",
            "movement" => "move 5 feet",
            _ => throw new InvalidOperationException("Unknown turn-budget resource.")
        };
        return runner.RunAsync(new ActionRequest
        {
            Intent = intent,
            RoleEntityIds = new Dictionary<string, string>
            {
                ["subject"] = subject,
                ["encounter"] = "encounter.dnd2024.feature-10.training"
            },
            Input = input,
            Seed = 917
        });
    }

    private static string Component(EntitySnapshot? entity) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == Conditions).Data;

    private static void AssertEntries(string data, (string Condition, string? Source)[] expected)
    {
        using var document = JsonDocument.Parse(data);
        var entries = document.RootElement.GetProperty("entries");
        Assert.Equal(expected.Length, entries.GetArrayLength());
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Condition, entries[index].GetProperty("condition").GetString());
            if (expected[index].Source is null) Assert.False(entries[index].TryGetProperty("sourceEntityId", out _));
            else Assert.Equal(expected[index].Source, entries[index].GetProperty("sourceEntityId").GetString());
        }
    }

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

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
