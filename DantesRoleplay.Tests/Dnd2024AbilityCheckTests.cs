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
    public async Task Encounter_initiative_atomically_interrupts_each_participants_active_rest()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentHitPoints: 10, currentMinute: 100);
        await harness.AddHitPointsAsync("subject.low", 10, 10);
        await harness.AddEncounterFixturesAsync();
        var highRestRoles = new Dictionary<string, string>
        {
            ["creature"] = "subject.high", ["world"] = "world.rest.fixture",
            ["policy"] = "content.dnd2024.rest-policy.standard.v1"
        };
        var lowRestRoles = new Dictionary<string, string>
        {
            ["creature"] = "subject.low", ["world"] = "world.rest.fixture",
            ["policy"] = "content.dnd2024.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", highRestRoles, "{\"kind\":\"short\"}", 0,
            "39400000000000000000000000000000"));
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", lowRestRoles, "{\"kind\":\"long\"}", 0,
            "39400000000000000000000000000001"));
        var (input, seed) = await EncounterOrderWithHighFirstAsync(harness);
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };

        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.encounter-initiative-order", encounterRoles, input, seed);
        var request = harness.ActionForRoles(
            "mechanic.dnd2024.encounter-initiative-order", encounterRoles, input, seed,
            "39400000000000000000000000000002");
        var applied = await harness.Runner.RunAsync(request);
        var replayed = await harness.Runner.RunAsync(request);

        Assert.True(evaluated.Ok, evaluated.Run?.Error);
        using (var data = JsonDocument.Parse(evaluated.Run!.Output.Data))
        {
            var interruptions = data.RootElement.GetProperty("restInterruptions");
            Assert.Equal(2, interruptions.GetArrayLength());
            Assert.Contains(interruptions.EnumerateArray(), value =>
                value.GetProperty("participantId").GetString() == "subject.high" &&
                value.GetProperty("outcome").GetString() == "short-stopped");
            Assert.Contains(interruptions.EnumerateArray(), value =>
                value.GetProperty("participantId").GetString() == "subject.low" &&
                value.GetProperty("outcome").GetString() == "long-resumed");
        }
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(4, applied.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high",
            "dnd2024.rest.world"));
        var longEpisode = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.rest-episode");
        Assert.Equal(2, longEpisode!.Revision);
        using var state = JsonDocument.Parse(longEpisode.ValueJson);
        Assert.Equal(1, state.RootElement.GetProperty("interruptionCount").GetInt32());
        Assert.Equal(540, state.RootElement.GetProperty("requiredMinutes").GetInt32());
    }

    [Fact]
    public async Task Encounter_initiative_leaves_ready_rest_unchanged_and_rejects_orphaned_active_rest()
    {
        await using var readyHarness = await DndHarness.CreateAsync();
        await readyHarness.AddRestBeginFixturesAsync(currentHitPoints: 10, currentMinute: 100);
        await readyHarness.AddEncounterFixturesAsync();
        var restRoles = RestBeginRoles();
        await readyHarness.Runner.RunAsync(readyHarness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", restRoles, "{\"kind\":\"short\"}", 0,
            "39500000000000000000000000000000"));
        await readyHarness.SetRestClockAsync(160, 8);
        await readyHarness.Runner.RunAsync(readyHarness.ActionForRoles(
            "mechanic.dnd2024.rest.progress", restRoles, "{\"activity\":\"light\"}", 0,
            "39500000000000000000000000000001"));
        var readyBefore = await readyHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var (readyInput, readySeed) = await EncounterOrderWithHighFirstAsync(readyHarness);
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var readyOrder = await readyHarness.Runner.RunAsync(readyHarness.ActionForRoles(
            "mechanic.dnd2024.encounter-initiative-order", encounterRoles, readyInput, readySeed,
            "39500000000000000000000000000002"));
        var readyAfter = await readyHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, readyOrder.Disposition);
        Assert.Equal(1, readyOrder.AppliedEffectCount);
        Assert.Equal(readyBefore!.Revision, readyAfter!.Revision);
        Assert.Equal(readyBefore.ValueJson, readyAfter.ValueJson);

        await using var corruptHarness = await DndHarness.CreateAsync();
        await corruptHarness.AddRestBeginFixturesAsync(currentHitPoints: 10, currentMinute: 100);
        await corruptHarness.AddEncounterFixturesAsync();
        await corruptHarness.Runner.RunAsync(corruptHarness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", RestBeginRoles(), "{\"kind\":\"short\"}", 0,
            "39600000000000000000000000000000"));
        var (corruptInput, corruptSeed) = await EncounterOrderWithHighFirstAsync(corruptHarness);
        Assert.True(await corruptHarness.Edges.RemoveRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high",
            "dnd2024.rest.world", 1));
        var failed = await corruptHarness.Runner.RunAsync(corruptHarness.ActionForRoles(
            "mechanic.dnd2024.encounter-initiative-order", encounterRoles,
            corruptInput, corruptSeed, "39600000000000000000000000000001"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await corruptHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.fixture", "dnd2024.encounter-initiative-order"));
        var unchanged = await corruptHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        Assert.Equal(1, unchanged!.Revision);
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

    [Fact]
    public async Task Character_creation_abilities_resolve_standard_array_and_soldier_increases_without_effects()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationAbilityFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["policy"] = "content.dnd2024.ability-assignment.standard-array.v1",
            ["background"] = "content.dnd2024.background.soldier.v1"
        };
        const string input = "{\"scores\":{\"wis\":10,\"cha\":12,\"str\":15,\"int\":8,\"con\":13,\"dex\":14},\"increases\":{\"con\":1,\"str\":2}}";
        const string canonicalOrder = "{\"increases\":{\"str\":2,\"con\":1},\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12}}";

        var first = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character-abilities.resolve", roles, input, 0);
        var reordered = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character-abilities.resolve", roles, canonicalOrder, long.MaxValue);

        Assert.True(first.Ok, first.Run?.Error ?? string.Join("; ", first.Problems));
        Assert.True(reordered.Ok, reordered.Run?.Error ?? string.Join("; ", reordered.Problems));
        Assert.Equal(first.Run!.Output.Data, reordered.Run!.Output.Data);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var root = data.RootElement;
        Assert.Equal("character-abilities-resolve", root.GetProperty("test").GetString());
        Assert.Equal("fixed-multiset", root.GetProperty("allocationFamily").GetString());
        var final = root.GetProperty("finalScores");
        Assert.Equal(17, final.GetProperty("str").GetInt32());
        Assert.Equal(14, final.GetProperty("dex").GetInt32());
        Assert.Equal(14, final.GetProperty("con").GetInt32());
        Assert.Equal(8, final.GetProperty("int").GetInt32());
        Assert.Equal(10, final.GetProperty("wis").GetInt32());
        Assert.Equal(12, final.GetProperty("cha").GetInt32());
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        var request = harness.ActionForRoles(
            "mechanic.dnd2024.character-abilities.resolve", roles, input, 0,
            "8123456789abcdef0123456789abcdee");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
    }

    [Fact]
    public async Task Character_creation_abilities_support_the_three_plus_one_background_pattern()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationAbilityFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character-abilities.resolve",
            new Dictionary<string, string>
            {
                ["policy"] = "content.dnd2024.ability-assignment.standard-array.v1",
                ["background"] = "content.dnd2024.background.soldier.v1"
            },
            "{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":1,\"dex\":1,\"con\":1}}",
            17);

        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        var final = data.RootElement.GetProperty("finalScores");
        Assert.Equal(16, final.GetProperty("str").GetInt32());
        Assert.Equal(15, final.GetProperty("dex").GetInt32());
        Assert.Equal(14, final.GetProperty("con").GetInt32());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_abilities_support_declared_point_cost_and_enforce_the_score_cap()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationAbilityFixturesAsync();
        const string pointPolicy = "content.test.ability-assignment.point-cost.v1";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, pointPolicy, "Point Cost");
        await harness.AddApplicationComponentAsync(pointPolicy,
            "dnd2024.character.ability-assignment-policy",
            "{\"policyVersion\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Creation > Step 3: Ability Scores > Generate Your Scores > Point Cost, PDF p. 21\"},\"scoreBounds\":{\"minimum\":8,\"maximum\":15},\"allocation\":{\"family\":\"point-budget\",\"budget\":27,\"costs\":[{\"score\":8,\"cost\":0},{\"score\":9,\"cost\":1},{\"score\":10,\"cost\":2},{\"score\":11,\"cost\":3},{\"score\":12,\"cost\":4},{\"score\":13,\"cost\":5},{\"score\":14,\"cost\":7},{\"score\":15,\"cost\":9}]}}");
        var roles = new Dictionary<string, string>
        {
            ["policy"] = pointPolicy,
            ["background"] = "content.dnd2024.background.soldier.v1"
        };
        var pointCost = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character-abilities.resolve", roles,
            "{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}}",
            0);
        Assert.True(pointCost.Ok, pointCost.Run?.Error);
        Assert.Contains("\"allocationFamily\":\"point-budget\"", pointCost.Run!.Output.Data,
            StringComparison.Ordinal);

        const string capPolicy = "content.test.ability-assignment.cap.v1";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, capPolicy, "Cap fixture");
        await harness.AddApplicationComponentAsync(capPolicy,
            "dnd2024.character.ability-assignment-policy",
            "{\"policyVersion\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Creation > Step 3: Ability Scores, PDF p. 21\"},\"scoreBounds\":{\"minimum\":1,\"maximum\":20},\"allocation\":{\"family\":\"fixed-multiset\",\"values\":[8,10,12,13,14,20]}}");
        roles["policy"] = capPolicy;
        var overCap = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character-abilities.resolve", roles,
            "{\"scores\":{\"str\":20,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}}",
            0);
        Assert.False(overCap.Ok);
        Assert.Contains("above 20", overCap.Run?.Error, StringComparison.Ordinal);
        Assert.Empty(overCap.Run!.Output.Effects);
    }

    [Theory]
    [InlineData("{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":11},\"increases\":{\"str\":2,\"con\":1}}", "fixed multiset")]
    [InlineData("{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12,\"modifier\":2},\"increases\":{\"str\":2,\"con\":1}}", "exactly str")]
    [InlineData("{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"wis\":2,\"con\":1}}", "eligible ability")]
    [InlineData("{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"dex\":2}}", "source-declared")]
    [InlineData("{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":3,\"con\":1}}", "positive integer")]
    [InlineData("{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1},\"finalScores\":{}}", "exactly scores and increases")]
    public async Task Character_creation_abilities_reject_invalid_or_derived_input_without_state_change(
        string input, string error)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationAbilityFixturesAsync();
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.abilities");
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character-abilities.resolve",
            new Dictionary<string, string>
            {
                ["policy"] = "content.dnd2024.ability-assignment.standard-array.v1",
                ["background"] = "content.dnd2024.background.soldier.v1"
            }, input, 0);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.abilities");

        Assert.False(result.Ok);
        Assert.Contains(error, result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
        Assert.Equal(before!.ValueJson, after!.ValueJson);
    }

    [Fact]
    public async Task Character_creation_abilities_fail_closed_on_background_source_drift()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationAbilityFixturesAsync();
        await harness.ReplaceApplicationComponentRawAsync(
            "content.dnd2024.background.soldier.v1",
            "dnd2024.background.ability-increase-options",
            "{\"contentKey\":\"soldier\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Origins > wrong\"},\"eligibleAbilities\":[\"str\",\"dex\",\"con\"],\"allowedPatterns\":[\"plus-2-plus-1\",\"plus-1-each\"]}");

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character-abilities.resolve",
            new Dictionary<string, string>
            {
                ["policy"] = "content.dnd2024.ability-assignment.standard-array.v1",
                ["background"] = "content.dnd2024.background.soldier.v1"
            },
            "{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}}",
            0);

        Assert.False(result.Ok);
        Assert.Contains("do not match", result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_catalog_activates_all_nine_source_profiles()
    {
        var expected = new Dictionary<string, (string Sizes, int Speed, string Traits, string Choices, int Page)>(StringComparer.Ordinal)
        {
            ["dragonborn"] = ("medium", 30, "draconic-ancestry,breath-weapon,damage-resistance,darkvision,draconic-flight", "draconic-ancestry", 84),
            ["dwarf"] = ("medium", 30, "darkvision,dwarven-resilience,dwarven-toughness,stonecunning", "", 84),
            ["elf"] = ("medium", 30, "darkvision,elven-lineage,fey-ancestry,keen-senses,trance", "elven-lineage", 84),
            ["gnome"] = ("small", 30, "darkvision,gnomish-cunning,gnomish-lineage", "gnomish-lineage", 85),
            ["goliath"] = ("medium", 35, "giant-ancestry,large-form,powerful-build", "giant-ancestry", 85),
            ["halfling"] = ("small", 30, "brave,halfling-nimbleness,luck,naturally-stealthy", "", 86),
            ["human"] = ("small,medium", 30, "resourceful,skillful,versatile", "", 86),
            ["orc"] = ("medium", 30, "adrenaline-rush,darkvision,relentless-endurance,powerful-build", "", 86),
            ["tiefling"] = ("small,medium", 30, "darkvision,fiendish-legacy,otherworldly-presence", "fiendish-legacy", 86)
        };
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "character-creation", "species");
        var paths = Directory.GetFiles(directory, "content.dnd2024.species.*.v1.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(9, paths.Length);

        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "data", "dnd2024.species-profile.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var identityComponent = Assert.Single(entity.Components, value =>
                value.DefinitionId == "dnd2024.character.content-definition");
            var profileComponent = Assert.Single(entity.Components, value =>
                value.DefinitionId == "dnd2024.species-profile");
            var validation = validator.Validate(compilation.ProfileId,
                compilation.NormalizedSchema, profileComponent.Data);
            Assert.Equal(SchemaValueStatus.Valid, validation.Status);

            using var identityJson = JsonDocument.Parse(identityComponent.Data);
            using var profileJson = JsonDocument.Parse(profileComponent.Data);
            var identity = identityJson.RootElement;
            var profile = profileJson.RootElement;
            var key = identity.GetProperty("contentKey").GetString()!;
            var item = expected[key];
            Assert.Equal("species", identity.GetProperty("kind").GetString());
            Assert.Equal("active", identity.GetProperty("status").GetString());
            Assert.Equal(key, profile.GetProperty("contentKey").GetString());
            Assert.Equal("humanoid", profile.GetProperty("creatureType").GetString());
            Assert.Equal(item.Sizes, string.Join(',', profile.GetProperty("allowedSizes")
                .EnumerateArray().Select(value => value.GetString())));
            Assert.Equal(item.Speed, profile.GetProperty("baseSpeed").GetProperty("walkFeet").GetInt32());
            Assert.Equal(item.Traits, string.Join(',', profile.GetProperty("traitKeys")
                .EnumerateArray().Select(value => value.GetString())));
            Assert.Equal(item.Choices, string.Join(',', profile.GetProperty("choiceFamilies")
                .EnumerateArray().Select(value => value.GetString())));
            Assert.EndsWith($", PDF page {item.Page}",
                profile.GetProperty("sourceRef").GetProperty("locator").GetString(),
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    public async Task Character_creation_human_species_resolves_size_speed_and_explicit_trait_blockers(
        string size)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["species"] = "content.dnd2024.species.human.v1"
        };
        var input = "{\"size\":\"" + size + "\"}";
        var first = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-selection.resolve", roles, input, 0);
        var second = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-selection.resolve", roles, input, long.MaxValue);

        Assert.True(first.Ok, first.Run?.Error ?? string.Join("; ", first.Problems));
        Assert.True(second.Ok, second.Run?.Error ?? string.Join("; ", second.Problems));
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var root = data.RootElement;
        Assert.Equal("species-selection-resolve", root.GetProperty("test").GetString());
        Assert.Equal("content.dnd2024.species.human.v1",
            root.GetProperty("selectedSpecies").GetProperty("speciesDefinitionId").GetString());
        Assert.Equal(size, root.GetProperty("size").GetProperty("size").GetString());
        Assert.Equal(30, root.GetProperty("speed").GetProperty("walkFeet").GetInt32());
        Assert.Equal("Rules Glossary > Speed", root.GetProperty("speed")
            .GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Equal(new[] { "resourceful", "skillful", "versatile" },
            root.GetProperty("unresolvedTraitKeys").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
        Assert.Empty(root.GetProperty("grantedTraitKeys").EnumerateArray());
        Assert.Equal("blocked-unimplemented-traits", root.GetProperty("grantReadiness").GetString());
        Assert.False(root.GetProperty("readyForAtomicCreation").GetBoolean());
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        var request = harness.ActionForRoles("mechanic.dnd2024.species-selection.resolve", roles,
            input, 0, "9123456789abcdef0123456789abcdee");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
    }

    [Fact]
    public async Task Character_creation_fixed_species_derives_size_and_content_bound_speed()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var dragonborn = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-selection.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.dragonborn.v1"
            }, "{}", 0);
        var goliath = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-selection.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.goliath.v1"
            }, "{}", 0);

        Assert.True(dragonborn.Ok, dragonborn.Run?.Error);
        Assert.True(goliath.Ok, goliath.Run?.Error);
        using var dragonbornData = JsonDocument.Parse(dragonborn.Run!.Output.Data);
        using var goliathData = JsonDocument.Parse(goliath.Run!.Output.Data);
        Assert.Equal("medium", dragonbornData.RootElement.GetProperty("size")
            .GetProperty("size").GetString());
        Assert.Equal(30, dragonbornData.RootElement.GetProperty("speed")
            .GetProperty("walkFeet").GetInt32());
        Assert.Equal(35, goliathData.RootElement.GetProperty("speed")
            .GetProperty("walkFeet").GetInt32());
        Assert.Equal("giant-ancestry", goliathData.RootElement.GetProperty("choiceFamilies")[0]
            .GetString());
        Assert.Empty(dragonborn.Run.Output.Effects);
        Assert.Empty(goliath.Run.Output.Effects);
    }

    [Theory]
    [InlineData("content.dnd2024.species.human.v1", "{}", "requires exactly one allowed Size")]
    [InlineData("content.dnd2024.species.human.v1", "{\"size\":\"large\"}", "requires exactly one allowed Size")]
    [InlineData("content.dnd2024.species.human.v1", "{\"size\":\"small\",\"speed\":30}", "requires exactly one allowed Size")]
    [InlineData("content.dnd2024.species.dragonborn.v1", "{\"size\":\"medium\"}", "takes no Size input")]
    public async Task Character_creation_species_rejects_nonclosed_or_derived_size_input(
        string speciesId, string input, string error)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-selection.resolve",
            new Dictionary<string, string> { ["species"] = speciesId }, input, 0);

        Assert.False(result.Ok);
        Assert.Contains(error, result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_fails_closed_on_profile_source_drift()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.ReplaceApplicationComponentRawAsync(
            "content.dnd2024.species.human.v1", "dnd2024.species-profile",
            "{\"contentKey\":\"human\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Origins > Character Species > Dwarf, PDF page 84\"},\"creatureType\":\"humanoid\",\"allowedSizes\":[\"small\",\"medium\"],\"baseSpeed\":{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0},\"traitKeys\":[\"resourceful\",\"skillful\",\"versatile\"],\"choiceFamilies\":[]}");

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-selection.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.human.v1"
            }, "{\"size\":\"small\"}", 0);

        Assert.False(result.Ok);
        Assert.Contains("does not match", result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_rejects_a_noncanonical_definition_binding()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string fakeId = "content.test.species.human.v1";
        var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "content",
            "entities", "character-creation", "species", "content.dnd2024.species.human.v1.json");
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), path);
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, fakeId, "Copied Human");
        foreach (var component in entity.Components)
            await harness.AddApplicationComponentAsync(fakeId, component.DefinitionId, component.Data);

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-selection.resolve",
            new Dictionary<string, string> { ["species"] = fakeId },
            "{\"size\":\"small\"}", 0);

        Assert.False(result.Ok);
        Assert.Contains("not canonical", result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Theory]
    [InlineData("acrobatics")]
    [InlineData("animal-handling")]
    [InlineData("arcana")]
    [InlineData("athletics")]
    [InlineData("deception")]
    [InlineData("history")]
    [InlineData("insight")]
    [InlineData("intimidation")]
    [InlineData("investigation")]
    [InlineData("medicine")]
    [InlineData("nature")]
    [InlineData("perception")]
    [InlineData("performance")]
    [InlineData("persuasion")]
    [InlineData("religion")]
    [InlineData("sleight-of-hand")]
    [InlineData("stealth")]
    [InlineData("survival")]
    public async Task Character_creation_species_skillful_accepts_each_canonical_skill(string skill)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.human.v1"
            }, "{\"skill\":\"" + skill + "\"}", 0);

        Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems));
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        var root = data.RootElement;
        Assert.Equal(skill, root.GetProperty("selectedSkill").GetString());
        var target = root.GetProperty("target");
        Assert.Equal("dnd2024.skill-proficiencies", target.GetProperty("definitionId").GetString());
        Assert.Equal("skills", target.GetProperty("field").GetString());
        Assert.Equal("set-union", target.GetProperty("mergePolicy").GetString());
        Assert.Equal(skill, target.GetProperty("values")[0].GetString());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_skillful_is_deterministic_and_replay_safe()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["species"] = "content.dnd2024.species.human.v1"
        };
        const string input = "{\"skill\":\"perception\"}";
        var first = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-skillful.resolve", roles, input, 0);
        var second = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-skillful.resolve", roles, input, long.MaxValue);

        Assert.True(first.Ok, first.Run?.Error);
        Assert.True(second.Ok, second.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        var request = harness.ActionForRoles("mechanic.dnd2024.species-skillful.resolve", roles,
            input, 0, "a123456789abcdef0123456789abcdee");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
    }

    [Fact]
    public async Task Character_creation_species_skillful_requires_a_declared_entitlement()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.dragonborn.v1"
            }, "{\"skill\":\"perception\"}", 0);

        Assert.False(result.Ok);
        Assert.Contains("Skillful entitlement", result.Run?.Error, StringComparison.Ordinal);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"skill\":\"survival\",\"modifier\":2}")]
    [InlineData("{\"skill\":\"animal handling\"}")]
    [InlineData("{\"skill\":2}")]
    public async Task Character_creation_species_skillful_rejects_invalid_or_derived_input(string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.human.v1"
            }, input, 0);

        Assert.False(result.Ok);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_skillful_fails_closed_on_profile_source_drift()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.ReplaceApplicationComponentRawAsync(
            "content.dnd2024.species.human.v1", "dnd2024.species-profile",
            "{\"contentKey\":\"human\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Origins > Character Species > Dwarf, PDF page 84\"},\"creatureType\":\"humanoid\",\"allowedSizes\":[\"small\",\"medium\"],\"baseSpeed\":{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0},\"traitKeys\":[\"resourceful\",\"skillful\",\"versatile\"],\"choiceFamilies\":[]}");

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.human.v1"
            }, "{\"skill\":\"perception\"}", 0);

        Assert.False(result.Ok);
        Assert.Contains("does not match", result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_versatile_activates_all_origin_feat_profiles()
    {
        var expected = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["alert"] = false,
            ["magic-initiate"] = true,
            ["savage-attacker"] = false,
            ["skilled"] = true
        };
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "character-creation", "feats");
        var paths = Directory.GetFiles(directory, "content.dnd2024.feature.*.v1.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(4, paths.Length);
        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "data", "dnd2024.feat-profile.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var identityComponent = Assert.Single(entity.Components, value =>
                value.DefinitionId == "dnd2024.character.content-definition");
            var profileComponent = Assert.Single(entity.Components, value =>
                value.DefinitionId == "dnd2024.feat-profile");
            var validation = validator.Validate(compilation.ProfileId,
                compilation.NormalizedSchema, profileComponent.Data);
            Assert.Equal(SchemaValueStatus.Valid, validation.Status);
            using var identityJson = JsonDocument.Parse(identityComponent.Data);
            using var profileJson = JsonDocument.Parse(profileComponent.Data);
            var identity = identityJson.RootElement;
            var profile = profileJson.RootElement;
            var key = identity.GetProperty("contentKey").GetString()!;
            Assert.Equal("feature", identity.GetProperty("kind").GetString());
            Assert.Equal("active", identity.GetProperty("status").GetString());
            Assert.Equal(key, profile.GetProperty("contentKey").GetString());
            Assert.Equal("origin", profile.GetProperty("category").GetString());
            Assert.Equal(expected[key], profile.GetProperty("repeatable").GetBoolean());
            Assert.Equal($"Feats > Origin Feats > {entity.Name.Split(" (", StringSplitOptions.None)[0]}, PDF page 87",
                profile.GetProperty("sourceRef").GetProperty("locator").GetString());
        }
    }

    [Fact]
    public async Task Character_creation_species_versatile_resolves_skilled_mixed_choices_canonically()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["species"] = "content.dnd2024.species.human.v1",
            ["feat"] = "content.dnd2024.feature.skilled.v1"
        };
        const string input = "{\"choices\":[{\"kind\":\"tool\",\"id\":\"thieves-tools\"},{\"kind\":\"skill\",\"id\":\"stealth\"},{\"kind\":\"skill\",\"id\":\"perception\"}]}";
        const string reordered = "{\"choices\":[{\"id\":\"perception\",\"kind\":\"skill\"},{\"id\":\"thieves-tools\",\"kind\":\"tool\"},{\"id\":\"stealth\",\"kind\":\"skill\"}]}";
        var first = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-versatile-skilled.resolve", roles, input, 0);
        var second = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-versatile-skilled.resolve", roles, reordered, long.MaxValue);

        Assert.True(first.Ok, first.Run?.Error ?? string.Join("; ", first.Problems));
        Assert.True(second.Ok, second.Run?.Error ?? string.Join("; ", second.Problems));
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var root = data.RootElement;
        Assert.Equal("content.dnd2024.feature.skilled.v1",
            root.GetProperty("selectedFeat").GetProperty("featDefinitionId").GetString());
        Assert.True(root.GetProperty("selectedFeat").GetProperty("repeatable").GetBoolean());
        Assert.Equal(new[] { "perception", "stealth" }, root.GetProperty("skillContribution")
            .GetProperty("values").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(new[] { "thieves-tools" }, root.GetProperty("toolContribution")
            .GetProperty("values").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("set-union", root.GetProperty("skillContribution")
            .GetProperty("mergePolicy").GetString());
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        var request = harness.ActionForRoles(
            "mechanic.dnd2024.species-versatile-skilled.resolve", roles, input, 0,
            "b123456789abcdef0123456789abcdee");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
    }

    [Theory]
    [InlineData("{\"choices\":[{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"skill\",\"id\":\"history\"},{\"kind\":\"skill\",\"id\":\"nature\"}]}", 3, 0)]
    [InlineData("{\"choices\":[{\"kind\":\"tool\",\"id\":\"dice-set\"},{\"kind\":\"tool\",\"id\":\"lute\"},{\"kind\":\"tool\",\"id\":\"smiths-tools\"}]}", 0, 3)]
    public async Task Character_creation_species_versatile_supports_all_skill_or_all_tool_skilled_choices(
        string input, int skills, int tools)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-versatile-skilled.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.human.v1",
                ["feat"] = "content.dnd2024.feature.skilled.v1"
            }, input, 0);

        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        Assert.Equal(skills, data.RootElement.GetProperty("skillContribution")
            .GetProperty("values").GetArrayLength());
        Assert.Equal(tools, data.RootElement.GetProperty("toolContribution")
            .GetProperty("values").GetArrayLength());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Theory]
    [InlineData("content.dnd2024.species.dragonborn.v1", "content.dnd2024.feature.skilled.v1", "Versatile entitlement")]
    [InlineData("content.dnd2024.species.human.v1", "content.dnd2024.feature.alert.v1", "requires the Skilled")]
    public async Task Character_creation_species_versatile_requires_entitlement_and_skilled_behavior(
        string speciesId, string featId, string error)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-versatile-skilled.resolve",
            new Dictionary<string, string> { ["species"] = speciesId, ["feat"] = featId },
            "{\"choices\":[{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"skill\",\"id\":\"history\"},{\"kind\":\"skill\",\"id\":\"nature\"}]}", 0);

        Assert.False(result.Ok);
        Assert.Contains(error, result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"tool\",\"id\":\"lute\"}]}")]
    [InlineData("{\"choices\":[{\"kind\":\"language\",\"id\":\"common\"},{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"tool\",\"id\":\"lute\"}]}")]
    [InlineData("{\"choices\":[{\"kind\":\"skill\",\"id\":\"animal handling\"},{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"tool\",\"id\":\"lute\"}]}")]
    [InlineData("{\"choices\":[{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"tool\",\"id\":\"unknown\"},{\"kind\":\"tool\",\"id\":\"lute\"}],\"featId\":\"content.fake\"}")]
    public async Task Character_creation_species_versatile_rejects_invalid_or_derived_choices(string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-versatile-skilled.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.human.v1",
                ["feat"] = "content.dnd2024.feature.skilled.v1"
            }, input, 0);

        Assert.False(result.Ok);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_versatile_fails_closed_on_feat_source_drift()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        await harness.ReplaceApplicationComponentRawAsync(
            "content.dnd2024.feature.skilled.v1", "dnd2024.feat-profile",
            "{\"contentKey\":\"skilled\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Feats > Origin Feats > Alert, PDF page 87\"},\"category\":\"origin\",\"repeatable\":true}");
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.species-versatile-skilled.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "content.dnd2024.species.human.v1",
                ["feat"] = "content.dnd2024.feature.skilled.v1"
            }, "{\"choices\":[{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"skill\",\"id\":\"history\"},{\"kind\":\"tool\",\"id\":\"lute\"}]}", 0);

        Assert.False(result.Ok);
        Assert.Contains("does not match", result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_heroic_inspiration_grants_once_and_is_replay_safe()
    {
        await using var harness = await DndHarness.CreateAsync();
        var roles = new Dictionary<string, string> { ["subject"] = "subject.high" };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.character-profile.record",
                new Dictionary<string, string> { ["actor"] = "subject.high" },
                "{\"mode\":\"record\",\"biography\":\"A steadfast adventurer.\"}", 0,
                "c123456789abcdef0123456789abcdee"))).Disposition);

        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.heroic-inspiration.grant", roles, "{}", long.MaxValue);
        var request = harness.ActionForRoles(
            "mechanic.dnd2024.heroic-inspiration.grant", roles, "{}", 0,
            "d123456789abcdef0123456789abcdee");
        var granted = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var beforeDuplicate = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.heroic-inspiration");
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.heroic-inspiration.grant", roles, "{}", 0,
            "e123456789abcdef0123456789abcdee"));
        var afterDuplicate = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.heroic-inspiration");

        Assert.True(evaluated.Ok, evaluated.Run?.Error);
        Assert.Single(evaluated.Run!.Output.Effects);
        Assert.Empty(evaluated.Run.Output.Events);
        Assert.Empty(evaluated.Run.Output.Notifications);
        Assert.Contains("\"heldBefore\":false", evaluated.Run.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"heldAfter\":true", evaluated.Run.Output.Data, StringComparison.Ordinal);
        Assert.Contains("Rules Glossary > Heroic Inspiration PDF page 183",
            evaluated.Run.Output.Data, StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, granted.Disposition);
        Assert.Equal(1, granted.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal("{}", beforeDuplicate!.ValueJson);
        Assert.Equal(1, beforeDuplicate.Revision);
        Assert.Equal(beforeDuplicate.ValueJson, afterDuplicate!.ValueJson);
        Assert.Equal(beforeDuplicate.Revision, afterDuplicate.Revision);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("{\"restCompleted\":true}")]
    [InlineData("{\"speciesId\":\"content.dnd2024.species.human.v1\"}")]
    public async Task Character_creation_heroic_inspiration_rejects_nonempty_or_nonobject_input(
        string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddApplicationComponentAsync(
            "subject.high", "dnd2024.character.profile", "{\"pronouns\":\"they/them\"}");

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.heroic-inspiration.grant",
            new Dictionary<string, string> { ["subject"] = "subject.high" }, input, 0);

        Assert.False(result.Ok);
        if (result.Run is not null)
            Assert.Empty(result.Run.Output.Effects);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.heroic-inspiration"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("primitive")]
    [InlineData("unknown-field")]
    [InlineData("untrimmed")]
    public async Task Character_creation_heroic_inspiration_requires_a_valid_nonempty_profile(
        string profileCase)
    {
        await using var harness = await DndHarness.CreateAsync();
        if (profileCase != "missing")
        {
            await harness.AddApplicationComponentAsync("subject.high", "dnd2024.character.profile",
                profileCase == "empty" ? "{}" : "{\"biography\":\"Valid before corruption.\"}");
            if (profileCase == "primitive")
                await harness.ReplaceApplicationComponentRawAsync(
                    "subject.high", "dnd2024.character.profile", "42");
            if (profileCase == "unknown-field")
                await harness.ReplaceApplicationComponentRawAsync(
                    "subject.high", "dnd2024.character.profile", "{\"player\":\"yes\"}");
            if (profileCase == "untrimmed")
                await harness.ReplaceApplicationComponentRawAsync(
                    "subject.high", "dnd2024.character.profile", "{\"biography\":\" invalid\"}");
        }

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.heroic-inspiration.grant",
            new Dictionary<string, string> { ["subject"] = "subject.high" }, "{}", 0);

        Assert.False(result.Ok);
        Assert.Empty(result.Run!.Output.Effects);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.heroic-inspiration"));
    }

    [Fact]
    public async Task Character_creation_heroic_inspiration_refuses_corrupt_held_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddApplicationComponentAsync(
            "subject.high", "dnd2024.character.profile", "{\"appearance\":\"A silver cloak.\"}");
        await harness.AddApplicationComponentAsync(
            "subject.high", "dnd2024.heroic-inspiration", "{}");
        await harness.ReplaceApplicationComponentRawAsync(
            "subject.high", "dnd2024.heroic-inspiration", "{\"available\":true}");

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.heroic-inspiration.grant",
            new Dictionary<string, string> { ["subject"] = "subject.high" }, "{}", 0);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.heroic-inspiration");

        Assert.False(result.Ok);
        Assert.Contains("state is invalid", result.Run?.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Run!.Output.Effects);
        Assert.Equal("{\"available\":true}", after!.ValueJson);
        Assert.Equal(1, after.Revision);
    }

    [Fact]
    public async Task Character_creation_rest_policy_is_exact_immutable_srd_content()
    {
        var root = RepositoryRoot();
        var relative =
            "catalog/applications/dnd2024/content/entities/character-creation/rest/content.dnd2024.rest-policy.standard.v1.json";
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var schemaPath = Path.Combine(root, "catalog", "applications", "dnd2024", "components",
            "data", "dnd2024.rest-policy.schema.json");
        var schema = await File.ReadAllTextAsync(schemaPath);
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        Assert.Contains(relative, harness.ActiveSourcePaths);
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
        Assert.Equal("content.dnd2024.rest-policy.standard.v1", entity.Id);
        var component = Assert.Single(entity.Components);
        Assert.Equal("dnd2024.rest-policy", component.DefinitionId);
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(
            compilation.ProfileId, compilation.NormalizedSchema, component.Data).Status);
        using var document = JsonDocument.Parse(component.Data);
        var policy = document.RootElement;
        Assert.Equal("standard", policy.GetProperty("policyKey").GetString());
        Assert.Equal(1, policy.GetProperty("policyVersion").GetInt32());
        Assert.Equal("Rules Glossary > Long Rest and Short Rest, PDF pages 185 and 187",
            policy.GetProperty("sourceRef").GetProperty("locator").GetString());
        var shortRest = policy.GetProperty("shortRest");
        Assert.Equal(60, shortRest.GetProperty("minimumMinutes").GetInt32());
        Assert.Equal(new[] { "initiative", "non-cantrip-spell", "damage" },
            shortRest.GetProperty("interruptions").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
        Assert.Equal(new[] { "spend-hit-point-dice", "source-specific-recharge" },
            shortRest.GetProperty("benefits").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
        var longRest = policy.GetProperty("longRest");
        Assert.Equal(480, longRest.GetProperty("minimumMinutes").GetInt32());
        Assert.Equal(360, longRest.GetProperty("minimumSleepMinutes").GetInt32());
        Assert.Equal(120, longRest.GetProperty("maximumLightActivityMinutes").GetInt32());
        Assert.Equal(960, longRest.GetProperty("restartWaitMinutes").GetInt32());
        Assert.Equal(60, longRest.GetProperty("partialShortRestMinutes").GetInt32());
        Assert.Equal(60, longRest.GetProperty("additionalMinutesPerInterruption").GetInt32());
        Assert.Equal(new[]
        {
            "initiative", "non-cantrip-spell", "damage", "walking-or-physical-exertion"
        }, longRest.GetProperty("interruptions").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
        Assert.Equal(new[]
        {
            "restore-hit-points", "restore-hit-point-dice", "restore-hit-point-maximum",
            "restore-ability-scores", "reduce-exhaustion", "source-specific-recharge"
        }, longRest.GetProperty("benefits").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
        Assert.DoesNotContain(longRest.GetProperty("benefits").EnumerateArray(),
            value => value.GetString() == "expire-temporary-hit-points");

        var changed = component.Data.Replace("\"minimumMinutes\":480", "\"minimumMinutes\":479",
            StringComparison.Ordinal);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(
            compilation.ProfileId, compilation.NormalizedSchema, changed).Status);
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, entity.Id, entity.Name);
        await harness.AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, entity.Id, "dnd2024.rest-policy");
        Assert.Equal(component.Data, stored!.ValueJson);
        Assert.Equal(1, stored.Revision);
    }

    [Theory]
    [InlineData("short", 60, "Rules Glossary > Short Rest, PDF page 187",
        "f123456789abcdef0123456789abcdee")]
    [InlineData("long", 480, "Rules Glossary > Long Rest, PDF page 185",
        "0123456789abcdef0123456789abcdf0")]
    public async Task Character_creation_rest_begin_uses_base_world_clock_and_commits_atomically(
        string kind, int requiredMinutes, string locator, string operationId)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentHitPoints: 1, currentMinute: 321);
        var roles = new Dictionary<string, string>
        {
            ["creature"] = "subject.high",
            ["world"] = "world.rest.fixture",
            ["policy"] = "content.dnd2024.rest-policy.standard.v1"
        };
        var input = "{\"kind\":\"" + kind + "\"}";
        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.begin", roles, input, long.MaxValue);
        var request = harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, input, 0, operationId);
        var started = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var beforeDuplicate = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, input, 0,
            kind == "short"
                ? "1123456789abcdef0123456789abcdf0"
                : "2123456789abcdef0123456789abcdf0"));
        var afterDuplicate = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var membership = await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high", "dnd2024.rest.world");

        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
        Assert.Equal(2, evaluated.Run!.Output.Effects.Count);
        Assert.Empty(evaluated.Run.Output.Events);
        Assert.Empty(evaluated.Run.Output.Notifications);
        using var result = JsonDocument.Parse(evaluated.Run.Output.Data);
        Assert.Equal(321, result.RootElement.GetProperty("startedAtMinute").GetInt32());
        Assert.Equal(requiredMinutes, result.RootElement.GetProperty("requiredMinutes").GetInt32());
        Assert.Equal(locator, result.RootElement.GetProperty("sourceRef")
            .GetProperty("locator").GetString());
        Assert.True(started.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            started.Disposition + ": " + string.Join("; ", started.Problems.Select(value =>
                value.Code + " " + value.SafeMessage)));
        Assert.Equal(2, started.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        using var episode = JsonDocument.Parse(beforeDuplicate!.ValueJson);
        Assert.Equal(kind, episode.RootElement.GetProperty("kind").GetString());
        Assert.Equal("active", episode.RootElement.GetProperty("status").GetString());
        Assert.Equal(321, episode.RootElement.GetProperty("startedAtMinute").GetInt32());
        Assert.Equal(321, episode.RootElement.GetProperty("observedAtMinute").GetInt32());
        Assert.Equal(7, episode.RootElement.GetProperty("observedClockRevision").GetInt32());
        Assert.Equal(requiredMinutes, episode.RootElement.GetProperty("requiredMinutes").GetInt32());
        Assert.Equal(0, episode.RootElement.GetProperty("lightActivityMinutes").GetInt32());
        if (kind == "long")
        {
            Assert.Equal(0, episode.RootElement.GetProperty("sleepMinutes").GetInt32());
            Assert.Equal(0, episode.RootElement.GetProperty("interruptionCount").GetInt32());
        }
        Assert.Equal(1, beforeDuplicate.Revision);
        Assert.Equal(beforeDuplicate.ValueJson, afterDuplicate!.ValueJson);
        Assert.Equal(beforeDuplicate.Revision, afterDuplicate.Revision);
        Assert.NotNull(membership);
        Assert.Equal("{}", membership.DataJson);
        Assert.Equal(1, membership.Revision);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"nap\"}")]
    [InlineData("{\"kind\":\"long\",\"startedAtMinute\":0}")]
    [InlineData("{\"kind\":\"short\",\"currentHitPoints\":1}")]
    [InlineData("{\"kind\":\"long\",\"status\":\"ready\"}")]
    public async Task Character_creation_rest_begin_rejects_caller_derived_or_invalid_input(
        string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.begin", RestBeginRoles(), input, 0);

        Assert.False(result.Ok);
        if (result.Run is not null)
            Assert.Empty(result.Run.Output.Effects);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high", "dnd2024.rest.world"));
    }

    [Theory]
    [InlineData("zero-hp")]
    [InlineData("inactive-world")]
    [InlineData("corrupt-clock")]
    [InlineData("wrong-policy")]
    public async Task Character_creation_rest_begin_fails_closed_on_ineligible_or_drifted_state(
        string stateCase)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentHitPoints: stateCase == "zero-hp" ? 0 : 1);
        if (stateCase == "inactive-world")
            await harness.ReplaceApplicationComponentRawAsync("world.rest.fixture",
                "game.core.world.root",
                "{\"status\":\"draft\",\"summary\":\"A quiet test world.\",\"visibility\":\"party\"}");
        if (stateCase == "corrupt-clock")
            await harness.ReplaceApplicationComponentRawAsync("world.rest.fixture",
                "game.core.world.clock",
                "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":123,\"revision\":-1}");
        if (stateCase == "wrong-policy")
            await harness.ReplaceApplicationComponentRawAsync(
                "content.dnd2024.rest-policy.standard.v1", "dnd2024.rest-policy",
                (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
                    "content.dnd2024.rest-policy.standard.v1", "dnd2024.rest-policy"))!.ValueJson
                    .Replace("\"policyVersion\":1", "\"policyVersion\":2", StringComparison.Ordinal));

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.begin", RestBeginRoles(), "{\"kind\":\"long\"}", 0);

        Assert.False(result.Ok);
        Assert.Empty(result.Run!.Output.Effects);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
    }

    [Fact]
    public async Task Character_creation_rest_begin_requires_explicit_game_base_component_mapping()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync();

        var result = await harness.EvaluateRolesWithoutGameBaseMappingAsync(
            "mechanic.dnd2024.rest.begin", RestBeginRoles(), "{\"kind\":\"short\"}", 0);

        Assert.False(result.Ok);
        Assert.Contains(result.Problems,
            problem => problem.Contains("COMPONENT_MAPPING_MISSING", StringComparison.Ordinal));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
    }

    [Fact]
    public async Task Character_creation_rest_progress_marks_short_rest_ready_at_exact_hour_without_benefit()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"short\"}", 0,
            "31000000000000000000000000000001"));
        await harness.SetRestClockAsync(159, 8);
        var firstRequest = harness.ActionForRoles(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"light\"}", 0,
            "31000000000000000000000000000002");
        var first = await harness.Runner.RunAsync(firstRequest);
        var replay = await harness.Runner.RunAsync(firstRequest);
        var active = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        await harness.SetRestClockAsync(160, 9);
        var finalEvaluation = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"light\"}", 0);
        var final = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"light\"}", 0,
            "31000000000000000000000000000003"));
        var ready = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, started.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        using (var state = JsonDocument.Parse(active!.ValueJson))
        {
            Assert.Equal("active", state.RootElement.GetProperty("status").GetString());
            Assert.Equal(59, state.RootElement.GetProperty("lightActivityMinutes").GetInt32());
            Assert.Equal(159, state.RootElement.GetProperty("observedAtMinute").GetInt32());
            Assert.Equal(8, state.RootElement.GetProperty("observedClockRevision").GetInt32());
        }
        Assert.True(finalEvaluation.Ok, finalEvaluation.Run?.Error);
        Assert.Empty(finalEvaluation.Run!.Output.Events);
        Assert.Empty(finalEvaluation.Run.Output.Notifications);
        Assert.Contains("\"benefitsGranted\":false", finalEvaluation.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, final.Disposition);
        using var finalState = JsonDocument.Parse(ready!.ValueJson);
        Assert.Equal("ready", finalState.RootElement.GetProperty("status").GetString());
        Assert.Equal(60, finalState.RootElement.GetProperty("lightActivityMinutes").GetInt32());
        Assert.Equal(3, ready.Revision);
    }

    [Fact]
    public async Task Character_creation_rest_progress_requires_six_hours_sleep_and_limits_light_activity()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"long\"}", 0,
                "32000000000000000000000000000001"))).Disposition);
        await harness.SetRestClockAsync(460, 8);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"sleep\"}", 0,
                "32000000000000000000000000000002"))).Disposition);
        await harness.SetRestClockAsync(580, 9);
        var completed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"light\"}", 0,
            "32000000000000000000000000000003"));
        var episode = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, completed.Disposition);
        using var state = JsonDocument.Parse(episode!.ValueJson);
        Assert.Equal("ready", state.RootElement.GetProperty("status").GetString());
        Assert.Equal(360, state.RootElement.GetProperty("sleepMinutes").GetInt32());
        Assert.Equal(120, state.RootElement.GetProperty("lightActivityMinutes").GetInt32());
        Assert.Equal(480, state.RootElement.GetProperty("requiredMinutes").GetInt32());
    }

    [Fact]
    public async Task Character_creation_long_rest_interruption_adds_an_hour_and_reports_credit_only()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"long\"}", 0,
            "33000000000000000000000000000001"));
        await harness.SetRestClockAsync(160, 8);
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"sleep\"}", 0,
            "33000000000000000000000000000002"));
        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0);
        var interrupted = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0,
            "33000000000000000000000000000003"));
        var episode = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        Assert.True(evaluated.Ok, evaluated.Run?.Error);
        Assert.Contains("\"shortRestCreditEligible\":true", evaluated.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"benefitsGranted\":false", evaluated.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, interrupted.Disposition);
        using var state = JsonDocument.Parse(episode!.ValueJson);
        Assert.Equal("active", state.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, state.RootElement.GetProperty("interruptionCount").GetInt32());
        Assert.Equal(540, state.RootElement.GetProperty("requiredMinutes").GetInt32());
        Assert.Equal(60, state.RootElement.GetProperty("sleepMinutes").GetInt32());

        await harness.SetRestClockAsync(460, 9);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"sleep\"}", 0,
                "33000000000000000000000000000004"))).Disposition);
        await harness.SetRestClockAsync(580, 10);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"light\"}", 0,
                "33000000000000000000000000000005"))).Disposition);
        await harness.SetRestClockAsync(640, 11);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"sleep\"}", 0,
                "33000000000000000000000000000006"))).Disposition);
        var ready = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        using var readyState = JsonDocument.Parse(ready!.ValueJson);
        Assert.Equal("ready", readyState.RootElement.GetProperty("status").GetString());
        Assert.Equal(420, readyState.RootElement.GetProperty("sleepMinutes").GetInt32());
        Assert.Equal(120, readyState.RootElement.GetProperty("lightActivityMinutes").GetInt32());
    }

    [Theory]
    [InlineData("initiative", "34000000000000000000000000000001")]
    [InlineData("non-cantrip-spell", "34000000000000000000000000000002")]
    [InlineData("damage", "34000000000000000000000000000003")]
    public async Task Character_creation_short_rest_interruptions_remove_episode_and_membership_atomically(
        string interruption, string operationId)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"short\"}", 0,
            "34000000000000000000000000000000"));
        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.interrupt", roles,
            "{\"kind\":\"" + interruption + "\"}", 0);
        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.interrupt", roles,
            "{\"kind\":\"" + interruption + "\"}", 0, operationId));

        Assert.True(evaluated.Ok, evaluated.Run?.Error);
        Assert.Equal(2, evaluated.Run!.Output.Effects.Count);
        Assert.Contains("\"outcome\":\"stopped\"", evaluated.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"benefitsGranted\":false", evaluated.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high", "dnd2024.rest.world"));
    }

    [Theory]
    [InlineData("initiative")]
    [InlineData("non-cantrip-spell")]
    [InlineData("damage")]
    [InlineData("walking-or-physical-exertion")]
    public async Task Character_creation_long_rest_accepts_each_exact_interruption(string interruption)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"long\"}", 0,
            "35000000000000000000000000000000"));

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.interrupt", roles,
            "{\"kind\":\"" + interruption + "\"}", 0);

        Assert.True(result.Ok, result.Run?.Error);
        Assert.Single(result.Run!.Output.Effects);
        Assert.Contains("\"requiredMinutes\":540", result.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"shortRestCreditEligible\":false", result.Run.Output.Data,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unchanged-clock")]
    [InlineData("short-sleep")]
    [InlineData("extra-input")]
    [InlineData("excess-long-light")]
    public async Task Character_creation_rest_progress_rejects_unauthenticated_or_invalid_activity(
        string stateCase)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        var kind = stateCase == "excess-long-light" ? "long" : "short";
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"" + kind + "\"}", 0,
            "36000000000000000000000000000000"));
        if (stateCase != "unchanged-clock")
            await harness.SetRestClockAsync(stateCase == "excess-long-light" ? 221 : 101, 8);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var input = stateCase switch
        {
            "short-sleep" => "{\"activity\":\"sleep\"}",
            "extra-input" => "{\"activity\":\"light\",\"minutes\":1}",
            _ => "{\"activity\":\"light\"}"
        };

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.progress", roles, input, 0);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        Assert.False(result.Ok);
        Assert.Empty(result.Run!.Output.Effects);
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Character_creation_rest_interruption_rejects_unclassified_time_and_unknown_kind()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"long\"}", 0,
            "37000000000000000000000000000000"));
        await harness.SetRestClockAsync(101, 8);
        var unclassified = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0);
        await harness.SetRestClockAsync(100, 7);
        var unknown = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.interrupt", roles, "{\"kind\":\"loud-noise\"}", 0);
        var episode = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        Assert.False(unclassified.Ok);
        Assert.False(unknown.Ok);
        Assert.Empty(unclassified.Run!.Output.Effects);
        Assert.Empty(unknown.Run!.Output.Effects);
        Assert.Equal(1, episode!.Revision);
    }

    [Theory]
    [InlineData("missing-membership")]
    [InlineData("corrupt-episode")]
    [InlineData("incoherent-clock")]
    public async Task Character_creation_rest_progress_fails_closed_on_corrupt_scope_or_state(
        string stateCase)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"long\"}", 0,
            "37500000000000000000000000000000"));
        if (stateCase == "missing-membership")
            Assert.True(await harness.Edges.RemoveRelationshipAsync(
                DndHarness.StateSpaceId, "world.rest.fixture", "subject.high",
                "dnd2024.rest.world", 1));
        if (stateCase == "corrupt-episode")
        {
            var episode = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
            await harness.ReplaceApplicationComponentRawAsync(
                "subject.high", "dnd2024.rest-episode",
                episode!.ValueJson.Replace("\"sleepMinutes\":0", "\"sleepMinutes\":1",
                    StringComparison.Ordinal));
        }
        await harness.SetRestClockAsync(101, stateCase == "incoherent-clock" ? 7 : 8);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        var result = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"sleep\"}", 0);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        Assert.False(result.Ok);
        Assert.Empty(result.Run!.Output.Effects);
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Character_creation_duration_ready_rest_rejects_more_progress_or_interruption()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", roles, "{\"kind\":\"short\"}", 0,
            "38000000000000000000000000000000"));
        await harness.SetRestClockAsync(160, 8);
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"light\"}", 0,
            "38000000000000000000000000000001"));
        await harness.SetRestClockAsync(161, 9);

        var progress = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.progress", roles, "{\"activity\":\"light\"}", 0);
        var interruption = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0);

        Assert.False(progress.Ok);
        Assert.False(interruption.Ok);
        Assert.Empty(progress.Run!.Output.Effects);
        Assert.Empty(interruption.Run!.Output.Effects);
    }

    [Theory]
    [InlineData("short", "short-stopped", "39000000000000000000000000000001")]
    [InlineData("long", "long-resumed", "39000000000000000000000000000002")]
    public async Task Weapon_damage_automatically_interrupts_active_rest_in_the_damage_transaction(
        string restKind, string expectedOutcome, string operationId)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        await harness.AddCombatFixturesAsync();
        var restRoles = new Dictionary<string, string>
        {
            ["creature"] = "target.fixture",
            ["world"] = "world.rest.fixture",
            ["policy"] = "content.dnd2024.rest-policy.standard.v1"
        };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.rest.begin", restRoles,
                "{\"kind\":\"" + restKind + "\"}", 0,
                "39000000000000000000000000000000"))).Disposition);
        var damageRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["weapon"] = "weapon.fixture",
            ["target"] = "target.fixture"
        };
        const string input = "{\"ability\":\"str\",\"critical\":false}";

        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", damageRoles, input, 77);
        var request = harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", damageRoles, input, 77, operationId);
        var applied = await harness.Runner.RunAsync(request);
        var replayed = await harness.Runner.RunAsync(request);

        Assert.True(evaluated.Ok, evaluated.Run?.Error);
        using (var data = JsonDocument.Parse(evaluated.Run!.Output.Data))
        {
            Assert.True(data.RootElement.GetProperty("damage").GetInt32() > 0);
            var interruption = data.RootElement.GetProperty("restInterruption");
            Assert.Equal(expectedOutcome, interruption.GetProperty("outcome").GetString());
            Assert.False(interruption.GetProperty("benefitsGranted").GetBoolean());
        }
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var hp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        Assert.Equal(2, hp!.Revision);
        if (restKind == "short")
        {
            Assert.Equal(3, applied.AppliedEffectCount);
            Assert.Null(await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode"));
            Assert.Null(await harness.Edges.GetRelationshipAsync(
                DndHarness.StateSpaceId, "world.rest.fixture", "target.fixture",
                "dnd2024.rest.world"));
        }
        else
        {
            Assert.Equal(2, applied.AppliedEffectCount);
            var episode = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");
            Assert.Equal(2, episode!.Revision);
            using var state = JsonDocument.Parse(episode.ValueJson);
            Assert.Equal(1, state.RootElement.GetProperty("interruptionCount").GetInt32());
            Assert.Equal(540, state.RootElement.GetProperty("requiredMinutes").GetInt32());
            Assert.Equal("active", state.RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Weapon_damage_absorbed_by_temporary_hp_still_interrupts_an_active_short_rest()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        await harness.AddCombatFixturesAsync();
        await harness.AddApplicationComponentAsync("target.fixture",
            "dnd2024.temporary-hit-points",
            "{\"amount\":100,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Temporary Hit Points (PDF p. 18)\"}}");
        var restRoles = new Dictionary<string, string>
        {
            ["creature"] = "target.fixture", ["world"] = "world.rest.fixture",
            ["policy"] = "content.dnd2024.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", restRoles, "{\"kind\":\"short\"}", 0,
            "39100000000000000000000000000000"));
        var damageRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["target"] = "target.fixture"
        };

        var applied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", damageRoles,
            "{\"ability\":\"str\",\"critical\":false}", 77,
            "39100000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(3, applied.AppliedEffectCount);
        var hp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        Assert.Equal(1, hp!.Revision);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode"));
    }

    [Theory]
    [InlineData("immune")]
    [InlineData("ready")]
    public async Task Weapon_damage_does_not_interrupt_when_no_damage_is_taken_or_rest_is_ready(
        string stateCase)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        await harness.AddCombatFixturesAsync();
        if (stateCase == "immune")
            await harness.AddApplicationComponentAsync("target.fixture", "dnd2024.damage-mitigation",
                "{\"resistances\":[],\"immunities\":[\"piercing\"],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Resistance and Vulnerability; Immunity (PDF p. 17)\"}}");
        var restRoles = new Dictionary<string, string>
        {
            ["creature"] = "target.fixture", ["world"] = "world.rest.fixture",
            ["policy"] = "content.dnd2024.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", restRoles,
            "{\"kind\":\"" + (stateCase == "ready" ? "short" : "long") + "\"}", 0,
            "39200000000000000000000000000000"));
        if (stateCase == "ready")
        {
            await harness.SetRestClockAsync(160, 8);
            await harness.Runner.RunAsync(harness.ActionForRoles(
                "mechanic.dnd2024.rest.progress", restRoles, "{\"activity\":\"light\"}", 0,
                "39200000000000000000000000000001"));
        }
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");
        var damageRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["target"] = "target.fixture"
        };

        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.weapon-damage.apply", damageRoles,
            "{\"ability\":\"str\",\"critical\":false}", 77);
        var applied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", damageRoles,
            "{\"ability\":\"str\",\"critical\":false}", 77,
            "39200000000000000000000000000002"));
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");

        Assert.True(evaluated.Ok, evaluated.Run?.Error);
        using (var data = JsonDocument.Parse(evaluated.Run!.Output.Data))
            Assert.Equal(JsonValueKind.Null,
                data.RootElement.GetProperty("restInterruption").ValueKind);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "target.fixture",
            "dnd2024.rest.world"));
    }

    [Fact]
    public async Task Weapon_damage_rejects_orphaned_rest_episode_before_any_damage_effect()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        await harness.AddCombatFixturesAsync();
        var restRoles = new Dictionary<string, string>
        {
            ["creature"] = "target.fixture", ["world"] = "world.rest.fixture",
            ["policy"] = "content.dnd2024.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.rest.begin", restRoles, "{\"kind\":\"short\"}", 0,
            "39300000000000000000000000000000"));
        Assert.True(await harness.Edges.RemoveRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "target.fixture",
            "dnd2024.rest.world", 1));
        var hpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        var episodeBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.weapon-damage.apply", new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
                ["target"] = "target.fixture"
            }, "{\"ability\":\"str\",\"critical\":false}", 77,
            "39300000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        var hpAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.hit-points");
        var episodeAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");
        Assert.Equal(hpBefore!.Revision, hpAfter!.Revision);
        Assert.Equal(hpBefore.ValueJson, hpAfter.ValueJson);
        Assert.Equal(episodeBefore!.Revision, episodeAfter!.Revision);
        Assert.Equal(episodeBefore.ValueJson, episodeAfter.ValueJson);
    }

    private static Dictionary<string, string> RestBeginRoles() => new()
    {
        ["creature"] = "subject.high",
        ["world"] = "world.rest.fixture",
        ["policy"] = "content.dnd2024.rest-policy.standard.v1"
    };

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
            .Where(path => Path.GetFileName(path) == "content.dnd2024.class.fighter.v1.json"
                || Path.GetFileName(path).StartsWith("content.dnd2024.feature.fighter.",
                    StringComparison.Ordinal))
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

    public static TheoryData<string, int, string[], string[], string[], bool, bool>
        BasicClassCreationCases => new()
        {
            { "barbarian", 12, ["str", "con"], ["perception", "survival"], ["simple", "martial"], false, false },
            { "bard", 8, ["dex", "cha"], ["arcana", "perception", "persuasion"], ["simple"], true, false },
            { "cleric", 8, ["wis", "cha"], ["insight", "religion"], ["simple"], true, false },
            { "druid", 8, ["int", "wis"], ["nature", "perception"], ["simple"], true, false },
            { "fighter", 10, ["str", "con"], ["perception", "survival"], ["simple", "martial"], false, false },
            { "monk", 8, ["str", "dex"], ["acrobatics", "insight"], ["simple"], false, true },
            { "paladin", 10, ["wis", "cha"], ["persuasion", "religion"], ["simple", "martial"], true, false },
            { "ranger", 10, ["str", "dex"], ["nature", "perception", "stealth"], ["simple", "martial"], true, false },
            { "rogue", 8, ["dex", "int"], ["acrobatics", "investigation", "sleight-of-hand", "stealth"], ["simple"], false, true },
            { "sorcerer", 6, ["con", "cha"], ["arcana", "persuasion"], ["simple"], true, false },
            { "warlock", 8, ["wis", "cha"], ["arcana", "investigation"], ["simple"], true, false },
            { "wizard", 6, ["int", "wis"], ["arcana", "investigation"], ["simple"], true, false }
        };

    public static TheoryData<string, string, string, int, int, int, int, int>
        BasicClassSpellcastingCases => new()
        {
            { "bard", "full", "cha", 2, 4, 0, 2, 1 },
            { "cleric", "full", "wis", 3, 4, 0, 2, 1 },
            { "druid", "full", "wis", 2, 4, 0, 2, 1 },
            { "paladin", "half", "cha", 0, 2, 0, 2, 1 },
            { "ranger", "half", "wis", 0, 2, 0, 2, 1 },
            { "sorcerer", "full", "cha", 4, 2, 0, 2, 1 },
            { "warlock", "pact", "cha", 2, 2, 0, 1, 1 },
            { "wizard", "full", "int", 3, 4, 6, 2, 1 }
        };

    public static TheoryData<string, string, string[]> BasicClassPrimaryAbilityCases => new()
    {
        { "barbarian", "all", ["str"] },
        { "bard", "all", ["cha"] },
        { "cleric", "all", ["wis"] },
        { "druid", "all", ["wis"] },
        { "fighter", "one-of", ["str", "dex"] },
        { "monk", "all", ["dex", "wis"] },
        { "paladin", "all", ["str", "cha"] },
        { "ranger", "all", ["dex", "wis"] },
        { "rogue", "all", ["dex"] },
        { "sorcerer", "all", ["cha"] },
        { "warlock", "all", ["cha"] },
        { "wizard", "all", ["int"] }
    };

    [Fact]
    public async Task Basic_character_creation_commits_core_state_participation_pending_ledger_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.aric";
        const string worldId = "world.character-creation.fixture";
        var roles = BasicCreationRoles(worldId, "content.dnd2024.species.human.v1");
        const string input =
            "{\"characterId\":\"actor.basic.aric\",\"name\":\"Aric\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";

        var evaluated = await harness.EvaluateRolesAsync(
            "mechanic.dnd2024.character.basic.create", roles, input, 0);
        var request = harness.ActionForRoles(
            "mechanic.dnd2024.character.basic.create", roles, input, 0,
            "a123456789abcdef0123456789abcdf0");
        var created = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
        Assert.Equal(19, evaluated.Run!.Output.Effects.Count);
        Assert.Empty(evaluated.Run.Output.Events);
        Assert.Empty(evaluated.Run.Output.Notifications);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        Assert.Equal(19, created.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));

        using var abilities = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.abilities"))!.ValueJson);
        Assert.Equal(17, abilities.RootElement.GetProperty("str").GetInt32());
        Assert.Equal(14, abilities.RootElement.GetProperty("dex").GetInt32());
        Assert.Equal(14, abilities.RootElement.GetProperty("con").GetInt32());
        using var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.hit-points"))!.ValueJson);
        Assert.Equal(12, hitPoints.RootElement.GetProperty("current").GetInt32());
        Assert.Equal(12, hitPoints.RootElement.GetProperty("maximum").GetInt32());
        using var armorClass = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.armor-class"))!.ValueJson);
        Assert.Equal(12, armorClass.RootElement.GetProperty("value").GetInt32());
        using var skills = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.skill-proficiencies"))!.ValueJson);
        Assert.Equal(new[] { "athletics", "intimidation", "perception", "survival" },
            skills.RootElement.GetProperty("skills").EnumerateArray()
                .Select(value => value.GetString()).ToArray());
        using var saves = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.saving-throw-proficiencies"))!.ValueJson);
        Assert.Equal(new[] { "str", "con" }, saves.RootElement.GetProperty("abilities")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
        using var weapons = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.weapon-proficiencies"))!.ValueJson);
        Assert.Equal(new[] { "simple", "martial" }, weapons.RootElement.GetProperty("categories")
            .EnumerateArray().Select(value => value.GetString()).ToArray());

        using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
        Assert.Equal("basic-playable", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("soldier-fighter-level-1-v1",
            record.RootElement.GetProperty("templateKey").GetString());
        Assert.Equal(14, record.RootElement.GetProperty("appliedComponentIds").GetArrayLength());
        var pending = record.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(15, pending.Length);
        Assert.Contains(pending, value => value.GetProperty("ownerDefinitionId").GetString() ==
            "content.dnd2024.species.human.v1" &&
            value.GetProperty("entitlementKey").GetString() == "trait:resourceful");
        Assert.Contains(pending, value => value.GetProperty("ownerDefinitionId").GetString() ==
            "content.dnd2024.feature.fighter.second-wind.v1" &&
            value.GetProperty("reason").GetString() == "behavior-unimplemented");
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.heroic-inspiration"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.equipment-state"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.tool-proficiencies"));

        var participationId = worldId + ".participation." + actorId;
        using var participation = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, participationId,
            "game.core.campaign.character-participation"))!.ValueJson);
        Assert.Equal("active", participation.RootElement.GetProperty("status").GetString());
        Assert.Equal("{}", (await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            worldId, participationId,
            "dnd2024.campaign.has-character-participation"))!.DataJson);
        Assert.Equal("{}", (await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            participationId, actorId,
            "dnd2024.campaign.character-participation.for-actor"))!.DataJson);

        Assert.NotNull(await harness.ReadEntityFreshAsync(actorId));
        Assert.NotNull(await harness.ReadRelationshipFreshAsync(worldId, participationId,
            "dnd2024.campaign.has-character-participation"));
        var sheet = await harness.EvaluateRolesAsync("mechanic.dnd2024.character-sheet.read",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0);
        var initiative = await harness.EvaluateRolesAsync("mechanic.dnd2024.initiative.roll",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 17);
        Assert.True(sheet.Ok, sheet.Run?.Error);
        Assert.True(initiative.Ok, initiative.Run?.Error);
    }

    [Theory]
    [MemberData(nameof(BasicClassCreationCases))]
    public async Task Basic_character_creation_supports_every_srd_level_one_class_model(
        string classKey,
        int hitDieSides,
        string[] savingThrows,
        string[] classSkills,
        string[] weaponCategories,
        bool spellcastingPending,
        bool restrictedMartialPending)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var actorId = "actor.basic." + classKey;
        var classId = "content.dnd2024.class." + classKey + ".v1";
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Test " + classKey,
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new { str = 2, con = 1 }
            },
            speciesSelection = new { size = "medium" }
        });

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "content.dnd2024.species.human.v1", classId),
            input, 0, "1123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
        using var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.hit-points"))!.ValueJson);
        Assert.Equal(hitDieSides + 2,
            hitPoints.RootElement.GetProperty("maximum").GetInt32());
        using var saves = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.saving-throw-proficiencies"))!.ValueJson);
        Assert.Equal(savingThrows, saves.RootElement.GetProperty("abilities").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
        using var skills = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.skill-proficiencies"))!.ValueJson);
        Assert.Equal(new[] { "athletics", "intimidation" }.Concat(classSkills)
                .Order(StringComparer.Ordinal),
            skills.RootElement.GetProperty("skills").EnumerateArray()
                .Select(value => value.GetString()));
        using var weapons = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.weapon-proficiencies"))!.ValueJson);
        Assert.Equal(weaponCategories, weapons.RootElement.GetProperty("categories")
            .EnumerateArray().Select(value => value.GetString()).ToArray());

        using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
        Assert.Equal("soldier-" + classKey + "-level-1-v1",
            record.RootElement.GetProperty("templateKey").GetString());
        var selections = record.RootElement.GetProperty("selections");
        Assert.Equal(classId, selections.GetProperty("classDefinitionId").GetString());
        Assert.Equal(classSkills, selections.GetProperty("classSkillChoices").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
        var pending = record.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(spellcastingPending, pending.Any(value =>
            value.GetProperty("ownerDefinitionId").GetString() == classId &&
            value.GetProperty("entitlementKey").GetString()!.StartsWith(
                "spellcasting:", StringComparison.Ordinal)));
        Assert.Equal(restrictedMartialPending, pending.Any(value =>
            value.GetProperty("ownerDefinitionId").GetString() == classId &&
            value.GetProperty("entitlementKey").GetString()!.StartsWith(
                "weapon:martial-property:", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(BasicClassSpellcastingCases))]
    public async Task Basic_character_creation_class_models_preserve_exact_level_one_spell_tables(
        string classKey,
        string kind,
        string ability,
        int cantrips,
        int preparedSpells,
        int spellbookSpells,
        int level1Slots,
        int slotLevel)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var classId = "content.dnd2024.class." + classKey + ".v1";

        using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId,
            "dnd2024.class-creation-profile"))!.ValueJson);
        var spellcasting = profile.RootElement.GetProperty("spellcasting");
        Assert.Equal(kind, spellcasting.GetProperty("kind").GetString());
        Assert.Equal(ability, spellcasting.GetProperty("ability").GetString());
        Assert.Equal(cantrips, spellcasting.GetProperty("cantrips").GetInt32());
        Assert.Equal(preparedSpells, spellcasting.GetProperty("preparedSpells").GetInt32());
        Assert.Equal(spellbookSpells, spellcasting.GetProperty("spellbookSpells").GetInt32());
        Assert.Equal(level1Slots, spellcasting.GetProperty("level1Slots").GetInt32());
        Assert.Equal(slotLevel, spellcasting.GetProperty("slotLevel").GetInt32());
    }

    [Theory]
    [MemberData(nameof(BasicClassPrimaryAbilityCases))]
    public async Task Basic_character_creation_class_models_preserve_primary_ability_meaning(
        string classKey,
        string mode,
        string[] abilities)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var classId = "content.dnd2024.class." + classKey + ".v1";

        using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId,
            "dnd2024.class-creation-profile"))!.ValueJson);
        var primary = profile.RootElement.GetProperty("primaryAbilities");
        Assert.Equal(mode, primary.GetProperty("mode").GetString());
        Assert.Equal(abilities, primary.GetProperty("abilities").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
    }

    [Fact]
    public async Task Basic_character_creation_supports_fixed_size_species_and_source_speed()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.goliath";
        var roles = BasicCreationRoles(
            "world.character-creation.fixture", "content.dnd2024.species.goliath.v1");
        const string input =
            "{\"characterId\":\"actor.basic.goliath\",\"name\":\"Kava\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":1,\"dex\":1,\"con\":1}},\"speciesSelection\":{}}";

        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character.basic.create", roles, input, long.MaxValue,
            "b123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        using var size = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature-size"))!.ValueJson);
        using var speed = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.speed"))!.ValueJson);
        Assert.Equal("medium", size.RootElement.GetProperty("size").GetString());
        Assert.Equal(35, speed.RootElement.GetProperty("walkFeet").GetInt32());
    }

    [Theory]
    [InlineData("{\"characterId\":\"actor.basic.invalid\",\"name\":\"Invalid\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"large\"}}")]
    [InlineData("{\"characterId\":\"actor.basic.invalid\",\"name\":\" Invalid\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}")]
    [InlineData("{\"characterId\":\"actor.basic.invalid\",\"name\":\"Invalid\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"hitPoints\":12}")]
    public async Task Basic_character_creation_rejects_illegal_or_derived_input_unchanged(string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.invalid";
        const string worldId = "world.character-creation.fixture";
        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character.basic.create",
            BasicCreationRoles(worldId, "content.dnd2024.species.human.v1"), input, 0,
            "c123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            worldId + ".participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_rolls_back_after_all_effects_are_staged()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.rollback";
        const string worldId = "world.character-creation.fixture";
        const string input =
            "{\"characterId\":\"actor.basic.rollback\",\"name\":\"Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"small\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character.basic.create",
            BasicCreationRoles(worldId, "content.dnd2024.species.human.v1"), input, 0,
            "d123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        var participationId = worldId + ".participation." + actorId;
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, participationId));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            worldId, participationId, "dnd2024.campaign.has-character-participation"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            participationId, actorId,
            "dnd2024.campaign.character-participation.for-actor"));
    }

    [Theory]
    [InlineData("inactive-world")]
    [InlineData("source-drift")]
    [InlineData("class-profile-drift")]
    public async Task Basic_character_creation_rejects_inactive_or_source_drifted_state_unchanged(
        string invalidState)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.invalid-state";
        const string worldId = "world.character-creation.fixture";
        if (invalidState == "inactive-world")
        {
            await harness.ReplaceApplicationComponentRawAsync(worldId, "game.core.world.root",
                "{\"status\":\"archived\",\"summary\":\"An inactive fixture.\",\"visibility\":\"party\"}");
        }
        else if (invalidState == "source-drift")
        {
            await harness.ReplaceApplicationComponentRawAsync("content.dnd2024.species.human.v1",
                "dnd2024.species-profile",
                "{\"contentKey\":\"human\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.drifted\",\"locator\":\"Character Origins > Character Species > Human\"},\"creatureType\":\"humanoid\",\"allowedSizes\":[\"small\",\"medium\"],\"baseSpeed\":{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0},\"traitKeys\":[\"resourceful\",\"skillful\",\"versatile\"],\"choiceFamilies\":[]}");
        }
        else
        {
            await harness.ReplaceApplicationComponentRawAsync(
                "content.dnd2024.class.fighter.v1", "dnd2024.class-creation-profile",
                "{\"classKey\":\"wizard\",\"primaryAbilities\":{\"mode\":\"all\",\"abilities\":[\"int\"]},\"savingThrows\":[\"int\",\"wis\"],\"skills\":{\"choiceCount\":2,\"options\":[\"arcana\",\"history\",\"insight\",\"investigation\",\"medicine\",\"nature\",\"religion\"],\"fixedChoices\":[\"arcana\",\"investigation\"]},\"weapons\":{\"categories\":[\"simple\"],\"restrictedMartialProperties\":[]},\"armorTraining\":[],\"tools\":{\"fixed\":[],\"choiceGroups\":[]},\"spellcasting\":{\"kind\":\"full\",\"ability\":\"int\",\"cantrips\":3,\"preparedSpells\":4,\"spellbookSpells\":6,\"level1Slots\":2,\"slotLevel\":1},\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Classes > Wizard, PDF pages 77–78\"}}");
        }

        const string input =
            "{\"characterId\":\"actor.basic.invalid-state\",\"name\":\"Invalid State\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character.basic.create",
            BasicCreationRoles(worldId, "content.dnd2024.species.human.v1"), input, 0,
            invalidState switch
            {
                "inactive-world" => "e123456789abcdef0123456789abcdf0",
                "source-drift" => "f123456789abcdef0123456789abcdf0",
                _ => "a223456789abcdef0123456789abcdf0"
            }));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            worldId + ".participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_rejects_existing_actor_without_partial_participation()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.existing";
        const string worldId = "world.character-creation.fixture";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Existing Actor");
        const string input =
            "{\"characterId\":\"actor.basic.existing\",\"name\":\"Replacement\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "mechanic.dnd2024.character.basic.create",
            BasicCreationRoles(worldId, "content.dnd2024.species.human.v1"), input, 0,
            "0123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        var existing = await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId);
        Assert.NotNull(existing);
        Assert.Equal("Existing Actor", existing.Name);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            worldId + ".participation." + actorId));
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
        private static readonly ApplicationIdentifier GameApplication = ApplicationIdentifier.Parse("game");
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
            SqliteStateSpaceEdgeStore edges,
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
            Edges = edges;
            Runner = runner;
        }

        public SqliteEntityComponentStore Entities { get; }
        public SqliteStateSpaceEdgeStore Edges { get; }
        public ApplicationActionRunner Runner { get; }

        public static async Task<DndHarness> CreateAsync(
            bool includeLegacyEquipmentExtension = false,
            bool failTransactionAfterEffects = false)
        {
            var fixture = new SqliteFixture();
            var db = fixture.CreateContext();
            var applications = new SqliteApplicationRegistry(db);
            applications.Register(new(
                GameApplication, "Game Core", "Generic world, clock, and campaign state owners.", []));
            var revision = applications.Register(new(
                Application, "D&D 2024", "A modular D&D 2024 application.", [GameApplication]));
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
                "data/dnd2024.character-creation-record",
                "data/dnd2024.class-creation-profile",
                "data/dnd2024.character.profile",
                "data/dnd2024.heroic-inspiration",
                "data/dnd2024.rest-policy",
                "data/dnd2024.rest-episode",
                "data/dnd2024.character.ability-assignment-policy",
                "data/dnd2024.background.ability-increase-options",
                "data/dnd2024.species-profile",
                "data/dnd2024.selected-species",
                "data/dnd2024.feat-profile",
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
            foreach (var componentId in new[]
                     {
                         "game.core.world.root", "game.core.world.clock",
                         "game.core.campaign.character-participation"
                     })
            {
                var definition = await GameDefinitionAsync(componentId);
                additionalTypes[definition.Id] = types.Define(new(
                    GameApplication, definition.Id, definition.Schema));
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
            var applier = new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges,
                failTransactionAfterEffects
                    ? [new RejectAfterEffectsTransactionParticipant()]
                    : null);
            var runner = new ApplicationActionRunner(
                catalogs, activations, stateSpaces, types, entities, edges, evaluator, applier, operations);
            return new(fixture, db, catalogs, abilities, level, skills, saves, weaponProfile, weaponProficiencies,
                armorClass, hitPoints, initiativeOrder, turnState, speed, turnBudget, conditions, additionalTypes,
                activation.Activation.Winners.Select(value => value.RelativePath).ToHashSet(StringComparer.Ordinal),
                entities, edges, runner);
        }

        public async Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
            string subjectId, string input, long seed, string localMechanicId = "mechanic.dnd2024.check.ability")
            => await EvaluateRolesAsync(localMechanicId, new Dictionary<string, string> { ["subject"] = subjectId }, input, seed);

        public async Task<ApplicationMechanicEvaluationResult> EvaluateRolesAsync(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed)
            => await EvaluateRolesWithMappingAsync(localMechanicId, roles, input, seed,
                includeGameBaseMapping: true);

        public async Task<ApplicationMechanicEvaluationResult> EvaluateRolesWithoutGameBaseMappingAsync(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed)
            => await EvaluateRolesWithMappingAsync(localMechanicId, roles, input, seed,
                includeGameBaseMapping: false);

        private async Task<ApplicationMechanicEvaluationResult> EvaluateRolesWithMappingAsync(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed,
            bool includeGameBaseMapping)
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
                if (includeGameBaseMapping || type.Owner != GameApplication)
                    componentMapping[componentId] = new(type.QualifiedId, type.Version, type.SchemaHash);
            var mapping = new ApplicationMechanicProjectionMapping(componentMapping,
                new Dictionary<string, string>
                {
                    ["rest.world"] = "dnd2024.rest.world",
                    ["campaign.has-character-participation"] =
                        "dnd2024.campaign.has-character-participation",
                    ["campaign.character-participation.for-actor"] =
                        "dnd2024.campaign.character-participation.for-actor"
                });
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

        public async Task AddCharacterCreationAbilityFixturesAsync()
        {
            var directory = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
                "content", "entities", "character-creation");
            foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
                Assert.Contains(relative, ActiveSourcePaths);
                var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
                await Entities.CreateEntityAsync(StateSpaceId, entity.Id, entity.Name);
                foreach (var component in entity.Components)
                    await AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
            }
        }

        public async Task AddCharacterCreationSpeciesFixturesAsync()
        {
            var directory = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
                "content", "entities", "character-creation", "species");
            foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
                Assert.Contains(relative, ActiveSourcePaths);
                var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
                await Entities.CreateEntityAsync(StateSpaceId, entity.Id, entity.Name);
                foreach (var component in entity.Components)
                    await AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
            }
        }

        public async Task AddCharacterCreationFeatFixturesAsync()
        {
            var directory = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
                "content", "entities", "character-creation", "feats");
            foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
                Assert.Contains(relative, ActiveSourcePaths);
                var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
                await Entities.CreateEntityAsync(StateSpaceId, entity.Id, entity.Name);
                foreach (var component in entity.Components)
                    await AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
            }
        }

        public async Task AddBasicCharacterCreationFixturesAsync(
            string worldId = "world.character-creation.fixture")
        {
            await AddCharacterCreationAbilityFixturesAsync();
            await AddCharacterCreationSpeciesFixturesAsync();
            await Entities.CreateEntityAsync(StateSpaceId, worldId, "Character Creation World");
            await AddApplicationComponentAsync(worldId, "game.core.world.root",
                "{\"status\":\"active\",\"summary\":\"A source-bound basic character creation fixture.\",\"visibility\":\"party\"}");

            var directory = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
                "content", "entities", "character-progression");
            foreach (var path in Directory.GetFiles(directory, "content.dnd2024.class.*.json")
                         .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
                Assert.Contains(relative, ActiveSourcePaths);
                var classEntity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
                await Entities.CreateEntityAsync(StateSpaceId, classEntity.Id, classEntity.Name);
                foreach (var component in classEntity.Components)
                    await AddApplicationComponentAsync(
                        classEntity.Id, component.DefinitionId, component.Data);
            }
        }

        public async Task<EcsEntityView?> ReadEntityFreshAsync(string entityId)
        {
            await using var fresh = _fixture.CreateContext();
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(fresh, schemas);
            return await new SqliteEntityComponentStore(fresh, types, schemas)
                .GetEntityAsync(StateSpaceId, entityId);
        }

        public async Task<(string DataJson, int Revision)?> ReadRelationshipFreshAsync(
            string fromEntityId, string toEntityId, string kind)
        {
            await using var fresh = _fixture.CreateContext();
            var edges = new SqliteStateSpaceEdgeStore(fresh,
                new SqliteStateSpaceRegistry(fresh, new SqliteApplicationRegistry(fresh)));
            var relationship = await edges.GetRelationshipAsync(
                StateSpaceId, fromEntityId, toEntityId, kind);
            return relationship is null ? null : (relationship.DataJson, relationship.Revision);
        }

        public async Task AddRestBeginFixturesAsync(int currentHitPoints = 1, int currentMinute = 123)
        {
            await Entities.AddComponentAsync(new(StateSpaceId, "subject.high",
                new(_hitPoints.QualifiedId, _hitPoints.Version, _hitPoints.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    current = currentHitPoints,
                    maximum = 10,
                    sourceRef = new
                    {
                        sourceId = "source.dnd2024.srd-5.2.1",
                        locator = "Playing the Game > Damage and Healing > Hit Points"
                    }
                }), 0));
            await Entities.CreateEntityAsync(StateSpaceId, "world.rest.fixture", "Rest World");
            await AddApplicationComponentAsync("world.rest.fixture", "game.core.world.root",
                "{\"status\":\"active\",\"summary\":\"A quiet test world.\",\"visibility\":\"party\"}");
            await AddApplicationComponentAsync("world.rest.fixture", "game.core.world.clock",
                JsonSerializer.Serialize(new
                {
                    calendarId = "calendar.fixture", currentMinute, revision = 7
                }));
            var relative =
                "catalog/applications/dnd2024/content/entities/character-creation/rest/content.dnd2024.rest-policy.standard.v1.json";
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(Path.Combine(
                RepositoryRoot(), relative.Replace('/', Path.DirectorySeparatorChar))), relative);
            await Entities.CreateEntityAsync(StateSpaceId, entity.Id, entity.Name);
            foreach (var component in entity.Components)
                await AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
        }

        public async Task SetRestClockAsync(int currentMinute, int revision)
            => await ReplaceApplicationComponentRawAsync("world.rest.fixture", "game.core.world.clock",
                JsonSerializer.Serialize(new
                {
                    calendarId = "calendar.fixture", currentMinute, revision
                }));

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

        public async Task AddHitPointsAsync(string entityId, int current, int maximum)
            => await Entities.AddComponentAsync(new(StateSpaceId, entityId,
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

        private static async Task<ComponentDefinitionFile> GameDefinitionAsync(string componentId)
        {
            var path = Path.Combine(RepositoryRoot(), "catalog", "components", componentId + ".json");
            var definition = ComponentDefinitionFile.Parse(await File.ReadAllTextAsync(path),
                "catalog/components/" + componentId + ".json",
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

        private sealed class RejectAfterEffectsTransactionParticipant : IApplicationEcsTransactionParticipant
        {
            public Task StageAsync(
                ApplicationEcsEffectBatch batch,
                IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
                string operationId,
                CancellationToken cancellationToken = default) =>
                throw new ApplicationEcsTransactionParticipantException(
                    "Injected rejection after all basic-character effects were staged.");
        }

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

    private static Dictionary<string, string> BasicCreationRoles(
        string worldId,
        string speciesId,
        string classId = "content.dnd2024.class.fighter.v1") =>
        new(StringComparer.Ordinal)
        {
            ["world"] = worldId,
            ["policy"] = "content.dnd2024.ability-assignment.standard-array.v1",
            ["background"] = "content.dnd2024.background.soldier.v1",
            ["species"] = speciesId,
            ["class"] = classId
        };

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
