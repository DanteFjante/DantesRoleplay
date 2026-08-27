using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024AbilityCheckTests
{
    [Fact]
    public async Task Activated_raw_check_derives_modifier_is_effect_free_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        var first = await harness.EvaluateAsync("subject.high", "{\"ability\":\"str\",\"dc\":30}", 77);
        var second = await harness.EvaluateAsync("subject.high", "{\"ability\":\"str\",\"dc\":30}", 77);

        Assert.True(first.Ok, string.Join("; ", first.Problems));
        Assert.True(second.Ok, string.Join("; ", second.Problems));
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        Assert.Equal(first.Run.Output.Narration, second.Run.Output.Narration);
        Assert.Empty(first.Run!.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        using var result = JsonDocument.Parse(first.Run.Output.Data);
        Assert.Equal("ability-check", result.RootElement.GetProperty("test").GetString());
        Assert.Equal("str", result.RootElement.GetProperty("ability").GetString());
        Assert.Equal(10, result.RootElement.GetProperty("modifier").GetInt32());
        var roll = result.RootElement.GetProperty("roll").GetInt32();
        Assert.InRange(roll, 1, 20);
        Assert.Equal(roll + 10, result.RootElement.GetProperty("total").GetInt32());
        Assert.False(result.RootElement.GetProperty("succeeded").GetBoolean());

        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.abilities");
        var action = harness.Action("subject.high", "{\"ability\":\"str\",\"dc\":30}", 77,
            "0123456789abcdef0123456789abcdef");
        var committed = await harness.Runner.RunAsync(action);
        var replay = await harness.Runner.RunAsync(action);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.abilities");
        Assert.Equal(before!.ValueJson, after!.ValueJson);
    }

    [Fact]
    public async Task Raw_check_rejects_undeclared_input_before_an_output()
    {
        await using var harness = await DndHarness.CreateAsync();

        var result = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":10,\"proficiencyBonus\":2}", 77);

        Assert.True(result.Evaluated);
        Assert.False(result.Run!.Ok);
        Assert.Contains("ability, dc, and optional skill", result.Run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Raw_check_has_no_natural_one_or_twenty_override()
    {
        await using var harness = await DndHarness.CreateAsync();
        ApplicationMechanicEvaluationResult? naturalOne = null;
        ApplicationMechanicEvaluationResult? naturalTwenty = null;

        for (var seed = 1; seed <= 512 && (naturalOne is null || naturalTwenty is null); seed++)
        {
            var high = await harness.EvaluateAsync("subject.high", "{\"ability\":\"str\",\"dc\":11}", seed);
            var low = await harness.EvaluateAsync("subject.low", "{\"ability\":\"str\",\"dc\":16}", seed);
            if (Roll(high) == 1) naturalOne = high;
            if (Roll(low) == 20) naturalTwenty = low;
        }

        Assert.NotNull(naturalOne);
        Assert.NotNull(naturalTwenty);
        Assert.True(Succeeded(naturalOne!));
        Assert.False(Succeeded(naturalTwenty!));
    }

    [Fact]
    public async Task Named_skill_check_derives_proficiency_once_from_known_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", 5, ["stealth"]);

        var proficient = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":40,\"skill\":\"stealth\"}", 77);
        var untrained = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":40,\"skill\":\"acrobatics\"}", 77);

        Assert.True(proficient.Ok, proficient.Run?.Error);
        Assert.True(untrained.Ok, untrained.Run?.Error);
        using var proficientData = JsonDocument.Parse(proficient.Run!.Output.Data);
        using var untrainedData = JsonDocument.Parse(untrained.Run!.Output.Data);
        Assert.True(proficientData.RootElement.GetProperty("proficient").GetBoolean());
        Assert.False(untrainedData.RootElement.GetProperty("proficient").GetBoolean());
        Assert.Equal("dex", proficientData.RootElement.GetProperty("defaultAbility").GetString());
        Assert.Equal(3, proficientData.RootElement.GetProperty("total").GetInt32()
            - untrainedData.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Explicit_advantage_and_disadvantage_select_dice_without_stacking()
    {
        await using var harness = await DndHarness.CreateAsync();
        var normal = await harness.EvaluateAsync("subject.high", "{\"ability\":\"str\",\"dc\":40}", 77);
        var advantage = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":40,\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"help\"},{\"kind\":\"advantage\",\"source\":\"feature\"}]}", 77);
        var disadvantage = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":40,\"rollCircumstances\":[{\"kind\":\"disadvantage\",\"source\":\"hazard\"}]}", 77);
        var mixed = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":40,\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"help\"},{\"kind\":\"disadvantage\",\"source\":\"hazard\"}]}", 77);

        Assert.True(normal.Ok, normal.Run?.Error);
        Assert.True(advantage.Ok, advantage.Run?.Error);
        Assert.True(disadvantage.Ok, disadvantage.Run?.Error);
        Assert.True(mixed.Ok, mixed.Run?.Error);
        AssertRollMode(normal, "normal", 1, values => values[0]);
        AssertRollMode(advantage, "advantage", 2, values => Math.Max(values[0], values[1]));
        AssertRollMode(disadvantage, "disadvantage", 2, values => Math.Min(values[0], values[1]));
        AssertRollMode(mixed, "normal", 1, values => values[0]);
        Assert.Equal(Roll(normal), Roll(mixed));
    }

    [Fact]
    public async Task Circumstance_input_is_closed_and_rejects_duplicate_or_deferred_condition_sources()
    {
        await using var harness = await DndHarness.CreateAsync();
        var duplicate = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":10,\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"help\"},{\"kind\":\"advantage\",\"source\":\"help\"}]}", 77);
        var condition = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":10,\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"condition:invisible\"}]}", 77);

        Assert.False(duplicate.Ok);
        Assert.Contains("must not repeat", duplicate.Run?.Error, StringComparison.Ordinal);
        Assert.False(condition.Ok);
        Assert.Contains("explicit advantage or disadvantage", condition.Run?.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_throw_recorder_and_resolver_use_separate_canonical_proficiency_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        var recorded = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.saving-throw-proficiencies.record", "subject.high",
            "{\"abilities\":[\"wis\",\"con\"]}", 0, "c123456789abcdef0123456789abcdef"));
        await harness.AddProficiencyStateAsync("subject.high", 5, []);
        await harness.AddProficiencyStateAsync("subject.low", 5, []);
        await harness.AddSavingThrowStateAsync("subject.low", []);
        var proficient = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"con\",\"dc\":40}", 77, "mechanic.dnd2024.saving-throw");
        var untrained = await harness.EvaluateAsync("subject.low",
            "{\"ability\":\"con\",\"dc\":40}", 77, "mechanic.dnd2024.saving-throw");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.saving-throw-proficiencies");
        Assert.Contains("[\"con\",\"wis\"]", stored!.ValueJson, StringComparison.Ordinal);
        Assert.True(proficient.Ok, proficient.Run?.Error);
        Assert.True(untrained.Ok, untrained.Run?.Error);
        using var proficientData = JsonDocument.Parse(proficient.Run!.Output.Data);
        using var untrainedData = JsonDocument.Parse(untrained.Run!.Output.Data);
        Assert.Equal("saving-throw", proficientData.RootElement.GetProperty("test").GetString());
        Assert.True(proficientData.RootElement.GetProperty("proficient").GetBoolean());
        Assert.False(untrainedData.RootElement.GetProperty("proficient").GetBoolean());
        Assert.Equal(3, proficientData.RootElement.GetProperty("total").GetInt32()
            - untrainedData.RootElement.GetProperty("total").GetInt32());
        Assert.Empty(proficient.Run.Output.Effects);
    }

    [Fact]
    public async Task Saving_throw_supports_d20_modes_and_voluntary_failure_without_a_roll()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", 5, []);
        await harness.AddSavingThrowStateAsync("subject.high", []);
        var advantage = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":40,\"rollCircumstances\":[{\"kind\":\"advantage\",\"source\":\"help\"}]}",
            77, "mechanic.dnd2024.saving-throw");
        var voluntary = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":0,\"voluntaryFailure\":true}", 77,
            "mechanic.dnd2024.saving-throw");

        Assert.True(advantage.Ok, advantage.Run?.Error);
        Assert.True(voluntary.Ok, voluntary.Run?.Error);
        AssertRollMode(advantage, "advantage", 2, values => Math.Max(values[0], values[1]));
        using var data = JsonDocument.Parse(voluntary.Run!.Output.Data);
        Assert.Equal("voluntary-failure", data.RootElement.GetProperty("resolution").GetString());
        Assert.Empty(data.RootElement.GetProperty("rolls").EnumerateArray());
        Assert.False(data.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("total").ValueKind);
        Assert.Empty(voluntary.Run.Output.Effects);
    }

    [Fact]
    public async Task Initiative_derives_dexterity_without_persisting_a_count()
    {
        await using var harness = await DndHarness.CreateAsync();
        var result = await harness.EvaluateAsync("subject.high", "{}", 77,
            "mechanic.dnd2024.initiative.roll");

        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        var root = data.RootElement;
        Assert.Equal("initiative", root.GetProperty("test").GetString());
        Assert.Equal("dex", root.GetProperty("ability").GetString());
        Assert.Equal(root.GetProperty("roll").GetInt32() + 0, root.GetProperty("initiative").GetInt32());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Fresh_host_encounter_composes_initiative_and_transacts_the_turn_lifecycle()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        var first = await harness.EvaluateAsync("subject.high", "{}", DeriveSeed(77, 0),
            "mechanic.dnd2024.initiative.roll");
        var second = await harness.EvaluateAsync("subject.low", "{}", DeriveSeed(77, 1),
            "mechanic.dnd2024.initiative.roll");
        Assert.True(first.Ok, first.Run?.Error);
        Assert.True(second.Ok, second.Run?.Error);
        var ties = Initiative(first) == Initiative(second)
            ? new[] { new[] { "subject.high", "subject.low" } }
            : [];
        var input = JsonSerializer.Serialize(new
        {
            participants = new Dictionary<string, object>
            {
                ["subject.high"] = new(),
                ["subject.low"] = new()
            },
            tieDecisions = ties
        });
        var encounter = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var preview = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.encounter-initiative-order", encounter, input, 77);
        Assert.True(preview.Ok, preview.Run?.Error ?? string.Join("; ", preview.Problems));
        var ordered = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-initiative-order", encounter, input, 77,
            "e123456789abcdef0123456789abcdef"));
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-turn.start", encounter, "{}", 0,
            "f123456789abcdef0123456789abcdef"));
        var advanced = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-turn.advance", encounter, "{}", 0,
            "0123456789abcdef0123456789abcdea"));
        var wrapped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-turn.advance", encounter, "{}", 0,
            "1123456789abcdef0123456789abcdea"));
        var ended = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-turn.end", encounter, "{}", 0,
            "2123456789abcdef0123456789abcdea"));

        Assert.True(ordered.Successful, string.Join("; ", ordered.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        Assert.True(started.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", started.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, advanced.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, wrapped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, ended.Disposition);
        Assert.Equal(1, ordered.AppliedEffectCount);
        Assert.Equal(2, started.AppliedEffectCount);
        Assert.Equal(2, advanced.AppliedEffectCount);
        Assert.Equal(2, wrapped.AppliedEffectCount);
        Assert.Equal(1, ended.AppliedEffectCount);
        var state = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.fixture", "dnd2024.encounter-turn-state");
        using var stateJson = JsonDocument.Parse(state!.ValueJson);
        Assert.Equal("ended", stateJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, stateJson.RootElement.GetProperty("round").GetInt32());
        Assert.Equal(0, stateJson.RootElement.GetProperty("turnIndex").GetInt32());
    }

    [Fact]
    public async Task Fresh_host_combat_primitives_resolve_against_authoritative_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCombatFixturesAsync();
        var roles = new Dictionary<string, string> { ["subject"] = "subject.high", ["weapon"] = "weapon.fixture", ["target"] = "target.fixture" };
        var attack = await harness.EvaluateRolesAsync("mechanic.dnd2024.weapon-attack", roles, "{\"ability\":\"str\"}", 77);
        var damage = await harness.EvaluateRolesAsync("mechanic.dnd2024.weapon-damage.roll",
            new Dictionary<string, string> { ["subject"] = "subject.high", ["weapon"] = "weapon.fixture" },
            "{\"ability\":\"str\",\"critical\":false}", 77);
        Assert.True(attack.Ok, attack.Run?.Error ?? string.Join("; ", attack.Problems));
        Assert.True(damage.Ok, damage.Run?.Error ?? string.Join("; ", damage.Problems));
        Assert.Contains("\"hit\":true", attack.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(damage.Run!.Output.Effects);
        var applied = await harness.Runner.RunAsync(harness.ActionForRoles("mechanic.dnd2024.weapon-damage.apply", roles,
            "{\"ability\":\"str\",\"critical\":false}", 77, "d123456789abcdef0123456789abcdef"));
        Assert.True(applied.Disposition == ApplicationActionExecutionDisposition.Succeeded, string.Join("; ", applied.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        Assert.Equal(1, applied.AppliedEffectCount);
        var hp = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        using var hitPoints = JsonDocument.Parse(hp!.ValueJson);
        Assert.InRange(hitPoints.RootElement.GetProperty("current").GetInt32(), 0, 19);
        Assert.Equal(20, hitPoints.RootElement.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public async Task Fresh_host_slice_12_composes_play_replay_and_unchanged_failure()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        await harness.AddCombatFixturesAsync();
        const string extensionPath =
            "catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/item.dnd2024.hempen-rope-50-foot.v1.json";
        Assert.DoesNotContain(extensionPath, harness.ActiveSourcePaths);

        var first = await harness.EvaluateAsync("subject.high", "{}", DeriveSeed(120, 0),
            "mechanic.dnd2024.initiative.roll");
        var second = await harness.EvaluateAsync("subject.low", "{}", DeriveSeed(120, 1),
            "mechanic.dnd2024.initiative.roll");
        Assert.True(first.Ok, first.Run?.Error);
        Assert.True(second.Ok, second.Run?.Error);
        var ties = Initiative(first) == Initiative(second)
            ? new[] { new[] { "subject.high", "subject.low" } }
            : [];
        var encounterRoles = new Dictionary<string, string>
        {
            ["encounter"] = "encounter.fixture"
        };
        var ordered = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-initiative-order", encounterRoles,
            JsonSerializer.Serialize(new
            {
                participants = new Dictionary<string, object>
                {
                    ["subject.high"] = new(), ["subject.low"] = new()
                },
                tieDecisions = ties
            }), 120, "12000000000000000000000000000001"));
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-turn.start", encounterRoles, "{}", 0,
            "12000000000000000000000000000002"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, ordered.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, started.Disposition);

        var granted = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "target.fixture",
            "{\"mode\":\"grant\",\"amount\":2}", 0,
            "12000000000000000000000000000003"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, granted.Disposition);

        var combatRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["target"] = "target.fixture"
        };
        var damageRequest = harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", combatRoles,
            "{\"ability\":\"str\",\"critical\":false}", 120,
            "12000000000000000000000000000004");
        var damaged = await harness.Runner.RunAsync(damageRequest);
        var afterDamage = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        var replayed = await harness.Runner.RunAsync(damageRequest);
        var afterReplay = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, damaged.Disposition);
        Assert.Equal(2, damaged.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        Assert.Equal(afterDamage!.Revision, afterReplay!.Revision);
        Assert.Equal(afterDamage.ValueJson, afterReplay.ValueJson);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.temporary-hit-points"));

        var healed = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.healing.apply", "target.fixture", "{\"amount\":3}", 0,
            "12000000000000000000000000000005"));
        var afterHealing = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, healed.Disposition);
        Assert.Equal(1, healed.AppliedEffectCount);
        Assert.True(afterHealing!.Revision > afterReplay.Revision);

        await harness.AddDamageTargetAsync("target.slice12.corrupt", 20, 20);
        await harness.AddApplicationComponentAsync("target.slice12.corrupt",
            "dnd2024.temporary-hit-points",
            "{\"amount\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Temporary Hit Points (PDF p. 18)\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.slice12.corrupt", "dnd2024.temporary-hit-points", "{}");
        var corruptBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.slice12.corrupt", "dnd2024.hit-points");
        combatRoles["target"] = "target.slice12.corrupt";
        var rejected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", combatRoles,
            "{\"ability\":\"str\",\"critical\":false}", 120,
            "12000000000000000000000000000006"));
        var corruptAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.slice12.corrupt", "dnd2024.hit-points");
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, rejected.Disposition);
        Assert.Equal(corruptBefore!.Revision, corruptAfter!.Revision);
        Assert.Equal(corruptBefore.ValueJson, corruptAfter.ValueJson);
    }

    [Fact]
    public async Task Combat_recorders_commit_closed_authoritative_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, "weapon.recorder", "Recorder weapon");
        var armor = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.armor-class.write", "subject.high", "{\"mode\":\"record\",\"value\":18}", 0,
            "3123456789abcdef0123456789abcdea"));
        var hitPoints = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.hit-points.write", "subject.high", "{\"mode\":\"record\",\"current\":14,\"maximum\":14}", 0,
            "4123456789abcdef0123456789abcdea"));
        var proficiencies = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.weapon-proficiencies.write", "subject.high", "{\"mode\":\"record\",\"categories\":[\"simple\"]}", 0,
            "5123456789abcdef0123456789abcdea"));
        var profile = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-profile.write", new Dictionary<string, string> { ["weapon"] = "weapon.recorder" },
            "{\"mode\":\"record\",\"category\":\"simple\",\"kind\":\"melee\",\"attackAbilities\":[\"str\",\"dex\"],\"damage\":{\"count\":1,\"faces\":4,\"type\":\"piercing\"}}",
            0, "6123456789abcdef0123456789abcdea"));

        Assert.All([armor, hitPoints, proficiencies, profile], result =>
        {
            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
            Assert.Equal(1, result.AppliedEffectCount);
        });
        var storedHitPoints = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.hit-points");
        var storedProfile = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "weapon.recorder", "dnd2024.weapon-profile");
        Assert.Contains("\"current\":14", storedHitPoints!.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"attackAbilities\":[\"str\",\"dex\"]", storedProfile!.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Proficiency_recorders_commit_canonical_state_through_the_activated_action_path()
    {
        await using var harness = await DndHarness.CreateAsync();
        var level = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.character-level.record", "subject.high", "{\"level\":5}", 0,
            "a123456789abcdef0123456789abcdef"));
        var skills = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.skill-proficiencies.record", "subject.high", "{\"skills\":[\"stealth\",\"athletics\"]}", 0,
            "b123456789abcdef0123456789abcdef"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, level.Disposition);
        Assert.Equal(1, level.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, skills.Disposition);
        Assert.Equal(1, skills.AppliedEffectCount);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.skill-proficiencies");
        Assert.Contains("[\"athletics\",\"stealth\"]", stored!.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Speed_writer_records_corrects_and_replays_canonical_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string recordedInput =
            "{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":15,\"flyFeet\":0,\"swimFeet\":20}";
        var action = harness.ActionFor("mechanic.dnd2024.speed.write", "subject.high", recordedInput, 0,
            "7123456789abcdef0123456789abcdea");

        var recorded = await harness.Runner.RunAsync(action);
        var replayed = await harness.Runner.RunAsync(action);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(1, recorded.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.speed");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Revision);
        Assert.Equal(
            "{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":15,\"flyFeet\":0,\"swimFeet\":20,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}",
            stored.ValueJson);

        var firstRead = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.speed.read");
        var secondRead = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.speed.read");
        Assert.True(firstRead.Ok, firstRead.Run?.Error);
        Assert.True(secondRead.Ok, secondRead.Run?.Error);
        Assert.Equal(firstRead.Run!.Output.Data, secondRead.Run!.Output.Data);
        Assert.Empty(firstRead.Run.Output.Effects);
        Assert.Empty(firstRead.Run.Output.Events);
        Assert.Empty(firstRead.Run.Output.Notifications);
        using (var readData = JsonDocument.Parse(firstRead.Run.Output.Data))
        {
            Assert.True(readData.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal(JsonValueKind.Null, readData.RootElement.GetProperty("problem").ValueKind);
            Assert.Equal(30, readData.RootElement.GetProperty("speed").GetProperty("walkFeet").GetInt32());
            Assert.Equal(15, readData.RootElement.GetProperty("speed").GetProperty("climbFeet").GetInt32());
        }

        const string correctedInput =
            "{\"mode\":\"correct\",\"walkFeet\":40,\"burrowFeet\":10,\"climbFeet\":0,\"flyFeet\":60,\"swimFeet\":0}";
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.speed.write", "subject.high", correctedInput, 0,
            "8123456789abcdef0123456789abcdea"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(1, corrected.AppliedEffectCount);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.speed");
        Assert.Equal(2, stored!.Revision);
        Assert.Equal(
            "{\"walkFeet\":40,\"burrowFeet\":10,\"climbFeet\":0,\"flyFeet\":60,\"swimFeet\":0,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}",
            stored.ValueJson);
    }

    [Fact]
    public async Task Speed_family_rejects_invalid_writes_and_preserves_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        var absent = await harness.EvaluateAsync("subject.low", "{}", 0,
            "mechanic.dnd2024.speed.read");
        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Empty(absent.Run!.Output.Effects);
        Assert.Contains("\"problem\":\"absent\"", absent.Run.Output.Data, StringComparison.Ordinal);

        var invalidRecord = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.speed.write", "subject.low",
            "{\"mode\":\"record\",\"walkFeet\":0,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}",
            0, "9123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalidRecord.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.speed"));

        const string valid =
            "{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}";
        var recorded = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.speed.write", "subject.high", valid, 0,
            "a123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.speed");

        var duplicate = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.speed.write", "subject.high", valid, 0,
            "b123456789abcdef0123456789abcdea"));
        var invalidCorrection = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.speed.write", "subject.high",
            "{\"mode\":\"correct\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":7,\"swimFeet\":0}",
            0, "c123456789abcdef0123456789abcdea"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalidCorrection.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.speed");
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Speed_reader_distinguishes_malformed_and_invalid_persisted_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string valid =
            "{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}";
        var recorded = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.speed.write", "subject.high", valid, 0,
            "d123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);

        var source = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "catalog", "applications", "dnd2024", "mechanics", "movement",
            "mechanic.dnd2024.speed.read.js"));
        var malformed = await new JintMechanicEngine().RunAsync(source, new MechanicProjection
        {
            Seed = 0,
            Input = "{}",
            Roles = new()
            {
                ["subject"] = new("subject.high", "subject.high", new Dictionary<string, string>
                {
                    ["dnd2024.speed"] = "{"
                })
            }
        }, ExecutionLimits.Default);
        Assert.True(malformed.Ok, malformed.Error);
        Assert.Empty(malformed.Output.Effects);
        Assert.Contains("\"problem\":\"malformed\"", malformed.Output.Data, StringComparison.Ordinal);

        await harness.ReplaceSpeedRawAsync("subject.high",
            "{\"walkFeet\":0,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}");
        var invalid = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.speed.read");
        Assert.True(invalid.Ok, invalid.Run?.Error);
        Assert.Empty(invalid.Run!.Output.Effects);
        Assert.Contains("\"problem\":\"invalid\"", invalid.Run.Output.Data, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":5,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}", true)]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":1000,\"burrowFeet\":1000,\"climbFeet\":1000,\"flyFeet\":1000,\"swimFeet\":1000}", true)]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":0,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}", false)]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":1005,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}", false)]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":7,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}", false)]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":7.5,\"swimFeet\":0}", false)]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0,\"currentMovement\":10}", false)]
    [InlineData("{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0}", false)]
    public async Task Speed_writer_enforces_closed_canonical_boundaries(string input, bool expectedOk)
    {
        await using var harness = await DndHarness.CreateAsync();

        var result = await harness.EvaluateAsync("subject.low", input, 0,
            "mechanic.dnd2024.speed.write");

        Assert.True(result.Evaluated);
        Assert.Equal(expectedOk, result.Run!.Ok);
        if (expectedOk) Assert.Single(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Turn_budget_writer_records_corrects_and_replays_exact_canonical_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string recordInput =
            "{\"mode\":\"record\",\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":30}";
        var action = harness.ActionFor("mechanic.dnd2024.turn-budget.write", "subject.high", recordInput, 0,
            "e123456789abcdef0123456789abcdea");

        var recorded = await harness.Runner.RunAsync(action);
        var replayed = await harness.Runner.RunAsync(action);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(1, recorded.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Revision);
        Assert.Equal(
            "{\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":30,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn\"}}",
            stored.ValueJson);

        const string correctInput =
            "{\"mode\":\"correct\",\"action\":false,\"bonusAction\":true,\"reaction\":false,\"freeInteraction\":false,\"movementRemainingFeet\":5}";
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.turn-budget.write", "subject.high", correctInput, 0,
            "f123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(1, corrected.AppliedEffectCount);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");
        Assert.Equal(2, stored!.Revision);
        Assert.Contains("\"action\":false", stored.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"movementRemainingFeet\":5", stored.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turn_budget_writer_rejects_wrong_transitions_and_preserves_exact_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string recordInput =
            "{\"mode\":\"record\",\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":30}";
        var absentCorrection = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.turn-budget.write", "subject.low",
            recordInput.Replace("\"record\"", "\"correct\"", StringComparison.Ordinal), 0,
            "0123456789abcdef0123456789abcdeb"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, absentCorrection.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.turn-budget"));

        var recorded = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.turn-budget.write", "subject.high", recordInput, 0,
            "1123456789abcdef0123456789abcdeb"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");

        var duplicate = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.turn-budget.write", "subject.high", recordInput, 0,
            "2123456789abcdef0123456789abcdeb"));
        await harness.ReplaceTurnBudgetRawAsync("subject.high",
            "{\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":1001,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn\"}}");
        var invalidBytes = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");
        var invalidCorrection = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.turn-budget.write", "subject.high",
            recordInput.Replace("\"record\"", "\"correct\"", StringComparison.Ordinal), 0,
            "3123456789abcdef0123456789abcdeb"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalidCorrection.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");
        Assert.Equal(invalidBytes!.Revision, after!.Revision);
        Assert.Equal(invalidBytes.ValueJson, after.ValueJson);
        Assert.Equal(1, before!.Revision);
    }

    [Theory]
    [InlineData("{\"mode\":\"record\",\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":0}", true)]
    [InlineData("{\"mode\":\"record\",\"action\":false,\"bonusAction\":false,\"reaction\":false,\"freeInteraction\":false,\"movementRemainingFeet\":1000}", true)]
    [InlineData("{\"mode\":\"record\",\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":-1}", false)]
    [InlineData("{\"mode\":\"record\",\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":1001}", false)]
    [InlineData("{\"mode\":\"record\",\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":1.5}", false)]
    [InlineData("{\"mode\":\"record\",\"action\":\"true\",\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":30}", false)]
    [InlineData("{\"mode\":\"record\",\"action\":true,\"bonusAction\":true,\"reaction\":true,\"freeInteraction\":true,\"movementRemainingFeet\":30,\"sourceRef\":{}}", false)]
    public async Task Turn_budget_writer_enforces_closed_canonical_boundaries(string input, bool expectedOk)
    {
        await using var harness = await DndHarness.CreateAsync();

        var result = await harness.EvaluateAsync("subject.low", input, 0,
            "mechanic.dnd2024.turn-budget.write");

        Assert.True(result.Evaluated);
        Assert.Equal(expectedOk, result.Run!.Ok);
        if (expectedOk) Assert.Single(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Conditions_writer_records_scopes_canonicalizes_and_clears_instances()
    {
        await using var harness = await DndHarness.CreateAsync();
        var recorded = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
            "4123456789abcdef0123456789abcdeb"));
        var poisoned = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.conditions.write", "subject.high",
            "{\"mode\":\"apply\",\"conditions\":[\"poisoned\",\"prone\"]}", 0,
            "5123456789abcdef0123456789abcdeb"));
        var roles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["source"] = "subject.low"
        };
        var frightened = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.conditions.write", roles,
            "{\"mode\":\"apply\",\"conditions\":[\"frightened\"]}", 0,
            "6123456789abcdef0123456789abcdeb"));

        Assert.All([recorded, poisoned, frightened], result =>
        {
            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
            Assert.Equal(1, result.AppliedEffectCount);
        });
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        Assert.Equal(3, stored!.Revision);
        Assert.Contains(
            "\"entries\":[{\"condition\":\"frightened\",\"sourceEntityId\":\"subject.low\"},{\"condition\":\"poisoned\"},{\"condition\":\"prone\"}]",
            stored.ValueJson, StringComparison.Ordinal);

        var cleared = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.conditions.write", roles,
            "{\"mode\":\"clear\",\"conditions\":[\"frightened\"]}", 0,
            "7123456789abcdef0123456789abcdeb"));
        var petrified = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.conditions.write", "subject.high",
            "{\"mode\":\"apply\",\"conditions\":[\"petrified\"]}", 0,
            "8123456789abcdef0123456789abcdeb"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, cleared.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, petrified.Disposition);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        Assert.DoesNotContain("frightened", stored!.ValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("poisoned", stored.ValueJson, StringComparison.Ordinal);
        Assert.Contains("petrified", stored.ValueJson, StringComparison.Ordinal);
        Assert.Contains("prone", stored.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Damage_mitigation_writer_records_corrects_and_replays_canonical_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        foreach (var relative in new[]
                 {
                     "catalog/applications/dnd2024/components/combat/dnd2024.damage-mitigation.json",
                     "catalog/applications/dnd2024/mechanics/combat/mechanic.dnd2024.damage-mitigation.write.md",
                     "catalog/applications/dnd2024/mechanics/combat/mechanic.dnd2024.damage.resolve.md",
                     "catalog/applications/dnd2024/procedures/combat/procedure.mechanic.dnd2024.damage-mitigation.md",
                     "catalog/applications/dnd2024/procedures/combat/procedure.mechanic.dnd2024.damage.resolve.md"
                 })
            Assert.Contains(relative, harness.ActiveSourcePaths);

        const string input =
            "{\"mode\":\"record\",\"resistances\":[\"fire\",\"acid\"],\"immunities\":[\"poison\"],\"vulnerabilities\":[\"cold\"]}";
        var request = harness.ActionFor(
            "mechanic.dnd2024.damage-mitigation.write", "subject.high", input, 0,
            "aa23456789abcdef0123456789abcdea");
        var recorded = await harness.Runner.RunAsync(request);
        var replayed = await harness.Runner.RunAsync(request);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(1, recorded.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.damage-mitigation");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Revision);
        Assert.Equal(
            "{\"resistances\":[\"acid\",\"fire\"],\"immunities\":[\"poison\"],\"vulnerabilities\":[\"cold\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Resistance and Vulnerability; Immunity (PDF p. 17)\"}}",
            stored.ValueJson);

        const string correctedInput =
            "{\"mode\":\"correct\",\"resistances\":[\"thunder\"],\"immunities\":[\"fire\"],\"vulnerabilities\":[]}";
        var correctionPreview = await harness.EvaluateAsync(
            "subject.high", correctedInput, 0, "mechanic.dnd2024.damage-mitigation.write");
        Assert.True(correctionPreview.Ok, correctionPreview.Run?.Error);
        Assert.Contains("\"previous\":{\"resistances\":[\"acid\",\"fire\"]",
            correctionPreview.Run!.Output.Data, StringComparison.Ordinal);
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.damage-mitigation.write", "subject.high",
            correctedInput,
            0, "ab23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(1, corrected.AppliedEffectCount);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.damage-mitigation");
        Assert.Equal(2, stored!.Revision);
        Assert.Contains("\"resistances\":[\"thunder\"]", stored.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Damage_mitigation_profile_composes_conditions_and_distinguishes_unknown_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        var absent = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.damage.resolve",
            new Dictionary<string, string> { ["defender"] = "subject.low" }, "{}", 0);

        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Empty(absent.Run!.Output.Effects);
        Assert.Empty(absent.Run.Output.Events);
        Assert.Empty(absent.Run.Output.Notifications);
        using (var data = JsonDocument.Parse(absent.Run.Output.Data))
        {
            Assert.False(data.RootElement.GetProperty("mitigationKnown").GetBoolean());
            Assert.False(data.RootElement.GetProperty("conditionsKnown").GetBoolean());
            Assert.Equal(0, data.RootElement.GetProperty("resistances").GetArrayLength());
            Assert.False(data.RootElement.GetProperty("petrified").GetBoolean());
        }

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.damage-mitigation.write", "subject.high",
                "{\"mode\":\"record\",\"resistances\":[\"cold\"],\"immunities\":[\"poison\"],\"vulnerabilities\":[\"fire\"]}",
                0, "ac23456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "ad23456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high",
                "{\"mode\":\"apply\",\"conditions\":[\"petrified\"]}", 0,
                "ae23456789abcdef0123456789abcdea"))).Disposition);

        var first = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.damage.resolve",
            new Dictionary<string, string> { ["defender"] = "subject.high" }, "{}", 0);
        var second = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.damage.resolve",
            new Dictionary<string, string> { ["defender"] = "subject.high" }, "{}", 0);
        Assert.True(first.Ok, first.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        using var profile = JsonDocument.Parse(first.Run.Output.Data);
        Assert.True(profile.RootElement.GetProperty("mitigationKnown").GetBoolean());
        Assert.True(profile.RootElement.GetProperty("conditionsKnown").GetBoolean());
        Assert.True(profile.RootElement.GetProperty("petrified").GetBoolean());
        Assert.Equal("cold", profile.RootElement.GetProperty("resistances")[0].GetString());
        Assert.Equal("poison", profile.RootElement.GetProperty("immunities")[0].GetString());
        Assert.Equal("fire", profile.RootElement.GetProperty("vulnerabilities")[0].GetString());
    }

    [Fact]
    public async Task Damage_mitigation_family_rejects_invalid_input_and_corrupt_state_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        var invalid = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.damage-mitigation.write", "subject.low",
            "{\"mode\":\"record\",\"resistances\":[\"fire\",\"fire\"],\"immunities\":[],\"vulnerabilities\":[]}",
            0, "af23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.damage-mitigation"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.damage-mitigation.write", "subject.high",
                "{\"mode\":\"record\",\"resistances\":[\"acid\"],\"immunities\":[],\"vulnerabilities\":[]}",
                0, "ba23456789abcdef0123456789abcdea"))).Disposition);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.damage-mitigation");
        var duplicateRecord = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.damage-mitigation.write", "subject.high",
            "{\"mode\":\"record\",\"resistances\":[],\"immunities\":[],\"vulnerabilities\":[]}",
            0, "bb23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicateRecord.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.damage-mitigation");
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);

        await harness.ReplaceApplicationComponentRawAsync(
            "subject.high", "dnd2024.damage-mitigation",
            "{\"resistances\":[\"fire\",\"acid\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Resistance and Vulnerability; Immunity (PDF p. 17)\"}}");
        var corruptProfile = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.damage.resolve",
            new Dictionary<string, string> { ["defender"] = "subject.high" }, "{}", 0);
        Assert.False(corruptProfile.Ok);
        Assert.Empty(corruptProfile.Run!.Output.Effects);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.low", "{\"mode\":\"record\"}", 0,
                "bc23456789abcdef0123456789abcdea"))).Disposition);
        await harness.ReplaceConditionsRawAsync("subject.low", "{\"entries\":[]}");
        var corruptConditions = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.damage.resolve",
            new Dictionary<string, string> { ["defender"] = "subject.low" }, "{}", 0);
        Assert.False(corruptConditions.Ok);
        Assert.True(corruptConditions.Run is null || corruptConditions.Run.Output.Effects.Count == 0);
    }

    [Fact]
    public async Task Weapon_damage_mitigation_applies_srd_order_once_and_replays_one_hp_write()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCombatFixturesAsync();
        const string locator =
            "Playing the Game > Damage and Healing > Resistance and Vulnerability; Immunity (PDF p. 17)";
        await harness.AddDamageTargetAsync("target.resistant", 100, 100,
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.vulnerable", 100, 100,
            "{\"resistances\":[],\"immunities\":[],\"vulnerabilities\":[\"piercing\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.combined", 100, 100,
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[\"piercing\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.immune", 100, 100,
            "{\"resistances\":[\"piercing\"],\"immunities\":[\"piercing\"],\"vulnerabilities\":[\"piercing\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.petrified", 100, 100,
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "target.petrified", "{\"mode\":\"record\"}", 0,
                "bd23456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "target.petrified",
                "{\"mode\":\"apply\",\"conditions\":[\"petrified\"]}", 0,
                "be23456789abcdef0123456789abcdea"))).Disposition);

        static Dictionary<string, string> Roles(string target) => new()
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture", ["target"] = target
        };
        const string input = "{\"ability\":\"str\",\"critical\":false}";
        var normal = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.fixture"), input, 77);
        var resistant = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.resistant"), input, 77);
        var vulnerable = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.vulnerable"), input, 77);
        var combined = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.combined"), input, 77);
        var immune = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.immune"), input, 77);
        var petrified = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.petrified"), input, 77);
        Assert.All([normal, resistant, vulnerable, combined, immune, petrified], result =>
            Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems)));

        using var normalData = JsonDocument.Parse(normal.Run!.Output.Data);
        using var resistantData = JsonDocument.Parse(resistant.Run!.Output.Data);
        using var vulnerableData = JsonDocument.Parse(vulnerable.Run!.Output.Data);
        using var combinedData = JsonDocument.Parse(combined.Run!.Output.Data);
        using var immuneData = JsonDocument.Parse(immune.Run!.Output.Data);
        using var petrifiedData = JsonDocument.Parse(petrified.Run!.Output.Data);
        var raw = normalData.RootElement.GetProperty("rawDamage").GetInt32();
        Assert.True(raw > 0);
        Assert.Equal(raw, normalData.RootElement.GetProperty("damage").GetInt32());
        Assert.Equal(raw / 2, resistantData.RootElement.GetProperty("damage").GetInt32());
        Assert.Equal(raw * 2, vulnerableData.RootElement.GetProperty("damage").GetInt32());
        Assert.Equal((raw / 2) * 2, combinedData.RootElement.GetProperty("damage").GetInt32());
        Assert.Equal(0, immuneData.RootElement.GetProperty("damage").GetInt32());
        Assert.True(immuneData.RootElement.GetProperty("immune").GetBoolean());
        Assert.False(immuneData.RootElement.GetProperty("resistanceApplied").GetBoolean());
        Assert.False(immuneData.RootElement.GetProperty("vulnerabilityApplied").GetBoolean());
        Assert.Empty(immune.Run.Output.Effects);
        Assert.Equal(raw / 2, petrifiedData.RootElement.GetProperty("damage").GetInt32());
        Assert.Equal(2, petrifiedData.RootElement.GetProperty("resistanceReasons").GetArrayLength());
        Assert.Equal("damage-mitigation:piercing",
            petrifiedData.RootElement.GetProperty("resistanceReasons")[0].GetString());
        Assert.Equal("condition:petrified",
            petrifiedData.RootElement.GetProperty("resistanceReasons")[1].GetString());

        var request = harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.fixture"), input, 77,
            "bf23456789abcdef0123456789abcdea");
        var applied = await harness.Runner.RunAsync(request);
        var replayed = await harness.Runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(1, applied.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var normalHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        Assert.Equal(2, normalHp!.Revision);
        using (var hp = JsonDocument.Parse(normalHp.ValueJson))
            Assert.Equal(20 - raw, hp.RootElement.GetProperty("current").GetInt32());

        var immuneAction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", Roles("target.immune"), input, 77,
            "ca23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, immuneAction.Disposition);
        Assert.Equal(0, immuneAction.AppliedEffectCount);
        var immuneHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.immune", "dnd2024.hit-points");
        Assert.Equal(1, immuneHp!.Revision);
        using (var hp = JsonDocument.Parse(immuneHp.ValueJson))
            Assert.Equal(100, hp.RootElement.GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task Weapon_damage_mitigation_rejects_corrupt_profile_before_hp_effect()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCombatFixturesAsync();
        const string locator =
            "Playing the Game > Damage and Healing > Resistance and Vulnerability; Immunity (PDF p. 17)";
        await harness.AddDamageTargetAsync("target.corrupt", 40, 40,
            "{\"resistances\":[\"acid\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.corrupt", "dnd2024.damage-mitigation",
            "{\"resistances\":[\"fire\",\"acid\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        var roles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture", ["target"] = "target.corrupt"
        };
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.corrupt", "dnd2024.hit-points");
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", roles,
            "{\"ability\":\"str\",\"critical\":false}", 77,
            "cb23456789abcdef0123456789abcdea"));
        var injected = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", roles,
            "{\"ability\":\"str\",\"critical\":false,\"damage\":999}", 77);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.False(injected.Ok);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.corrupt", "dnd2024.hit-points");
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Temporary_hit_points_are_positive_nonstacking_replayable_and_expirable()
    {
        await using var harness = await DndHarness.CreateAsync();
        var hpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.hit-points");
        var grant = harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":8}", 0,
            "d123456789abcdef0123456789abcdea");
        var granted = await harness.Runner.RunAsync(grant);
        var replayed = await harness.Runner.RunAsync(grant);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, granted.Disposition);
        Assert.Equal(1, granted.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var buffer = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.temporary-hit-points");
        Assert.NotNull(buffer);
        Assert.Equal(1, buffer.Revision);
        Assert.Contains("\"amount\":8", buffer.ValueJson, StringComparison.Ordinal);
        Assert.Contains("Temporary Hit Points (PDF p. 18)", buffer.ValueJson,
            StringComparison.Ordinal);

        var kept = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":12,\"onExisting\":\"keep\"}", 0,
            "e123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, kept.Disposition);
        Assert.Equal(0, kept.AppliedEffectCount);
        var afterKeep = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.temporary-hit-points");
        Assert.Equal(buffer.Revision, afterKeep!.Revision);
        Assert.Equal(buffer.ValueJson, afterKeep.ValueJson);

        var replaced = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":5,\"onExisting\":\"replace\"}", 0,
            "f123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, replaced.Disposition);
        Assert.Equal(1, replaced.AppliedEffectCount);
        buffer = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.temporary-hit-points");
        Assert.Equal(2, buffer!.Revision);
        Assert.Contains("\"amount\":5", buffer.ValueJson, StringComparison.Ordinal);

        var invalid = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":0,\"onExisting\":\"keep\"}", 0,
            "0123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        var afterInvalid = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.temporary-hit-points");
        Assert.Equal(buffer.Revision, afterInvalid!.Revision);
        Assert.Equal(buffer.ValueJson, afterInvalid.ValueJson);

        var expired = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"expire\"}", 0, "1123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, expired.Disposition);
        Assert.Equal(1, expired.AppliedEffectCount);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.temporary-hit-points"));
        var absentExpiry = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"expire\"}", 0, "2123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, absentExpiry.Disposition);
        Assert.Equal(hpBefore, await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.hit-points"));
    }

    [Fact]
    public async Task Healing_clamps_preserves_temporary_hp_and_avoids_a_full_hp_write()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddDamageTargetAsync("target.healing", 3, 10);
        await harness.AddApplicationComponentAsync("target.healing", "dnd2024.temporary-hit-points",
            "{\"amount\":8,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Temporary Hit Points (PDF p. 18)\"}}");
        var roles = new Dictionary<string, string> { ["subject"] = "target.healing" };
        var preview = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.healing.apply", roles, "{\"amount\":20}", 0);
        Assert.True(preview.Ok, preview.Run?.Error);
        using (var data = JsonDocument.Parse(preview.Run!.Output.Data))
        {
            Assert.Equal(7, data.RootElement.GetProperty("appliedAmount").GetInt32());
            Assert.Equal(13, data.RootElement.GetProperty("lostToMaximum").GetInt32());
            Assert.Equal(10, data.RootElement.GetProperty("afterCurrent").GetInt32());
        }
        Assert.Single(preview.Run.Output.Effects);
        Assert.Empty(preview.Run.Output.Events);

        var temporaryBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.temporary-hit-points");
        var healed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.healing.apply", roles, "{\"amount\":4}", 0,
            "3123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, healed.Disposition);
        Assert.Equal(1, healed.AppliedEffectCount);
        var hp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.hit-points");
        Assert.Contains("\"current\":7", hp!.ValueJson, StringComparison.Ordinal);
        var temporaryAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.temporary-hit-points");
        Assert.Equal(temporaryBefore!.Revision, temporaryAfter!.Revision);
        Assert.Equal(temporaryBefore.ValueJson, temporaryAfter.ValueJson);

        var capped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.healing.apply", roles, "{\"amount\":20}", 0,
            "4123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, capped.Disposition);
        Assert.Equal(1, capped.AppliedEffectCount);
        var fullBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.hit-points");
        Assert.Contains("\"current\":10", fullBefore!.ValueJson, StringComparison.Ordinal);
        var atMaximumRequest = harness.ActionForRoles(
            "mechanic.dnd2024.healing.apply", roles, "{\"amount\":1}", 0,
            "5123456789abcdef0123456789abcdea");
        var atMaximum = await harness.Runner.RunAsync(atMaximumRequest);
        var atMaximumReplay = await harness.Runner.RunAsync(atMaximumRequest);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, atMaximum.Disposition);
        Assert.Equal(0, atMaximum.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, atMaximumReplay.Disposition);
        var fullAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.hit-points");
        Assert.Equal(fullBefore.Revision, fullAfter!.Revision);
        Assert.Equal(fullBefore.ValueJson, fullAfter.ValueJson);
    }

    [Fact]
    public async Task Weapon_damage_spends_temporary_hp_after_mitigation_before_hp_atomically()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCombatFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["target"] = "target.fixture"
        };
        const string input = "{\"ability\":\"str\",\"critical\":false}";
        var baseline = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", roles, input, 77);
        Assert.True(baseline.Ok, baseline.Run?.Error);
        using var baselineData = JsonDocument.Parse(baseline.Run!.Output.Data);
        var raw = baselineData.RootElement.GetProperty("damage").GetInt32();
        Assert.True(raw > 2);
        Assert.Equal(0, baselineData.RootElement.GetProperty("temporaryBefore").GetInt32());
        Assert.Equal(raw, baselineData.RootElement.GetProperty("hitPointDamage").GetInt32());

        static string Temporary(int amount) => JsonSerializer.Serialize(new
        {
            amount,
            sourceRef = new
            {
                sourceId = "source.dnd2024.srd-5.2.1",
                locator = "Playing the Game > Damage and Healing > Temporary Hit Points (PDF p. 18)"
            }
        });
        const string mitigationLocator =
            "Playing the Game > Damage and Healing > Resistance and Vulnerability; Immunity (PDF p. 17)";
        await harness.AddDamageTargetAsync("target.temp.partial", 20, 20);
        await harness.AddApplicationComponentAsync(
            "target.temp.partial", "dnd2024.temporary-hit-points", Temporary(raw - 1));
        await harness.AddDamageTargetAsync("target.temp.exact", 20, 20);
        await harness.AddApplicationComponentAsync(
            "target.temp.exact", "dnd2024.temporary-hit-points", Temporary(raw));
        await harness.AddDamageTargetAsync("target.temp.retained", 20, 20);
        await harness.AddApplicationComponentAsync(
            "target.temp.retained", "dnd2024.temporary-hit-points", Temporary(raw + 1));
        await harness.AddDamageTargetAsync("target.temp.resistant", 20, 20,
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + mitigationLocator + "\"}}");
        await harness.AddApplicationComponentAsync(
            "target.temp.resistant", "dnd2024.temporary-hit-points", Temporary(1));
        await harness.AddDamageTargetAsync("target.temp.overkill", 1, 20);
        await harness.AddApplicationComponentAsync(
            "target.temp.overkill", "dnd2024.temporary-hit-points", Temporary(1));

        static Dictionary<string, string> TargetRoles(string target) => new()
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture", ["target"] = target
        };
        var partial = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.partial"), input, 77);
        var exact = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.exact"), input, 77);
        var retained = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.retained"), input, 77);
        var resistant = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.resistant"), input, 77);
        var overkill = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.overkill"), input, 77);
        Assert.All([partial, exact, retained, resistant, overkill], result =>
            Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems)));
        using (var data = JsonDocument.Parse(partial.Run!.Output.Data))
        {
            Assert.Equal(raw - 1, data.RootElement.GetProperty("temporaryAbsorbed").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("temporaryAfter").GetInt32());
            Assert.Equal(1, data.RootElement.GetProperty("hitPointDamage").GetInt32());
            Assert.Equal(2, partial.Run.Output.Effects.Count);
        }
        using (var data = JsonDocument.Parse(exact.Run!.Output.Data))
        {
            Assert.Equal(raw, data.RootElement.GetProperty("temporaryAbsorbed").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("hitPointDamage").GetInt32());
            Assert.Single(exact.Run.Output.Effects);
        }
        using (var data = JsonDocument.Parse(retained.Run!.Output.Data))
        {
            Assert.Equal(1, data.RootElement.GetProperty("temporaryAfter").GetInt32());
            Assert.Equal(0, data.RootElement.GetProperty("hitPointDamage").GetInt32());
            Assert.Single(retained.Run.Output.Effects);
        }
        using (var data = JsonDocument.Parse(resistant.Run!.Output.Data))
        {
            var mitigated = raw / 2;
            Assert.Equal(mitigated, data.RootElement.GetProperty("damage").GetInt32());
            Assert.Equal(1, data.RootElement.GetProperty("temporaryAbsorbed").GetInt32());
            Assert.Equal(mitigated - 1,
                data.RootElement.GetProperty("hitPointDamage").GetInt32());
        }
        using (var data = JsonDocument.Parse(overkill.Run!.Output.Data))
            Assert.Equal(raw - 2, data.RootElement.GetProperty("overkill").GetInt32());

        var partialRequest = harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.partial"), input, 77,
            "8123456789abcdef0123456789abcdea");
        var applied = await harness.Runner.RunAsync(partialRequest);
        var replayed = await harness.Runner.RunAsync(partialRequest);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(2, applied.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.partial", "dnd2024.temporary-hit-points"));
        var partialHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.partial", "dnd2024.hit-points");
        Assert.Equal(2, partialHp!.Revision);
        Assert.Contains("\"current\":19", partialHp.ValueJson, StringComparison.Ordinal);

        var exactApplied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.exact"), input, 77,
            "9123456789abcdef0123456789abcdea"));
        Assert.Equal(1, exactApplied.AppliedEffectCount);
        var exactHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.exact", "dnd2024.hit-points");
        Assert.Equal(1, exactHp!.Revision);
        Assert.Contains("\"current\":20", exactHp.ValueJson, StringComparison.Ordinal);

        var retainedApplied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", TargetRoles("target.temp.retained"), input, 77,
            "a123456789abcdef0123456789abcdea"));
        Assert.Equal(1, retainedApplied.AppliedEffectCount);
        var retainedBuffer = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.retained", "dnd2024.temporary-hit-points");
        Assert.Equal(2, retainedBuffer!.Revision);
        Assert.Contains("\"amount\":1", retainedBuffer.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weapon_damage_rejects_corrupt_temporary_hp_before_any_root_effect()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCombatFixturesAsync();
        await harness.AddDamageTargetAsync("target.temp.corrupt", 20, 20);
        await harness.AddApplicationComponentAsync("target.temp.corrupt",
            "dnd2024.temporary-hit-points",
            "{\"amount\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Temporary Hit Points (PDF p. 18)\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.temp.corrupt", "dnd2024.temporary-hit-points", "{}");
        var hpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.hit-points");
        var bufferBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.temporary-hit-points");
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
                ["target"] = "target.temp.corrupt"
            }, "{\"ability\":\"str\",\"critical\":false}", 77,
            "b123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        var hpAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.hit-points");
        var bufferAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.temporary-hit-points");
        Assert.Equal(hpBefore!.Revision, hpAfter!.Revision);
        Assert.Equal(hpBefore.ValueJson, hpAfter.ValueJson);
        Assert.Equal(bufferBefore!.Revision, bufferAfter!.Revision);
        Assert.Equal(bufferBefore.ValueJson, bufferAfter.ValueJson);
    }

    [Fact]
    public async Task Temporary_hit_points_and_healing_reject_corrupt_or_derived_input_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddDamageTargetAsync("target.invalid-healing", 3, 10);
        await harness.AddApplicationComponentAsync("target.invalid-healing",
            "dnd2024.temporary-hit-points",
            "{\"amount\":8,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Temporary Hit Points (PDF p. 18)\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.invalid-healing", "dnd2024.temporary-hit-points", "{}");
        var corruptTemporaryBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.temporary-hit-points");
        var corruptTemporary = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.temporary-hit-points.write", "target.invalid-healing",
            "{\"mode\":\"grant\",\"amount\":4,\"onExisting\":\"keep\"}", 0,
            "6123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, corruptTemporary.Disposition);
        var corruptTemporaryAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.temporary-hit-points");
        Assert.Equal(corruptTemporaryBefore!.ValueJson, corruptTemporaryAfter!.ValueJson);

        await harness.ReplaceCoreComponentRawAsync("target.invalid-healing", "dnd2024.hit-points",
            "{\"current\":11,\"maximum\":10,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Hit Points\"}}");
        var corruptHpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.hit-points");
        var corruptHealing = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.healing.apply", "target.invalid-healing", "{\"amount\":4}", 0,
            "7123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, corruptHealing.Disposition);
        var corruptHpAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.hit-points");
        Assert.Equal(corruptHpBefore!.Revision, corruptHpAfter!.Revision);
        Assert.Equal(corruptHpBefore.ValueJson, corruptHpAfter.ValueJson);
        var injected = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.healing.apply",
            new Dictionary<string, string> { ["subject"] = "target.invalid-healing" },
            "{\"amount\":4,\"afterCurrent\":7}", 0);
        Assert.False(injected.Ok);
    }

    [Fact]
    public async Task Conditions_writer_tracks_exhaustion_through_level_six_without_an_unsupported_event()
    {
        await using var harness = await DndHarness.CreateAsync();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "9123456789abcdef0123456789abcdeb"))).Disposition);
        var preview = await harness.EvaluateAsync("subject.high", "{\"mode\":\"exhaust\",\"levels\":6}", 0,
            "mechanic.dnd2024.conditions.write");
        Assert.True(preview.Ok, preview.Run?.Error);
        Assert.Empty(preview.Run!.Output.Events);
        Assert.Contains("\"lethal\":true", preview.Run.Output.Data, StringComparison.Ordinal);

        var exhausted = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.conditions.write", "subject.high",
            "{\"mode\":\"exhaust\",\"levels\":6}", 0,
            "a123456789abcdef0123456789abcdeb"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, exhausted.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        Assert.Contains("{\"condition\":\"exhaustion\",\"level\":6}", stored!.ValueJson, StringComparison.Ordinal);

        var recovered = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.conditions.write", "subject.high",
            "{\"mode\":\"recover\",\"levels\":6}", 0,
            "b123456789abcdef0123456789abcdeb"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recovered.Disposition);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        Assert.DoesNotContain("exhaustion", stored!.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Condition_state_effects_distinguish_unknown_and_derive_stable_shared_branches()
    {
        await using var harness = await DndHarness.CreateAsync();
        var absent = await harness.EvaluateAsync("subject.low", "{}", 0,
            "mechanic.dnd2024.d20-test.state-effects");
        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Contains("\"conditionsKnown\":false", absent.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(absent.Run.Output.Effects);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "c123456789abcdef0123456789abcdeb"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high",
                "{\"mode\":\"apply\",\"conditions\":[\"poisoned\",\"restrained\",\"unconscious\"]}", 0,
                "d123456789abcdef0123456789abcdeb"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high",
                "{\"mode\":\"exhaust\",\"levels\":2}", 0,
                "e123456789abcdef0123456789abcdeb"))).Disposition);

        var first = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.d20-test.state-effects");
        var second = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.d20-test.state-effects");
        Assert.True(first.Ok, first.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var root = data.RootElement;
        Assert.True(root.GetProperty("conditionsKnown").GetBoolean());
        Assert.Equal(2, root.GetProperty("exhaustionLevel").GetInt32());
        Assert.Equal(-4, root.GetProperty("derivedModifiers")[0].GetProperty("value").GetInt32());
        Assert.Contains(root.GetProperty("effectiveConditions").EnumerateArray(),
            value => value.GetString() == "incapacitated");
        Assert.Contains(root.GetProperty("effectiveConditions").EnumerateArray(),
            value => value.GetString() == "prone");
        Assert.Equal("condition:poisoned",
            root.GetProperty("byTest").GetProperty("abilityCheck")[0].GetProperty("source").GetString());
        Assert.Equal("condition:unconscious",
            root.GetProperty("byTest").GetProperty("savingThrow").GetProperty("str")
                .GetProperty("automaticFailure").GetString());
        Assert.Equal(4, root.GetProperty("prohibitions").GetArrayLength());
        Assert.Equal("movement", root.GetProperty("prohibitions")[3].GetProperty("resource").GetString());
        Assert.Equal("condition:restrained", root.GetProperty("prohibitions")[3].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Conditions_writer_rejects_invalid_sources_duplicates_and_corrupt_state_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "f123456789abcdef0123456789abcdeb"))).Disposition);
        var missingSource = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.conditions.write", "subject.high",
            "{\"mode\":\"apply\",\"conditions\":[\"grappled\"]}", 0,
            "0123456789abcdef0123456789abcdec"));
        var selfRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["source"] = "subject.high"
        };
        var selfSource = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.conditions.write", selfRoles,
            "{\"mode\":\"apply\",\"conditions\":[\"charmed\"]}", 0,
            "1123456789abcdef0123456789abcdec"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, missingSource.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, selfSource.Disposition);

        await harness.ReplaceConditionsRawAsync("subject.high",
            "{\"entries\":[{\"condition\":\"poisoned\"},{\"condition\":\"poisoned\"}],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary\"}}");
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        var correction = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.conditions.write", "subject.high",
            "{\"mode\":\"apply\",\"conditions\":[\"prone\"]}", 0,
            "2123456789abcdef0123456789abcdec"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, correction.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Turn_lifecycle_restores_only_the_new_participant_and_applies_exhaustion_reduction()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "3123456789abcdef0123456789abcdec"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high",
                "{\"mode\":\"exhaust\",\"levels\":2}", 0,
                "4123456789abcdef0123456789abcdec"))).Disposition);
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var orderRequest = await EncounterOrderWithHighFirstAsync(harness);
        var order = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-initiative-order", encounterRoles,
            orderRequest.Input, orderRequest.Seed, "5123456789abcdef0123456789abcdec"));
        Assert.True(order.Successful, string.Join("; ", order.Problems.Select(value => value.SafeMessage)));

        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-turn.start", encounterRoles, "{}", 0,
            "6123456789abcdef0123456789abcdec"));
        Assert.True(started.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", started.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        Assert.Equal(2, started.AppliedEffectCount);
        var high = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");
        var low = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.turn-budget");
        Assert.Contains("\"movementRemainingFeet\":20", high!.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"action\":false", low!.ValueJson, StringComparison.Ordinal);

        var advanced = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-turn.advance", encounterRoles, "{}", 0,
            "7123456789abcdef0123456789abcdec"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, advanced.Disposition);
        Assert.Equal(2, advanced.AppliedEffectCount);
        low = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.turn-budget");
        Assert.Contains("\"action\":true", low!.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"movementRemainingFeet\":30", low.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turn_budget_spender_enforces_active_turn_off_turn_reaction_and_condition_prohibitions()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.turn-budget.write", "subject.low",
                "{\"mode\":\"correct\",\"action\":false,\"bonusAction\":false,\"reaction\":true,\"freeInteraction\":false,\"movementRemainingFeet\":0}",
                0, "7123456789abcdef0123456789abcded"))).Disposition);
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var orderRequest = await EncounterOrderWithHighFirstAsync(harness);
        Assert.True((await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.encounter-initiative-order", encounterRoles,
            orderRequest.Input, orderRequest.Seed, "8123456789abcdef0123456789abcdec"))).Successful);
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.encounter-turn.start", encounterRoles, "{}", 0,
                "9123456789abcdef0123456789abcdec"));
        Assert.True(started.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", started.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));

        var activeRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["encounter"] = "encounter.fixture"
        };
        var offTurnRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.low",
            ["encounter"] = "encounter.fixture"
        };
        var action = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.turn-budget.spend", activeRoles, "{\"resource\":\"action\"}", 0,
            "a123456789abcdef0123456789abcdec"));
        var repeated = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.turn-budget.spend", activeRoles, "{\"resource\":\"action\"}", 0,
            "b123456789abcdef0123456789abcdec"));
        var offTurnAction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.turn-budget.spend", offTurnRoles, "{\"resource\":\"action\"}", 0,
            "c123456789abcdef0123456789abcdec"));
        var offTurnReaction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.turn-budget.spend", offTurnRoles, "{\"resource\":\"reaction\"}", 0,
            "d123456789abcdef0123456789abcdec"));
        var movement = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.turn-budget.spend", activeRoles,
            "{\"resource\":\"movement\",\"feet\":15}", 0,
            "e123456789abcdef0123456789abcdec"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, action.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, repeated.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, offTurnAction.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, offTurnReaction.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, movement.Disposition);
        var activeBudget = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");
        Assert.Contains("\"movementRemainingFeet\":15", activeBudget!.ValueJson, StringComparison.Ordinal);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "f123456789abcdef0123456789abcdec"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "mechanic.dnd2024.conditions.write", "subject.high",
                "{\"mode\":\"apply\",\"conditions\":[\"stunned\"]}", 0,
                "0123456789abcdef0123456789abcded"))).Disposition);
        var prohibited = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.turn-budget.spend", activeRoles,
            "{\"resource\":\"movement\",\"feet\":5}", 0,
            "1123456789abcdef0123456789abcded"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, prohibited.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.turn-budget");
        Assert.Equal(activeBudget.ValueJson, after!.ValueJson);
    }

    [Fact]
    public async Task Character_content_definition_is_source_fixed_write_once_and_replay_safe()
    {
        await using var harness = await DndHarness.CreateAsync();
        var roles = new Dictionary<string, string> { ["content"] = "subject.high" };
        const string input = "{\"kind\":\"species\",\"contentKey\":\"human\",\"contentVersion\":1,\"status\":\"active\",\"locator\":\"Character Creation > Species PDF page 40\"}";
        var request = harness.ActionForRoles("mechanic.dnd2024.character-content-definition.record",
            roles, input, 0, "2123456789abcdef0123456789abcded");

        var first = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character-content-definition.record", roles, input, 0,
            "3123456789abcdef0123456789abcded"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.content-definition");
        Assert.Equal(1, stored!.Revision);
        Assert.Contains("\"sourceId\":\"source.dnd2024.srd-5.2.1\"", stored.ValueJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Character_profile_requires_explicit_transitions_and_preserves_failed_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        var roles = new Dictionary<string, string> { ["actor"] = "subject.high" };
        var recorded = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character-profile.record", roles,
            "{\"mode\":\"record\",\"biography\":\"A patient cartographer.\",\"pronouns\":\"they/them\"}",
            0, "4123456789abcdef0123456789abcded"));
        var corrected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character-profile.record", roles,
            "{\"mode\":\"correct\",\"appearance\":\"Ink-stained gloves.\"}",
            0, "5123456789abcdef0123456789abcded"));
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.profile");
        var invalid = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character-profile.record", roles,
            "{\"mode\":\"correct\",\"biography\":\" untrimmed\"}",
            0, "6123456789abcdef0123456789abcded"));
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.profile");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        Assert.Equal(2, before!.Revision);
        Assert.Equal(before.ValueJson, after!.ValueJson);
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("huge")]
    [InlineData("gargantuan")]
    public async Task Creature_size_accepts_every_closed_category(string size)
    {
        await using var harness = await DndHarness.CreateAsync();
        var result = await harness.EvaluateRolesAsync("mechanic.dnd2024.creature-size.record",
            new Dictionary<string, string> { ["creature"] = "subject.low" },
            "{\"size\":\"" + size + "\"}", 0);
        Assert.True(result.Ok, result.Run?.Error);
        Assert.Single(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Language_and_tool_recorders_canonicalize_correct_and_reject_unknown_members()
    {
        await using var harness = await DndHarness.CreateAsync();
        var language = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.language-proficiencies.record", "subject.high",
            "{\"mode\":\"record\",\"languages\":[\"elvish\",\"common\"]}", 0,
            "7123456789abcdef0123456789abcded"));
        var tool = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.tool-proficiencies.record", "subject.high",
            "{\"mode\":\"record\",\"tools\":[\"thieves-tools\",\"dice-set\"]}", 0,
            "8123456789abcdef0123456789abcded"));
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.language-proficiencies.record", "subject.high",
            "{\"mode\":\"correct\",\"languages\":[]}", 0,
            "9123456789abcdef0123456789abcded"));
        var invalid = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.tool-proficiencies.record", "subject.high",
            "{\"mode\":\"correct\",\"tools\":[\"laser-cutter\"]}", 0,
            "a123456789abcdef0123456789abcded"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, language.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, tool.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        var languages = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.language-proficiencies");
        var tools = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.tool-proficiencies");
        Assert.Equal(2, languages!.Revision);
        Assert.StartsWith("{\"languages\":[]", languages.ValueJson, StringComparison.Ordinal);
        Assert.Equal(1, tools!.Revision);
        Assert.Contains("\"tools\":[\"dice-set\",\"thieves-tools\"]", tools.ValueJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_instance_record_create_read_and_move_use_definition_identity_and_containment()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "item.dnd2024.robe.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Robe definition", SeparateItemDefinition());
        var recordRoles = new Dictionary<string, string>
        {
            ["item"] = "subject.low", ["definition"] = definitionId
        };
        var recorded = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-instance.record", recordRoles, "{}", 0,
            "b123456789abcdef0123456789abcded"));
        var createRoles = new Dictionary<string, string>
        {
            ["definition"] = definitionId, ["destination"] = "subject.high"
        };
        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-instance.create-and-place", createRoles,
            "{\"itemId\":\"item.campaign.robe\",\"name\":\"Traveler's Robe\",\"slot\":\"carried\"}",
            0, "c123456789abcdef0123456789abcded"));
        var read = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.campaign.robe" }, "{}", 0);
        var moved = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-instance.move", new Dictionary<string, string>
            {
                ["item"] = "item.campaign.robe", ["destination"] = "subject.low"
            }, "{\"slot\":\"gift\"}", 0, "d123456789abcdef0123456789abcded"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        Assert.True(read.Ok, read.Run?.Error);
        Assert.Contains("\"containerId\":\"subject.high\"", read.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, moved.Disposition);
    }

    [Fact]
    public async Task Fungible_stack_lifecycle_conserves_count_and_deletes_zero()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "item.dnd2024.arrow.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Arrow definition", FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.stack.recorded", "Recorded Arrows", definitionId,
            "subject.low");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.item-stack.record", new Dictionary<string, string>
                {
                    ["item"] = "item.stack.recorded", ["definition"] = definitionId
                }, "{\"count\":2}", 0, "d123456789abcdef0123456789abcdee"))).Disposition);
        var definitionAndDestination = new Dictionary<string, string>
        {
            ["definition"] = definitionId, ["destination"] = "subject.high"
        };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.item-stack.create-and-place", definitionAndDestination,
                "{\"count\":10,\"itemId\":\"item.stack.arrows\",\"name\":\"Arrows\",\"slot\":\"quiver\"}",
                0, "e123456789abcdef0123456789abcded"))).Disposition);
        const string childDefinition = "item.dnd2024.token.v1";
        await harness.AddItemDefinitionAsync(childDefinition, "Token definition", SeparateItemDefinition());
        await harness.AddPhysicalItemAsync("item.stack.child", "Token", childDefinition,
            "item.stack.arrows", "inside");
        var blockedByContents = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":1}", 0, "e223456789abcdef0123456789abcded"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, blockedByContents.Disposition);
        Assert.Contains("\"count\":10", (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.stack.arrows", "dnd2024.item-quantity"))!.ValueJson,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.item-instance.move", new Dictionary<string, string>
                {
                    ["item"] = "item.stack.child", ["destination"] = "subject.high"
                }, "{\"slot\":\"carried\"}", 0, "e323456789abcdef0123456789abcded"))).Disposition);
        var split = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-stack.split", new Dictionary<string, string>
            {
                ["source"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":3,\"itemId\":\"item.stack.arrows-split\",\"name\":\"Three Arrows\"}",
            0, "f123456789abcdef0123456789abcded"));
        var merged = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-stack.merge", new Dictionary<string, string>
            {
                ["source"] = "item.stack.arrows-split", ["target"] = "item.stack.arrows",
                ["definition"] = definitionId
            }, "{}", 0, "0123456789abcdef0123456789abcdee"));
        var partial = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":4}", 0, "1123456789abcdef0123456789abcdee"));
        var final = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":6}", 0, "2123456789abcdef0123456789abcdee"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, split.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, merged.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, partial.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, final.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.stack.arrows"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.stack.arrows-split"));
    }

    [Fact]
    public async Task Equipment_and_transfer_require_definition_eligibility_direct_custody_and_unequipped_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "item.dnd2024.spear.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Spear definition",
            SeparateItemDefinition("[\"held\"]"));
        await harness.AddPhysicalItemAsync("item.spear", "Spear", definitionId, "subject.high");
        var roles = new Dictionary<string, string>
        {
            ["item"] = "item.spear", ["holder"] = "subject.high"
        };
        var equipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.equip", roles, "{\"state\":\"held\"}", 0,
            "3123456789abcdef0123456789abcdee"));
        var blocked = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.transfer", new Dictionary<string, string>
            {
                ["item"] = "item.spear", ["source"] = "subject.high", ["destination"] = "subject.low"
            }, "{\"slot\":\"carried\"}", 0, "4123456789abcdef0123456789abcdee"));
        var read = await harness.EvaluateRolesAsync("mechanic.dnd2024.item.equipment.read",
            new Dictionary<string, string> { ["item"] = "item.spear" }, "{}", 0);
        var unequipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.unequip", roles, "{}", 0,
            "5123456789abcdef0123456789abcdee"));
        var transferred = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.transfer", new Dictionary<string, string>
            {
                ["item"] = "item.spear", ["source"] = "subject.high", ["destination"] = "subject.low"
            }, "{\"slot\":\"carried\"}", 0, "6123456789abcdef0123456789abcdee"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, equipped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, blocked.Disposition);
        Assert.Contains("\"state\":\"held\"", read.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, unequipped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, transferred.Disposition);
    }

    [Fact]
    public async Task Item_transfer_enforces_direct_container_item_count_capacity_without_partial_move()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string itemDefinition = "item.dnd2024.stone.v1";
        const string bagDefinition = "item.dnd2024.bag.v1";
        await harness.AddItemDefinitionAsync(itemDefinition, "Stone definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(bagDefinition, "Bag definition", ContainerItemDefinition(1));
        await harness.AddPhysicalItemAsync("item.bag", "Bag", bagDefinition, "subject.low");
        await harness.AddPhysicalItemAsync("item.stone.one", "Stone One", itemDefinition, "subject.high");
        await harness.AddPhysicalItemAsync("item.stone.two", "Stone Two", itemDefinition, "subject.high");
        var rolesOne = new Dictionary<string, string>
        {
            ["item"] = "item.stone.one", ["source"] = "subject.high", ["destination"] = "item.bag"
        };
        var rolesTwo = new Dictionary<string, string>(rolesOne) { ["item"] = "item.stone.two" };

        var first = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.transfer", rolesOne, "{\"slot\":\"inside\"}", 0,
            "9123456789abcdef0123456789abcdee"));
        var second = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.transfer", rolesTwo, "{\"slot\":\"inside\"}", 0,
            "a123456789abcdef0123456789abcdee"));
        var secondRead = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.stone.two" }, "{}", 0);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, second.Disposition);
        Assert.Contains("\"containerId\":\"subject.high\"", secondRead.Run!.Output.Data,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_activity_is_descriptor_driven_atomic_and_duplicate_safe()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string sourceDefinition = "item.dnd2024.package.v1";
        const string grantDefinition = "item.dnd2024.rope.v1";
        await harness.AddItemDefinitionAsync(grantDefinition, "Rope definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(sourceDefinition, "Package definition", FungibleItemDefinition(),
            "{\"activities\":[{\"id\":\"open\",\"kind\":\"consume-and-grant-item\",\"consumeQuantity\":1,\"grant\":{\"definitionId\":\"item.dnd2024.rope.v1\",\"name\":\"Rope\",\"slot\":\"unpacked\"}}]}");
        await harness.AddPhysicalItemAsync("item.package-stack", "Packages", sourceDefinition,
            "subject.high", quantity: 2);
        var roles = new Dictionary<string, string>
        {
            ["item"] = "item.package-stack", ["definition"] = sourceDefinition,
            ["grantDefinition"] = grantDefinition
        };
        const string input = "{\"activityId\":\"open\",\"grantItemId\":\"item.granted-rope\"}";
        var used = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-activity.use", roles, input, 0,
            "7123456789abcdef0123456789abcdee"));
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.package-stack", "dnd2024.item-quantity");
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item-activity.use", roles, input, 0,
            "8123456789abcdef0123456789abcdee"));
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.package-stack", "dnd2024.item-quantity");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, used.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal(before!.ValueJson, after!.ValueJson);
        Assert.Contains("\"count\":1", after.ValueJson, StringComparison.Ordinal);
        Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.granted-rope"));
    }

    private static string SeparateItemDefinition(string? equipmentModes = null)
        => "{\"definitionVersion\":1,\"kind\":\"adventuring-gear\",\"stackPolicy\":\"separate\",\"massPounds\":{\"numerator\":1,\"denominator\":1}" +
           (equipmentModes is null ? "" : ",\"equipmentModes\":" + equipmentModes) +
           ",\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Equipment > Adventuring Gear\"}}";

    private static string FungibleItemDefinition()
        => "{\"definitionVersion\":1,\"kind\":\"ammunition\",\"stackPolicy\":\"fungible\",\"massPounds\":{\"numerator\":1,\"denominator\":20},\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Equipment > Ammunition\"}}";

    private static string ContainerItemDefinition(int itemCount)
        => "{\"definitionVersion\":1,\"kind\":\"adventuring-gear\",\"stackPolicy\":\"separate\",\"massPounds\":{\"numerator\":1,\"denominator\":1},\"capacity\":{\"itemCount\":" + itemCount + "},\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Equipment > Adventuring Gear\"}}";

    private static string CurrencyItemDefinition(string denomination, int copperValue)
        => "{\"definitionVersion\":1,\"kind\":\"currency\",\"stackPolicy\":\"fungible\",\"massPounds\":{\"numerator\":1,\"denominator\":50},\"currency\":{\"denomination\":\"" + denomination + "\",\"copperValue\":" + copperValue + ",\"coinsPerPound\":50},\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Equipment > Coins\"}}";

    [Fact]
    public async Task Inventory_burden_and_carrying_capacity_compose_exact_bounded_views()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string robe = "item.dnd2024.robe-reader.v1";
        const string arrows = "item.dnd2024.arrow-reader.v1";
        await harness.AddItemDefinitionAsync(robe, "Robe definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(arrows, "Arrow definition", FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.reader.robe", "Robe", robe, "subject.high",
            equipmentState: "worn");
        await harness.AddPhysicalItemAsync("item.reader.arrows", "Arrows", arrows, "subject.high",
            quantity: 20);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.creature-size.record",
                new Dictionary<string, string> { ["creature"] = "subject.high" },
                "{\"size\":\"medium\"}", 0, "b123456789abcdef0123456789abcdee"))).Disposition);

        var inventory = await harness.EvaluateRolesAsync("mechanic.dnd2024.inventory.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var carrying = await harness.EvaluateRolesAsync("mechanic.dnd2024.carrying-capacity.read",
            new Dictionary<string, string> { ["creature"] = "subject.high" }, "{}", 0);

        Assert.True(inventory.Ok, inventory.Run?.Error);
        Assert.Contains("\"mayOmitDeeperContents\":true", inventory.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"equipmentState\":\"worn\"", inventory.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"massPounds\":{\"numerator\":2,\"denominator\":1}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.True(carrying.Ok, carrying.Run?.Error);
        Assert.Contains("\"carryingCapacityPounds\":{\"numerator\":450,\"denominator\":1}",
            carrying.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(carrying.Run.Output.Effects);
    }

    [Fact]
    public async Task Currency_reader_derives_mixed_physical_coin_value_without_wallet_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string cp = "item.dnd2024.cp.v1";
        const string gp = "item.dnd2024.gp.v1";
        await harness.AddItemDefinitionAsync(cp, "Copper definition", CurrencyItemDefinition("cp", 1));
        await harness.AddItemDefinitionAsync(gp, "Gold definition", CurrencyItemDefinition("gp", 100));
        await harness.AddPhysicalItemAsync("item.coins.cp", "Copper Pieces", cp, "subject.high",
            quantity: 10);
        await harness.AddPhysicalItemAsync("item.coins.gp", "Gold Pieces", gp, "subject.high",
            quantity: 2);

        var result = await harness.EvaluateRolesAsync("mechanic.dnd2024.currency-value.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(result.Ok, result.Run?.Error);
        Assert.Contains("\"coinCount\":12", result.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"copperValue\":210", result.Run.Output.Data, StringComparison.Ordinal);
        Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_static_currency_cohort_is_schema_valid_and_consumed_by_existing_readers()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "currency");
        var paths = Directory.GetFiles(contentRoot, "currency.dnd2024.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(5, paths.Length);

        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "data", "dnd2024.item-definition.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        var definitions = new Dictionary<string, EntityFile>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var component = Assert.Single(entity.Components);
            Assert.Equal("dnd2024.item-definition", component.DefinitionId);
            var validation = validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
                component.Data);
            Assert.Equal(SchemaValueStatus.Valid, validation.Status);
            definitions.Add(entity.Id, entity);
            await harness.AddItemDefinitionAsync(entity.Id, entity.Name, component.Data);
        }

        var cp = definitions["currency.dnd2024.copper-piece.v1"];
        var gp = definitions["currency.dnd2024.gold-piece.v1"];
        await harness.AddPhysicalItemAsync("item.static-coins.cp", "Copper Pieces", cp.Id,
            "subject.high", quantity: 10);
        await harness.AddPhysicalItemAsync("item.static-coins.gp", "Gold Pieces", gp.Id,
            "subject.high", quantity: 2);

        var currency = await harness.EvaluateRolesAsync("mechanic.dnd2024.currency-value.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(currency.Ok, currency.Run?.Error);
        Assert.Contains("\"coinCount\":12", currency.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"copperValue\":210", currency.Run.Output.Data, StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"massPounds\":{\"numerator\":6,\"denominator\":25}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(currency.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_static_adventuring_gear_is_schema_valid_and_enforces_backpack_capacity()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "adventuring-gear");
        var paths = Directory.GetFiles(contentRoot, "item.dnd2024.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(9, paths.Length);

        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "data", "dnd2024.item-definition.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        var definitions = new Dictionary<string, EntityFile>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var component = Assert.Single(entity.Components);
            Assert.Equal("dnd2024.item-definition", component.DefinitionId);
            var validation = validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
                component.Data);
            Assert.Equal(SchemaValueStatus.Valid, validation.Status);
            using var definition = JsonDocument.Parse(component.Data);
            Assert.Equal("source.dnd2024.srd-5.2.1", definition.RootElement.GetProperty("sourceRef")
                .GetProperty("sourceId").GetString());
            Assert.StartsWith("Equipment > Adventuring Gear > ",
                definition.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString(),
                StringComparison.Ordinal);
            definitions.Add(entity.Id, entity);
            await harness.AddItemDefinitionAsync(entity.Id, entity.Name, component.Data);
        }

        Assert.DoesNotContain("item.dnd2024.hempen-rope-50-foot.v1", definitions.Keys);
        Assert.DoesNotContain("item.dnd2024.quiver.v1", definitions.Keys);
        await harness.AddPhysicalItemAsync("item.static.backpack", "Backpack",
            definitions["item.dnd2024.backpack.v1"].Id, "subject.low");
        for (var index = 0; index < 7; index++)
        {
            await harness.AddPhysicalItemAsync($"item.static.waterskin.{index}", $"Waterskin {index}",
                definitions["item.dnd2024.waterskin.v1"].Id, "subject.high");
        }

        for (var index = 0; index < 7; index++)
        {
            var moved = await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.item.transfer", new Dictionary<string, string>
                {
                    ["item"] = $"item.static.waterskin.{index}",
                    ["source"] = "subject.high",
                    ["destination"] = "item.static.backpack"
                }, "{\"slot\":\"inside\"}", 0, (index + 1).ToString("x32")));
            Assert.Equal(index < 6
                    ? ApplicationActionExecutionDisposition.Succeeded
                    : ApplicationActionExecutionDisposition.Failed,
                moved.Disposition);
        }

        var refused = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.static.waterskin.6" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.low" }, "{}", 0);
        Assert.True(refused.Ok, refused.Run?.Error);
        Assert.Contains("\"containerId\":\"subject.high\"", refused.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"massPounds\":{\"numerator\":35,\"denominator\":1}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(refused.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Optional_legacy_rope_is_consumed_only_when_extension_profile_is_selected()
    {
        const string relativePath =
            "catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/item.dnd2024.hempen-rope-50-foot.v1.json";
        await using var coreOnly = await DndHarness.CreateAsync();
        Assert.DoesNotContain(relativePath, coreOnly.ActiveSourcePaths);

        await using var extended = await DndHarness.CreateAsync(includeLegacyEquipmentExtension: true);
        Assert.Contains(relativePath, extended.ActiveSourcePaths);
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relativePath);
        var component = Assert.Single(entity.Components);
        Assert.Equal("dnd2024.item-definition", component.DefinitionId);
        await extended.AddItemDefinitionAsync(entity.Id, entity.Name, component.Data);
        await extended.AddPhysicalItemAsync("item.compatibility.rope", "Hempen Rope", entity.Id,
            "subject.high");

        var burden = await extended.EvaluateRolesAsync("mechanic.dnd2024.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"massPounds\":{\"numerator\":5,\"denominator\":1}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_static_armor_table_matches_srd_profiles_and_existing_equipment_readers()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "armor");
        var paths = Directory.GetFiles(contentRoot, "item.dnd2024.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(13, paths.Length);
        var expected = new Dictionary<string,
            (int Mass, string Category, int ArmorClass, string? Dexterity, int? Strength,
                bool Stealth, int Don, int Doff, int Bonus, string Mode)>(StringComparer.Ordinal)
        {
            ["item.dnd2024.padded-armor.v1"] = (8, "light", 11, "full", null, true, 1, 1, 0, "worn"),
            ["item.dnd2024.leather-armor.v1"] = (10, "light", 11, "full", null, false, 1, 1, 0, "worn"),
            ["item.dnd2024.studded-leather-armor.v1"] = (13, "light", 12, "full", null, false, 1, 1, 0, "worn"),
            ["item.dnd2024.hide-armor.v1"] = (12, "medium", 12, "max-2", null, false, 5, 1, 0, "worn"),
            ["item.dnd2024.chain-shirt.v1"] = (20, "medium", 13, "max-2", null, false, 5, 1, 0, "worn"),
            ["item.dnd2024.scale-mail.v1"] = (45, "medium", 14, "max-2", null, true, 5, 1, 0, "worn"),
            ["item.dnd2024.breastplate.v1"] = (20, "medium", 14, "max-2", null, false, 5, 1, 0, "worn"),
            ["item.dnd2024.half-plate-armor.v1"] = (40, "medium", 15, "max-2", null, true, 5, 1, 0, "worn"),
            ["item.dnd2024.ring-mail.v1"] = (40, "heavy", 14, "none", null, true, 10, 5, 0, "worn"),
            ["item.dnd2024.chain-mail.v1"] = (55, "heavy", 16, "none", 13, true, 10, 5, 0, "worn"),
            ["item.dnd2024.splint-armor.v1"] = (60, "heavy", 17, "none", 15, true, 10, 5, 0, "worn"),
            ["item.dnd2024.plate-armor.v1"] = (65, "heavy", 18, "none", 15, true, 10, 5, 0, "worn"),
            ["item.dnd2024.shield.v1"] = (6, "shield", 0, null, null, false, 0, 0, 2, "held")
        };

        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "data", "dnd2024.item-definition.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var component = Assert.Single(entity.Components);
            Assert.Equal("dnd2024.item-definition", component.DefinitionId);
            var validation = validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
                component.Data);
            Assert.Equal(SchemaValueStatus.Valid, validation.Status);
            using var definition = JsonDocument.Parse(component.Data);
            var value = definition.RootElement;
            var profile = value.GetProperty("armorProfile");
            var official = expected[entity.Id];
            Assert.Equal(official.Category == "shield" ? "shield" : "armor",
                value.GetProperty("kind").GetString());
            Assert.Equal(official.Mass, value.GetProperty("massPounds").GetProperty("numerator").GetInt32());
            Assert.Equal(official.Category, profile.GetProperty("category").GetString());
            Assert.Equal(official.Mode, value.GetProperty("equipmentModes")[0].GetString());
            var donDoff = profile.GetProperty("donDoff");
            if (official.Category == "shield")
            {
                Assert.Equal(official.Bonus, profile.GetProperty("armorClassBonus").GetInt32());
                Assert.Equal("utilize-action", donDoff.GetProperty("kind").GetString());
                Assert.False(profile.TryGetProperty("baseArmorClass", out _));
            }
            else
            {
                Assert.Equal(official.ArmorClass, profile.GetProperty("baseArmorClass").GetInt32());
                Assert.Equal(official.Dexterity, profile.GetProperty("dexterityRule").GetString());
                Assert.Equal(official.Stealth, profile.GetProperty("stealthDisadvantage").GetBoolean());
                Assert.Equal(official.Don, donDoff.GetProperty("donMinutes").GetInt32());
                Assert.Equal(official.Doff, donDoff.GetProperty("doffMinutes").GetInt32());
                if (official.Strength is int strength)
                    Assert.Equal(strength, profile.GetProperty("strengthMinimum").GetInt32());
                else
                    Assert.False(profile.TryGetProperty("strengthMinimum", out _));
            }
            Assert.Equal("Equipment > Armor > Armor table (PDF p. 92)",
                value.GetProperty("sourceRef").GetProperty("locator").GetString());
            await harness.AddItemDefinitionAsync(entity.Id, entity.Name, component.Data);
            await harness.AddPhysicalItemAsync($"instance.{entity.Id}", entity.Name, entity.Id,
                "subject.high");
        }

        var armorEquipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.equip", new Dictionary<string, string>
            {
                ["item"] = "instance.item.dnd2024.padded-armor.v1",
                ["holder"] = "subject.high"
            }, "{\"state\":\"worn\"}", 0, "e123456789abcdef0123456789abcdee"));
        var shieldEquipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.equip", new Dictionary<string, string>
            {
                ["item"] = "instance.item.dnd2024.shield.v1",
                ["holder"] = "subject.high"
            }, "{\"state\":\"held\"}", 0, "f123456789abcdef0123456789abcdee"));
        var armorEquipment = await harness.EvaluateRolesAsync("mechanic.dnd2024.item.equipment.read",
            new Dictionary<string, string>
            {
                ["item"] = "instance.item.dnd2024.padded-armor.v1"
            }, "{}", 0);
        var shieldEquipment = await harness.EvaluateRolesAsync("mechanic.dnd2024.item.equipment.read",
            new Dictionary<string, string> { ["item"] = "instance.item.dnd2024.shield.v1" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, armorEquipped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, shieldEquipped.Disposition);
        Assert.True(armorEquipment.Ok, armorEquipment.Run?.Error);
        Assert.Contains("\"state\":\"worn\"", armorEquipment.Run!.Output.Data, StringComparison.Ordinal);
        Assert.True(shieldEquipment.Ok, shieldEquipment.Run?.Error);
        Assert.Contains("\"state\":\"held\"", shieldEquipment.Run!.Output.Data, StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"massPounds\":{\"numerator\":394,\"denominator\":1}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(armorEquipment.Run.Output.Effects);
        Assert.Empty(shieldEquipment.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_weapon_profiles_and_item_links_are_closed_and_consumed_by_existing_readers()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "weapons");
        var paths = Directory.GetFiles(contentRoot, "weapon.dnd2024.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(6, paths.Length);
        var expected = new Dictionary<string, (string Category, string Kind, int Count, int Faces, string Type)>(StringComparer.Ordinal)
        {
            ["weapon.dnd2024.battleaxe"] = ("martial", "melee", 1, 8, "slashing"),
            ["weapon.dnd2024.dagger"] = ("simple", "melee", 1, 4, "piercing"),
            ["weapon.dnd2024.flail"] = ("martial", "melee", 1, 8, "bludgeoning"),
            ["weapon.dnd2024.greatsword"] = ("martial", "melee", 2, 6, "slashing"),
            ["weapon.dnd2024.javelin"] = ("simple", "melee", 1, 6, "piercing"),
            ["weapon.dnd2024.shortbow"] = ("simple", "ranged", 1, 6, "piercing")
        };
        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "combat", "dnd2024.weapon-profile.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var component = Assert.Single(entity.Components);
            Assert.Equal("dnd2024.weapon-profile", component.DefinitionId);
            var validation = validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
                component.Data);
            Assert.Equal(SchemaValueStatus.Valid, validation.Status);
            using var profileJson = JsonDocument.Parse(component.Data);
            var profile = profileJson.RootElement;
            Assert.Equal(5, profile.EnumerateObject().Count());
            var official = expected[entity.Id];
            Assert.Equal(official.Category, profile.GetProperty("category").GetString());
            Assert.Equal(official.Kind, profile.GetProperty("kind").GetString());
            Assert.Equal(official.Count, profile.GetProperty("damage").GetProperty("count").GetInt32());
            Assert.Equal(official.Faces, profile.GetProperty("damage").GetProperty("faces").GetInt32());
            Assert.Equal(official.Type, profile.GetProperty("damage").GetProperty("type").GetString());
            await harness.AddWeaponProfileAsync(entity.Id, entity.Name, component.Data);
            profileIds.Add(entity.Id);
        }

        var itemPaths = Directory.GetFiles(contentRoot, "item.dnd2024.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(4, itemPaths.Length);
        var itemSchema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "data", "dnd2024.item-definition.schema.json"));
        var itemCompilation = validator.Compile(itemSchema);
        Assert.True(itemCompilation.IsAccepted, string.Join("; ", itemCompilation.Diagnostics));
        foreach (var path in itemPaths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var component = Assert.Single(entity.Components);
            var validation = validator.Validate(itemCompilation.ProfileId,
                itemCompilation.NormalizedSchema, component.Data);
            Assert.Equal(SchemaValueStatus.Valid, validation.Status);
            using var definitionJson = JsonDocument.Parse(component.Data);
            var definition = definitionJson.RootElement;
            Assert.Equal("weapon", definition.GetProperty("kind").GetString());
            var profileId = definition.GetProperty("weaponProfileId").GetString();
            Assert.NotNull(profileId);
            Assert.Contains(profileId!, profileIds);
            await harness.AddItemDefinitionAsync(entity.Id, entity.Name, component.Data);
            await harness.AddPhysicalItemAsync($"instance.{entity.Id}", entity.Name, entity.Id,
                "subject.high");
        }

        await harness.AddCombatFixturesAsync();
        var attack = await harness.EvaluateRolesAsync("mechanic.dnd2024.weapon-attack",
            new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["weapon"] = "weapon.dnd2024.shortbow",
                ["target"] = "target.fixture"
            }, "{\"ability\":\"dex\"}", 77);
        var damage = await harness.EvaluateRolesAsync("mechanic.dnd2024.weapon-damage.roll",
            new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["weapon"] = "weapon.dnd2024.greatsword"
            }, "{\"ability\":\"str\",\"critical\":false}", 77);
        var equipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.item.equip", new Dictionary<string, string>
            {
                ["item"] = "instance.item.dnd2024.dagger.v1", ["holder"] = "subject.high"
            }, "{\"state\":\"held\"}", 0, "a123456789abcdef0123456789abcded"));
        var burden = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(attack.Ok, attack.Run?.Error);
        Assert.Contains("\"proficient\":true", attack.Run!.Output.Data, StringComparison.Ordinal);
        Assert.True(damage.Ok, damage.Run?.Error);
        using var damageData = JsonDocument.Parse(damage.Run!.Output.Data);
        Assert.Equal("slashing", damageData.RootElement.GetProperty("type").GetString());
        Assert.Equal(2, damageData.RootElement.GetProperty("rolls").GetArrayLength());
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, equipped.Disposition);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"massPounds\":{\"numerator\":11,\"denominator\":1}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(attack.Run.Output.Effects);
        Assert.Empty(damage.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Derived_inventory_readers_fail_closed_on_visible_incompatible_stack_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "item.dnd2024.invalid-stack.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Invalid stack definition",
            FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.invalid-stack", "Invalid Stack", definitionId,
            "subject.high");

        var inventory = await harness.EvaluateRolesAsync("mechanic.dnd2024.inventory.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("mechanic.dnd2024.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.False(inventory.Ok);
        Assert.False(burden.Ok);
        Assert.Empty(inventory.Run!.Output.Effects);
        Assert.Empty(burden.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_experience_records_corrects_replays_and_derives_only_next_level_threshold()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", 1, []);
        var record = harness.ActionFor("mechanic.dnd2024.character-experience.write", "subject.high",
            "{\"mode\":\"record\",\"total\":250}", 0,
            "c123456789abcdef0123456789abcdee");
        var recorded = await harness.Runner.RunAsync(record);
        var replay = await harness.Runner.RunAsync(record);
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "mechanic.dnd2024.character-experience.write", "subject.high",
            "{\"mode\":\"correct\",\"total\":300}", 0,
            "d123456789abcdef0123456789abcdee"));
        var read = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.character-experience.read");
        var unknown = await harness.EvaluateAsync("subject.low", "{}", 0,
            "mechanic.dnd2024.character-experience.read");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Contains("\"status\":\"eligible-for-next-level\"", read.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"nextThreshold\":300", read.Run.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unknown\"", unknown.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Empty(read.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_fighter_progression_is_closed_schema_valid_and_consumed_by_existing_reader()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "character-progression");
        var paths = Directory.GetFiles(contentRoot, "content.dnd2024.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(6, paths.Length);

        var validator = new BoundedJsonSchemaValidator();
        var contentSchema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "data", "dnd2024.character.content-definition.schema.json"));
        var contentCompilation = validator.Compile(contentSchema);
        Assert.True(contentCompilation.IsAccepted, string.Join("; ", contentCompilation.Diagnostics));
        var progressionSchema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "proficiency", "dnd2024.class-progression.schema.json"));
        var progressionCompilation = validator.Compile(progressionSchema);
        Assert.True(progressionCompilation.IsAccepted,
            string.Join("; ", progressionCompilation.Diagnostics));

        var expectedFeatures = new HashSet<string>(StringComparer.Ordinal)
        {
            "content.dnd2024.feature.fighter.action-surge.v1",
            "content.dnd2024.feature.fighter.fighting-style.v1",
            "content.dnd2024.feature.fighter.second-wind.v1",
            "content.dnd2024.feature.fighter.tactical-mind.v1",
            "content.dnd2024.feature.fighter.weapon-mastery.v1"
        };
        await using var harness = await DndHarness.CreateAsync();
        EntityFile? fighter = null;
        var actualFeatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var content = Assert.Single(entity.Components, value =>
                value.DefinitionId == "dnd2024.character.content-definition");
            var contentValidation = validator.Validate(contentCompilation.ProfileId,
                contentCompilation.NormalizedSchema, content.Data);
            Assert.Equal(SchemaValueStatus.Valid, contentValidation.Status);
            using var contentJson = JsonDocument.Parse(content.Data);
            var kind = contentJson.RootElement.GetProperty("kind").GetString();
            Assert.Equal("active", contentJson.RootElement.GetProperty("status").GetString());

            await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, entity.Id, entity.Name);
            await harness.AddApplicationComponentAsync(entity.Id, content.DefinitionId, content.Data);
            var progression = entity.Components.SingleOrDefault(value =>
                value.DefinitionId == "dnd2024.class-progression");
            if (kind == "class")
            {
                fighter = entity;
                Assert.NotNull(progression);
                var progressionValidation = validator.Validate(progressionCompilation.ProfileId,
                    progressionCompilation.NormalizedSchema, progression!.Data);
                Assert.Equal(SchemaValueStatus.Valid, progressionValidation.Status);
                await harness.AddApplicationComponentAsync(entity.Id, progression.DefinitionId,
                    progression.Data);
            }
            else
            {
                Assert.Equal("feature", kind);
                Assert.Null(progression);
                actualFeatures.Add(entity.Id);
            }
        }

        Assert.NotNull(fighter);
        Assert.True(expectedFeatures.SetEquals(actualFeatures));
        var roles = new Dictionary<string, string> { ["class"] = fighter!.Id };
        var level1 = await harness.EvaluateRolesAsync("mechanic.dnd2024.class-progression.read",
            roles, "{\"classLevel\":1}", 0);
        var level2 = await harness.EvaluateRolesAsync("mechanic.dnd2024.class-progression.read",
            roles, "{\"classLevel\":2}", long.MaxValue);
        var level3 = await harness.EvaluateRolesAsync("mechanic.dnd2024.class-progression.read",
            roles, "{\"classLevel\":3}", 0);

        Assert.True(level1.Ok, level1.Run?.Error);
        Assert.True(level2.Ok, level2.Run?.Error);
        Assert.True(level3.Ok, level3.Run?.Error);
        using var level1Json = JsonDocument.Parse(level1.Run!.Output.Data);
        using var level2Json = JsonDocument.Parse(level2.Run!.Output.Data);
        using var level3Json = JsonDocument.Parse(level3.Run!.Output.Data);
        var level1Entitlements = level1Json.RootElement.GetProperty("featureEntitlements")
            .EnumerateArray().ToArray();
        var level2Entitlements = level2Json.RootElement.GetProperty("featureEntitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(new[]
        {
            "content.dnd2024.feature.fighter.fighting-style.v1",
            "content.dnd2024.feature.fighter.second-wind.v1",
            "content.dnd2024.feature.fighter.weapon-mastery.v1"
        }, level1Entitlements.Select(value => value.GetProperty("definitionId").GetString()));
        Assert.Equal(new[]
        {
            "content.dnd2024.feature.fighter.action-surge.v1",
            "content.dnd2024.feature.fighter.tactical-mind.v1"
        }, level2Entitlements.Select(value => value.GetProperty("definitionId").GetString()));
        Assert.All(level1Entitlements.Concat(level2Entitlements), value =>
            Assert.Equal("unimplemented", value.GetProperty("behaviorStatus").GetString()));
        Assert.Equal("unsupported-level", level3Json.RootElement.GetProperty("status").GetString());
        Assert.Empty(level1.Run.Output.Effects);
        Assert.Empty(level1.Run.Output.Events);
        Assert.Empty(level1.Run.Output.Notifications);
        Assert.Empty(level2.Run.Output.Effects);
        Assert.Empty(level3.Run.Output.Effects);
    }

    [Fact]
    public async Task Class_progression_reports_canonical_unimplemented_entitlements_and_source_mismatch()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string classId = "content.dnd2024.class.fighter.v1";
        const string locator = "Classes > Fighter PDF page 60";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, classId, "Fighter");
        await harness.AddApplicationComponentAsync(classId, "dnd2024.character.content-definition",
            "{\"kind\":\"class\",\"contentKey\":\"fighter\",\"contentVersion\":1,\"status\":\"active\",\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        var progression =
            "{\"hitDieSides\":10,\"fixedHitPointGainBeforeConstitution\":6,\"levels\":[{\"classLevel\":1,\"featureDefinitionIds\":[],\"choiceSetDefinitionIds\":[]},{\"classLevel\":2,\"featureDefinitionIds\":[\"content.dnd2024.feature.action-surge.v1\"],\"choiceSetDefinitionIds\":[]}],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"" + locator + "\"}}";
        await harness.AddApplicationComponentAsync(classId, "dnd2024.class-progression", progression);
        var roles = new Dictionary<string, string> { ["class"] = classId };

        var supported = await harness.EvaluateRolesAsync("mechanic.dnd2024.class-progression.read",
            roles, "{\"classLevel\":2}", 0);
        var unsupported = await harness.EvaluateRolesAsync("mechanic.dnd2024.class-progression.read",
            roles, "{\"classLevel\":3}", 0);
        await harness.ReplaceApplicationComponentRawAsync(classId, "dnd2024.class-progression",
            progression.Replace(locator, "Classes > Fighter PDF page 61", StringComparison.Ordinal));
        var mismatch = await harness.EvaluateRolesAsync("mechanic.dnd2024.class-progression.read",
            roles, "{\"classLevel\":2}", 0);

        Assert.Contains("\"status\":\"supported\"", supported.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"behaviorStatus\":\"unimplemented\"", supported.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unsupported-level\"", unsupported.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"problem\":\"source-mismatch\"", mismatch.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Empty(supported.Run.Output.Effects);
    }

    [Fact]
    public async Task Character_sheet_reader_derives_complete_canonical_effect_free_view_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceCoreComponentRawAsync("subject.high", "dnd2024.abilities",
            "{\"str\":16,\"dex\":14,\"con\":14,\"int\":10,\"wis\":15,\"cha\":8}");
        await harness.AddProficiencyStateAsync("subject.high", 1, ["athletics", "perception"]);
        await harness.AddSavingThrowStateAsync("subject.high", ["str", "con"]);

        var first = await harness.EvaluateAsync("subject.high", "{}", 1,
            "mechanic.dnd2024.character-sheet.read");
        var otherSeed = await harness.EvaluateAsync("subject.high", "{}", long.MaxValue,
            "mechanic.dnd2024.character-sheet.read");

        Assert.True(first.Ok, first.Run?.Error);
        Assert.True(otherSeed.Ok, otherSeed.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, otherSeed.Run!.Output.Data);
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        using var result = JsonDocument.Parse(first.Run.Output.Data);
        var data = result.RootElement;
        Assert.Equal("character-sheet-core", data.GetProperty("test").GetString());
        Assert.Equal(1, data.GetProperty("level").GetInt32());
        Assert.Equal(2, data.GetProperty("proficiencyBonus").GetInt32());
        var abilities = data.GetProperty("abilities").EnumerateArray().ToArray();
        Assert.Equal(["str", "dex", "con", "int", "wis", "cha"],
            abilities.Select(value => value.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(16, abilities[0].GetProperty("score").GetInt32());
        Assert.Equal(3, abilities[0].GetProperty("modifier").GetInt32());
        var saves = data.GetProperty("savingThrows").EnumerateArray().ToArray();
        Assert.Equal(6, saves.Length);
        Assert.Equal(["str", "dex", "con", "int", "wis", "cha"],
            saves.Select(value => value.GetProperty("ability").GetString()!).ToArray());
        Assert.True(saves[0].GetProperty("proficient").GetBoolean());
        Assert.Equal(5, saves[0].GetProperty("modifier").GetInt32());
        var skills = data.GetProperty("skills").EnumerateArray().ToArray();
        Assert.Equal(18, skills.Length);
        Assert.Equal(["acrobatics", "animal-handling", "arcana", "athletics", "deception",
            "history", "insight", "intimidation", "investigation", "medicine", "nature",
            "perception", "performance", "persuasion", "religion", "sleight-of-hand", "stealth",
            "survival"], skills.Select(value => value.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(["dex", "wis", "int", "str", "cha", "int", "wis", "cha", "int", "wis",
            "int", "wis", "cha", "cha", "int", "dex", "dex", "wis"],
            skills.Select(value => value.GetProperty("ability").GetString()!).ToArray());
        var perception = skills.Single(value =>
            value.GetProperty("id").GetString() == "perception");
        Assert.Equal("wis", perception.GetProperty("ability").GetString());
        Assert.True(perception.GetProperty("proficient").GetBoolean());
        Assert.Equal(4, perception.GetProperty("modifier").GetInt32());
        Assert.Equal("dex", data.GetProperty("initiative").GetProperty("ability").GetString());
        Assert.Equal(2, data.GetProperty("initiative").GetProperty("modifier").GetInt32());
        var passive = data.GetProperty("basePassivePerceptionBreakdown");
        Assert.Equal(10, passive.GetProperty("base").GetInt32());
        Assert.Equal(4, passive.GetProperty("modifier").GetInt32());
        Assert.Equal(14, passive.GetProperty("total").GetInt32());

        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.abilities");
        var request = harness.ActionFor("mechanic.dnd2024.character-sheet.read", "subject.high",
            "{}", 99, "a123456789abcdef0123456789abcde0");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.abilities");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Character_sheet_reader_covers_score_level_and_proficiency_boundaries()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceCoreComponentRawAsync("subject.low", "dnd2024.abilities",
            "{\"str\":1,\"dex\":30,\"con\":2,\"int\":3,\"wis\":30,\"cha\":1}");
        await harness.AddProficiencyStateAsync("subject.low", 20, []);
        await harness.AddSavingThrowStateAsync("subject.low", ["cha", "wis", "int", "con", "dex", "str"]);

        var result = await harness.EvaluateAsync("subject.low", "{}", 0,
            "mechanic.dnd2024.character-sheet.read");

        Assert.True(result.Ok, result.Run?.Error);
        using var document = JsonDocument.Parse(result.Run!.Output.Data);
        var data = document.RootElement;
        Assert.Equal(6, data.GetProperty("proficiencyBonus").GetInt32());
        Assert.Equal(-5, data.GetProperty("abilityModifiers").GetProperty("str").GetInt32());
        Assert.Equal(10, data.GetProperty("abilityModifiers").GetProperty("dex").GetInt32());
        Assert.Equal(16, data.GetProperty("savingThrowModifiers").GetProperty("dex").GetInt32());
        Assert.Equal(["str", "dex", "con", "int", "wis", "cha"],
            data.GetProperty("savingThrowProficiencies").EnumerateArray()
                .Select(value => value.GetString()!).ToArray());
        Assert.Equal(10, data.GetProperty("skillModifiers").GetProperty("perception").GetInt32());
        Assert.Equal(20, data.GetProperty("basePassivePerception").GetInt32());
        Assert.Equal(20, data.GetProperty("basePassivePerceptionBreakdown")
            .GetProperty("total").GetInt32());
        AssertCharacterSheetMatchesSourceVector(result.Run.Output.Data, "level-twenty-boundaries");
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(12, 4)]
    [InlineData(13, 5)]
    [InlineData(16, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    public async Task Character_sheet_reader_derives_every_proficiency_bonus_boundary(
        int level, int expectedBonus)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", level, []);
        await harness.AddSavingThrowStateAsync("subject.high", []);

        var result = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.character-sheet.read");

        Assert.True(result.Ok, result.Run?.Error);
        using var document = JsonDocument.Parse(result.Run!.Output.Data);
        Assert.Equal(expectedBonus, document.RootElement.GetProperty("proficiencyBonus").GetInt32());
    }

    [Fact]
    public async Task Character_sheet_reader_rejects_input_injection_and_corrupt_source_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", 1, ["perception"]);
        await harness.AddSavingThrowStateAsync("subject.high", ["str"]);

        var injected = await harness.EvaluateAsync("subject.high", "{\"proficiencyBonus\":6}", 0,
            "mechanic.dnd2024.character-sheet.read");
        Assert.False(injected.Ok);
        Assert.Contains("empty object", injected.Run?.Error, StringComparison.Ordinal);

        await harness.ReplaceCoreComponentRawAsync("subject.high", "dnd2024.skill-proficiencies",
            "{\"skills\":[\"perception\",\"perception\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Proficiency > Skill Proficiencies and Skills\"}}");
        var duplicate = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.character-sheet.read");
        Assert.False(duplicate.Ok);
        Assert.Contains("unique canonical IDs", duplicate.Run?.Error, StringComparison.Ordinal);

        await harness.ReplaceCoreComponentRawAsync("subject.high", "dnd2024.character-level",
            "{\"level\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"wrong\"}}");
        var drifted = await harness.EvaluateAsync("subject.high", "{}", 0,
            "mechanic.dnd2024.character-sheet.read");
        Assert.False(drifted.Ok);
        Assert.Contains("source-drifted", drifted.Run?.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Character_sheet_javascript_rejects_a_malformed_raw_projection()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "catalog", "applications",
            "dnd2024", "mechanics", "proficiency", "mechanic.dnd2024.character-sheet.read.js"));
        var valid = new Dictionary<string, string>
        {
            ["dnd2024.abilities"] = "{\"str\":10,\"dex\":10,\"con\":10,\"int\":10,\"wis\":10,\"cha\":10}",
            ["dnd2024.character-level"] = "{\"level\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Creation > Level Advancement > Character Advancement\"}}",
            ["dnd2024.skill-proficiencies"] = "{\"skills\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Proficiency > Skill Proficiencies and Skills\"}}",
            ["dnd2024.saving-throw-proficiencies"] = "{\"abilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Proficiency > Saving Throw Proficiencies\"}}"
        };
        static MechanicProjection Projection(IReadOnlyDictionary<string, string> components) => new()
        {
            Roles = new Dictionary<string, EntityProjection>
            {
                ["subject"] = new("subject", "Subject", components)
            },
            Input = "{}",
            Seed = 0
        };
        var cases = new[]
        {
            (Component: "dnd2024.character-level", Value: "{\"level\":1",
                Error: "missing or malformed"),
            (Component: "dnd2024.abilities",
                Value: "{\"str\":31,\"dex\":10,\"con\":10,\"int\":10,\"wis\":10,\"cha\":10}",
                Error: "1 through 30"),
            (Component: "dnd2024.character-level",
                Value: "{\"level\":1,\"proficiencyBonus\":2,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Creation > Level Advancement > Character Advancement\"}}",
                Error: "invalid or source-drifted")
        };
        foreach (var @case in cases)
        {
            var components = new Dictionary<string, string>(valid) { [@case.Component] = @case.Value };
            var result = await new JintMechanicEngine().RunAsync(source, Projection(components),
                ExecutionLimits.Default);
            Assert.False(result.Ok);
            Assert.Contains(@case.Error, result.Error, StringComparison.Ordinal);
            Assert.Empty(result.Output.Effects);
            Assert.Empty(result.Output.Events);
            Assert.Empty(result.Output.Notifications);
        }
    }

    [Fact]
    public async Task Dice_primitive_is_seeded_bounded_closed_and_effect_free()
    {
        await using var harness = await DndHarness.CreateAsync();
        var noRoles = new Dictionary<string, string>();
        var first = await harness.EvaluateRolesAsync("mechanic.dnd2024.dice", noRoles,
            "{\"count\":2,\"sides\":6,\"modifier\":3}", 4242);
        var replay = await harness.EvaluateRolesAsync("mechanic.dnd2024.dice", noRoles,
            "{\"count\":2,\"sides\":6,\"modifier\":3}", 4242);
        var defaults = await harness.EvaluateRolesAsync("mechanic.dnd2024.dice", noRoles, "{}", 7);
        var invalid = await harness.EvaluateRolesAsync("mechanic.dnd2024.dice", noRoles,
            "{\"count\":101}", 7);
        var extra = await harness.EvaluateRolesAsync("mechanic.dnd2024.dice", noRoles,
            "{\"cheat\":20}", 7);

        Assert.True(first.Ok, first.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, replay.Run!.Output.Data);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var rolls = data.RootElement.GetProperty("rolls").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        Assert.Equal(2, rolls.Length);
        Assert.All(rolls, value => Assert.InRange(value, 1, 6));
        Assert.Contains("\"count\":1,\"sides\":20,\"modifier\":0", defaults.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.False(invalid.Ok);
        Assert.False(extra.Ok);
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);
    }

    [Fact]
    public void Slice_9_closure_classifies_every_donor_derivation_and_hashes_the_activated_cohort()
    {
        var root = RepositoryRoot();
        var inventoryPath = Path.Combine(root, "ruleset", "dnd2024", "adoption", "evidence",
            "slice-9-derivation-candidates.json");
        var closurePath = Path.Combine(root, "ruleset", "dnd2024", "adoption", "evidence",
            "slice-9-closure.json");
        using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        using var closure = JsonDocument.Parse(File.ReadAllText(closurePath));
        var closureRoot = closure.RootElement;
        Assert.Equal("dnd-code-adoption-slice-9-closure/v1",
            closureRoot.GetProperty("format").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inventoryPath))),
            closureRoot.GetProperty("inventorySha256").GetString());

        var candidates = inventory.RootElement.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(17, candidates.Length);
        Assert.Equal(17, candidates.Select(value => value.GetProperty("key").GetString())
            .Distinct(StringComparer.Ordinal).Count());
        var dispositions = candidates.Select(value => value.GetProperty("disposition").GetString()!)
            .ToArray();
        var summary = closureRoot.GetProperty("candidateGroups");
        Assert.Equal(17, summary.GetProperty("classified").GetInt32());
        Assert.Equal(dispositions.Count(value => value == "retain-current"),
            summary.GetProperty("retainedCurrent").GetInt32());
        Assert.Equal(dispositions.Count(value => value == "split-retain-adapt"),
            summary.GetProperty("splitRetainedAndAdapted").GetInt32());
        Assert.Equal(dispositions.Count(value => value.StartsWith("retain-and-defer-", StringComparison.Ordinal)),
            summary.GetProperty("retainedAndDeferred").GetInt32());
        Assert.Equal(dispositions.Count(value => value == "adapt-slice-9b"),
            summary.GetProperty("adapted").GetInt32());
        Assert.Equal(dispositions.Count(value => value is "reject" or "reject-runtime"),
            summary.GetProperty("rejected").GetInt32());
        Assert.Equal(dispositions.Count(value => value.StartsWith("defer-", StringComparison.Ordinal)),
            summary.GetProperty("deferred").GetInt32());
        Assert.Empty(summary.GetProperty("unresolved").EnumerateArray());

        var mechanic = closureRoot.GetProperty("activated").GetProperty("mechanic");
        var mechanicContract = Path.Combine(root,
            mechanic.GetProperty("contractPath").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        var mechanicSource = Path.Combine(root,
            mechanic.GetProperty("sourcePath").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        var procedure = closureRoot.GetProperty("activated").GetProperty("procedure");
        var procedurePath = Path.Combine(root,
            procedure.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains("id: mechanic.dnd2024.character-sheet.read",
            File.ReadAllText(mechanicContract), StringComparison.Ordinal);
        Assert.Contains("id: procedure.mechanic.dnd2024.character-sheet",
            File.ReadAllText(procedurePath), StringComparison.Ordinal);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mechanicContract))),
            mechanic.GetProperty("contractSha256").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mechanicSource))),
            mechanic.GetProperty("sourceSha256").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(procedurePath))),
            procedure.GetProperty("sha256").GetString());
        Assert.Empty(closureRoot.GetProperty("activated").GetProperty("components").EnumerateArray());
        Assert.Empty(closureRoot.GetProperty("activated").GetProperty("storedProjections").EnumerateArray());
        Assert.Empty(closureRoot.GetProperty("activated").GetProperty("publicOperations").EnumerateArray());
    }

    [Fact]
    public void Slice_8_closure_matches_every_classified_mechanic_component_and_procedure()
    {
        var root = RepositoryRoot();
        var matrixPath = Path.Combine(root,
            "ruleset", "dnd2024", "adoption", "evidence", "coverage-matrix-1b.json");
        using var matrix = JsonDocument.Parse(File.ReadAllText(matrixPath));
        using var closure = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ruleset", "dnd2024", "adoption", "evidence", "slice-8-closure.json")));
        var closureRoot = closure.RootElement;
        Assert.Equal("dnd-code-adoption-slice-8-closure-v1", closureRoot.GetProperty("format").GetString());
        Assert.Equal("ruleset/dnd2024/adoption/evidence/coverage-matrix-1b.json",
            closureRoot.GetProperty("matrix").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(matrixPath))),
            closureRoot.GetProperty("matrixSha256").GetString());
        var mechanics = new HashSet<string>(StringComparer.Ordinal);
        var components = new HashSet<string>(StringComparer.Ordinal);
        var procedures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in matrix.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("disposition").GetString() != "recover-archive") continue;
            var key = row.GetProperty("capabilityKey").GetString()!;
            var title = row.GetProperty("title").GetString()!;
            if (key.StartsWith("mechanic.", StringComparison.Ordinal)
                && title.StartsWith("mechanic.dnd2024.", StringComparison.Ordinal)) mechanics.Add(title);
            else if (key.StartsWith("componentdefinition.", StringComparison.Ordinal)
                     && title.StartsWith("dnd2024.", StringComparison.Ordinal)) components.Add(title);
            else if (key.StartsWith("procedure.", StringComparison.Ordinal)
                     && title.StartsWith("procedure.mechanic.dnd2024.", StringComparison.Ordinal)) procedures.Add(title);
        }

        static HashSet<string> FrontMatterIds(string directory, string prefix) =>
            Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
                .SelectMany(path => File.ReadLines(path)
                    .Where(line => line.StartsWith("id: ", StringComparison.Ordinal))
                    .Select(line => line[4..].Trim()))
                .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
        var currentMechanics = FrontMatterIds(Path.Combine(root, "catalog", "applications", "dnd2024", "mechanics"),
            "mechanic.dnd2024.");
        var currentProcedures = FrontMatterIds(Path.Combine(root, "catalog", "applications", "dnd2024", "procedures"),
            "procedure.mechanic.dnd2024.");
        var currentComponents = Directory.EnumerateFiles(Path.Combine(root, "catalog", "applications", "dnd2024", "components"),
                "*.json", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".schema.json", StringComparison.Ordinal))
            .Select(path => JsonDocument.Parse(File.ReadAllText(path)))
            .Select(document =>
            {
                using (document) return document.RootElement.GetProperty("id").GetString()!;
            }).ToHashSet(StringComparer.Ordinal);
        var replacements = closureRoot.GetProperty("components").GetProperty("replacements")
            .EnumerateArray().Select(value => value.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var resolvedComponents = new HashSet<string>(currentComponents, StringComparer.Ordinal);
        resolvedComponents.UnionWith(replacements);

        Assert.Equal(51, mechanics.Count);
        Assert.Equal(26, components.Count);
        Assert.Equal(39, procedures.Count);
        Assert.Equal(51, closureRoot.GetProperty("mechanics").GetProperty("classified").GetInt32());
        Assert.Equal(51, closureRoot.GetProperty("mechanics").GetProperty("active").GetInt32());
        Assert.Empty(closureRoot.GetProperty("mechanics").GetProperty("unresolved").EnumerateArray());
        Assert.Equal(26, closureRoot.GetProperty("components").GetProperty("classified").GetInt32());
        Assert.Equal(25, closureRoot.GetProperty("components").GetProperty("active").GetInt32());
        Assert.Empty(closureRoot.GetProperty("components").GetProperty("unresolved").EnumerateArray());
        Assert.Equal(39, closureRoot.GetProperty("procedures").GetProperty("classified").GetInt32());
        Assert.Equal(39, closureRoot.GetProperty("procedures").GetProperty("active").GetInt32());
        Assert.Empty(closureRoot.GetProperty("procedures").GetProperty("unresolved").EnumerateArray());
        Assert.True(mechanics.IsSubsetOf(currentMechanics),
            "Mechanics: " + string.Join(", ", mechanics.Except(currentMechanics)));
        Assert.True(components.IsSubsetOf(resolvedComponents),
            "Components: " + string.Join(", ", components.Except(resolvedComponents)));
        Assert.True(procedures.IsSubsetOf(currentProcedures),
            "Procedures: " + string.Join(", ", procedures.Except(currentProcedures)));
        Assert.Equal("dnd2024.source", Assert.Single(replacements));
        Assert.Equal("replace", closureRoot.GetProperty("components")
            .GetProperty("replacements")[0].GetProperty("disposition").GetString());
    }

    private static void AssertCharacterSheetMatchesSourceVector(string dataJson, string caseId)
    {
        var root = RepositoryRoot();
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ruleset", "dnd2024",
            "adoption", "conformance", "fixtures", "adapted-character-sheet.source-vectors.json")));
        using var actual = JsonDocument.Parse("{\"data\":" + dataJson + "}");
        var sourceCase = vectors.RootElement.GetProperty("cases").EnumerateArray().Single(value =>
            value.GetProperty("id").GetString() == caseId);
        foreach (var comparison in vectors.RootElement.GetProperty("compare").EnumerateArray())
        {
            var pointer = comparison.GetProperty("pointer").GetString()!;
            var expected = FollowJsonPointer(sourceCase.GetProperty("result"), pointer);
            var observed = FollowJsonPointer(actual.RootElement, pointer);
            Assert.True(JsonElement.DeepEquals(expected, observed),
                comparison.GetProperty("name").GetString() + " differs for " + caseId);
        }
    }

    private static JsonElement FollowJsonPointer(JsonElement root, string pointer)
    {
        var current = root;
        foreach (var raw in pointer.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current.ValueKind == JsonValueKind.Array
                ? current[int.Parse(segment, System.Globalization.CultureInfo.InvariantCulture)]
                : current.GetProperty(segment);
        }
        return current;
    }

    private static int Roll(ApplicationMechanicEvaluationResult result)
    {
        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        return data.RootElement.GetProperty("roll").GetInt32();
    }

    private static async Task<(string Input, long Seed)> EncounterOrderWithHighFirstAsync(DndHarness harness)
    {
        for (long seed = 1; seed <= 512; seed++)
        {
            var high = await harness.EvaluateAsync("subject.high", "{}", DeriveSeed(seed, 0),
                "mechanic.dnd2024.initiative.roll");
            var low = await harness.EvaluateAsync("subject.low", "{}", DeriveSeed(seed, 1),
                "mechanic.dnd2024.initiative.roll");
            var highValue = Initiative(high);
            var lowValue = Initiative(low);
            if (highValue < lowValue) continue;
            var ties = highValue == lowValue
                ? new[] { new[] { "subject.high", "subject.low" } }
                : [];
            return (JsonSerializer.Serialize(new
            {
                participants = new Dictionary<string, object>
                {
                    ["subject.high"] = new(),
                    ["subject.low"] = new()
                },
                tieDecisions = ties
            }), seed);
        }
        throw new InvalidOperationException("No deterministic Initiative seed ordered subject.high first.");
    }

    private static int Initiative(ApplicationMechanicEvaluationResult result)
    {
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        return data.RootElement.GetProperty("initiative").GetInt32();
    }

    private static long DeriveSeed(long parentSeed, int ordinal)
    {
        unchecked
        {
            var value = parentSeed ^ (long)0x9E3779B97F4A7C15UL;
            value += (long)ordinal * (long)0x632BE59BD9B4E019UL;
            value ^= value >> 30; value *= (long)0xBF58476D1CE4E5B9UL;
            value ^= value >> 27; value *= (long)0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    private static bool Succeeded(ApplicationMechanicEvaluationResult result)
    {
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        return data.RootElement.GetProperty("succeeded").GetBoolean();
    }

    private static void AssertRollMode(
        ApplicationMechanicEvaluationResult result,
        string mode,
        int count,
        Func<int[], int> expected)
    {
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        var root = data.RootElement;
        var rolls = root.GetProperty("rolls").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Assert.Equal(mode, root.GetProperty("rollMode").GetString());
        Assert.Equal(count, rolls.Length);
        Assert.Equal(expected(rolls), root.GetProperty("roll").GetInt32());
    }

    private sealed class DndHarness : IAsyncDisposable
    {
        public const string StateSpaceId = "dnd2024-ability-check";
        private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("dnd2024");
        private readonly SqliteFixture _fixture;
        private readonly DantesRoleplayDbContext _db;
        private readonly ActivatedApplicationCatalogProvider _catalogs;
        private readonly RegisteredComponentTypeVersion _abilities;
        private readonly RegisteredComponentTypeVersion _level;
        private readonly RegisteredComponentTypeVersion _skills;
        private readonly RegisteredComponentTypeVersion _saves;
        private readonly RegisteredComponentTypeVersion _weaponProfile;
        private readonly RegisteredComponentTypeVersion _weaponProficiencies;
        private readonly RegisteredComponentTypeVersion _armorClass;
        private readonly RegisteredComponentTypeVersion _hitPoints;
        private readonly RegisteredComponentTypeVersion _initiativeOrder;
        private readonly RegisteredComponentTypeVersion _turnState;
        private readonly RegisteredComponentTypeVersion _speed;
        private readonly RegisteredComponentTypeVersion _turnBudget;
        private readonly RegisteredComponentTypeVersion _conditions;
        private readonly IReadOnlyDictionary<string, RegisteredComponentTypeVersion> _additionalTypes;
        public IReadOnlySet<string> ActiveSourcePaths { get; }

        private DndHarness(
            SqliteFixture fixture,
            DantesRoleplayDbContext db,
            ActivatedApplicationCatalogProvider catalogs,
            RegisteredComponentTypeVersion abilities,
            RegisteredComponentTypeVersion level,
            RegisteredComponentTypeVersion skills,
            RegisteredComponentTypeVersion saves,
            RegisteredComponentTypeVersion weaponProfile,
            RegisteredComponentTypeVersion weaponProficiencies,
            RegisteredComponentTypeVersion armorClass,
            RegisteredComponentTypeVersion hitPoints,
            RegisteredComponentTypeVersion initiativeOrder,
            RegisteredComponentTypeVersion turnState,
            RegisteredComponentTypeVersion speed,
            RegisteredComponentTypeVersion turnBudget,
            RegisteredComponentTypeVersion conditions,
            IReadOnlyDictionary<string, RegisteredComponentTypeVersion> additionalTypes,
            IReadOnlySet<string> activeSourcePaths,
            SqliteEntityComponentStore entities,
            ApplicationActionRunner runner)
        {
            _fixture = fixture;
            _db = db;
            _catalogs = catalogs;
            _abilities = abilities;
            _level = level;
            _skills = skills;
            _saves = saves;
            _weaponProfile = weaponProfile;
            _weaponProficiencies = weaponProficiencies;
            _armorClass = armorClass;
            _hitPoints = hitPoints;
            _initiativeOrder = initiativeOrder;
            _turnState = turnState;
            _speed = speed;
            _turnBudget = turnBudget;
            _conditions = conditions;
            _additionalTypes = additionalTypes;
            ActiveSourcePaths = activeSourcePaths;
            Entities = entities;
            Runner = runner;
        }

        public SqliteEntityComponentStore Entities { get; }
        public ApplicationActionRunner Runner { get; }

        public static async Task<DndHarness> CreateAsync(bool includeLegacyEquipmentExtension = false)
        {
            var fixture = new SqliteFixture();
            var db = fixture.CreateContext();
            var applications = new SqliteApplicationRegistry(db);
            var revision = applications.Register(new(
                Application, "D&D 2024", "A modular D&D 2024 application.", []));
            var sources = new SqliteSourceRegistry(db);
            sources.Register(new(
                Application, "dnd2024-core", "workspace", "catalog/applications/dnd2024/**/*",
                SourceTrust.Trusted, 0, "dnd2024-core-catalog"));
            if (includeLegacyEquipmentExtension)
                sources.Register(new(
                    Application, "dnd2024-extension.legacy-equipment", "workspace",
                    "catalog/extensions/dnd2024/legacy-equipment/**/*", SourceTrust.Trusted, 100,
                    "dnd2024-extension.legacy-equipment"));
            IReadOnlyList<string> sourceIds = includeLegacyEquipmentExtension
                ? ["dnd2024-core", "dnd2024-extension.legacy-equipment"]
                : ["dnd2024-core"];
            var roots = new WorkspaceRoot();
            var preview = new ApplicationPreviewService(
                applications, sources,
                new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
                new SourceOverlayResolver());
            var previewResult = await preview.PreviewAsync(Application, sourceIds);
            Assert.True(previewResult.IsValid, string.Join("; ", previewResult.Problems.Select(value => value.Code)));

            var operations = new OperationLog(db);
            var activations = new ApplicationActivationService(
                db, preview, new EmptyImpact(), operations);
            var activationRequest = new ApplicationActivationRequest(
                Application, previewResult.PreviewFingerprint, null, sourceIds);
            var context = ActivationContext();
            Assert.Equal("would-activate",
                (await activations.PreviewAsync(activationRequest, context)).Outcome);
            var activation = await activations.ActivateAsync(activationRequest, context);

            var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
            stateSpaces.Create(new(StateSpaceId, revision, activation.Activation.ActivationFingerprint));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var abilityDefinition = await DefinitionAsync("abilities/dnd2024.abilities");
            var levelDefinition = await DefinitionAsync("proficiency/dnd2024.character-level");
            var skillDefinition = await DefinitionAsync("proficiency/dnd2024.skill-proficiencies");
            var saveDefinition = await DefinitionAsync("proficiency/dnd2024.saving-throw-proficiencies");
            var weaponProfileDefinition = await DefinitionAsync("combat/dnd2024.weapon-profile");
            var weaponProficienciesDefinition = await DefinitionAsync("combat/dnd2024.weapon-proficiencies");
            var armorClassDefinition = await DefinitionAsync("combat/dnd2024.armor-class");
            var hitPointsDefinition = await DefinitionAsync("combat/dnd2024.hit-points");
            var initiativeOrderDefinition = await DefinitionAsync("combat/dnd2024.encounter-initiative-order");
            var turnStateDefinition = await DefinitionAsync("combat/dnd2024.encounter-turn-state");
            var speedDefinition = await DefinitionAsync("movement/dnd2024.speed");
            var turnBudgetDefinition = await DefinitionAsync("combat/dnd2024.turn-budget");
            var conditionsDefinition = await DefinitionAsync("conditions/dnd2024.conditions");
            var abilities = types.Define(new(Application, abilityDefinition.Id, abilityDefinition.Schema));
            var level = types.Define(new(Application, levelDefinition.Id, levelDefinition.Schema));
            var skills = types.Define(new(Application, skillDefinition.Id, skillDefinition.Schema));
            var saves = types.Define(new(Application, saveDefinition.Id, saveDefinition.Schema));
            var weaponProfile = types.Define(new(Application, weaponProfileDefinition.Id, weaponProfileDefinition.Schema));
            var weaponProficiencies = types.Define(new(Application, weaponProficienciesDefinition.Id, weaponProficienciesDefinition.Schema));
            var armorClass = types.Define(new(Application, armorClassDefinition.Id, armorClassDefinition.Schema));
            var hitPoints = types.Define(new(Application, hitPointsDefinition.Id, hitPointsDefinition.Schema));
            var initiativeOrder = types.Define(new(Application, initiativeOrderDefinition.Id, initiativeOrderDefinition.Schema));
            var turnState = types.Define(new(Application, turnStateDefinition.Id, turnStateDefinition.Schema));
            var speed = types.Define(new(Application, speedDefinition.Id, speedDefinition.Schema));
            var turnBudget = types.Define(new(Application, turnBudgetDefinition.Id, turnBudgetDefinition.Schema));
            var conditions = types.Define(new(Application, conditionsDefinition.Id, conditionsDefinition.Schema));
            var additionalTypes = new Dictionary<string, RegisteredComponentTypeVersion>(StringComparer.Ordinal);
            foreach (var path in new[]
            {
                "data/dnd2024.character.content-definition",
                "data/dnd2024.character.profile",
                "data/dnd2024.creature-size",
                "data/dnd2024.language-proficiencies",
                "data/dnd2024.tool-proficiencies",
                "data/dnd2024.equipment-state",
                "data/dnd2024.item-activity",
                "data/dnd2024.item-definition",
                "data/dnd2024.item-instance",
                "data/dnd2024.item-quantity",
                "combat/dnd2024.damage-mitigation",
                "combat/dnd2024.temporary-hit-points",
                "proficiency/dnd2024.character-experience",
                "proficiency/dnd2024.class-progression"
            })
            {
                var definition = await DefinitionAsync(path);
                additionalTypes[definition.Id] = types.Define(new(Application, definition.Id, definition.Schema));
            }
            var entities = new SqliteEntityComponentStore(db, types, schemas);
            await AddSubjectAsync(entities, abilities, "subject.high",
                "{\"str\":30,\"dex\":10,\"con\":10,\"int\":10,\"wis\":10,\"cha\":10}");
            await AddSubjectAsync(entities, abilities, "subject.low",
                "{\"str\":1,\"dex\":10,\"con\":10,\"int\":10,\"wis\":10,\"cha\":10}");

            var materializer = new ActivatedApplicationCatalogMaterializer(applications, activations, sources, roots);
            _ = materializer.BuildFeatureSnapshot(Application);
            var catalogs = new ActivatedApplicationCatalogProvider(
                new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
                materializer,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("dnd2024-ability-check-cursor-key")));
            var evaluator = new ApplicationMechanicEvaluator(
                catalogs, new ApplicationMechanicProjectionResolver(db, stateSpaces), new JintMechanicEngine());
            var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
            var applier = new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges);
            var runner = new ApplicationActionRunner(
                catalogs, activations, stateSpaces, types, entities, edges, evaluator, applier, operations);
            return new(fixture, db, catalogs, abilities, level, skills, saves, weaponProfile, weaponProficiencies,
                armorClass, hitPoints, initiativeOrder, turnState, speed, turnBudget, conditions, additionalTypes,
                activation.Activation.Winners.Select(value => value.RelativePath).ToHashSet(StringComparer.Ordinal),
                entities, runner);
        }

        public async Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
            string subjectId, string input, long seed, string localMechanicId = "mechanic.dnd2024.check.ability")
            => await EvaluateRolesAsync(localMechanicId, new Dictionary<string, string> { ["subject"] = subjectId }, input, seed);

        public async Task<ApplicationMechanicEvaluationResult> EvaluateRolesAsync(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed)
        {
            var record = Record(localMechanicId);
            var componentMapping = new Dictionary<string, EcsComponentReference>
                {
                    ["dnd2024.abilities"] = new(_abilities.QualifiedId, _abilities.Version, _abilities.SchemaHash),
                    ["dnd2024.character-level"] = new(_level.QualifiedId, _level.Version, _level.SchemaHash),
                    ["dnd2024.skill-proficiencies"] = new(_skills.QualifiedId, _skills.Version, _skills.SchemaHash),
                    ["dnd2024.saving-throw-proficiencies"] = new(_saves.QualifiedId, _saves.Version, _saves.SchemaHash),
                    ["dnd2024.weapon-profile"] = new(_weaponProfile.QualifiedId, _weaponProfile.Version, _weaponProfile.SchemaHash),
                    ["dnd2024.weapon-proficiencies"] = new(_weaponProficiencies.QualifiedId, _weaponProficiencies.Version, _weaponProficiencies.SchemaHash),
                    ["dnd2024.armor-class"] = new(_armorClass.QualifiedId, _armorClass.Version, _armorClass.SchemaHash),
                    ["dnd2024.hit-points"] = new(_hitPoints.QualifiedId, _hitPoints.Version, _hitPoints.SchemaHash),
                    ["dnd2024.encounter-initiative-order"] = new(_initiativeOrder.QualifiedId, _initiativeOrder.Version, _initiativeOrder.SchemaHash),
                    ["dnd2024.encounter-turn-state"] = new(_turnState.QualifiedId, _turnState.Version, _turnState.SchemaHash),
                    ["dnd2024.speed"] = new(_speed.QualifiedId, _speed.Version, _speed.SchemaHash),
                    ["dnd2024.turn-budget"] = new(_turnBudget.QualifiedId, _turnBudget.Version, _turnBudget.SchemaHash),
                    ["dnd2024.conditions"] = new(_conditions.QualifiedId, _conditions.Version, _conditions.SchemaHash)
                };
            foreach (var (componentId, type) in _additionalTypes)
                componentMapping[componentId] = new(type.QualifiedId, type.Version, type.SchemaHash);
            var mapping = new ApplicationMechanicProjectionMapping(componentMapping,
                new Dictionary<string, string>());
            return await new ApplicationMechanicEvaluator(
                _catalogs, new ApplicationMechanicProjectionResolver(_db,
                    new SqliteStateSpaceRegistry(_db, new SqliteApplicationRegistry(_db))),
                new JintMechanicEngine()).EvaluateAsync(new(
                    StateSpaceId, Application, record.Summary.QualifiedId, record.Summary.ContentFingerprint,
                mapping, roles, input, seed));
        }

        public ApplicationActionExecutionRequest Action(
            string subjectId, string input, long seed, string operationId)
            => ActionFor("mechanic.dnd2024.check.ability", subjectId, input, seed, operationId);

        public ApplicationActionExecutionRequest ActionFor(
            string localMechanicId, string subjectId, string input, long seed, string operationId)
        {
            var record = Record(localMechanicId);
            var subject = record.Summary.QualifiedId + "\n" + subjectId + "\n" + input + "\n" + seed;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
            return new(StateSpaceId, Application, record.Summary.QualifiedId, record.Summary.ContentFingerprint,
                new Dictionary<string, string> { ["subject"] = subjectId }, input, seed,
                new(operationId, fingerprint));
        }

        public ApplicationActionExecutionRequest ActionForRoles(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed, string operationId)
        {
            var record = Record(localMechanicId);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Summary.QualifiedId + "\n" + input + "\n" + seed)));
            return new(StateSpaceId, Application, record.Summary.QualifiedId, record.Summary.ContentFingerprint,
                roles, input, seed, new(operationId, fingerprint));
        }

        public async Task AddProficiencyStateAsync(string subjectId, int level, IReadOnlyList<string> skills)
        {
            await Entities.AddComponentAsync(new(StateSpaceId, subjectId,
                new(_level.QualifiedId, _level.Version, _level.SchemaHash), JsonSerializer.Serialize(new
                {
                    level,
                    sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Character Creation > Level Advancement > Character Advancement" }
                }), 0));
            await Entities.AddComponentAsync(new(StateSpaceId, subjectId,
                new(_skills.QualifiedId, _skills.Version, _skills.SchemaHash), JsonSerializer.Serialize(new
                {
                    skills,
                    sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Proficiency > Skill Proficiencies and Skills" }
                }), 0));
        }

        public async Task AddSavingThrowStateAsync(string subjectId, IReadOnlyList<string> abilities)
        {
            await Entities.AddComponentAsync(new(StateSpaceId, subjectId,
                new(_saves.QualifiedId, _saves.Version, _saves.SchemaHash), JsonSerializer.Serialize(new
                {
                    abilities,
                    sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Proficiency > Saving Throw Proficiencies" }
                }), 0));
        }

        public async Task AddCombatFixturesAsync()
        {
            await AddProficiencyStateAsync("subject.high", 5, []);
            await Entities.AddComponentAsync(new(StateSpaceId, "subject.high", new(_weaponProficiencies.QualifiedId, _weaponProficiencies.Version, _weaponProficiencies.SchemaHash), "{\"categories\":[\"simple\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Equipment > Weapons > Weapon Proficiency\"}}", 0));
            await Entities.CreateEntityAsync(StateSpaceId, "weapon.fixture", "Dagger");
            await Entities.AddComponentAsync(new(StateSpaceId, "weapon.fixture", new(_weaponProfile.QualifiedId, _weaponProfile.Version, _weaponProfile.SchemaHash), "{\"category\":\"simple\",\"kind\":\"melee\",\"attackAbilities\":[\"str\",\"dex\"],\"damage\":{\"count\":1,\"faces\":4,\"type\":\"piercing\"},\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Equipment > Weapons\"}}", 0));
            await Entities.CreateEntityAsync(StateSpaceId, "target.fixture", "Target");
            await Entities.AddComponentAsync(new(StateSpaceId, "target.fixture", new(_armorClass.QualifiedId, _armorClass.Version, _armorClass.SchemaHash), "{\"value\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > D20 Tests > Attack Rolls > Armor Class\"}}", 0));
            await Entities.AddComponentAsync(new(StateSpaceId, "target.fixture", new(_hitPoints.QualifiedId, _hitPoints.Version, _hitPoints.SchemaHash), "{\"current\":20,\"maximum\":20,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Hit Points\"}}", 0));
        }

        public async Task AddDamageTargetAsync(
            string targetId, int current, int maximum, string? mitigationJson = null)
        {
            await Entities.CreateEntityAsync(StateSpaceId, targetId, targetId);
            await Entities.AddComponentAsync(new(StateSpaceId, targetId,
                new(_hitPoints.QualifiedId, _hitPoints.Version, _hitPoints.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    current,
                    maximum,
                    sourceRef = new
                    {
                        sourceId = "source.dnd2024.srd-5.2.1",
                        locator = "Playing the Game > Damage and Healing > Hit Points"
                    }
                }), 0));
            if (mitigationJson is not null)
                await AddApplicationComponentAsync(targetId, "dnd2024.damage-mitigation", mitigationJson);
        }

        public async Task AddEncounterFixturesAsync()
        {
            await Entities.CreateEntityAsync(StateSpaceId, "encounter.fixture", "Encounter");
            foreach (var subjectId in new[] { "subject.high", "subject.low" })
            {
                await Entities.AddComponentAsync(new(StateSpaceId, subjectId,
                    new(_speed.QualifiedId, _speed.Version, _speed.SchemaHash),
                    "{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}", 0));
                await Entities.AddComponentAsync(new(StateSpaceId, subjectId,
                    new(_turnBudget.QualifiedId, _turnBudget.Version, _turnBudget.SchemaHash),
                    "{\"action\":false,\"bonusAction\":false,\"reaction\":false,\"freeInteraction\":false,\"movementRemainingFeet\":0,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn\"}}", 0));
            }
            var edges = new SqliteStateSpaceEdgeStore(_db,
                new SqliteStateSpaceRegistry(_db, new SqliteApplicationRegistry(_db)));
            await edges.MoveContainmentAsync(StateSpaceId, "subject.high", "encounter.fixture", "participant", 0);
            await edges.MoveContainmentAsync(StateSpaceId, "subject.low", "encounter.fixture", "participant", 0);
        }

        public async Task AddItemDefinitionAsync(string definitionId, string name, string definitionJson,
            string? activityJson = null)
        {
            await Entities.CreateEntityAsync(StateSpaceId, definitionId, name);
            var definition = _additionalTypes["dnd2024.item-definition"];
            await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                new(definition.QualifiedId, definition.Version, definition.SchemaHash), definitionJson, 0));
            if (activityJson is not null)
            {
                var activity = _additionalTypes["dnd2024.item-activity"];
                await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                    new(activity.QualifiedId, activity.Version, activity.SchemaHash), activityJson, 0));
            }
        }

        public async Task AddWeaponProfileAsync(string profileId, string name, string profileJson)
        {
            await Entities.CreateEntityAsync(StateSpaceId, profileId, name);
            await Entities.AddComponentAsync(new(StateSpaceId, profileId,
                new(_weaponProfile.QualifiedId, _weaponProfile.Version, _weaponProfile.SchemaHash),
                profileJson, 0));
        }

        public async Task AddApplicationComponentAsync(string entityId, string componentId,
            string valueJson)
        {
            var type = _additionalTypes[componentId];
            await Entities.AddComponentAsync(new(StateSpaceId, entityId,
                new(type.QualifiedId, type.Version, type.SchemaHash), valueJson, 0));
        }

        public async Task ReplaceApplicationComponentRawAsync(string entityId, string componentId,
            string valueJson)
        {
            var type = _additionalTypes[componentId];
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId && value.EntityId == entityId
                && value.QualifiedTypeId == type.QualifiedId);
            row.Data = valueJson;
            await _db.SaveChangesAsync();
        }

        public async Task ReplaceCoreComponentRawAsync(string entityId, string componentId,
            string valueJson)
        {
            var type = componentId switch
            {
                "dnd2024.abilities" => _abilities,
                "dnd2024.character-level" => _level,
                "dnd2024.skill-proficiencies" => _skills,
                "dnd2024.saving-throw-proficiencies" => _saves,
                "dnd2024.hit-points" => _hitPoints,
                _ => throw new ArgumentOutOfRangeException(nameof(componentId), componentId,
                    "Not a registered core component.")
            };
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId && value.EntityId == entityId
                && value.QualifiedTypeId == type.QualifiedId);
            row.Data = valueJson;
            await _db.SaveChangesAsync();
        }

        public async Task AddPhysicalItemAsync(string itemId, string name, string definitionId,
            string? containerId = null, string slot = "carried", int? quantity = null,
            string? equipmentState = null)
        {
            await Entities.CreateEntityAsync(StateSpaceId, itemId, name);
            var instance = _additionalTypes["dnd2024.item-instance"];
            await Entities.AddComponentAsync(new(StateSpaceId, itemId,
                new(instance.QualifiedId, instance.Version, instance.SchemaHash),
                JsonSerializer.Serialize(new { definitionId }), 0));
            if (quantity is not null)
            {
                var type = _additionalTypes["dnd2024.item-quantity"];
                await Entities.AddComponentAsync(new(StateSpaceId, itemId,
                    new(type.QualifiedId, type.Version, type.SchemaHash),
                    JsonSerializer.Serialize(new { count = quantity.Value, stackKey = definitionId }), 0));
            }
            if (equipmentState is not null)
            {
                var type = _additionalTypes["dnd2024.equipment-state"];
                await Entities.AddComponentAsync(new(StateSpaceId, itemId,
                    new(type.QualifiedId, type.Version, type.SchemaHash),
                    JsonSerializer.Serialize(new { state = equipmentState }), 0));
            }
            if (containerId is not null)
            {
                var edges = new SqliteStateSpaceEdgeStore(_db,
                    new SqliteStateSpaceRegistry(_db, new SqliteApplicationRegistry(_db)));
                await edges.MoveContainmentAsync(StateSpaceId, itemId, containerId, slot, 0);
            }
        }

        public async Task ReplaceSpeedRawAsync(string subjectId, string data)
        {
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId
                && value.EntityId == subjectId
                && value.QualifiedTypeId == _speed.QualifiedId);
            row.Data = data;
            await _db.SaveChangesAsync();
        }

        public async Task ReplaceTurnBudgetRawAsync(string subjectId, string data)
        {
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId
                && value.EntityId == subjectId
                && value.QualifiedTypeId == _turnBudget.QualifiedId);
            row.Data = data;
            await _db.SaveChangesAsync();
        }

        public async Task ReplaceConditionsRawAsync(string subjectId, string data)
        {
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId
                && value.EntityId == subjectId
                && value.QualifiedTypeId == _conditions.QualifiedId);
            row.Data = data;
            await _db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => DisposeAsyncCore();

        private async ValueTask DisposeAsyncCore()
        {
            await _db.DisposeAsync();
            _fixture.Dispose();
        }

        private CatalogRecordView Mechanic()
            => Record("mechanic.dnd2024.check.ability");

        private CatalogRecordView Record(string localMechanicId)
        {
            Assert.True(_catalogs.TryGet(Application, out var catalog));
            return catalog.Inspect(new(Application, Application.Value,
                Application.Value + "." + localMechanicId));
        }

        private static async Task AddSubjectAsync(
            SqliteEntityComponentStore entities,
            RegisteredComponentTypeVersion abilities,
            string id,
            string scores)
        {
            await entities.CreateEntityAsync(StateSpaceId, id, id);
            await entities.AddComponentAsync(new(StateSpaceId, id,
                new(abilities.QualifiedId, abilities.Version, abilities.SchemaHash), scores, 0));
        }

        private static async Task<ComponentDefinitionFile> DefinitionAsync(string relative)
        {
            var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "components",
                relative.Replace('/', Path.DirectorySeparatorChar) + ".json");
            var definition = ComponentDefinitionFile.Parse(await File.ReadAllTextAsync(path), relative + ".json",
                await File.ReadAllTextAsync(Path.ChangeExtension(path, ".schema.json")));
            var compilation = new BoundedJsonSchemaValidator().Compile(definition.Schema);
            Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
            return definition;
        }

        private static ApplicationActivationContext ActivationContext() => new(
            "1123456789abcdef0123456789abcdef",
            "Activate the exact D&D 2024 ability-check source in disposable test state.",
            ["procedure.system.use"],
            new AuthorizationAuditEvidence(
                "principal." + new string('a', 64), "test", "modify", "system.private-host",
                "dnd2024-ability-check", true, "PRIVATE_OPERATOR_ALLOWED"));

        private sealed class WorkspaceRoot : IAllowedSourceRootResolver
        {
            public bool TryResolve(string allowedRootId, out string canonicalPath)
            {
                canonicalPath = allowedRootId == "workspace" ? RepositoryRoot() : "";
                return canonicalPath.Length > 0;
            }
        }

        private sealed class EmptyImpact : IProjectionImpactService
        {
            public ProjectionImpactReport Analyze(
                ApplicationIdentifier applicationId,
                string? rootId = null,
                bool transitive = true) => new(
                    applicationId, new string('F', 64), null, transitive, [], [], []);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
