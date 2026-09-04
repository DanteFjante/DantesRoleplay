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
using DantesRoleplay.Events;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
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
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");
        var action = harness.Action("subject.high", "{\"ability\":\"str\",\"dc\":30}", 77,
            "0123456789abcdef0123456789abcdef");
        var committed = await harness.Runner.RunAsync(action);
        var replay = await harness.Runner.RunAsync(action);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");
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

        Assert.True(proficient.Ok, proficient.Run?.Error ?? string.Join("; ", proficient.Problems));
        Assert.True(untrained.Ok, untrained.Run?.Error ?? string.Join("; ", untrained.Problems));
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
            "dnd2024.mechanic.saving-throw-proficiencies.record", "subject.high",
            "{\"abilities\":[\"wis\",\"con\"]}", 0, "c123456789abcdef0123456789abcdef"));
        await harness.AddProficiencyStateAsync("subject.high", 5, []);
        await harness.AddProficiencyStateAsync("subject.low", 5, []);
        await harness.AddSavingThrowStateAsync("subject.low", []);
        var proficient = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"con\",\"dc\":40}", 77, "dnd2024.mechanic.saving-throw");
        var untrained = await harness.EvaluateAsync("subject.low",
            "{\"ability\":\"con\",\"dc\":40}", 77, "dnd2024.mechanic.saving-throw");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");
        using (var storedState = JsonDocument.Parse(stored!.ValueJson))
        {
            var entries = storedState.RootElement.GetProperty("entries");
            Assert.True(entries.TryGetProperty("dnd2024.vocabulary.ability.constitution", out _));
            Assert.True(entries.TryGetProperty("dnd2024.vocabulary.ability.wisdom", out _));
            Assert.Contains("saving-throw", storedState.RootElement.GetProperty("recordedFamilies")
                .EnumerateArray().Select(value => value.GetString()));
        }
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
            77, "dnd2024.mechanic.saving-throw");
        var voluntary = await harness.EvaluateAsync("subject.high",
            "{\"ability\":\"str\",\"dc\":0,\"voluntaryFailure\":true}", 77,
            "dnd2024.mechanic.saving-throw");

        Assert.True(advantage.Ok, advantage.Run?.Error ?? string.Join("; ", advantage.Problems));
        Assert.True(voluntary.Ok, voluntary.Run?.Error ?? string.Join("; ", voluntary.Problems));
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
            "dnd2024.mechanic.initiative.roll");

        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        var root = data.RootElement;
        Assert.Equal("initiative", root.GetProperty("test").GetString());
        Assert.Equal("dex", root.GetProperty("ability").GetString());
        Assert.Equal(root.GetProperty("roll").GetInt32() + 0, root.GetProperty("initiative").GetInt32());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 4)]
    [InlineData(13, 5)]
    [InlineData(17, 6)]
    public async Task Alert_initiative_proficiency_is_optional_and_derives_each_level_band(
        int level,
        int expectedBonus)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterLevelAsync("subject.high", level);
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", AlertGrantState());

        var omitted = await harness.EvaluateAsync("subject.high", "{}", 77,
            "dnd2024.mechanic.initiative.roll");
        var declined = await harness.EvaluateAsync("subject.high",
            "{\"useAlertInitiativeProficiency\":false}", 77,
            "dnd2024.mechanic.initiative.roll");
        var used = await harness.EvaluateAsync("subject.high",
            "{\"useAlertInitiativeProficiency\":true}", 77,
            "dnd2024.mechanic.initiative.roll");

        Assert.True(omitted.Ok, omitted.Run?.Error);
        Assert.True(declined.Ok, declined.Run?.Error);
        Assert.True(used.Ok, used.Run?.Error);
        using var omittedData = JsonDocument.Parse(omitted.Run!.Output.Data);
        using var declinedData = JsonDocument.Parse(declined.Run!.Output.Data);
        using var usedData = JsonDocument.Parse(used.Run!.Output.Data);
        var omittedRoot = omittedData.RootElement;
        var declinedRoot = declinedData.RootElement;
        var usedRoot = usedData.RootElement;

        Assert.Equal(omittedRoot.GetProperty("rolls").GetRawText(),
            declinedRoot.GetProperty("rolls").GetRawText());
        Assert.Equal(omittedRoot.GetProperty("rolls").GetRawText(),
            usedRoot.GetProperty("rolls").GetRawText());
        Assert.Equal(omittedRoot.GetProperty("initiative").GetInt32(),
            declinedRoot.GetProperty("initiative").GetInt32());
        Assert.Equal(expectedBonus, usedRoot.GetProperty("initiative").GetInt32()
            - omittedRoot.GetProperty("initiative").GetInt32());

        foreach (var root in new[] { omittedRoot, declinedRoot })
        {
            var evidence = root.GetProperty("alertInitiativeProficiency");
            Assert.True(evidence.GetProperty("available").GetBoolean());
            Assert.False(evidence.GetProperty("used").GetBoolean());
            Assert.Equal(expectedBonus, evidence.GetProperty("bonus").GetInt32());
            Assert.Equal("dnd2024.source.srd-5.2.1",
                evidence.GetProperty("sourceRef").GetProperty("sourceId").GetString());
            Assert.Equal("Feats > Origin Feats > Alert, PDF page 87",
                evidence.GetProperty("sourceRef").GetProperty("locator").GetString());
            Assert.DoesNotContain(root.GetProperty("modifiers").EnumerateArray(), value =>
                value.GetProperty("source").GetString() == "feat:alert");
        }

        var usedEvidence = usedRoot.GetProperty("alertInitiativeProficiency");
        Assert.True(usedEvidence.GetProperty("available").GetBoolean());
        Assert.True(usedEvidence.GetProperty("used").GetBoolean());
        Assert.Equal(expectedBonus, usedEvidence.GetProperty("bonus").GetInt32());
        Assert.Equal("dnd2024.source.srd-5.2.1",
            usedEvidence.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Feats > Origin Feats > Alert, PDF page 87",
            usedEvidence.GetProperty("sourceRef").GetProperty("locator").GetString());
        Assert.Contains(usedRoot.GetProperty("modifiers").EnumerateArray(), value =>
            value.GetProperty("source").GetString() == "feat:alert"
            && value.GetProperty("value").GetInt32() == expectedBonus);
        Assert.Empty(omitted.Run.Output.Effects);
        Assert.Empty(declined.Run.Output.Effects);
        Assert.Empty(used.Run.Output.Effects);
    }

    [Fact]
    public async Task Alert_initiative_proficiency_use_without_alert_is_denied_effect_free()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterLevelAsync("subject.high", 5);

        var result = await harness.EvaluateAsync("subject.high",
            "{\"useAlertInitiativeProficiency\":true}", 77,
            "dnd2024.mechanic.initiative.roll");

        Assert.False(result.Ok);
        if (result.Run is not null) Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Alert_initiative_proficiency_accepts_schema_valid_external_grant_provenance()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterLevelAsync("subject.high", 5);
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", AlertGrantState(
                grantedByDefinitionId: "content.extension.background.investigator.v1",
                locator: "Extension > Investigator > Alert Grant"));

        var result = await harness.EvaluateAsync("subject.high",
            "{\"useAlertInitiativeProficiency\":true}", 77,
            "dnd2024.mechanic.initiative.roll");

        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        Assert.Equal(3, data.RootElement.GetProperty("alertInitiativeProficiency")
            .GetProperty("bonus").GetInt32());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Initiative_ignores_other_valid_feature_grants_when_alert_is_absent()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements",
            "{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.content.feature.fighter.second-wind.v1\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.class.fighter.v1\"},\"grantKind\":\"class-feature\",\"classLevel\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Classes > Fighter, PDF page 60\"}}]}");

        var baseline = await harness.EvaluateAsync("subject.low", "{}", 77,
            "dnd2024.mechanic.initiative.roll");
        var result = await harness.EvaluateAsync("subject.high", "{}", 77,
            "dnd2024.mechanic.initiative.roll");

        Assert.True(result.Ok, result.Run?.Error);
        using var baselineData = JsonDocument.Parse(baseline.Run!.Output.Data);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        Assert.Equal(baselineData.RootElement.GetProperty("initiative").GetInt32(),
            data.RootElement.GetProperty("initiative").GetInt32());
        var evidence = data.RootElement.GetProperty("alertInitiativeProficiency");
        Assert.False(evidence.GetProperty("available").GetBoolean());
        Assert.False(evidence.GetProperty("used").GetBoolean());
        Assert.Equal(0, evidence.GetProperty("bonus").GetInt32());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("sourceRef").ValueKind);
        Assert.Empty(result.Run.Output.Effects);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("wrong-kind")]
    [InlineData("wrong-configuration")]
    [InlineData("wrong-source-id")]
    [InlineData("extra-property")]
    public async Task Alert_initiative_proficiency_rejects_invalid_grant_state_effect_free(
        string stateCase)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterLevelAsync("subject.high", 5);
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", AlertGrantState());
        var invalid = stateCase switch
        {
            "malformed" => "{}",
            "duplicate" => AlertGrantState(duplicate: true),
            "wrong-kind" => AlertGrantState(grantKind: "class-feature"),
            "wrong-configuration" => AlertGrantState(configurationKey: "wizard"),
            "wrong-source-id" => AlertGrantState().Replace(
                "dnd2024.source.srd-5.2.1", "drifted source",
                StringComparison.Ordinal),
            _ => AlertGrantState(extraProperty: true)
        };
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", invalid);

        var result = await harness.EvaluateAsync("subject.high",
            "{\"useAlertInitiativeProficiency\":true}", 77,
            "dnd2024.mechanic.initiative.roll");

        Assert.False(result.Ok);
        if (result.Run is not null) Assert.Empty(result.Run.Output.Effects);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("out-of-range")]
    [InlineData("wrong-source")]
    public async Task Alert_initiative_proficiency_rejects_missing_or_invalid_membership_effect_free(
        string stateCase)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", AlertGrantState());
        if (stateCase != "missing")
        {
            await harness.AddCharacterLevelAsync("subject.high", 5);
            var invalid = stateCase switch
            {
                "malformed" => "{}",
                "out-of-range" =>
                    "{\"classRef\":{\"entityId\":\"dnd2024.content.class.fighter.v1\"},\"level\":21}",
                _ =>
                    "{\"classRef\":{\"entityId\":\"content.extension.class.fighter.v1\"},\"level\":5}"
            };
            await harness.ReplaceClassMembershipRawAsync("subject.high", invalid);
        }

        var result = await harness.EvaluateAsync("subject.high",
            "{\"useAlertInitiativeProficiency\":true}", 77,
            "dnd2024.mechanic.initiative.roll");

        Assert.False(result.Ok);
        if (result.Run is not null) Assert.Empty(result.Run.Output.Effects);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("{\"useAlertInitiativeProficiency\":1}")]
    [InlineData("{\"useAlertInitiativeProficiency\":true,\"bonus\":3}")]
    public async Task Alert_initiative_proficiency_rejects_non_boolean_or_extra_input(
        string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterLevelAsync("subject.high", 5);
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", AlertGrantState());

        var result = await harness.EvaluateAsync("subject.high", input, 77,
            "dnd2024.mechanic.initiative.roll");

        Assert.False(result.Ok);
        if (result.Run is not null) Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Criminal_creation_grants_usable_alert_and_leaves_only_initiative_swap_pending()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.alert.criminal";
        const string input =
            "{\"characterId\":\"actor.alert.criminal\",\"name\":\"Alert Criminal\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"dex\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1", "dnd2024.content.class.fighter.v1",
            "dnd2024.content.background.criminal.v1");

        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3d1a00000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        using var entitlements = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.character.feature-entitlements"))!.ValueJson);
        var alertEntitlement = Assert.Single(
            entitlements.RootElement.GetProperty("entitlements").EnumerateArray(),
            value => value.GetProperty("featureRef").GetProperty("entityId").GetString()
                == "dnd2024.feat.alert");
        Assert.Equal("origin-feat", alertEntitlement.GetProperty("grantKind").GetString());
        Assert.Equal("default", alertEntitlement.GetProperty("configurationKey").GetString());

        using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.character-creation-record"))!.ValueJson);
        var alertPending = record.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray().Where(value => value.GetProperty("ownerDefinitionId").GetString()
                == "dnd2024.feat.alert").ToArray();
        Assert.Equal("behavior:initiative-swap",
            Assert.Single(alertPending).GetProperty("entitlementKey").GetString());

        var initiative = await harness.EvaluateAsync(actorId,
            "{\"useAlertInitiativeProficiency\":true}", 77,
            "dnd2024.mechanic.initiative.roll");
        Assert.True(initiative.Ok, initiative.Run?.Error);
        using var initiativeData = JsonDocument.Parse(initiative.Run!.Output.Data);
        var evidence = initiativeData.RootElement.GetProperty("alertInitiativeProficiency");
        Assert.True(evidence.GetProperty("used").GetBoolean());
        Assert.Equal(2, evidence.GetProperty("bonus").GetInt32());
        Assert.Empty(initiative.Run.Output.Effects);
    }

    [Fact]
    public async Task Encounter_initiative_composes_alert_adjustment_and_preserves_rest_interruption()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentHitPoints: 10, currentMinute: 100);
        await harness.AddEncounterFixturesAsync();
        await harness.AddCharacterLevelAsync("subject.high", 5);
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", AlertGrantState());
        var restStarted = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", RestBeginRoles(), "{\"kind\":\"short\"}", 0,
            "cc3d1a00000000000000000000000001"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, restStarted.Disposition);

        const long seed = 77;
        var high = await harness.EvaluateAsync("subject.high",
            "{\"useAlertInitiativeProficiency\":true}", DeriveSeed(seed, 0),
            "dnd2024.mechanic.initiative.roll");
        var low = await harness.EvaluateAsync("subject.low", "{}", DeriveSeed(seed, 1),
            "dnd2024.mechanic.initiative.roll");
        Assert.True(high.Ok, high.Run?.Error);
        Assert.True(low.Ok, low.Run?.Error);
        var highCount = Initiative(high);
        var lowCount = Initiative(low);
        var expectedOrder = highCount >= lowCount
            ? new[] { "subject.high", "subject.low" }
            : new[] { "subject.low", "subject.high" };
        var tieDecisions = highCount == lowCount
            ? new[] { new[] { "subject.high", "subject.low" } }
            : [];
        var input = JsonSerializer.Serialize(new
        {
            participants = new Dictionary<string, object>
            {
                ["subject.high"] = new { useAlertInitiativeProficiency = true },
                ["subject.low"] = new { }
            },
            participationIds = EncounterParticipationIds(),
            tieDecisions
        });
        var encounterRoles = new Dictionary<string, string>
        {
            ["encounter"] = "encounter.fixture"
        };

        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles, input, seed);
        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
        using (var data = JsonDocument.Parse(evaluated.Run!.Output.Data))
        {
            Assert.Equal(expectedOrder, data.RootElement.GetProperty("order").EnumerateArray()
                .Select(value => value.GetProperty("participantId").GetString()).ToArray());
            var interruption = Assert.Single(data.RootElement.GetProperty("restInterruptions")
                .EnumerateArray());
            Assert.Equal("subject.high", interruption.GetProperty("participantId").GetString());
            Assert.Equal("short-stopped", interruption.GetProperty("outcome").GetString());
        }

        var applied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles, input, seed,
            "cc3d1a00000000000000000000000002"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(12, applied.AppliedEffectCount);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high",
            "dnd2024.rest.world"));
    }

    [Fact]
    public async Task Fresh_host_encounter_composes_initiative_and_transacts_the_turn_lifecycle()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        var first = await harness.EvaluateAsync("subject.high", "{}", DeriveSeed(77, 0),
            "dnd2024.mechanic.initiative.roll");
        var second = await harness.EvaluateAsync("subject.low", "{}", DeriveSeed(77, 1),
            "dnd2024.mechanic.initiative.roll");
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
            participationIds = EncounterParticipationIds(),
            tieDecisions = ties
        });
        var encounter = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var preview = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.encounter-initiative-order", encounter, input, 77);
        Assert.True(preview.Ok, preview.Run?.Error ?? string.Join("; ", preview.Problems));
        var ordered = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounter, input, 77,
            "e123456789abcdef0123456789abcdef"));
        Assert.True(ordered.Successful,
            string.Join("; ", ordered.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        var startPreview = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.encounter-turn.start", encounter,
            "{\"roundId\":\"encounter.round.1\",\"turnId\":\"encounter.turn.1.0\"}", 0);
        Assert.True(startPreview.Ok, startPreview.Run?.Error ?? string.Join("; ", startPreview.Problems));
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.start", encounter, "{\"roundId\":\"encounter.round.1\",\"turnId\":\"encounter.turn.1.0\"}", 0,
            "f123456789abcdef0123456789abcdef"));
        var advanced = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.advance", encounter, "{\"roundId\":null,\"turnId\":\"encounter.turn.1.1\"}", 0,
            "0123456789abcdef0123456789abcdea"));
        var wrapped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.advance", encounter, "{\"roundId\":\"encounter.round.2\",\"turnId\":\"encounter.turn.2.0\"}", 0,
            "1123456789abcdef0123456789abcdea"));
        var ended = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.end", encounter, "{}", 0,
            "2123456789abcdef0123456789abcdea"));

        Assert.True(started.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", started.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, advanced.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, wrapped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, ended.Disposition);
        Assert.Equal(10, ordered.AppliedEffectCount);
        Assert.Equal(10, started.AppliedEffectCount);
        Assert.Equal(8, advanced.AppliedEffectCount);
        Assert.Equal(14, wrapped.AppliedEffectCount);
        Assert.Equal(4, ended.AppliedEffectCount);
        var finalRound = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.round.2", "dnd2024.encounter.round");
        using var roundJson = JsonDocument.Parse(finalRound!.ValueJson);
        Assert.Equal("complete", roundJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, roundJson.RootElement.GetProperty("number").GetInt32());
        var finalTurn = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.turn.2.0", "dnd2024.encounter.turn");
        using var turnJson = JsonDocument.Parse(finalTurn!.ValueJson);
        Assert.Equal("complete", turnJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, turnJson.RootElement.GetProperty("ordinal").GetInt32());
        Assert.Null(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "encounter.fixture", "encounter.turn.2.0",
            "dnd2024.encounter.active-turn"));
    }

    [Fact]
    public async Task Encounter_initiative_rolls_back_every_participation_entity_component_and_link_on_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddEncounterFixturesAsync();
        var (input, seed) = await EncounterOrderWithHighFirstAsync(harness);
        var roles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", roles, input, seed,
            "d123456789abcdef0123456789abcdef"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        foreach (var participationId in EncounterParticipationIds().Values)
        {
            Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, participationId));
            Assert.Null(await harness.Edges.GetRelationshipAsync(
                DndHarness.StateSpaceId, "encounter.fixture", participationId,
                "dnd2024.encounter.has-participation"));
        }
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
            ["policy"] = "dnd2024.content.rest-policy.standard.v1"
        };
        var lowRestRoles = new Dictionary<string, string>
        {
            ["creature"] = "subject.low", ["world"] = "world.rest.fixture",
            ["policy"] = "dnd2024.content.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", highRestRoles, "{\"kind\":\"short\"}", 0,
            "39400000000000000000000000000000"));
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", lowRestRoles, "{\"kind\":\"long\"}", 0,
            "39400000000000000000000000000001"));
        var (input, seed) = await EncounterOrderWithHighFirstAsync(harness);
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };

        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles, input, seed);
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles, input, seed,
            "39400000000000000000000000000002");
        var applied = await harness.Runner.RunAsync(request);
        var replayed = await harness.Runner.RunAsync(request);

        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
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
        Assert.Equal(13, applied.AppliedEffectCount);
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
            "dnd2024.mechanic.rest.begin", restRoles, "{\"kind\":\"short\"}", 0,
            "39500000000000000000000000000000"));
        await readyHarness.Runner.RunAsync(readyHarness.ActionForRoles(
            "dnd2024.mechanic.rest.progress", restRoles,
            "{\"activity\":\"light\",\"minutes\":60}", 0,
            "39500000000000000000000000000001"));
        var readyBefore = await readyHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var (readyInput, readySeed) = await EncounterOrderWithHighFirstAsync(readyHarness);
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var readyOrder = await readyHarness.Runner.RunAsync(readyHarness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles, readyInput, readySeed,
            "39500000000000000000000000000002"));
        var readyAfter = await readyHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, readyOrder.Disposition);
        Assert.Equal(10, readyOrder.AppliedEffectCount);
        Assert.Equal(readyBefore!.Revision, readyAfter!.Revision);
        Assert.Equal(readyBefore.ValueJson, readyAfter.ValueJson);

        await using var corruptHarness = await DndHarness.CreateAsync();
        await corruptHarness.AddRestBeginFixturesAsync(currentHitPoints: 10, currentMinute: 100);
        await corruptHarness.AddEncounterFixturesAsync();
        await corruptHarness.Runner.RunAsync(corruptHarness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", RestBeginRoles(), "{\"kind\":\"short\"}", 0,
            "39600000000000000000000000000000"));
        var (corruptInput, corruptSeed) = await EncounterOrderWithHighFirstAsync(corruptHarness);
        Assert.True(await corruptHarness.Edges.RemoveRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high",
            "dnd2024.rest.world", 1));
        var failed = await corruptHarness.Runner.RunAsync(corruptHarness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles,
            corruptInput, corruptSeed, "39600000000000000000000000000001"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await corruptHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.participation.high", "dnd2024.encounter.participation"));
        var unchanged = await corruptHarness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        Assert.Equal(1, unchanged!.Revision);
    }

    [Fact]
    public async Task Fresh_host_combat_primitives_resolve_against_authoritative_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCombatFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture", ["target"] = "target.fixture"
        };
        var attack = await harness.EvaluateRolesAsync("dnd2024.mechanic.weapon-attack", roles, "{\"ability\":\"str\"}", 77);
        var damage = await harness.EvaluateRolesAsync("dnd2024.mechanic.weapon-damage.roll",
            new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
                ["activity"] = "activity.weapon.fixture"
            },
            "{\"ability\":\"str\",\"critical\":false}", 77);
        Assert.True(attack.Ok, attack.Run?.Error ?? string.Join("; ", attack.Problems));
        Assert.True(damage.Ok, damage.Run?.Error ?? string.Join("; ", damage.Problems));
        Assert.Contains("\"hit\":true", attack.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(damage.Run!.Output.Effects);
    }

    [Fact]
    public async Task Fresh_host_slice_12_composes_play_replay_and_unchanged_failure()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        await harness.AddCombatFixturesAsync();
        const string extensionPath =
            "catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/dnd2024.extension.legacy-equipment.item.hempen-rope-50-foot.v1.json";
        Assert.DoesNotContain(extensionPath, harness.ActiveSourcePaths);

        var first = await harness.EvaluateAsync("subject.high", "{}", DeriveSeed(120, 0),
            "dnd2024.mechanic.initiative.roll");
        var second = await harness.EvaluateAsync("subject.low", "{}", DeriveSeed(120, 1),
            "dnd2024.mechanic.initiative.roll");
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
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles,
            JsonSerializer.Serialize(new
            {
                participants = new Dictionary<string, object>
                {
                    ["subject.high"] = new(), ["subject.low"] = new()
                },
                participationIds = EncounterParticipationIds(),
                tieDecisions = ties
            }), 120, "12000000000000000000000000000001"));
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.start", encounterRoles, "{\"roundId\":\"encounter.round.1\",\"turnId\":\"encounter.turn.1.0\"}", 0,
            "12000000000000000000000000000002"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, ordered.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, started.Disposition);

        var granted = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "target.fixture",
            "{\"mode\":\"grant\",\"amount\":2}", 0,
            "12000000000000000000000000000003"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, granted.Disposition);

        var combatRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture",
            ["target"] = "target.fixture"
        };
        var damageRequest = harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", combatRoles,
            "{\"ability\":\"str\",\"critical\":false}", 120,
            "12000000000000000000000000000004");
        var damaged = await harness.Runner.RunAsync(damageRequest);
        var afterDamage = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
        var replayed = await harness.Runner.RunAsync(damageRequest);
        var afterReplay = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, damaged.Disposition);
        Assert.Equal(2, damaged.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        Assert.Equal(afterDamage!.Revision, afterReplay!.Revision);
        Assert.Equal(afterDamage.ValueJson, afterReplay.ValueJson);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.temporary-hit-points"));

        var healed = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.healing.apply", "target.fixture", "{\"amount\":3}", 0,
            "12000000000000000000000000000005"));
        var afterHealing = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, healed.Disposition);
        Assert.Equal(1, healed.AppliedEffectCount);
        Assert.True(afterHealing!.Revision > afterReplay.Revision);

        await harness.AddDamageTargetAsync("target.slice12.corrupt", 20, 20);
        await harness.AddApplicationComponentAsync("target.slice12.corrupt",
            "dnd2024.creature.temporary-hit-points",
            "{\"amount\":1,\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.slice12.corrupt", "dnd2024.creature.temporary-hit-points", "{}");
        var corruptBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.slice12.corrupt", "dnd2024.creature.hit-points");
        combatRoles["target"] = "target.slice12.corrupt";
        var rejected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", combatRoles,
            "{\"ability\":\"str\",\"critical\":false}", 120,
            "12000000000000000000000000000006"));
        var corruptAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.slice12.corrupt", "dnd2024.creature.hit-points");
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, rejected.Disposition);
        Assert.Equal(corruptBefore!.Revision, corruptAfter!.Revision);
        Assert.Equal(corruptBefore.ValueJson, corruptAfter.ValueJson);
    }

    [Fact]
    public async Task Combat_writers_commit_closed_authoritative_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, "weapon.recorder", "Recorder weapon");
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, "activity.weapon.recorder", "Recorder attack");
        await harness.AddApplicationComponentAsync("activity.weapon.recorder", "dnd2024.core.version",
            "{\"revision\":1,\"status\":\"active\"}");
        var hitPoints = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.hit-points.write", "subject.high", "{\"mode\":\"record\",\"current\":14,\"maximum\":14}", 0,
            "4123456789abcdef0123456789abcdea"));
        var proficiencies = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.weapon-proficiencies.write", "subject.high", "{\"mode\":\"record\",\"categories\":[\"simple\"]}", 0,
            "5123456789abcdef0123456789abcdea"));
        var profile = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-profile.write", new Dictionary<string, string>
            {
                ["weapon"] = "weapon.recorder", ["activity"] = "activity.weapon.recorder"
            },
            "{\"mode\":\"record\",\"categoryId\":\"dnd2024.equipment.weapon-category.simple\",\"attackMode\":\"melee\",\"abilityIds\":[\"dnd2024.vocabulary.ability.strength\",\"dnd2024.vocabulary.ability.dexterity\"],\"damage\":{\"kind\":\"dice\",\"count\":1,\"dieId\":\"dnd2024.vocabulary.die.d4\",\"typeId\":\"dnd2024.vocabulary.damage-type.piercing\"},\"range\":{\"normalFeet\":5}}",
            0, "6123456789abcdef0123456789abcdea"));

        Assert.All([hitPoints, proficiencies], result =>
        {
            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
            Assert.Equal(1, result.AppliedEffectCount);
        });
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, profile.Disposition);
        Assert.Equal(6, profile.AppliedEffectCount);
        var storedHitPoints = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points");
        var storedWeapon = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "weapon.recorder", "dnd2024.item.weapon");
        var storedAttack = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "activity.weapon.recorder", "dnd2024.activity.attack");
        Assert.Contains("\"current\":14", storedHitPoints!.ValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceRef", storedHitPoints.ValueJson, StringComparison.Ordinal);
        Assert.Contains("dnd2024.equipment.weapon-category.simple", storedWeapon!.ValueJson,
            StringComparison.Ordinal);
        Assert.Contains("dnd2024.vocabulary.ability.strength", storedAttack!.ValueJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weapon_proficiency_writer_records_current_state_replays_and_upgrades_legacy_state()
    {
        await using (var recordHarness = await DndHarness.CreateAsync())
        {
            var action = recordHarness.ActionFor(
                "dnd2024.mechanic.weapon-proficiencies.write", "subject.high",
                "{\"mode\":\"record\",\"categories\":[\"simple\"]}", 0,
                "cc3e3000000000000000000000000001");
            var recorded = await recordHarness.Runner.RunAsync(action);
            var replay = await recordHarness.Runner.RunAsync(action);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            var state = await recordHarness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");
            using var document = JsonDocument.Parse(state!.ValueJson);
            Assert.Equal(new[] { "simple" }, ReadWeaponCategoryIds(document.RootElement).ToArray());
            Assert.Empty(ReadWeaponPropertyIds(document.RootElement));
            Assert.Contains("weapon", document.RootElement.GetProperty("recordedFamilies")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(1, state.Revision);
        }

        await using (var upgradeHarness = await DndHarness.CreateAsync())
        {
            await upgradeHarness.AddCombatFixturesAsync();
            var corrected = await upgradeHarness.Runner.RunAsync(upgradeHarness.ActionFor(
                "dnd2024.mechanic.weapon-proficiencies.write", "subject.high",
                "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"restrictedMartialProperties\":[\"light\",\"finesse\"]}",
                0, "cc3e3000000000000000000000000002"));

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
            var state = await upgradeHarness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");
            using var document = JsonDocument.Parse(state!.ValueJson);
            Assert.Equal(new[] { "simple" }, ReadWeaponCategoryIds(document.RootElement).ToArray());
            Assert.Equal(new[] { "finesse", "light" },
                ReadWeaponPropertyIds(document.RootElement).ToArray());
            Assert.Equal(2, state.Revision);
        }
    }

    [Theory]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"simple\"],\"restrictedMartialProperties\":[\"heavy\"]}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"simple\"],\"restrictedMartialProperties\":[\"light\",\"light\"]}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"simple\",\"martial\"],\"restrictedMartialProperties\":[\"light\"]}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"simple\"],\"restrictedMartialProperties\":\"light\"}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"simple\"],\"restrictedMartialProperties\":[],\"extra\":true}")]
    public async Task Weapon_proficiency_writer_rejects_invalid_redundant_or_extra_state_unchanged(
        string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        var result = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.weapon-proficiencies.write", "subject.high", input, 0,
            "cc3e3000000000000000000000000003"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies"));
    }

    [Fact]
    public async Task Weapon_proficiency_writer_rejects_corrupt_prior_state_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCombatFixturesAsync();
        const string corrupt =
            "{\"categories\":[\"simple\"],\"restrictedMartialProperties\":[\"heavy\"],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Weapons > Weapon Proficiency\"}}";
        await harness.ReplaceCoreComponentRawAsync(
            "subject.high", "dnd2024.creature.proficiencies", corrupt);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");

        var result = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.weapon-proficiencies.write", "subject.high",
            "{\"mode\":\"correct\",\"categories\":[\"simple\"],\"restrictedMartialProperties\":[]}",
            0, "cc3e3000000000000000000000000004"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        var state = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");
        Assert.Equal(before!.ValueJson, state!.ValueJson);
        Assert.Equal(before.Revision, state.Revision);
    }

    [Fact]
    public async Task Unified_proficiency_schema_accepts_ranked_membership_and_rejects_legacy_or_malformed_state()
    {
        var schema = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "catalog",
            "applications", "dnd2024", "components",
            "dnd2024.creature.proficiencies.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        const string entry =
            "{\"rankRef\":{\"entityId\":\"dnd2024.vocabulary.proficiency-rank.proficiency\"},\"sourceRefs\":[{\"entityId\":\"dnd2024.source.srd-5.2.1\"}]}";

        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(compilation.ProfileId,
            compilation.NormalizedSchema,
            "{\"entries\":{\"dnd2024.equipment.weapon-category.simple\":" + entry + "},\"recordedFamilies\":[\"weapon\"]}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(compilation.ProfileId,
            compilation.NormalizedSchema,
            "{\"categories\":[\"simple\"]}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(compilation.ProfileId,
            compilation.NormalizedSchema,
            "{\"entries\":{\"dnd2024.equipment.weapon-category.simple\":{\"rankRef\":{\"entityId\":\"invalid\"},\"sourceRefs\":[{\"entityId\":\"dnd2024.source.srd-5.2.1\"}]}},\"recordedFamilies\":[\"weapon\"]}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(compilation.ProfileId,
            compilation.NormalizedSchema,
            "{\"entries\":{},\"recordedFamilies\":[\"weapon\",\"weapon\"]}").Status);
    }

    [Fact]
    public async Task Proficiency_state_and_derived_level_use_the_activated_action_path()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterLevelAsync("subject.high", 5);
        var level = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-level.read");
        var skills = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.skill-proficiencies.record", "subject.high", "{\"skills\":[\"stealth\",\"athletics\"]}", 0,
            "b123456789abcdef0123456789abcdef"));

        Assert.True(level.Ok, level.Run?.Error);
        Assert.Contains("\"totalLevel\":5", level.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(level.Run.Output.Effects);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, skills.Disposition);
        Assert.Equal(1, skills.AppliedEffectCount);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");
        using var state = JsonDocument.Parse(stored!.ValueJson);
        Assert.Equal(new[] { "athletics", "stealth" }, ReadSkillIds(state.RootElement).ToArray());
    }

    [Fact]
    public async Task Character_level_derives_multiclass_total_and_rejects_invalid_aggregates()
    {
        await using var harness = await DndHarness.CreateAsync();
        var absent = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.character-level.read");
        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Contains("\"present\":false", absent.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(absent.Run.Output.Effects);

        await harness.AddClassMembershipAsync("subject.high", "fighter",
            "dnd2024.content.class.fighter.v1", 12);
        await harness.AddClassMembershipAsync("subject.high", "wizard",
            "dnd2024.content.class.wizard.v1", 8);
        var total = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-level.read");
        Assert.True(total.Ok, total.Run?.Error);
        using (var data = JsonDocument.Parse(total.Run!.Output.Data))
        {
            Assert.Equal(20, data.RootElement.GetProperty("totalLevel").GetInt32());
            Assert.Equal(6, data.RootElement.GetProperty("proficiencyBonus").GetInt32());
            Assert.Equal(2, data.RootElement.GetProperty("membershipCount").GetInt32());
        }
        Assert.Empty(total.Run.Output.Effects);

        await harness.AddClassMembershipAsync("subject.high", "rogue",
            "dnd2024.content.class.rogue.v1", 1);
        var overflow = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-level.read");
        Assert.False(overflow.Ok);
        Assert.True(overflow.Run is null || overflow.Run.Output.Effects.Count == 0);

        await harness.AddClassMembershipAsync("subject.low", "fighter-a",
            "dnd2024.content.class.fighter.v1", 1);
        await harness.AddClassMembershipAsync("subject.low", "fighter-b",
            "dnd2024.content.class.fighter.v1", 1);
        var duplicate = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.character-level.read");
        Assert.False(duplicate.Ok);
        Assert.True(duplicate.Run is null || duplicate.Run.Output.Effects.Count == 0);
    }

    [Fact]
    public async Task Armor_class_is_derived_from_selected_source_and_rejects_unknown_sources()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddApplicationComponentAsync("subject.high", "dnd2024.creature.defenses",
            "{\"armorClassSource\":{\"entityId\":\"dnd2024.content.defense.unarmored.v1\"},\"damageResponses\":[]}");

        var derived = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.armor-class.read");
        Assert.True(derived.Ok, derived.Run?.Error);
        using (var data = JsonDocument.Parse(derived.Run!.Output.Data))
            Assert.Equal(10, data.RootElement.GetProperty("armorClass").GetInt32());
        Assert.Empty(derived.Run.Output.Effects);

        await harness.ReplaceApplicationComponentRawAsync("subject.high", "dnd2024.creature.defenses",
            "{\"armorClassSource\":{\"entityId\":\"dnd2024.content.defense.unknown.v1\"},\"damageResponses\":[]}");
        var rejected = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.armor-class.read");
        Assert.False(rejected.Ok);
        Assert.True(rejected.Run is null || rejected.Run.Output.Effects.Count == 0);
    }

    [Fact]
    public async Task Monk_level_one_rules_activate_replay_attack_and_fail_closed_for_equipment_or_stale_fingerprints()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", JsonSerializer.Serialize(new
            {
                entitlements = new object[]
                {
                    new
                    {
                        featureRef = new { entityId = "dnd2024.content.feature.monk.martial-arts.v1" },
                        grantedByRef = new { entityId = "dnd2024.content.class.monk.v1" },
                        grantKind = "class-feature", classLevel = 1,
                        sourceRef = new { sourceId = "dnd2024.source.srd-5.2.1", locator = "Classes > Monk > Martial Arts" }
                    },
                    new
                    {
                        featureRef = new { entityId = "dnd2024.content.feature.monk.unarmored-defense.v1" },
                        grantedByRef = new { entityId = "dnd2024.content.class.monk.v1" },
                        grantKind = "class-feature", classLevel = 1,
                        sourceRef = new { sourceId = "dnd2024.source.srd-5.2.1", locator = "Classes > Monk > Unarmored Defense" }
                    }
                }
            }));
        await harness.AddClassMembershipAsync("subject.high", "monk",
            "dnd2024.content.class.monk.v1", 1);
        await harness.AddApplicationComponentAsync("subject.high", "dnd2024.creature.defenses",
            "{\"armorClassSource\":{\"entityId\":\"dnd2024.content.defense.unarmored.v1\"},\"damageResponses\":[]}");
        await harness.AddApplicationComponentAsync("subject.low", "dnd2024.creature.defenses",
            "{\"armorClassSource\":{\"entityId\":\"dnd2024.content.defense.unarmored.v1\"},\"damageResponses\":[]}");

        var derived = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.armor-class.monk-unarmored");
        Assert.True(derived.Ok, derived.Run?.Error);
        using (var data = JsonDocument.Parse(derived.Run!.Output.Data))
        {
            Assert.True(data.RootElement.GetProperty("eligible").GetBoolean());
            Assert.True(data.RootElement.GetProperty("armorClass").GetInt32() >= 10);
        }

        var action = harness.ActionFor("dnd2024.mechanic.monk.unarmored-defense.activate",
            "subject.high", "{}", 0, "8123456789abcdef0123456789abcdea");
        var activated = await harness.Runner.RunAsync(action);
        var replayed = await harness.Runner.RunAsync(action);
        var stale = await harness.Runner.RunAsync(action with
        {
            ContentFingerprint = new string('0', 64),
            ExecutionIdentity = action.ExecutionIdentity with { OperationId = "8123456789abcdef0123456789abcdeb" }
        });
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, activated.Disposition);
        Assert.Equal(1, activated.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Stale, stale.Disposition);
        Assert.NotNull(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "subject.high", "dnd2024.character.monk-unarmored-defense"));
        var defenses = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "subject.high", "dnd2024.creature.defenses");
        Assert.NotNull(defenses);
        using (var defenseState = JsonDocument.Parse(defenses!.ValueJson))
            Assert.Equal("dnd2024.content.defense.unarmored.v1",
                defenseState.RootElement.GetProperty("armorClassSource").GetProperty("entityId").GetString());

        var selected = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.armor-class.read");
        Assert.True(selected.Ok, selected.Run?.Error ?? string.Join("; ", selected.Problems));
        var attack = await harness.EvaluateRolesAsync("dnd2024.mechanic.monk.martial-arts.attack",
            new Dictionary<string, string> { ["subject"] = "subject.high", ["target"] = "subject.low" },
            "{\"ability\":\"dex\",\"economy\":\"action\"}", 77);
        Assert.True(attack.Ok, attack.Run?.Error ?? string.Join("; ", attack.Problems));
        using (var attackData = JsonDocument.Parse(attack.Run!.Output.Data))
            Assert.Equal("d6", attackData.RootElement.GetProperty("damageDie").GetString());

        const string armorDefinitionId = "dnd2024.fixture.armor.monk-ineligible";
        const string armorItemId = "item.fixture.armor.monk-ineligible";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, armorDefinitionId, "Fixture armor");
        await harness.AddApplicationComponentAsync(armorDefinitionId, "dnd2024.item.armor",
            "{\"category\":{\"entityId\":\"dnd2024.equipment.armor-category.light\"},\"armorClass\":{\"mechanicId\":\"dnd2024.mechanic.armor-class.unarmored\",\"inputBindings\":{\"base\":11}}}");
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, armorItemId, "Worn fixture armor");
        await harness.AddApplicationComponentAsync(armorItemId, "dnd2024.core.definition-link",
            JsonSerializer.Serialize(new { definition = new { entityId = armorDefinitionId } }));
        await harness.AddApplicationComponentAsync(armorItemId, "dnd2024.item.equipment",
            JsonSerializer.Serialize(new
            {
                equippedBy = new { entityId = "subject.high" },
                slots = new[] { new { entityId = "dnd2024.equipment-slot.body" } }
            }));
        await harness.Edges.MoveContainmentAsync(DndHarness.StateSpaceId, armorItemId,
            "subject.high", "carried", 0);
        var ineligible = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.armor-class.monk-unarmored");
        Assert.True(ineligible.Ok, ineligible.Run?.Error);
        using (var data = JsonDocument.Parse(ineligible.Run!.Output.Data))
        {
            Assert.False(data.RootElement.GetProperty("eligible").GetBoolean());
            Assert.Contains(data.RootElement.GetProperty("ineligibilityReasons").EnumerateArray(),
                value => value.GetString() == "armor-equipped");
        }

        var absentExtension = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.species-origin-traits.read");
        Assert.True(absentExtension.Ok, absentExtension.Run?.Error);
        using (var data = JsonDocument.Parse(absentExtension.Run!.Output.Data))
        {
            Assert.False(data.RootElement.GetProperty("known").GetBoolean());
            Assert.Equal("origin-unavailable", data.RootElement.GetProperty("problem").GetString());
        }
    }

    [Fact]
    public async Task Magic_initiate_requires_every_reviewed_choice_and_replays_one_complete_configuration()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceApplicationComponentRawAsync("subject.high",
            "dnd2024.character.feature-entitlements", JsonSerializer.Serialize(new
            {
                entitlements = new object[]
                {
                    new
                    {
                        featureRef = new { entityId = "dnd2024.content.feature.magic-initiate.v1" },
                        grantedByRef = new { entityId = "dnd2024.content.background.acolyte.v1" },
                        grantKind = "origin-feat", configurationKey = "cleric",
                        sourceRef = new { sourceId = "dnd2024.source.srd-5.2.1", locator = "Feats > Magic Initiate" }
                    }
                }
            }));
        foreach (var (id, level) in new[]
                 {
                     ("spell.fixture.cantrip-a", 0), ("spell.fixture.cantrip-b", 0),
                     ("spell.fixture.level-one", 1)
                 })
        {
            await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, id, id);
            await harness.AddApplicationComponentAsync(id, "dnd2024.spellcasting.spell",
                JsonSerializer.Serialize(new
                {
                    level,
                    school = new { entityId = "dnd2024.spell-school.divination" },
                    castingActivity = new { entityId = "dnd2024.shared.action.magic" },
                    ritual = false
                }));
            await harness.AddApplicationComponentAsync(id, "dnd2024.spellcasting.spell-list-membership",
                "{\"lists\":[{\"entityId\":\"dnd2024.spell-list.cleric\"}]}");
        }

        var incomplete = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.magic-initiate.configure",
            new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["cantripOne"] = "spell.fixture.cantrip-a",
                ["cantripTwo"] = "spell.fixture.cantrip-b"
            }, "{\"mode\":\"record\",\"spellcastingAbility\":\"wis\"}", 0,
            "8223456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, incomplete.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId, "subject.high",
            "dnd2024.character.magic-initiate-configuration"));

        var action = harness.ActionForRoles("dnd2024.mechanic.magic-initiate.configure",
            new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["cantripOne"] = "spell.fixture.cantrip-a",
                ["cantripTwo"] = "spell.fixture.cantrip-b", ["levelOneSpell"] = "spell.fixture.level-one"
            }, "{\"mode\":\"record\",\"spellcastingAbility\":\"wis\"}", 0,
            "8223456789abcdef0123456789abcdeb");
        var recorded = await harness.Runner.RunAsync(action);
        var replayed = await harness.Runner.RunAsync(action);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(1, recorded.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var stored = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId, "subject.high",
            "dnd2024.character.magic-initiate-configuration");
        Assert.NotNull(stored);
        using var configuration = JsonDocument.Parse(stored.ValueJson);
        Assert.Equal("wis", configuration.RootElement.GetProperty("spellcastingAbility").GetString());
        Assert.Equal("spell.fixture.level-one",
            configuration.RootElement.GetProperty("levelOneSpellRef").GetProperty("entityId").GetString());
        Assert.Equal(2, configuration.RootElement.GetProperty("cantripRefs").GetArrayLength());
    }

    [Fact]
    public async Task Speed_writer_records_corrects_and_replays_canonical_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string recordedInput =
            "{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":15,\"flyFeet\":0,\"swimFeet\":20}";
        var action = harness.ActionFor("dnd2024.mechanic.speed.write", "subject.high", recordedInput, 0,
            "7123456789abcdef0123456789abcdea");

        var recorded = await harness.Runner.RunAsync(action);
        var replayed = await harness.Runner.RunAsync(action);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(1, recorded.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.movement");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Revision);
        Assert.Contains("\"speeds\"", stored.ValueJson, StringComparison.Ordinal);
        Assert.Contains("dnd2024.vocabulary.movement-mode.walk", stored.ValueJson, StringComparison.Ordinal);

        var firstRead = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.speed.read");
        var secondRead = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.speed.read");
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
            "dnd2024.mechanic.speed.write", "subject.high", correctedInput, 0,
            "8123456789abcdef0123456789abcdea"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(1, corrected.AppliedEffectCount);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.movement");
        Assert.Equal(2, stored!.Revision);
        Assert.Contains("dnd2024.vocabulary.movement-mode.walk", stored.ValueJson, StringComparison.Ordinal);
        Assert.Contains("dnd2024.vocabulary.distance-unit.meter", stored.ValueJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Speed_family_rejects_invalid_writes_and_preserves_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        var absent = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.speed.read");
        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Empty(absent.Run!.Output.Effects);
        Assert.Contains("\"problem\":\"absent\"", absent.Run.Output.Data, StringComparison.Ordinal);

        var invalidRecord = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.speed.write", "subject.low",
            "{\"mode\":\"record\",\"walkFeet\":0,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}",
            0, "9123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalidRecord.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.creature.movement"));

        const string valid =
            "{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}";
        var recorded = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.speed.write", "subject.high", valid, 0,
            "a123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.movement");

        var duplicate = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.speed.write", "subject.high", valid, 0,
            "b123456789abcdef0123456789abcdea"));
        var invalidCorrection = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.speed.write", "subject.high",
            "{\"mode\":\"correct\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":7,\"swimFeet\":0}",
            0, "c123456789abcdef0123456789abcdea"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalidCorrection.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.movement");
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
            "dnd2024.mechanic.speed.write", "subject.high", valid, 0,
            "d123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);

        var source = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "catalog", "applications", "dnd2024", "mechanics", "movement",
            "dnd2024.mechanic.speed.read.js"));
        var malformed = await new JintMechanicEngine().RunAsync(source, new MechanicProjection
        {
            Seed = 0,
            Input = "{}",
            Roles = new()
            {
                ["subject"] = new("subject.high", "subject.high", new Dictionary<string, string>
                {
                    ["dnd2024.creature.movement"] = "{"
                })
            }
        }, ExecutionLimits.Default);
        Assert.True(malformed.Ok, malformed.Error);
        Assert.Empty(malformed.Output.Effects);
        Assert.Contains("\"problem\":\"malformed\"", malformed.Output.Data, StringComparison.Ordinal);

        await harness.ReplaceSpeedRawAsync("subject.high",
            "{\"walkFeet\":0,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}");
        var invalid = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.speed.read");
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
            "dnd2024.mechanic.speed.write");

        Assert.True(result.Evaluated);
        Assert.Equal(expectedOk, result.Run!.Ok);
        if (expectedOk) Assert.Single(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Turn_budget_writer_records_corrects_and_replays_exact_canonical_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddExplicitTurnAsync();
        const string recordInput =
            "{\"mode\":\"record\",\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}";
        var roles = new Dictionary<string, string> { ["turn"] = "turn.fixture" };
        var action = harness.ActionForRoles("dnd2024.mechanic.turn-budget.write", roles, recordInput, 0,
            "e123456789abcdef0123456789abcdea");

        var recorded = await harness.Runner.RunAsync(action);
        var replayed = await harness.Runner.RunAsync(action);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(1, recorded.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "turn.fixture", "dnd2024.combat.turn-budget");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Revision);
        Assert.Equal("{\"turn\":{\"entityId\":\"turn.fixture\"},\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}", stored.ValueJson);

        const string correctInput =
            "{\"mode\":\"correct\",\"remaining\":{\"actions\":0,\"bonusActions\":1,\"reactions\":0},\"movementSpent\":[{\"mode\":{\"entityId\":\"dnd2024.vocabulary.movement-mode.walk\"},\"distance\":{\"dimension\":\"distance\",\"value\":{\"numerator\":381,\"denominator\":125},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.meter\"}}}],\"interactionsUsed\":1}";
        var corrected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.write", roles, correctInput, 0,
            "f123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(1, corrected.AppliedEffectCount);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "turn.fixture", "dnd2024.combat.turn-budget");
        Assert.Equal(2, stored!.Revision);
        Assert.Contains("\"actions\":0", stored.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"numerator\":381", stored.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turn_budget_writer_rejects_wrong_transitions_and_preserves_exact_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddExplicitTurnAsync();
        const string recordInput =
            "{\"mode\":\"record\",\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}";
        var roles = new Dictionary<string, string> { ["turn"] = "turn.fixture" };
        var absentCorrection = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.write", roles,
            recordInput.Replace("\"record\"", "\"correct\"", StringComparison.Ordinal), 0,
            "0123456789abcdef0123456789abcdeb"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, absentCorrection.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "turn.fixture", "dnd2024.combat.turn-budget"));

        var recorded = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.write", roles, recordInput, 0,
            "1123456789abcdef0123456789abcdeb"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "turn.fixture", "dnd2024.combat.turn-budget");

        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.write", roles, recordInput, 0,
            "2123456789abcdef0123456789abcdeb"));
        await harness.ReplaceApplicationComponentRawAsync("turn.fixture", "dnd2024.combat.turn-budget",
            "{\"turn\":{\"entityId\":\"turn.fixture\"},\"remaining\":{\"actions\":-1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}");
        var invalidBytes = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "turn.fixture", "dnd2024.combat.turn-budget");
        var invalidCorrection = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.write", roles,
            recordInput.Replace("\"record\"", "\"correct\"", StringComparison.Ordinal), 0,
            "3123456789abcdef0123456789abcdeb"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalidCorrection.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "turn.fixture", "dnd2024.combat.turn-budget");
        Assert.Equal(invalidBytes!.Revision, after!.Revision);
        Assert.Equal(invalidBytes.ValueJson, after.ValueJson);
        Assert.Equal(1, before!.Revision);
    }

    [Theory]
    [InlineData("{\"mode\":\"record\",\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}", true)]
    [InlineData("{\"mode\":\"record\",\"remaining\":{\"actions\":0,\"bonusActions\":0,\"reactions\":0},\"movementSpent\":[{\"mode\":{\"entityId\":\"dnd2024.vocabulary.movement-mode.walk\"},\"distance\":{\"dimension\":\"distance\",\"value\":{\"numerator\":381,\"denominator\":125},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.meter\"}}}],\"interactionsUsed\":1}", true)]
    [InlineData("{\"mode\":\"record\",\"remaining\":{\"actions\":-1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}", false)]
    [InlineData("{\"mode\":\"record\",\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":{},\"interactionsUsed\":0}", false)]
    [InlineData("{\"mode\":\"record\",\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":-1}", false)]
    [InlineData("{\"mode\":\"record\",\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0,\"turn\":{}}", false)]
    public async Task Turn_budget_writer_enforces_closed_canonical_boundaries(string input, bool expectedOk)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddExplicitTurnAsync();

        var result = await harness.EvaluateRolesAsync("dnd2024.mechanic.turn-budget.write",
            new Dictionary<string, string> { ["turn"] = "turn.fixture" }, input, 0);

        Assert.True(result.Evaluated);
        Assert.Equal(expectedOk, result.Run!.Ok);
        if (expectedOk) Assert.Single(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Conditions_writer_records_scopes_canonicalizes_and_clears_instances()
    {
        await using var harness = await DndHarness.CreateAsync();
        var recorded = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
            "4123456789abcdef0123456789abcdeb"));
        var poisoned = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.conditions.write", "subject.high",
            "{\"mode\":\"apply\",\"conditions\":[\"poisoned\",\"prone\"]}", 0,
            "5123456789abcdef0123456789abcdeb"));
        var roles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["source"] = "subject.low"
        };
        var frightened = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.conditions.write", roles,
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
            "dnd2024.mechanic.conditions.write", roles,
            "{\"mode\":\"clear\",\"conditions\":[\"frightened\"]}", 0,
            "7123456789abcdef0123456789abcdeb"));
        var petrified = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.conditions.write", "subject.high",
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
                     "catalog/applications/dnd2024/components/dnd2024.creature.defenses.json",
                     "catalog/applications/dnd2024/mechanics/combat/dnd2024.mechanic.creature.defenses.write.md",
                     "catalog/applications/dnd2024/mechanics/combat/dnd2024.mechanic.damage.resolve.md",
                     "catalog/applications/dnd2024/procedures/combat/dnd2024.procedure.mechanic.damage-mitigation.md",
                     "catalog/applications/dnd2024/procedures/combat/dnd2024.procedure.mechanic.damage.resolve.md"
                 })
            Assert.Contains(relative, harness.ActiveSourcePaths);

        const string input =
            "{\"mode\":\"record\",\"resistances\":[\"fire\",\"acid\"],\"immunities\":[\"poison\"],\"vulnerabilities\":[\"cold\"]}";
        var request = harness.ActionFor(
            "dnd2024.mechanic.creature.defenses.write", "subject.high", input, 0,
            "aa23456789abcdef0123456789abcdea");
        var recorded = await harness.Runner.RunAsync(request);
        var replayed = await harness.Runner.RunAsync(request);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(1, recorded.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.defenses");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.Revision);
        using (var storedData = JsonDocument.Parse(stored.ValueJson))
        {
            var responses = storedData.RootElement.GetProperty("damageResponses");
            Assert.Equal(4, responses.GetArrayLength());
            Assert.Contains(responses.EnumerateArray(), entry =>
                entry.GetProperty("damageTypeRef").GetProperty("entityId").GetString()
                    == "dnd2024.vocabulary.damage-type.acid");
            Assert.Contains(responses.EnumerateArray(), entry =>
                entry.GetProperty("responseRef").GetProperty("entityId").GetString()
                    == "dnd2024.vocabulary.damage-response.immunity");
        }

        const string correctedInput =
            "{\"mode\":\"correct\",\"resistances\":[\"thunder\"],\"immunities\":[\"fire\"],\"vulnerabilities\":[]}";
        var correctionPreview = await harness.EvaluateAsync(
            "subject.high", correctedInput, 0, "dnd2024.mechanic.creature.defenses.write");
        Assert.True(correctionPreview.Ok, correctionPreview.Run?.Error);
        Assert.Contains("\"previous\":{\"resistances\":[\"acid\",\"fire\"]",
            correctionPreview.Run!.Output.Data, StringComparison.Ordinal);
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.creature.defenses.write", "subject.high",
            correctedInput,
            0, "ab23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(1, corrected.AppliedEffectCount);
        stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.defenses");
        Assert.Equal(2, stored!.Revision);
        Assert.Contains("dnd2024.vocabulary.damage-type.thunder", stored.ValueJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Damage_mitigation_profile_composes_conditions_and_distinguishes_unknown_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        var absent = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.damage.resolve",
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
                "dnd2024.mechanic.creature.defenses.write", "subject.high",
                "{\"mode\":\"record\",\"resistances\":[\"cold\"],\"immunities\":[\"poison\"],\"vulnerabilities\":[\"fire\"]}",
                0, "ac23456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "ad23456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high",
                "{\"mode\":\"apply\",\"conditions\":[\"petrified\"]}", 0,
                "ae23456789abcdef0123456789abcdea"))).Disposition);

        var first = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.damage.resolve",
            new Dictionary<string, string> { ["defender"] = "subject.high" }, "{}", 0);
        var second = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.damage.resolve",
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
            "dnd2024.mechanic.creature.defenses.write", "subject.low",
            "{\"mode\":\"record\",\"resistances\":[\"fire\",\"fire\"],\"immunities\":[],\"vulnerabilities\":[]}",
            0, "af23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.creature.defenses"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.creature.defenses.write", "subject.high",
                "{\"mode\":\"record\",\"resistances\":[\"acid\"],\"immunities\":[],\"vulnerabilities\":[]}",
                0, "ba23456789abcdef0123456789abcdea"))).Disposition);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.defenses");
        var duplicateRecord = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.creature.defenses.write", "subject.high",
            "{\"mode\":\"record\",\"resistances\":[],\"immunities\":[],\"vulnerabilities\":[]}",
            0, "bb23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicateRecord.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.defenses");
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);

        await harness.ReplaceApplicationComponentRawAsync(
            "subject.high", "dnd2024.creature.defenses",
            "{\"damageResponses\":[{\"damageTypeRef\":{\"entityId\":\"dnd2024.vocabulary.damage-type.fire\"},\"responseRef\":{\"entityId\":\"dnd2024.vocabulary.damage-response.resistance\"},\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}},{\"damageTypeRef\":{\"entityId\":\"dnd2024.vocabulary.damage-type.acid\"},\"responseRef\":{\"entityId\":\"dnd2024.vocabulary.damage-response.resistance\"},\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}]}");
        var corruptProfile = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.damage.resolve",
            new Dictionary<string, string> { ["defender"] = "subject.high" }, "{}", 0);
        Assert.False(corruptProfile.Ok);
        Assert.Empty(corruptProfile.Run!.Output.Effects);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.low", "{\"mode\":\"record\"}", 0,
                "bc23456789abcdef0123456789abcdea"))).Disposition);
        await harness.ReplaceConditionsRawAsync("subject.low", "{\"entries\":[]}");
        var corruptConditions = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.damage.resolve",
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
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.vulnerable", 100, 100,
            "{\"resistances\":[],\"immunities\":[],\"vulnerabilities\":[\"piercing\"],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.combined", 100, 100,
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[\"piercing\"],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.immune", 100, 100,
            "{\"resistances\":[\"piercing\"],\"immunities\":[\"piercing\"],\"vulnerabilities\":[\"piercing\"],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.AddDamageTargetAsync("target.petrified", 100, 100,
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "target.petrified", "{\"mode\":\"record\"}", 0,
                "bd23456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "target.petrified",
                "{\"mode\":\"apply\",\"conditions\":[\"petrified\"]}", 0,
                "be23456789abcdef0123456789abcdea"))).Disposition);

        static Dictionary<string, string> Roles(string target) => new()
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture", ["target"] = target
        };
        const string input = "{\"ability\":\"str\",\"critical\":false}";
        var normal = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.fixture"), input, 77);
        var resistant = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.resistant"), input, 77);
        var vulnerable = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.vulnerable"), input, 77);
        var combined = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.combined"), input, 77);
        var immune = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.immune"), input, 77);
        var petrified = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.petrified"), input, 77);
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
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.fixture"), input, 77,
            "bf23456789abcdef0123456789abcdea");
        var applied = await harness.Runner.RunAsync(request);
        var replayed = await harness.Runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(1, applied.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var normalHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
        Assert.Equal(2, normalHp!.Revision);
        using (var hp = JsonDocument.Parse(normalHp.ValueJson))
            Assert.Equal(20 - raw, hp.RootElement.GetProperty("current").GetInt32());

        var immuneAction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", Roles("target.immune"), input, 77,
            "ca23456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, immuneAction.Disposition);
        Assert.Equal(0, immuneAction.AppliedEffectCount);
        var immuneHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.immune", "dnd2024.creature.hit-points");
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
            "{\"resistances\":[\"acid\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.corrupt", "dnd2024.creature.defenses",
            "{\"damageResponses\":[{\"damageTypeRef\":{\"entityId\":\"dnd2024.vocabulary.damage-type.fire\"},\"responseRef\":{\"entityId\":\"dnd2024.vocabulary.damage-response.resistance\"},\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}},{\"damageTypeRef\":{\"entityId\":\"dnd2024.vocabulary.damage-type.acid\"},\"responseRef\":{\"entityId\":\"dnd2024.vocabulary.damage-response.resistance\"},\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}]}");
        var roles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture", ["target"] = "target.corrupt"
        };
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.corrupt", "dnd2024.creature.hit-points");
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", roles,
            "{\"ability\":\"str\",\"critical\":false}", 77,
            "cb23456789abcdef0123456789abcdea"));
        var injected = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", roles,
            "{\"ability\":\"str\",\"critical\":false,\"damage\":999}", 77);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.False(injected.Ok);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.corrupt", "dnd2024.creature.hit-points");
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Temporary_hit_points_are_positive_nonstacking_replayable_and_expirable()
    {
        await using var harness = await DndHarness.CreateAsync();
        var hpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points");
        var grant = harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":8}", 0,
            "d123456789abcdef0123456789abcdea");
        var granted = await harness.Runner.RunAsync(grant);
        var replayed = await harness.Runner.RunAsync(grant);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, granted.Disposition);
        Assert.Equal(1, granted.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        var buffer = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.temporary-hit-points");
        Assert.NotNull(buffer);
        Assert.Equal(1, buffer.Revision);
        Assert.Contains("\"amount\":8", buffer.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"entityId\":\"dnd2024.source.srd-5.2.1\"", buffer.ValueJson,
            StringComparison.Ordinal);

        var kept = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":12,\"onExisting\":\"keep\"}", 0,
            "e123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, kept.Disposition);
        Assert.Equal(0, kept.AppliedEffectCount);
        var afterKeep = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.temporary-hit-points");
        Assert.Equal(buffer.Revision, afterKeep!.Revision);
        Assert.Equal(buffer.ValueJson, afterKeep.ValueJson);

        var replaced = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":5,\"onExisting\":\"replace\"}", 0,
            "f123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, replaced.Disposition);
        Assert.Equal(1, replaced.AppliedEffectCount);
        buffer = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.temporary-hit-points");
        Assert.Equal(2, buffer!.Revision);
        Assert.Contains("\"amount\":5", buffer.ValueJson, StringComparison.Ordinal);

        var invalid = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"grant\",\"amount\":0,\"onExisting\":\"keep\"}", 0,
            "0123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        var afterInvalid = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.temporary-hit-points");
        Assert.Equal(buffer.Revision, afterInvalid!.Revision);
        Assert.Equal(buffer.ValueJson, afterInvalid.ValueJson);

        var expired = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"expire\"}", 0, "1123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, expired.Disposition);
        Assert.Equal(1, expired.AppliedEffectCount);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.temporary-hit-points"));
        var absentExpiry = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "subject.high",
            "{\"mode\":\"expire\"}", 0, "2123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, absentExpiry.Disposition);
        Assert.Equal(hpBefore, await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points"));
    }

    [Fact]
    public async Task Healing_clamps_preserves_temporary_hp_and_avoids_a_full_hp_write()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddDamageTargetAsync("target.healing", 3, 10);
        await harness.ReplaceCoreComponentRawAsync("target.healing",
            "dnd2024.creature.hit-points",
            "{\"current\":3,\"maximum\":10,\"maximumReduction\":2}");
        await harness.AddApplicationComponentAsync("target.healing", "dnd2024.creature.temporary-hit-points",
            "{\"amount\":8,\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}");
        var roles = new Dictionary<string, string> { ["subject"] = "target.healing" };
        var preview = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.healing.apply", roles, "{\"amount\":20}", 0);
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
            DndHarness.StateSpaceId, "target.healing", "dnd2024.creature.temporary-hit-points");
        var healed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.healing.apply", roles, "{\"amount\":4}", 0,
            "3123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, healed.Disposition);
        Assert.Equal(1, healed.AppliedEffectCount);
        var hp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.creature.hit-points");
        Assert.Contains("\"current\":7", hp!.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"maximumReduction\":2", hp.ValueJson, StringComparison.Ordinal);
        var temporaryAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.creature.temporary-hit-points");
        Assert.Equal(temporaryBefore!.Revision, temporaryAfter!.Revision);
        Assert.Equal(temporaryBefore.ValueJson, temporaryAfter.ValueJson);

        var capped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.healing.apply", roles, "{\"amount\":20}", 0,
            "4123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, capped.Disposition);
        Assert.Equal(1, capped.AppliedEffectCount);
        var fullBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.creature.hit-points");
        Assert.Contains("\"current\":10", fullBefore!.ValueJson, StringComparison.Ordinal);
        var atMaximumRequest = harness.ActionForRoles(
            "dnd2024.mechanic.healing.apply", roles, "{\"amount\":1}", 0,
            "5123456789abcdef0123456789abcdea");
        var atMaximum = await harness.Runner.RunAsync(atMaximumRequest);
        var atMaximumReplay = await harness.Runner.RunAsync(atMaximumRequest);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, atMaximum.Disposition);
        Assert.Equal(0, atMaximum.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, atMaximumReplay.Disposition);
        var fullAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.healing", "dnd2024.creature.hit-points");
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
            ["activity"] = "activity.weapon.fixture",
            ["target"] = "target.fixture"
        };
        const string input = "{\"ability\":\"str\",\"critical\":false}";
        var baseline = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", roles, input, 77);
        Assert.True(baseline.Ok, baseline.Run?.Error);
        using var baselineData = JsonDocument.Parse(baseline.Run!.Output.Data);
        var raw = baselineData.RootElement.GetProperty("damage").GetInt32();
        Assert.True(raw > 2);
        Assert.Equal(0, baselineData.RootElement.GetProperty("temporaryBefore").GetInt32());
        Assert.Equal(raw, baselineData.RootElement.GetProperty("hitPointDamage").GetInt32());

        static string Temporary(int amount) => JsonSerializer.Serialize(new
        {
            amount,
            sourceRef = new { entityId = "dnd2024.source.srd-5.2.1" }
        });
        const string mitigationLocator =
            "Playing the Game > Damage and Healing > Resistance and Vulnerability; Immunity (PDF p. 17)";
        await harness.AddDamageTargetAsync("target.temp.partial", 20, 20);
        await harness.ReplaceCoreComponentRawAsync("target.temp.partial",
            "dnd2024.creature.hit-points",
            "{\"current\":20,\"maximum\":20,\"maximumReduction\":4}");
        await harness.AddApplicationComponentAsync(
            "target.temp.partial", "dnd2024.creature.temporary-hit-points", Temporary(raw - 1));
        await harness.AddDamageTargetAsync("target.temp.exact", 20, 20);
        await harness.AddApplicationComponentAsync(
            "target.temp.exact", "dnd2024.creature.temporary-hit-points", Temporary(raw));
        await harness.AddDamageTargetAsync("target.temp.retained", 20, 20);
        await harness.AddApplicationComponentAsync(
            "target.temp.retained", "dnd2024.creature.temporary-hit-points", Temporary(raw + 1));
        await harness.AddDamageTargetAsync("target.temp.resistant", 20, 20,
            "{\"resistances\":[\"piercing\"],\"immunities\":[],\"vulnerabilities\":[],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + mitigationLocator + "\"}}");
        await harness.AddApplicationComponentAsync(
            "target.temp.resistant", "dnd2024.creature.temporary-hit-points", Temporary(1));
        await harness.AddDamageTargetAsync("target.temp.overkill", 1, 20);
        await harness.AddApplicationComponentAsync(
            "target.temp.overkill", "dnd2024.creature.temporary-hit-points", Temporary(1));

        static Dictionary<string, string> TargetRoles(string target) => new()
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture", ["target"] = target
        };
        var partial = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.partial"), input, 77);
        var exact = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.exact"), input, 77);
        var retained = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.retained"), input, 77);
        var resistant = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.resistant"), input, 77);
        var overkill = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.overkill"), input, 77);
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
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.partial"), input, 77,
            "8123456789abcdef0123456789abcdea");
        var applied = await harness.Runner.RunAsync(partialRequest);
        var replayed = await harness.Runner.RunAsync(partialRequest);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(2, applied.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replayed.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.partial", "dnd2024.creature.temporary-hit-points"));
        var partialHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.partial", "dnd2024.creature.hit-points");
        Assert.Equal(2, partialHp!.Revision);
        Assert.Contains("\"current\":19", partialHp.ValueJson, StringComparison.Ordinal);
        Assert.Contains("\"maximumReduction\":4", partialHp.ValueJson,
            StringComparison.Ordinal);

        var exactApplied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.exact"), input, 77,
            "9123456789abcdef0123456789abcdea"));
        Assert.Equal(1, exactApplied.AppliedEffectCount);
        var exactHp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.exact", "dnd2024.creature.hit-points");
        Assert.Equal(1, exactHp!.Revision);
        Assert.Contains("\"current\":20", exactHp.ValueJson, StringComparison.Ordinal);

        var retainedApplied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", TargetRoles("target.temp.retained"), input, 77,
            "a123456789abcdef0123456789abcdea"));
        Assert.Equal(1, retainedApplied.AppliedEffectCount);
        var retainedBuffer = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.retained", "dnd2024.creature.temporary-hit-points");
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
            "dnd2024.creature.temporary-hit-points",
            "{\"amount\":1,\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.temp.corrupt", "dnd2024.creature.temporary-hit-points", "{}");
        var hpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.creature.hit-points");
        var bufferBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.creature.temporary-hit-points");
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
                ["activity"] = "activity.weapon.fixture",
                ["target"] = "target.temp.corrupt"
            }, "{\"ability\":\"str\",\"critical\":false}", 77,
            "b123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        var hpAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.creature.hit-points");
        var bufferAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.temp.corrupt", "dnd2024.creature.temporary-hit-points");
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
            "dnd2024.creature.temporary-hit-points",
            "{\"amount\":8,\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}");
        await harness.ReplaceApplicationComponentRawAsync(
            "target.invalid-healing", "dnd2024.creature.temporary-hit-points", "{}");
        var corruptTemporaryBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.creature.temporary-hit-points");
        var corruptTemporary = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.temporary-hit-points.write", "target.invalid-healing",
            "{\"mode\":\"grant\",\"amount\":4,\"onExisting\":\"keep\"}", 0,
            "6123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, corruptTemporary.Disposition);
        var corruptTemporaryAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.creature.temporary-hit-points");
        Assert.Equal(corruptTemporaryBefore!.ValueJson, corruptTemporaryAfter!.ValueJson);

        await harness.ReplaceCoreComponentRawAsync("target.invalid-healing", "dnd2024.creature.hit-points",
            "{\"current\":11,\"maximum\":10}");
        var corruptHpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.creature.hit-points");
        var corruptHealing = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.healing.apply", "target.invalid-healing", "{\"amount\":4}", 0,
            "7123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, corruptHealing.Disposition);
        var corruptHpAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.invalid-healing", "dnd2024.creature.hit-points");
        Assert.Equal(corruptHpBefore!.Revision, corruptHpAfter!.Revision);
        Assert.Equal(corruptHpBefore.ValueJson, corruptHpAfter.ValueJson);
        var injected = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.healing.apply",
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
                "dnd2024.mechanic.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "9123456789abcdef0123456789abcdeb"))).Disposition);
        var preview = await harness.EvaluateAsync("subject.high", "{\"mode\":\"exhaust\",\"levels\":6}", 0,
            "dnd2024.mechanic.conditions.write");
        Assert.True(preview.Ok, preview.Run?.Error);
        Assert.Empty(preview.Run!.Output.Events);
        Assert.Contains("\"lethal\":true", preview.Run.Output.Data, StringComparison.Ordinal);

        var exhausted = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.conditions.write", "subject.high",
            "{\"mode\":\"exhaust\",\"levels\":6}", 0,
            "a123456789abcdef0123456789abcdeb"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, exhausted.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        Assert.Contains("{\"condition\":\"exhaustion\",\"level\":6}", stored!.ValueJson, StringComparison.Ordinal);

        var recovered = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.conditions.write", "subject.high",
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
            "dnd2024.mechanic.d20-test.state-effects");
        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Contains("\"conditionsKnown\":false", absent.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(absent.Run.Output.Effects);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "c123456789abcdef0123456789abcdeb"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high",
                "{\"mode\":\"apply\",\"conditions\":[\"poisoned\",\"restrained\",\"unconscious\"]}", 0,
                "d123456789abcdef0123456789abcdeb"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high",
                "{\"mode\":\"exhaust\",\"levels\":2}", 0,
                "e123456789abcdef0123456789abcdeb"))).Disposition);

        var first = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.d20-test.state-effects");
        var second = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.d20-test.state-effects");
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
                "dnd2024.mechanic.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "f123456789abcdef0123456789abcdeb"))).Disposition);
        var missingSource = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.conditions.write", "subject.high",
            "{\"mode\":\"apply\",\"conditions\":[\"grappled\"]}", 0,
            "0123456789abcdef0123456789abcdec"));
        var selfRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["source"] = "subject.high"
        };
        var selfSource = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.conditions.write", selfRoles,
            "{\"mode\":\"apply\",\"conditions\":[\"charmed\"]}", 0,
            "1123456789abcdef0123456789abcdec"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, missingSource.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, selfSource.Disposition);

        await harness.ReplaceConditionsRawAsync("subject.high",
            "{\"entries\":[{\"condition\":\"poisoned\"},{\"condition\":\"poisoned\"}],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary\"}}");
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        var correction = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.conditions.write", "subject.high",
            "{\"mode\":\"apply\",\"conditions\":[\"prone\"]}", 0,
            "2123456789abcdef0123456789abcdec"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, correction.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions");
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Turn_lifecycle_creates_a_fresh_budget_for_only_the_new_turn()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "3123456789abcdef0123456789abcdec"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high",
                "{\"mode\":\"exhaust\",\"levels\":2}", 0,
                "4123456789abcdef0123456789abcdec"))).Disposition);
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var orderRequest = await EncounterOrderWithHighFirstAsync(harness);
        var order = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles,
            orderRequest.Input, orderRequest.Seed, "5123456789abcdef0123456789abcdec"));
        Assert.True(order.Successful, string.Join("; ", order.Problems.Select(value => value.SafeMessage)));

        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.start", encounterRoles, "{\"roundId\":\"encounter.round.1\",\"turnId\":\"encounter.turn.1.0\"}", 0,
            "6123456789abcdef0123456789abcdec"));
        Assert.True(started.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", started.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        Assert.Equal(10, started.AppliedEffectCount);
        var high = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.turn.1.0", "dnd2024.combat.turn-budget");
        var low = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.low", "dnd2024.combat.turn-budget");
        Assert.Equal("{\"turn\":{\"entityId\":\"encounter.turn.1.0\"},\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}", high!.ValueJson);
        Assert.Null(low);

        var advanced = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.advance", encounterRoles, "{\"roundId\":null,\"turnId\":\"encounter.turn.1.1\"}", 0,
            "7123456789abcdef0123456789abcdec"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, advanced.Disposition);
        Assert.Equal(8, advanced.AppliedEffectCount);
        low = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.turn.1.1", "dnd2024.combat.turn-budget");
        Assert.Equal("{\"turn\":{\"entityId\":\"encounter.turn.1.1\"},\"remaining\":{\"actions\":1,\"bonusActions\":1,\"reactions\":1},\"movementSpent\":[],\"interactionsUsed\":0}", low!.ValueJson);
    }

    [Fact]
    public async Task Turn_budget_spender_enforces_active_turn_off_turn_reaction_and_condition_prohibitions()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var orderRequest = await EncounterOrderWithHighFirstAsync(harness);
        Assert.True((await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles,
            orderRequest.Input, orderRequest.Seed, "8123456789abcdef0123456789abcdec"))).Successful);
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.encounter-turn.start", encounterRoles, "{\"roundId\":\"encounter.round.1\",\"turnId\":\"encounter.turn.1.0\"}", 0,
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
            "dnd2024.mechanic.turn-budget.spend", activeRoles, "{\"resource\":\"action\"}", 0,
            "a123456789abcdef0123456789abcdec"));
        var repeated = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.spend", activeRoles, "{\"resource\":\"action\"}", 0,
            "b123456789abcdef0123456789abcdec"));
        var offTurnAction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.spend", offTurnRoles, "{\"resource\":\"action\"}", 0,
            "c123456789abcdef0123456789abcdec"));
        var offTurnReaction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.spend", offTurnRoles, "{\"resource\":\"reaction\"}", 0,
            "d123456789abcdef0123456789abcdec"));
        var movement = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.spend", activeRoles,
            "{\"resource\":\"movement\",\"distance\":{\"dimension\":\"distance\",\"value\":{\"numerator\":1143,\"denominator\":250},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.meter\"}}}", 0,
            "e123456789abcdef0123456789abcdec"));

        Assert.True(action.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", action.Problems.Select(problem => problem.Code + ": " + problem.SafeMessage)));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, repeated.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, offTurnAction.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, offTurnReaction.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, movement.Disposition);
        var activeBudget = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.turn.1.0", "dnd2024.combat.turn-budget");
        Assert.Contains("\"numerator\":1143", activeBudget!.ValueJson, StringComparison.Ordinal);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high", "{\"mode\":\"record\"}", 0,
                "f123456789abcdef0123456789abcdec"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionFor(
                "dnd2024.mechanic.conditions.write", "subject.high",
                "{\"mode\":\"apply\",\"conditions\":[\"stunned\"]}", 0,
                "0123456789abcdef0123456789abcded"))).Disposition);
        var prohibited = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.spend", activeRoles,
            "{\"resource\":\"movement\",\"distance\":{\"dimension\":\"distance\",\"value\":{\"numerator\":381,\"denominator\":250},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.meter\"}}}", 0,
            "1123456789abcdef0123456789abcded"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, prohibited.Disposition);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.turn.1.0", "dnd2024.combat.turn-budget");
        Assert.Equal(activeBudget.ValueJson, after!.ValueJson);

        var advanced = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.advance", encounterRoles,
            "{\"roundId\":null,\"turnId\":\"encounter.turn.1.1\"}", 0,
            "2123456789abcdef0123456789abcded"));
        var wrapped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.advance", encounterRoles,
            "{\"roundId\":\"encounter.round.2\",\"turnId\":\"encounter.turn.2.0\"}", 0,
            "3123456789abcdef0123456789abcded"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, advanced.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, wrapped.Disposition);

        var lowOffTurnAction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.spend", offTurnRoles, "{\"resource\":\"action\"}", 0,
            "4123456789abcdef0123456789abcded"));
        var lowOffTurnReaction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.turn-budget.spend", offTurnRoles, "{\"resource\":\"reaction\"}", 0,
            "5123456789abcdef0123456789abcded"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, lowOffTurnAction.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, lowOffTurnReaction.Disposition);
        var lowBudget = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.turn.1.1", "dnd2024.combat.turn-budget");
        Assert.Contains("\"reactions\":0", lowBudget!.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Character_content_definition_is_source_fixed_write_once_and_replay_safe()
    {
        await using var harness = await DndHarness.CreateAsync();
        var roles = new Dictionary<string, string> { ["content"] = "subject.high" };
        const string input = "{\"kind\":\"species\",\"contentKey\":\"human\",\"contentVersion\":1,\"status\":\"active\",\"locator\":\"Character Creation > Species PDF page 40\"}";
        var request = harness.ActionForRoles("dnd2024.mechanic.character-content-definition.record",
            roles, input, 0, "2123456789abcdef0123456789abcded");

        var first = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character-content-definition.record", roles, input, 0,
            "3123456789abcdef0123456789abcded"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        var stored = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.content-definition");
        Assert.Equal(1, stored!.Revision);
        Assert.Contains("\"sourceId\":\"dnd2024.source.srd-5.2.1\"", stored.ValueJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Character_profile_requires_explicit_transitions_and_preserves_failed_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        var roles = new Dictionary<string, string> { ["actor"] = "subject.high" };
        var recorded = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character-profile.record", roles,
            "{\"mode\":\"record\",\"biography\":\"A patient cartographer.\",\"pronouns\":\"they/them\"}",
            0, "4123456789abcdef0123456789abcded"));
        var corrected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character-profile.record", roles,
            "{\"mode\":\"correct\",\"appearance\":\"Ink-stained gloves.\",\"playerNotes\":\"Trust the northern guide.\"}",
            0, "5123456789abcdef0123456789abcded"));
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.identity");
        var invalid = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character-profile.record", roles,
            "{\"mode\":\"correct\",\"biography\":\" untrimmed\"}",
            0, "6123456789abcdef0123456789abcded"));
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.identity");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        Assert.Equal(2, before!.Revision);
        Assert.Contains("\"playerNotes\":\"Trust the northern guide.\"", before.ValueJson,
            StringComparison.Ordinal);
        Assert.Equal(before.ValueJson, after!.ValueJson);
    }

    [Fact]
    public async Task Character_creation_abilities_resolve_standard_array_and_soldier_increases_without_effects()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationAbilityFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["policy"] = "dnd2024.content.ability-assignment.standard-array.v1",
            ["background"] = "dnd2024.content.background.soldier.v1"
        };
        const string input = "{\"scores\":{\"wis\":10,\"cha\":12,\"str\":15,\"int\":8,\"con\":13,\"dex\":14},\"increases\":{\"con\":1,\"str\":2}}";
        const string canonicalOrder = "{\"increases\":{\"str\":2,\"con\":1},\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12}}";

        var first = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-abilities.resolve", roles, input, 0);
        var reordered = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-abilities.resolve", roles, canonicalOrder, long.MaxValue);

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
            "dnd2024.mechanic.character-abilities.resolve", roles, input, 0,
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
            "dnd2024.mechanic.character-abilities.resolve",
            new Dictionary<string, string>
            {
                ["policy"] = "dnd2024.content.ability-assignment.standard-array.v1",
                ["background"] = "dnd2024.content.background.soldier.v1"
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
            "{\"policyVersion\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Character Creation > Step 3: Ability Scores > Generate Your Scores > Point Cost, PDF p. 21\"},\"scoreBounds\":{\"minimum\":8,\"maximum\":15},\"allocation\":{\"family\":\"point-budget\",\"budget\":27,\"costs\":[{\"score\":8,\"cost\":0},{\"score\":9,\"cost\":1},{\"score\":10,\"cost\":2},{\"score\":11,\"cost\":3},{\"score\":12,\"cost\":4},{\"score\":13,\"cost\":5},{\"score\":14,\"cost\":7},{\"score\":15,\"cost\":9}]}}");
        var roles = new Dictionary<string, string>
        {
            ["policy"] = pointPolicy,
            ["background"] = "dnd2024.content.background.soldier.v1"
        };
        var pointCost = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-abilities.resolve", roles,
            "{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}}",
            0);
        Assert.True(pointCost.Ok, pointCost.Run?.Error);
        Assert.Contains("\"allocationFamily\":\"point-budget\"", pointCost.Run!.Output.Data,
            StringComparison.Ordinal);

        const string capPolicy = "content.test.ability-assignment.cap.v1";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, capPolicy, "Cap fixture");
        await harness.AddApplicationComponentAsync(capPolicy,
            "dnd2024.character.ability-assignment-policy",
            "{\"policyVersion\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Character Creation > Step 3: Ability Scores, PDF p. 21\"},\"scoreBounds\":{\"minimum\":1,\"maximum\":20},\"allocation\":{\"family\":\"fixed-multiset\",\"values\":[8,10,12,13,14,20]}}");
        roles["policy"] = capPolicy;
        var overCap = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-abilities.resolve", roles,
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
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");
        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-abilities.resolve",
            new Dictionary<string, string>
            {
                ["policy"] = "dnd2024.content.ability-assignment.standard-array.v1",
                ["background"] = "dnd2024.content.background.soldier.v1"
            }, input, 0);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");

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
            "dnd2024.content.background.soldier.v1",
            "dnd2024.background.ability-increase-options",
            "{\"contentKey\":\"soldier\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Character Origins > wrong\"},\"eligibleAbilities\":[\"str\",\"dex\",\"con\"],\"allowedPatterns\":[\"plus-2-plus-1\",\"plus-1-each\"]}");

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-abilities.resolve",
            new Dictionary<string, string>
            {
                ["policy"] = "dnd2024.content.ability-assignment.standard-array.v1",
                ["background"] = "dnd2024.content.background.soldier.v1"
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
        var paths = Directory.GetFiles(directory, "dnd2024.content.species.*.v1.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(9, paths.Length);

        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "dnd2024.species-profile.schema.json"));
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
            ["species"] = "dnd2024.content.species.human.v1"
        };
        var input = "{\"size\":\"" + size + "\"}";
        var first = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-selection.resolve", roles, input, 0);
        var second = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-selection.resolve", roles, input, long.MaxValue);

        Assert.True(first.Ok, first.Run?.Error ?? string.Join("; ", first.Problems));
        Assert.True(second.Ok, second.Run?.Error ?? string.Join("; ", second.Problems));
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var root = data.RootElement;
        Assert.Equal("species-selection-resolve", root.GetProperty("test").GetString());
        Assert.Equal("dnd2024.content.species.human.v1",
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

        var request = harness.ActionForRoles("dnd2024.mechanic.species-selection.resolve", roles,
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
            "dnd2024.mechanic.species-selection.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.dragonborn.v1"
            }, "{}", 0);
        var goliath = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-selection.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.goliath.v1"
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
    [InlineData("dnd2024.content.species.human.v1", "{}", "requires exactly one allowed Size")]
    [InlineData("dnd2024.content.species.human.v1", "{\"size\":\"large\"}", "requires exactly one allowed Size")]
    [InlineData("dnd2024.content.species.human.v1", "{\"size\":\"small\",\"speed\":30}", "requires exactly one allowed Size")]
    [InlineData("dnd2024.content.species.dragonborn.v1", "{\"size\":\"medium\"}", "takes no Size input")]
    public async Task Character_creation_species_rejects_nonclosed_or_derived_size_input(
        string speciesId, string input, string error)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-selection.resolve",
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
            "dnd2024.content.species.human.v1", "dnd2024.species-profile",
            "{\"contentKey\":\"human\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Character Origins > Character Species > Dwarf, PDF page 84\"},\"creatureType\":\"humanoid\",\"allowedSizes\":[\"small\",\"medium\"],\"baseSpeed\":{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0},\"traitKeys\":[\"resourceful\",\"skillful\",\"versatile\"],\"choiceFamilies\":[]}");

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-selection.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.human.v1"
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
            "entities", "character-creation", "species", "dnd2024.content.species.human.v1.json");
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), path);
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, fakeId, "Copied Human");
        foreach (var component in entity.Components)
            await harness.AddApplicationComponentAsync(fakeId, component.DefinitionId, component.Data);

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-selection.resolve",
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
            "dnd2024.mechanic.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.human.v1"
            }, "{\"skill\":\"" + skill + "\"}", 0);

        Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems));
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        var root = data.RootElement;
        Assert.Equal(skill, root.GetProperty("selectedSkill").GetString());
        var target = root.GetProperty("target");
        Assert.Equal("dnd2024.creature.proficiencies", target.GetProperty("definitionId").GetString());
        Assert.Equal("skill", target.GetProperty("family").GetString());
        Assert.Equal("entries", target.GetProperty("field").GetString());
        Assert.Equal("rank-and-source-union", target.GetProperty("mergePolicy").GetString());
        var entry = target.GetProperty("entries")[0];
        Assert.Equal("dnd2024.vocabulary.skill." + skill, entry.GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.vocabulary.proficiency-rank.proficiency",
            entry.GetProperty("rankRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.content.species.human.v1",
            entry.GetProperty("sourceRefs")[0].GetProperty("entityId").GetString());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Character_creation_species_skillful_is_deterministic_and_replay_safe()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["species"] = "dnd2024.content.species.human.v1"
        };
        const string input = "{\"skill\":\"perception\"}";
        var first = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-skillful.resolve", roles, input, 0);
        var second = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-skillful.resolve", roles, input, long.MaxValue);

        Assert.True(first.Ok, first.Run?.Error);
        Assert.True(second.Ok, second.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        var request = harness.ActionForRoles("dnd2024.mechanic.species-skillful.resolve", roles,
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
            "dnd2024.mechanic.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.dragonborn.v1"
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
            "dnd2024.mechanic.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.human.v1"
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
            "dnd2024.content.species.human.v1", "dnd2024.species-profile",
            "{\"contentKey\":\"human\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Character Origins > Character Species > Dwarf, PDF page 84\"},\"creatureType\":\"humanoid\",\"allowedSizes\":[\"small\",\"medium\"],\"baseSpeed\":{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0},\"traitKeys\":[\"resourceful\",\"skillful\",\"versatile\"],\"choiceFamilies\":[]}");

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-skillful.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.human.v1"
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
            ["dnd2024.feat.alert"] = false,
            ["dnd2024.feat.magic-initiate"] = true,
            ["dnd2024.feat.savage-attacker"] = false,
            ["dnd2024.feat.skilled"] = true
        };
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "character-options", "feats");

        await using var harness = await DndHarness.CreateAsync();
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            if (!expected.TryGetValue(entity.Id, out var repeatable)) continue;
            Assert.Contains(relative, harness.ActiveSourcePaths);
            found.Add(entity.Id);
            using var version = JsonDocument.Parse(Assert.Single(entity.Components,
                value => value.DefinitionId == "dnd2024.core.version").Data);
            using var source = JsonDocument.Parse(Assert.Single(entity.Components,
                value => value.DefinitionId == "dnd2024.core.source").Data);
            using var feat = JsonDocument.Parse(Assert.Single(entity.Components,
                value => value.DefinitionId == "dnd2024.advancement.feat").Data);
            Assert.Equal("active", version.RootElement.GetProperty("status").GetString());
            Assert.Equal("dnd2024.feat-category.origin",
                feat.RootElement.GetProperty("categoryRef").GetProperty("entityId").GetString());
            Assert.Equal(repeatable, feat.RootElement.GetProperty("repeatable").GetBoolean());
            Assert.StartsWith("Feats > ", source.RootElement.GetProperty("citations")[0]
                .GetProperty("locator").GetString(), StringComparison.Ordinal);
        }
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Character_creation_species_versatile_resolves_skilled_mixed_choices_canonically()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        var roles = new Dictionary<string, string>
        {
            ["species"] = "dnd2024.content.species.human.v1",
            ["feat"] = "dnd2024.feat.skilled"
        };
        const string input = "{\"choices\":[{\"kind\":\"tool\",\"id\":\"thieves-tools\"},{\"kind\":\"skill\",\"id\":\"stealth\"},{\"kind\":\"skill\",\"id\":\"perception\"}]}";
        const string reordered = "{\"choices\":[{\"id\":\"perception\",\"kind\":\"skill\"},{\"id\":\"thieves-tools\",\"kind\":\"tool\"},{\"id\":\"stealth\",\"kind\":\"skill\"}]}";
        var first = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-versatile-skilled.resolve", roles, input, 0);
        var second = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-versatile-skilled.resolve", roles, reordered, long.MaxValue);

        Assert.True(first.Ok, first.Run?.Error ?? string.Join("; ", first.Problems));
        Assert.True(second.Ok, second.Run?.Error ?? string.Join("; ", second.Problems));
        Assert.Equal(first.Run!.Output.Data, second.Run!.Output.Data);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var root = data.RootElement;
        Assert.Equal("dnd2024.feat.skilled",
            root.GetProperty("selectedFeat").GetProperty("featDefinitionId").GetString());
        Assert.True(root.GetProperty("selectedFeat").GetProperty("repeatable").GetBoolean());
        Assert.Equal(new[] { "dnd2024.vocabulary.skill.perception", "dnd2024.vocabulary.skill.stealth" },
            root.GetProperty("skillContribution").GetProperty("entries").EnumerateArray()
                .Select(value => value.GetProperty("entityId").GetString()).ToArray());
        Assert.Equal(new[] { "dnd2024.equipment.tool.thieves-tools" },
            root.GetProperty("toolContribution").GetProperty("entries").EnumerateArray()
                .Select(value => value.GetProperty("entityId").GetString()).ToArray());
        Assert.Equal("rank-and-source-union", root.GetProperty("skillContribution")
            .GetProperty("mergePolicy").GetString());
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        var request = harness.ActionForRoles(
            "dnd2024.mechanic.species-versatile-skilled.resolve", roles, input, 0,
            "b123456789abcdef0123456789abcdee");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
    }

    [Theory]
    [InlineData("{\"choices\":[{\"kind\":\"skill\",\"id\":\"arcana\"},{\"kind\":\"skill\",\"id\":\"history\"},{\"kind\":\"skill\",\"id\":\"nature\"}]}", 3, 0)]
    [InlineData("{\"choices\":[{\"kind\":\"tool\",\"id\":\"dice-set\"},{\"kind\":\"tool\",\"id\":\"lyre\"},{\"kind\":\"tool\",\"id\":\"smiths-tools\"}]}", 0, 3)]
    public async Task Character_creation_species_versatile_supports_all_skill_or_all_tool_skilled_choices(
        string input, int skills, int tools)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-versatile-skilled.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.human.v1",
                ["feat"] = "dnd2024.feat.skilled"
            }, input, 0);

        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        Assert.Equal(skills, data.RootElement.GetProperty("skillContribution")
            .GetProperty("entries").GetArrayLength());
        Assert.Equal(tools, data.RootElement.GetProperty("toolContribution")
            .GetProperty("entries").GetArrayLength());
        Assert.Empty(result.Run.Output.Effects);
    }

    [Theory]
    [InlineData("dnd2024.content.species.dragonborn.v1", "dnd2024.feat.skilled", "Versatile entitlement")]
    [InlineData("dnd2024.content.species.human.v1", "dnd2024.feat.alert", "requires the Skilled")]
    public async Task Character_creation_species_versatile_requires_entitlement_and_skilled_behavior(
        string speciesId, string featId, string error)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddCharacterCreationSpeciesFixturesAsync();
        await harness.AddCharacterCreationFeatFixturesAsync();
        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-versatile-skilled.resolve",
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
            "dnd2024.mechanic.species-versatile-skilled.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.human.v1",
                ["feat"] = "dnd2024.feat.skilled"
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
            "dnd2024.feat.skilled", "dnd2024.core.source",
            "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Feats > Alert (SRD 5.2.1, pages 87-87)\"}]}");
        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.species-versatile-skilled.resolve",
            new Dictionary<string, string>
            {
                ["species"] = "dnd2024.content.species.human.v1",
                ["feat"] = "dnd2024.feat.skilled"
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
                "dnd2024.mechanic.character-profile.record",
                new Dictionary<string, string> { ["actor"] = "subject.high" },
                "{\"mode\":\"record\",\"biography\":\"A steadfast adventurer.\"}", 0,
                "c123456789abcdef0123456789abcdee"))).Disposition);

        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.heroic-inspiration.grant", roles, "{}", long.MaxValue);
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.heroic-inspiration.grant", roles, "{}", 0,
            "d123456789abcdef0123456789abcdee");
        var granted = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var beforeDuplicate = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.heroic-inspiration");
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.heroic-inspiration.grant", roles, "{}", 0,
            "e123456789abcdef0123456789abcdee"));
        var afterDuplicate = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.heroic-inspiration");

        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
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
    [InlineData("{\"speciesId\":\"dnd2024.content.species.human.v1\"}")]
    public async Task Character_creation_heroic_inspiration_rejects_nonempty_or_nonobject_input(
        string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddApplicationComponentAsync(
            "subject.high", "dnd2024.character.identity", "{\"pronouns\":\"they/them\"}");

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.heroic-inspiration.grant",
            new Dictionary<string, string> { ["subject"] = "subject.high" }, input, 0);

        Assert.False(result.Ok);
        if (result.Run is not null)
            Assert.Empty(result.Run.Output.Effects);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.heroic-inspiration"));
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
            await harness.AddApplicationComponentAsync("subject.high", "dnd2024.character.identity",
                "{\"biography\":\"Valid before corruption.\"}");
            if (profileCase == "empty")
                await harness.ReplaceApplicationComponentRawAsync(
                    "subject.high", "dnd2024.character.identity", "{}");
            if (profileCase == "primitive")
                await harness.ReplaceApplicationComponentRawAsync(
                    "subject.high", "dnd2024.character.identity", "42");
            if (profileCase == "unknown-field")
                await harness.ReplaceApplicationComponentRawAsync(
                    "subject.high", "dnd2024.character.identity", "{\"player\":\"yes\"}");
            if (profileCase == "untrimmed")
                await harness.ReplaceApplicationComponentRawAsync(
                    "subject.high", "dnd2024.character.identity", "{\"biography\":\" invalid\"}");
        }

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.heroic-inspiration.grant",
            new Dictionary<string, string> { ["subject"] = "subject.high" }, "{}", 0);

        Assert.False(result.Ok);
        Assert.Empty(result.Run!.Output.Effects);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.heroic-inspiration"));
    }

    [Fact]
    public async Task Character_creation_heroic_inspiration_refuses_corrupt_held_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddApplicationComponentAsync(
            "subject.high", "dnd2024.character.identity", "{\"appearance\":\"A silver cloak.\"}");
        await harness.AddApplicationComponentAsync(
            "subject.high", "dnd2024.character.heroic-inspiration", "{}");
        await harness.ReplaceApplicationComponentRawAsync(
            "subject.high", "dnd2024.character.heroic-inspiration", "{\"available\":true}");

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.heroic-inspiration.grant",
            new Dictionary<string, string> { ["subject"] = "subject.high" }, "{}", 0);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.heroic-inspiration");

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
            "catalog/applications/dnd2024/content/entities/character-creation/rest/dnd2024.content.rest-policy.standard.v1.json";
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var schemaPath = Path.Combine(root, "catalog", "applications", "dnd2024", "components",
            "dnd2024.rest-policy.schema.json");
        var schema = await File.ReadAllTextAsync(schemaPath);
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));

        await using var harness = await DndHarness.CreateAsync();
        Assert.Contains(relative, harness.ActiveSourcePaths);
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
        Assert.Equal("dnd2024.content.rest-policy.standard.v1", entity.Id);
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
            ["policy"] = "dnd2024.content.rest-policy.standard.v1"
        };
        var input = "{\"kind\":\"" + kind + "\"}";
        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.begin", roles, input, long.MaxValue);
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", roles, input, 0, operationId);
        var started = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var beforeDuplicate = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", roles, input, 0,
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
            "dnd2024.mechanic.rest.begin", RestBeginRoles(), input, 0);

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
                "dnd2024.content.rest-policy.standard.v1", "dnd2024.rest-policy",
                (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
                    "dnd2024.content.rest-policy.standard.v1", "dnd2024.rest-policy"))!.ValueJson
                    .Replace("\"policyVersion\":1", "\"policyVersion\":2", StringComparison.Ordinal));

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.begin", RestBeginRoles(), "{\"kind\":\"long\"}", 0);

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
            "dnd2024.mechanic.rest.begin", RestBeginRoles(), "{\"kind\":\"short\"}", 0);

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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"short\"}", 0,
            "31000000000000000000000000000001"));
        var firstRequest = harness.ActionForRoles(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"light\",\"minutes\":59}", 0,
            "31000000000000000000000000000002");
        var first = await harness.Runner.RunAsync(firstRequest);
        var replay = await harness.Runner.RunAsync(firstRequest);
        var active = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var finalEvaluation = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"light\",\"minutes\":1}", 0);
        var final = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"light\",\"minutes\":1}", 0,
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
        Assert.Single(await harness.EventsAsync(first.OperationId));
        Assert.Single(await harness.EventsAsync(final.OperationId));
    }

    [Fact]
    public async Task Authoritative_clock_and_event_roll_back_when_a_late_participant_refuses()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.world.clock.advance",
            new Dictionary<string, string> { ["world"] = "world.rest.fixture" },
            "{\"minutes\":60}", 0, "31100000000000000000000000000001");

        var result = await harness.Runner.RunAsync(request);
        var clock = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "game.core.world.clock");

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        using var state = JsonDocument.Parse(clock!.ValueJson);
        Assert.Equal(100, state.RootElement.GetProperty("currentMinute").GetInt32());
        Assert.Equal(7, state.RootElement.GetProperty("revision").GetInt32());
        Assert.Empty(await harness.EventsAsync(result.OperationId));
    }

    [Fact]
    public async Task Character_creation_rest_progress_requires_six_hours_sleep_and_limits_light_activity()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentMinute: 100);
        var roles = RestBeginRoles();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"long\"}", 0,
                "32000000000000000000000000000001"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"sleep\",\"minutes\":360}", 0,
                "32000000000000000000000000000002"))).Disposition);
        var completed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"light\",\"minutes\":120}", 0,
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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"long\"}", 0,
            "33000000000000000000000000000001"));
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"sleep\",\"minutes\":60}", 0,
            "33000000000000000000000000000002"));
        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0);
        var interrupted = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0,
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

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"sleep\",\"minutes\":300}", 0,
                "33000000000000000000000000000004"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"light\",\"minutes\":120}", 0,
                "33000000000000000000000000000005"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"sleep\",\"minutes\":60}", 0,
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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"short\"}", 0,
            "34000000000000000000000000000000"));
        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.interrupt", roles,
            "{\"kind\":\"" + interruption + "\"}", 0);
        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.interrupt", roles,
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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"long\"}", 0,
            "35000000000000000000000000000000"));

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.interrupt", roles,
            "{\"kind\":\"" + interruption + "\"}", 0);

        Assert.True(result.Ok, result.Run?.Error);
        Assert.Single(result.Run!.Output.Effects);
        Assert.Contains("\"requiredMinutes\":540", result.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"shortRestCreditEligible\":false", result.Run.Output.Data,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stale-clock")]
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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"" + kind + "\"}", 0,
            "36000000000000000000000000000000"));
        if (stateCase == "stale-clock")
            await harness.SetRestClockAsync(101, 8);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");
        var input = stateCase switch
        {
            "short-sleep" => "{\"activity\":\"sleep\",\"minutes\":1}",
            "extra-input" => "{\"activity\":\"light\",\"minutes\":1,\"currentMinute\":0}",
            "excess-long-light" => "{\"activity\":\"light\",\"minutes\":121}",
            _ => "{\"activity\":\"light\",\"minutes\":1}"
        };

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.progress", roles, input, 0);
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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"long\"}", 0,
            "37000000000000000000000000000000"));
        await harness.SetRestClockAsync(101, 8);
        var unclassified = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0);
        await harness.SetRestClockAsync(100, 7);
        var unknown = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.interrupt", roles, "{\"kind\":\"loud-noise\"}", 0);
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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"long\"}", 0,
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
        if (stateCase == "incoherent-clock")
            await harness.SetRestClockAsync(101, 7);
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode");

        var result = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"sleep\",\"minutes\":1}", 0);
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
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"short\"}", 0,
            "38000000000000000000000000000000"));
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"light\",\"minutes\":60}", 0,
            "38000000000000000000000000000001"));

        var progress = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.progress", roles, "{\"activity\":\"light\",\"minutes\":1}", 0);
        var interruption = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.rest.interrupt", roles, "{\"kind\":\"damage\"}", 0);

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
            ["policy"] = "dnd2024.content.rest-policy.standard.v1"
        };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.begin", restRoles,
                "{\"kind\":\"" + restKind + "\"}", 0,
                "39000000000000000000000000000000"))).Disposition);
        var damageRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture",
            ["target"] = "target.fixture"
        };
        const string input = "{\"ability\":\"str\",\"critical\":false}";

        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", damageRoles, input, 77);
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", damageRoles, input, 77, operationId);
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
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
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
            "dnd2024.creature.temporary-hit-points",
            "{\"amount\":100,\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}");
        var restRoles = new Dictionary<string, string>
        {
            ["creature"] = "target.fixture", ["world"] = "world.rest.fixture",
            ["policy"] = "dnd2024.content.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", restRoles, "{\"kind\":\"short\"}", 0,
            "39100000000000000000000000000000"));
        var damageRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture",
            ["target"] = "target.fixture"
        };

        var applied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", damageRoles,
            "{\"ability\":\"str\",\"critical\":false}", 77,
            "39100000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, applied.Disposition);
        Assert.Equal(3, applied.AppliedEffectCount);
        var hp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
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
            await harness.ReplaceApplicationComponentRawAsync("target.fixture", "dnd2024.creature.defenses",
                "{\"armorClassSource\":{\"entityId\":\"dnd2024.content.defense.unarmored.v1\"},\"damageResponses\":[{\"damageTypeRef\":{\"entityId\":\"dnd2024.vocabulary.damage-type.piercing\"},\"responseRef\":{\"entityId\":\"dnd2024.vocabulary.damage-response.immunity\"},\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}]}");
        var restRoles = new Dictionary<string, string>
        {
            ["creature"] = "target.fixture", ["world"] = "world.rest.fixture",
            ["policy"] = "dnd2024.content.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", restRoles,
            "{\"kind\":\"" + (stateCase == "ready" ? "short" : "long") + "\"}", 0,
            "39200000000000000000000000000000"));
        if (stateCase == "ready")
        {
            await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", restRoles,
                "{\"activity\":\"light\",\"minutes\":60}", 0,
                "39200000000000000000000000000001"));
        }
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");
        var damageRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
            ["activity"] = "activity.weapon.fixture",
            ["target"] = "target.fixture"
        };

        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.weapon-damage.apply", damageRoles,
            "{\"ability\":\"str\",\"critical\":false}", 77);
        var applied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", damageRoles,
            "{\"ability\":\"str\",\"critical\":false}", 77,
            "39200000000000000000000000000002"));
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");

        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
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
            ["policy"] = "dnd2024.content.rest-policy.standard.v1"
        };
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", restRoles, "{\"kind\":\"short\"}", 0,
            "39300000000000000000000000000000"));
        Assert.True(await harness.Edges.RemoveRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "target.fixture",
            "dnd2024.rest.world", 1));
        var hpBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
        var episodeBefore = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.rest-episode");

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.weapon-damage.apply", new Dictionary<string, string>
            {
                ["subject"] = "subject.high", ["weapon"] = "weapon.fixture",
                ["activity"] = "activity.weapon.fixture",
                ["target"] = "target.fixture"
            }, "{\"ability\":\"str\",\"critical\":false}", 77,
            "39300000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        var hpAfter = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "target.fixture", "dnd2024.creature.hit-points");
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
        ["policy"] = "dnd2024.content.rest-policy.standard.v1"
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
        var result = await harness.EvaluateRolesAsync("dnd2024.mechanic.creature-size.record",
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
            "dnd2024.mechanic.language-proficiencies.record", "subject.high",
            "{\"mode\":\"record\",\"languages\":[\"elvish\",\"common\"]}", 0,
            "7123456789abcdef0123456789abcded"));
        var tool = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.tool-proficiencies.record", "subject.high",
            "{\"mode\":\"record\",\"tools\":[\"thieves-tools\",\"lyre\"]}", 0,
            "8123456789abcdef0123456789abcded"));
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.language-proficiencies.record", "subject.high",
            "{\"mode\":\"correct\",\"languages\":[]}", 0,
            "9123456789abcdef0123456789abcded"));
        var invalid = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.tool-proficiencies.record", "subject.high",
            "{\"mode\":\"correct\",\"tools\":[\"laser-cutter\"]}", 0,
            "a123456789abcdef0123456789abcded"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, language.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, tool.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        var languages = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.languages");
        var tools = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");
        Assert.Equal(2, languages!.Revision);
        Assert.StartsWith("{\"languages\":{}", languages.ValueJson, StringComparison.Ordinal);
        Assert.Equal(1, tools!.Revision);
        using var toolState = JsonDocument.Parse(tools.ValueJson);
        Assert.Equal(new[] { "lyre", "thieves-tools" }, ReadToolIds(toolState.RootElement).ToArray());
    }

    [Fact]
    public async Task Item_runtime_component_schemas_require_a_closed_definition_link_and_positive_quantity()
    {
        var root = RepositoryRoot();
        var componentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "components");
        var validator = new BoundedJsonSchemaValidator();
        var link = validator.Compile(await File.ReadAllTextAsync(Path.Combine(
            componentRoot, "dnd2024.core.definition-link.schema.json")));
        var quantity = validator.Compile(await File.ReadAllTextAsync(Path.Combine(
            componentRoot, "dnd2024.item.quantity.schema.json")));

        Assert.True(link.IsAccepted, string.Join("; ", link.Diagnostics));
        Assert.True(quantity.IsAccepted, string.Join("; ", quantity.Diagnostics));
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definition\":{\"entityId\":\"dnd2024.item.arrow.v1\"}}").Status);
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definition\":{\"entityId\":\"dnd2024.item.arrow.v1\"},\"definitionRevision\":1}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definitionId\":\"dnd2024.item.arrow.v1\"}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definition\":{\"entityId\":\"dnd2024.item.arrow.v1\"},\"extra\":true}").Status);
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(quantity.ProfileId,
            quantity.NormalizedSchema, "{\"current\":1}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(quantity.ProfileId,
            quantity.NormalizedSchema, "{\"current\":0}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(quantity.ProfileId,
            quantity.NormalizedSchema, "{\"count\":1,\"stackKey\":\"dnd2024.item.arrow.v1\"}").Status);
    }

    [Fact]
    public async Task Item_instance_record_create_read_and_move_use_definition_identity_and_containment()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.robe.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Robe definition", SeparateItemDefinition());
        var recordRoles = new Dictionary<string, string>
        {
            ["item"] = "subject.low", ["definition"] = definitionId
        };
        var recorded = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-instance.record", recordRoles, "{}", 0,
            "b123456789abcdef0123456789abcded"));
        var createRoles = new Dictionary<string, string>
        {
            ["definition"] = definitionId, ["destination"] = "subject.high"
        };
        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-instance.create-and-place", createRoles,
            "{\"itemId\":\"item.campaign.robe\",\"name\":\"Traveler's Robe\",\"slot\":\"carried\"}",
            0, "c123456789abcdef0123456789abcded"));
        var read = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.campaign.robe" }, "{}", 0);
        var moved = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-instance.move", new Dictionary<string, string>
            {
                ["item"] = "item.campaign.robe", ["destination"] = "subject.low"
            }, "{\"slot\":\"gift\"}", 0, "d123456789abcdef0123456789abcded"));

        Assert.True(recorded.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", recorded.Problems.Select(value => value.Code + ": " + value.SafeMessage)));
        Assert.True(created.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", created.Problems.Select(value => value.Code + ": " + value.SafeMessage)));
        Assert.True(read.Ok, read.Run?.Error);
        Assert.Contains("\"containerId\":\"subject.high\"", read.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.True(moved.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", moved.Problems.Select(value => value.Code + ": " + value.SafeMessage)));
    }

    [Fact]
    public async Task Fungible_stack_lifecycle_conserves_count_and_deletes_zero()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.arrow.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Arrow definition", FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.stack.recorded", "Recorded Arrows", definitionId,
            "subject.low", includeQuantity: false);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item-stack.record", new Dictionary<string, string>
                {
                    ["item"] = "item.stack.recorded", ["definition"] = definitionId
                }, "{\"count\":2}", 0, "d123456789abcdef0123456789abcdee"))).Disposition);
        var definitionAndDestination = new Dictionary<string, string>
        {
            ["definition"] = definitionId, ["destination"] = "subject.high"
        };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item-stack.create-and-place", definitionAndDestination,
                "{\"count\":10,\"itemId\":\"item.stack.arrows\",\"name\":\"Arrows\",\"slot\":\"quiver\"}",
                0, "e123456789abcdef0123456789abcded"))).Disposition);
        const string childDefinition = "dnd2024.item.token.v1";
        await harness.AddItemDefinitionAsync(childDefinition, "Token definition", SeparateItemDefinition());
        await harness.AddPhysicalItemAsync("item.stack.child", "Token", childDefinition,
            "item.stack.arrows", "inside");
        var blockedByContents = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":1}", 0, "e223456789abcdef0123456789abcded"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, blockedByContents.Disposition);
        Assert.Contains("\"current\":10", (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.stack.arrows", "dnd2024.item.quantity"))!.ValueJson,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item-instance.move", new Dictionary<string, string>
                {
                    ["item"] = "item.stack.child", ["destination"] = "subject.high"
                }, "{\"slot\":\"carried\"}", 0, "e323456789abcdef0123456789abcded"))).Disposition);
        var split = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.split", new Dictionary<string, string>
            {
                ["source"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":3,\"itemId\":\"item.stack.arrows-split\",\"name\":\"Three Arrows\"}",
            0, "f123456789abcdef0123456789abcded"));
        var merged = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.merge", new Dictionary<string, string>
            {
                ["source"] = "item.stack.arrows-split", ["target"] = "item.stack.arrows",
                ["definition"] = definitionId
            }, "{}", 0, "0123456789abcdef0123456789abcdee"));
        var partial = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":4}", 0, "1123456789abcdef0123456789abcdee"));
        var final = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.consume", new Dictionary<string, string>
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
        const string definitionId = "dnd2024.item.spear.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Spear definition",
            SeparateItemDefinition("[\"held\"]"));
        await harness.AddPhysicalItemAsync("item.spear", "Spear", definitionId, "subject.high");
        var roles = new Dictionary<string, string>
        {
            ["item"] = "item.spear", ["holder"] = "subject.high"
        };
        var equipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.equip", roles,
            "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}", 0,
            "3123456789abcdef0123456789abcdee"));
        var blocked = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.transfer", new Dictionary<string, string>
            {
                ["item"] = "item.spear", ["source"] = "subject.high", ["destination"] = "subject.low"
            }, "{\"slot\":\"carried\"}", 0, "4123456789abcdef0123456789abcdee"));
        var read = await harness.EvaluateRolesAsync("dnd2024.mechanic.item.equipment.read",
            new Dictionary<string, string> { ["item"] = "item.spear" }, "{}", 0);
        var unequipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.unequip", roles, "{}", 0,
            "5123456789abcdef0123456789abcdee"));
        var transferred = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.transfer", new Dictionary<string, string>
            {
                ["item"] = "item.spear", ["source"] = "subject.high", ["destination"] = "subject.low"
            }, "{\"slot\":\"carried\"}", 0, "6123456789abcdef0123456789abcdee"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, equipped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, blocked.Disposition);
        Assert.Contains("\"entityId\":\"dnd2024.equipment-slot.main-hand\"",
            read.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, unequipped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, transferred.Disposition);
    }

    [Fact]
    public async Task Item_transfer_enforces_direct_container_weight_capacity_without_partial_move()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string itemDefinition = "dnd2024.item.stone.v1";
        const string bagDefinition = "dnd2024.item.bag.v1";
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
            "dnd2024.mechanic.item.transfer", rolesOne, "{\"slot\":\"inside\"}", 0,
            "9123456789abcdef0123456789abcdee"));
        var second = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.transfer", rolesTwo, "{\"slot\":\"inside\"}", 0,
            "a123456789abcdef0123456789abcdee"));
        var secondRead = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-instance.read",
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
        const string sourceDefinition = "dnd2024.item.package.v1";
        const string grantDefinition = "dnd2024.item.rope.v1";
        await harness.AddItemDefinitionAsync(grantDefinition, "Rope definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(sourceDefinition, "Package definition", FungibleItemDefinition(),
            "{\"activities\":[{\"id\":\"open\",\"kind\":\"consume-and-grant-item\",\"consumeQuantity\":1,\"grant\":{\"definitionId\":\"dnd2024.item.rope.v1\",\"name\":\"Rope\",\"slot\":\"unpacked\"}}]}");
        await harness.AddPhysicalItemAsync("item.package-stack", "Packages", sourceDefinition,
            "subject.high", quantity: 2);
        var roles = new Dictionary<string, string>
        {
            ["item"] = "item.package-stack", ["definition"] = sourceDefinition,
            ["grantDefinition"] = grantDefinition
        };
        const string input = "{\"activityId\":\"open\",\"grantItemId\":\"item.granted-rope\"}";
        var used = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-activity.use", roles, input, 0,
            "7123456789abcdef0123456789abcdee"));
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.package-stack", "dnd2024.item.quantity");
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-activity.use", roles, input, 0,
            "8123456789abcdef0123456789abcdee"));
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.package-stack", "dnd2024.item.quantity");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, used.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal(before!.ValueJson, after!.ValueJson);
        Assert.Contains("\"current\":1", after.ValueJson, StringComparison.Ordinal);
        Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.granted-rope"));
    }

    private static string SeparateItemDefinition(string? equipmentModes = null)
        => "{\"definitionVersion\":1,\"kind\":\"adventuring-gear\",\"stackPolicy\":\"separate\",\"massPounds\":{\"numerator\":1,\"denominator\":1}" +
           (equipmentModes is null ? "" : ",\"equipmentModes\":" + equipmentModes) +
           ",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Adventuring Gear\"}}";

    private static string FungibleItemDefinition()
        => "{\"definitionVersion\":1,\"kind\":\"ammunition\",\"stackPolicy\":\"fungible\",\"massPounds\":{\"numerator\":1,\"denominator\":20},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Ammunition\"}}";

    private static string ContainerItemDefinition(int maximumWeightPounds)
        => "{\"definitionVersion\":1,\"kind\":\"adventuring-gear\",\"stackPolicy\":\"separate\",\"massPounds\":{\"numerator\":1,\"denominator\":1},\"capacity\":{\"weightPounds\":{\"numerator\":" + maximumWeightPounds + ",\"denominator\":1}},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Adventuring Gear\"}}";

    private static string CurrencyItemDefinition(string denomination, int copperValue)
        => "{\"definitionVersion\":1,\"kind\":\"currency\",\"stackPolicy\":\"fungible\",\"massPounds\":{\"numerator\":1,\"denominator\":50},\"currency\":{\"denomination\":\"" + denomination + "\",\"copperValue\":" + copperValue + ",\"coinsPerPound\":50},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Coins\"}}";

    [Fact]
    public async Task Inventory_burden_and_carrying_capacity_compose_exact_bounded_views()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string robe = "dnd2024.item.robe-reader.v1";
        const string arrows = "dnd2024.item.arrow-reader.v1";
        await harness.AddItemDefinitionAsync(robe, "Robe definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(arrows, "Arrow definition", FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.reader.robe", "Robe", robe, "subject.high");
        await harness.AddPhysicalItemAsync("item.reader.arrows", "Arrows", arrows, "subject.high",
            quantity: 20);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.creature-size.record",
                new Dictionary<string, string> { ["creature"] = "subject.high" },
                "{\"size\":\"medium\"}", 0, "b123456789abcdef0123456789abcdee"))).Disposition);

        var inventory = await harness.EvaluateRolesAsync("dnd2024.mechanic.inventory.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var carrying = await harness.EvaluateRolesAsync("dnd2024.mechanic.carrying-capacity.read",
            new Dictionary<string, string> { ["creature"] = "subject.high" }, "{}", 0);

        Assert.True(inventory.Ok,
            string.Join("; ", inventory.Problems.Append(inventory.Run?.Error ?? string.Empty)));
        Assert.Contains("\"mayOmitDeeperContents\":true", inventory.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":45359237,\"denominator\":50000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.True(carrying.Ok, carrying.Run?.Error);
        Assert.Contains("\"carryingCapacity\":{\"dimension\":\"mass\",\"value\":{\"numerator\":408233133,\"denominator\":2000000}",
            carrying.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(carrying.Run.Output.Effects);
    }

    [Fact]
    public async Task Currency_reader_derives_mixed_physical_coin_value_without_wallet_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string cp = "dnd2024.equipment.currency.copper-piece";
        const string gp = "dnd2024.equipment.currency.gold-piece";
        await harness.AddCanonicalCurrencyDefinitionFixtureAsync("copper-piece");
        await harness.AddCanonicalCurrencyDefinitionFixtureAsync("gold-piece");
        await harness.AddPhysicalItemAsync("item.coins.cp", "Copper Pieces", cp, "subject.high",
            quantity: 10);
        await harness.AddPhysicalItemAsync("item.coins.gp", "Gold Pieces", gp, "subject.high",
            quantity: 2);

        var result = await harness.EvaluateRolesAsync("dnd2024.mechanic.currency-value.read",
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
            "entities", "equipment", "base");
        var paths = Directory.GetFiles(contentRoot, "equipment.currency.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(5, paths.Length);

        await using var harness = await DndHarness.CreateAsync();
        var definitions = new Dictionary<string, EntityFile>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            Assert.Contains(entity.Components, component =>
                component.DefinitionId == "dnd2024.core.source");
            Assert.Contains(entity.Components, component =>
                component.DefinitionId == "dnd2024.core.version");
            Assert.Contains(entity.Components, component =>
                component.DefinitionId == "dnd2024.item.physical");
            definitions.Add(entity.Id, entity);
            await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, entity.Id, entity.Name);
            foreach (var component in entity.Components)
                await harness.AddApplicationComponentAsync(
                    entity.Id, component.DefinitionId, component.Data);
        }

        var cp = definitions["dnd2024.equipment.currency.copper-piece"];
        var gp = definitions["dnd2024.equipment.currency.gold-piece"];
        await harness.AddPhysicalItemAsync("item.static-coins.cp", "Copper Pieces", cp.Id,
            "subject.high", quantity: 10);
        await harness.AddPhysicalItemAsync("item.static-coins.gp", "Gold Pieces", gp.Id,
            "subject.high", quantity: 2);

        var currency = await harness.EvaluateRolesAsync("dnd2024.mechanic.currency-value.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(currency.Ok, currency.Run?.Error);
        Assert.Contains("\"coinCount\":12", currency.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"copperValue\":210", currency.Run.Output.Data, StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"value\":{\"numerator\":136077711,\"denominator\":1250000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(currency.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_split_adventuring_gear_is_schema_valid_and_enforces_backpack_capacity()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "equipment", "base");
        var paths = new[] { "equipment.gear.backpack.json", "equipment.gear.waterskin.json" }
            .Select(file => Path.Combine(contentRoot, file)).ToArray();

        await using var harness = await DndHarness.CreateAsync();
        var definitions = new Dictionary<string, EntityFile>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            Assert.Contains(entity.Components,
                component => component.DefinitionId == "dnd2024.item.physical");
            using var source = JsonDocument.Parse(entity.Components.Single(component =>
                component.DefinitionId == "dnd2024.core.source").Data);
            Assert.Equal("dnd2024.source.srd-5.2.1", source.RootElement.GetProperty("citations")[0]
                .GetProperty("sourceRef").GetProperty("entityId").GetString());
            definitions.Add(entity.Id, entity);
            await harness.AddCatalogEntityAsync(entity);
        }

        await harness.AddPhysicalItemAsync("item.static.backpack", "Backpack",
            definitions["dnd2024.equipment.gear.backpack"].Id, "subject.low");
        for (var index = 0; index < 7; index++)
        {
            await harness.AddPhysicalItemAsync($"item.static.waterskin.{index}", $"Waterskin {index}",
                definitions["dnd2024.equipment.gear.waterskin"].Id, "subject.high");
        }

        for (var index = 0; index < 7; index++)
        {
            var moved = await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item.transfer", new Dictionary<string, string>
                {
                    ["item"] = $"item.static.waterskin.{index}",
                    ["source"] = "subject.high",
                    ["destination"] = "item.static.backpack"
                }, "{\"slot\":\"inside\"}", 0, (index + 1).ToString("x32")));
            var expected = index < 6
                ? ApplicationActionExecutionDisposition.Succeeded
                : ApplicationActionExecutionDisposition.Failed;
            Assert.True(moved.Disposition == expected,
                $"Waterskin {index} expected {expected} but was {moved.Disposition}: "
                + string.Join("; ", moved.Problems.Select(problem =>
                    problem.Code + ": " + problem.SafeMessage)));
        }

        var refused = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.static.waterskin.6" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.low" }, "{}", 0);
        Assert.True(refused.Ok, refused.Run?.Error);
        Assert.Contains("\"containerId\":\"subject.high\"", refused.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":317514659,\"denominator\":20000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(refused.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Optional_legacy_rope_is_consumed_only_when_extension_profile_is_selected()
    {
        const string relativePath =
            "catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/dnd2024.extension.legacy-equipment.item.hempen-rope-50-foot.v1.json";
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

        var burden = await extended.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":45359237,\"denominator\":20000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_catalog_item_facets_drive_equipment_and_burden_readers()
    {
        var root = RepositoryRoot();
        const string relative =
            "catalog/applications/dnd2024/content/entities/equipment/weapon/equipment.weapon.club.json";
        await using var harness = await DndHarness.CreateAsync();
        Assert.Contains(relative, harness.ActiveSourcePaths);
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
        Assert.Equal("dnd2024.equipment.weapon.club", entity.Id);
        Assert.Contains(entity.Components, value => value.DefinitionId == "dnd2024.item.physical");
        Assert.Contains(entity.Components, value => value.DefinitionId == "dnd2024.item.weapon");
        Assert.Contains(entity.Components, value => value.DefinitionId == "dnd2024.item.equippable");
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, entity.Id, entity.Name);
        foreach (var component in entity.Components)
            await harness.AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
        await harness.AddPhysicalItemAsync("item.catalog.club", "Club", entity.Id, "subject.high");

        var equipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.equip", new Dictionary<string, string>
            {
                ["item"] = "item.catalog.club",
                ["holder"] = "subject.high"
            }, "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}", 0,
            "e123456789abcdef0123456789abcdee"));
        var equipment = await harness.EvaluateRolesAsync("dnd2024.mechanic.item.equipment.read",
            new Dictionary<string, string> { ["item"] = "item.catalog.club" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, equipped.Disposition);
        Assert.True(equipment.Ok, equipment.Run?.Error);
        Assert.Contains("\"definitionId\":\"dnd2024.equipment.weapon.club\"",
            equipment.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"entityId\":\"dnd2024.equipment-slot.main-hand\"",
            equipment.Run.Output.Data, StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":45359237,\"denominator\":50000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(equipment.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Derived_inventory_readers_fail_closed_on_visible_item_missing_required_quantity()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.invalid-stack.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Invalid stack definition",
            FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.invalid-stack", "Invalid Stack", definitionId,
            "subject.high");
        var quantity = (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "item.invalid-stack", "dnd2024.item.quantity"))!;
        Assert.True(await harness.Entities.RemoveComponentAsync(DndHarness.StateSpaceId,
            "item.invalid-stack", quantity.Type, quantity.Revision));

        var inventory = await harness.EvaluateRolesAsync("dnd2024.mechanic.inventory.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
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
        var record = harness.ActionFor("dnd2024.mechanic.character-experience.write", "subject.high",
            "{\"mode\":\"record\",\"total\":250}", 0,
            "c123456789abcdef0123456789abcdee");
        var recorded = await harness.Runner.RunAsync(record);
        var replay = await harness.Runner.RunAsync(record);
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.character-experience.write", "subject.high",
            "{\"mode\":\"correct\",\"total\":300}", 0,
            "d123456789abcdef0123456789abcdee"));
        var read = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-experience.read");
        var unknown = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.character-experience.read");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Contains("\"status\":\"eligible-for-next-level\"", read.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"nextThreshold\":300", read.Run.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unknown\"", unknown.Run!.Output.Data,
            StringComparison.Ordinal);
        var experience = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.experience");
        Assert.Equal("{\"total\":300}", experience!.ValueJson);
        Assert.Empty(read.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_fighter_progression_is_closed_schema_valid_and_consumed_by_existing_reader()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "character-progression");
        var paths = Directory.GetFiles(contentRoot, "dnd2024.content.*.json")
            .Where(path => Path.GetFileName(path) == "dnd2024.content.class.fighter.v1.json"
                || Path.GetFileName(path).StartsWith("dnd2024.content.feature.fighter.",
                    StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(6, paths.Length);

        var validator = new BoundedJsonSchemaValidator();
        var contentSchema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "dnd2024.character.content-definition.schema.json"));
        var contentCompilation = validator.Compile(contentSchema);
        Assert.True(contentCompilation.IsAccepted, string.Join("; ", contentCompilation.Diagnostics));
        var progressionSchema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "dnd2024.class-progression.schema.json"));
        var progressionCompilation = validator.Compile(progressionSchema);
        Assert.True(progressionCompilation.IsAccepted,
            string.Join("; ", progressionCompilation.Diagnostics));

        var expectedFeatures = new HashSet<string>(StringComparer.Ordinal)
        {
            "dnd2024.content.feature.fighter.action-surge.v1",
            "dnd2024.content.feature.fighter.fighting-style.v1",
            "dnd2024.content.feature.fighter.second-wind.v1",
            "dnd2024.content.feature.fighter.tactical-mind.v1",
            "dnd2024.content.feature.fighter.weapon-mastery.v1"
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
        var level1 = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":1}", 0);
        var level2 = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":2}", long.MaxValue);
        var level3 = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
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
            "dnd2024.content.feature.fighter.fighting-style.v1",
            "dnd2024.content.feature.fighter.second-wind.v1",
            "dnd2024.content.feature.fighter.weapon-mastery.v1"
        }, level1Entitlements.Select(value => value.GetProperty("definitionId").GetString()));
        Assert.Equal(new[]
        {
            "dnd2024.content.feature.fighter.action-surge.v1",
            "dnd2024.content.feature.fighter.tactical-mind.v1"
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
        const string classId = "dnd2024.content.class.fighter.v1";
        const string locator = "Classes > Fighter PDF page 60";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, classId, "Fighter");
        await harness.AddApplicationComponentAsync(classId, "dnd2024.character.content-definition",
            "{\"kind\":\"class\",\"contentKey\":\"fighter\",\"contentVersion\":1,\"status\":\"active\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        var progression =
            "{\"hitDieSides\":10,\"fixedHitPointGainBeforeConstitution\":6,\"levels\":[{\"classLevel\":1,\"featureDefinitionIds\":[],\"choiceSetDefinitionIds\":[]},{\"classLevel\":2,\"featureDefinitionIds\":[\"dnd2024.content.feature.action-surge.v1\"],\"choiceSetDefinitionIds\":[]}],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}";
        await harness.AddApplicationComponentAsync(classId, "dnd2024.class-progression", progression);
        var roles = new Dictionary<string, string> { ["class"] = classId };

        var supported = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":2}", 0);
        var unsupported = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":3}", 0);
        await harness.ReplaceApplicationComponentRawAsync(classId, "dnd2024.class-progression",
            progression.Replace(locator, "Classes > Fighter PDF page 61", StringComparison.Ordinal));
        var mismatch = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
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

    public static TheoryData<string, int, string[], string[], string[], string[], string[], bool>
        BasicClassCreationCases => new()
        {
            { "barbarian", 12, ["str", "con"], ["perception", "survival"], ["simple", "martial"], [], ["light", "medium", "shield"], false },
            { "bard", 8, ["dex", "cha"], ["arcana", "perception", "persuasion"], ["simple"], [], ["light"], true },
            { "cleric", 8, ["wis", "cha"], ["insight", "religion"], ["simple"], [], ["light", "medium", "shield"], true },
            { "druid", 8, ["int", "wis"], ["nature", "perception"], ["simple"], [], ["light", "shield"], true },
            { "fighter", 10, ["str", "con"], ["perception", "survival"], ["simple", "martial"], [], ["light", "medium", "heavy", "shield"], false },
            { "monk", 8, ["str", "dex"], ["acrobatics", "insight"], ["simple"], ["light"], [], false },
            { "paladin", 10, ["wis", "cha"], ["persuasion", "religion"], ["simple", "martial"], [], ["light", "medium", "heavy", "shield"], true },
            { "ranger", 10, ["str", "dex"], ["nature", "perception", "stealth"], ["simple", "martial"], [], ["light", "medium", "shield"], true },
            { "rogue", 8, ["dex", "int"], ["acrobatics", "investigation", "sleight-of-hand", "stealth"], ["simple"], ["finesse", "light"], ["light"], false },
            { "sorcerer", 6, ["con", "cha"], ["arcana", "persuasion"], ["simple"], [], [], true },
            { "warlock", 8, ["wis", "cha"], ["arcana", "investigation"], ["simple"], [], ["light"], true },
            { "wizard", 6, ["int", "wis"], ["arcana", "investigation"], ["simple"], [], [], true }
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

    public static TheoryData<string, int> BasicClassStartingCashCases => new()
    {
        { "barbarian", 75 },
        { "bard", 90 },
        { "cleric", 110 },
        { "druid", 50 },
        { "fighter", 155 },
        { "monk", 50 },
        { "paladin", 150 },
        { "ranger", 150 },
        { "rogue", 100 },
        { "sorcerer", 50 },
        { "warlock", 100 },
        { "wizard", 55 }
    };

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"entitlements\":{}}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.feat.alert\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.background.criminal.v1\"},\"grantKind\":\"origin-feat\",\"configurationKey\":\"default\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Feats > Alert\"},\"behaviorStatus\":\"implemented\"}]}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.feat.alert\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.background.criminal.v1\"},\"grantKind\":\"origin-feat\",\"configurationKey\":\"default\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Feats > Alert\"}},{\"featureRef\":{\"entityId\":\"dnd2024.feat.alert\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.background.criminal.v1\"},\"grantKind\":\"origin-feat\",\"configurationKey\":\"default\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Feats > Alert\"}}]}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.content.feature.fighter.second-wind.v1\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.class.fighter.v1\"},\"grantKind\":\"origin-feat\",\"classLevel\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Classes > Fighter\"}}]}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.content.feature.fighter.second-wind.v1\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.class.fighter.v1\"},\"grantKind\":\"class-feature\",\"classLevel\":1,\"sourceRef\":{\"sourceId\":\"drifted source\",\"locator\":\"Classes > Fighter\"}}]}")]
    public async Task Character_feature_entitlement_schema_rejects_malformed_duplicate_extra_or_drifted_state(
        string valueJson)
    {
        var schema = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "catalog",
            "applications", "dnd2024", "components",
            "dnd2024.character.feature-entitlements.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);

        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(
            compilation.ProfileId, compilation.NormalizedSchema, valueJson).Status);
    }

    [Fact]
    public async Task Armor_training_owner_records_reads_corrects_and_replays_canonical_membership()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string actorId = "actor.armor-training.owner";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Armor Owner");
        var roles = new Dictionary<string, string> { ["subject"] = actorId };
        var absent = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.armor-training.read", roles, "{}", 0);
        var record = harness.ActionForRoles("dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"record\",\"categories\":[\"shield\",\"light\",\"medium\"]}", 0,
            "cc3e1000000000000000000000000001");
        var recorded = await harness.Runner.RunAsync(record);
        var replay = await harness.Runner.RunAsync(record);
        var read = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.armor-training.read", roles, "{}", 0);
        var corrected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"correct\",\"categories\":[]}", 0,
            "cc3e1000000000000000000000000002"));

        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Contains("\"problem\":\"absent\"", absent.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Empty(absent.Run.Output.Effects);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.True(read.Ok, read.Run?.Error);
        using var readData = JsonDocument.Parse(read.Run!.Output.Data);
        Assert.Equal(new[] { "light", "medium", "shield" }, readData.RootElement
            .GetProperty("categories").EnumerateArray().Select(value => value.GetString()));
        using var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
        Assert.Empty(ReadArmorTrainingIds(state.RootElement));
        Assert.Contains("armor-training", state.RootElement.GetProperty("recordedFamilies")
            .EnumerateArray().Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"light\",\"light\"]}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"cloth\"]}")]
    [InlineData("{\"mode\":\"correct\",\"categories\":[\"light\"]}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"light\"],\"sourceRef\":{}}")]
    public async Task Armor_training_owner_rejects_invalid_or_wrong_state_input_unchanged(string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        const string actorId = "actor.armor-training.invalid";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Invalid Armor");
        var roles = new Dictionary<string, string> { ["subject"] = actorId };

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles, input, 0,
            "cc3e1000000000000000000000000003"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"));
    }

    [Fact]
    public async Task Armor_training_owner_rejects_record_over_existing_and_invalid_prior_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string actorId = "actor.armor-training.prior";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Prior Armor");
        await harness.AddApplicationComponentAsync(actorId, "dnd2024.creature.proficiencies",
            "{\"categories\":[\"light\"],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary > Armor Training\"}}");
        var roles = new Dictionary<string, string> { ["subject"] = actorId };
        var duplicateRecord = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"record\",\"categories\":[\"light\"]}", 0,
            "cc3e1000000000000000000000000004"));
        await harness.ReplaceApplicationComponentRawAsync(actorId, "dnd2024.creature.proficiencies",
            "{\"entries\":{\"dnd2024.equipment.armor-category.light\":{\"rankRef\":{\"entityId\":\"dnd2024.vocabulary.proficiency-rank.expertise\"},\"sourceRefs\":[{\"entityId\":\"dnd2024.source.srd-5.2.1\"}]}},\"recordedFamilies\":[\"armor-training\"]}");
        var invalidRead = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.armor-training.read", roles, "{}", 0);
        var correction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"correct\",\"categories\":[\"light\"]}", 0,
            "cc3e1000000000000000000000000005"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicateRecord.Disposition);
        Assert.True(invalidRead.Ok, invalidRead.Run?.Error);
        Assert.Contains("\"problem\":\"invalid\"", invalidRead.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Empty(invalidRead.Run.Output.Effects);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, correction.Disposition);
    }

    [Fact]
    public async Task Basic_character_creation_commits_core_state_participation_pending_ledger_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.aric";
        const string worldId = "world.character-creation.fixture";
        var roles = BasicCreationRoles(worldId, "dnd2024.content.species.human.v1");
        const string input =
            "{\"characterId\":\"actor.basic.aric\",\"name\":\"Aric\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";

        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character.basic.create", roles, input, 0);
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "a123456789abcdef0123456789abcdf0");
        var created = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
        Assert.Equal(21, evaluated.Run!.Output.Effects.Count);
        Assert.Empty(evaluated.Run.Output.Events);
        Assert.Empty(evaluated.Run.Output.Notifications);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        Assert.Equal(21, created.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));

        using var abilities = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.ability-scores"))!.ValueJson);
        var abilityScores = abilities.RootElement.GetProperty("scores");
        Assert.Equal(17, abilityScores.GetProperty("dnd2024.vocabulary.ability.strength").GetInt32());
        Assert.Equal(14, abilityScores.GetProperty("dnd2024.vocabulary.ability.dexterity").GetInt32());
        Assert.Equal(14, abilityScores.GetProperty("dnd2024.vocabulary.ability.constitution").GetInt32());
        using var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.hit-points"))!.ValueJson);
        Assert.Equal(12, hitPoints.RootElement.GetProperty("current").GetInt32());
        Assert.Equal(12, hitPoints.RootElement.GetProperty("maximum").GetInt32());
        var armorClass = await harness.EvaluateAsync(actorId, "{}", 0,
            "dnd2024.mechanic.armor-class.read");
        Assert.True(armorClass.Ok, armorClass.Run?.Error);
        using var armorClassData = JsonDocument.Parse(armorClass.Run!.Output.Data);
        Assert.Equal(12, armorClassData.RootElement.GetProperty("armorClass").GetInt32());
        var level = await harness.EvaluateAsync(actorId, "{}", 0,
            "dnd2024.mechanic.character-level.read");
        Assert.True(level.Ok, level.Run?.Error);
        Assert.Contains("\"totalLevel\":1", level.Run!.Output.Data, StringComparison.Ordinal);
        using var proficiencies = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
        Assert.Equal(new[] { "light", "medium", "heavy", "shield" },
            ReadArmorTrainingIds(proficiencies.RootElement));
        Assert.Equal(new[] { "athletics", "intimidation", "perception", "survival" },
            ReadSkillIds(proficiencies.RootElement).ToArray());
        Assert.Equal(new[] { "str", "con" }, ReadSavingThrowIds(proficiencies.RootElement).ToArray());
        Assert.Equal(new[] { "simple", "martial" },
            ReadWeaponCategoryIds(proficiencies.RootElement).ToArray());
        Assert.Empty(ReadWeaponPropertyIds(proficiencies.RootElement));
        using var languages = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.languages"))!.ValueJson);
        Assert.Equal(new[] { "common" }, ReadLanguageIds(languages.RootElement).ToArray());
        using var featureEntitlements = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character.feature-entitlements"))!.ValueJson);
        var entitlements = featureEntitlements.RootElement.GetProperty("entitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(4, entitlements.Length);
        Assert.Equal(new[]
        {
            "dnd2024.content.feature.fighter.fighting-style.v1",
            "dnd2024.content.feature.fighter.second-wind.v1",
            "dnd2024.content.feature.fighter.weapon-mastery.v1"
        }, entitlements.Where(value => value.GetProperty("grantKind").GetString() == "class-feature")
            .Select(value => value.GetProperty("featureRef").GetProperty("entityId").GetString()));
        var originEntitlement = Assert.Single(entitlements, value =>
            value.GetProperty("grantKind").GetString() == "origin-feat");
        Assert.Equal("dnd2024.feat.savage-attacker",
            originEntitlement.GetProperty("featureRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.content.background.soldier.v1",
            originEntitlement.GetProperty("grantedByRef").GetProperty("entityId").GetString());
        Assert.Equal("default", originEntitlement.GetProperty("configurationKey").GetString());
        Assert.False(originEntitlement.TryGetProperty("behaviorStatus", out _));

        using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
        Assert.Equal("basic-playable", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("soldier-fighter-level-1-v1",
            record.RootElement.GetProperty("templateKey").GetString());
        Assert.Equal(13, record.RootElement.GetProperty("appliedComponentIds").GetArrayLength());
        Assert.Contains("dnd2024.character.origin-selections",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        using var origin = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character.origin-selections"))!.ValueJson);
        Assert.Equal("dnd2024.content.species.human.v1",
            origin.RootElement.GetProperty("speciesRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.content.background.soldier.v1",
            origin.RootElement.GetProperty("backgroundRef").GetProperty("entityId").GetString());
        Assert.DoesNotContain("dnd2024.combat.turn-budget",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains("dnd2024.creature.proficiencies",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains("dnd2024.character.feature-entitlements",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        var pending = record.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(11, pending.Length);
        Assert.DoesNotContain(pending, value => value.GetProperty("entitlementKey").GetString()!
            .StartsWith("armor-training:", StringComparison.Ordinal));
        Assert.Contains(pending, value => value.GetProperty("ownerDefinitionId").GetString() ==
            "dnd2024.content.species.human.v1" &&
            value.GetProperty("entitlementKey").GetString() == "trait:resourceful");
        Assert.Contains(pending, value => value.GetProperty("ownerDefinitionId").GetString() ==
            "dnd2024.content.feature.fighter.second-wind.v1" &&
            value.GetProperty("reason").GetString() == "behavior-unimplemented");
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character.heroic-inspiration"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.equipment-state"));
        Assert.DoesNotContain("tool", proficiencies.RootElement.GetProperty("recordedFamilies")
            .EnumerateArray().Select(value => value.GetString()));

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
        var sheet = await harness.EvaluateRolesAsync("dnd2024.mechanic.character-sheet.read",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0);
        var projectedSheet = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-sheet.project",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0);
        var registeredSheet = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId,
            ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.character-sheet",
            new Dictionary<string, string> { ["subject"] = actorId }));
        var registeredSheetV2 = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId,
            ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.character-sheet-v2",
            new Dictionary<string, string> { ["subject"] = actorId }));
        var initiative = await harness.EvaluateRolesAsync("dnd2024.mechanic.initiative.roll",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 17);
        Assert.True(sheet.Ok, sheet.Run?.Error);
        Assert.True(projectedSheet.Ok,
            projectedSheet.Run?.Error ?? string.Join("; ", projectedSheet.Problems));
        using (var projected = JsonDocument.Parse(projectedSheet.Run!.Output.Data))
        {
            Assert.Equal(actorId, projected.RootElement.GetProperty("subject").GetProperty("id").GetString());
            Assert.Equal(1, projected.RootElement.GetProperty("level").GetInt32());
            Assert.Equal(2, projected.RootElement.GetProperty("proficiencyBonus").GetInt32());
            Assert.Equal(6, projected.RootElement.GetProperty("abilities").GetArrayLength());
            Assert.Equal(6, projected.RootElement.GetProperty("savingThrows").GetArrayLength());
            Assert.Equal(18, projected.RootElement.GetProperty("skills").GetArrayLength());
            Assert.Equal(12, projected.RootElement.GetProperty("hitPoints").GetProperty("maximum").GetInt32());
            Assert.Equal(12, projected.RootElement.GetProperty("armorClass").GetProperty("value").GetInt32());
            Assert.Equal(4, projected.RootElement.GetProperty("features").GetArrayLength());
            Assert.Empty(projected.RootElement.GetProperty("inventory").GetProperty("items").EnumerateArray());
        }
        using (var registered = JsonDocument.Parse(registeredSheet.DataJson))
        {
            Assert.Equal(actorId, registered.RootElement.GetProperty("subject").GetProperty("id").GetString());
            Assert.Equal(6, registered.RootElement.GetProperty("abilities").GetArrayLength());
            Assert.Equal(18, registered.RootElement.GetProperty("skills").GetArrayLength());
        }
        using (var registered = JsonDocument.Parse(registeredSheetV2.DataJson))
        {
            Assert.Equal(2, registered.RootElement.GetProperty("version").GetInt32());
            Assert.Equal("Fighter", registered.RootElement.GetProperty("classes")[0]
                .GetProperty("class").GetProperty("label").GetString());
            Assert.Equal("Strength", registered.RootElement.GetProperty("abilities")[0]
                .GetProperty("ability").GetProperty("label").GetString());
            Assert.Equal(0, registered.RootElement.GetProperty("wallet")
                .GetProperty("copperValue").GetInt32());
        }
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.StateSpaceFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.ResolutionFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.OutputSchemaHash);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.ResultFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.SourceRevisionFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheetV2.OutputSchemaHash);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheetV2.ResultFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheetV2.SourceRevisionFingerprint);
        Assert.True(initiative.Ok, initiative.Run?.Error);
    }

    [Fact]
    public async Task Character_origin_materialization_commits_one_receipted_component_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.origin.materialize";
        const string input =
            "{\"characterId\":\"actor.origin.materialize\",\"name\":\"Restored Origin\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1"),
            input, 0, "cc4a0000000000000000000000000000"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);

        var origin = (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            actorId, "dnd2024.character.origin-selections"))!;
        Assert.True(await harness.Entities.RemoveComponentAsync(DndHarness.StateSpaceId,
            actorId, origin.Type, origin.Revision));
        var creationRecord = (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            actorId, "dnd2024.character-creation-record"))!.ValueJson;
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.character.origin.materialize",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0,
            "cc4a1000000000000000000000000000");

        var materialized = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, materialized.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(actorId, Assert.Single(materialized.AffectedEntityIds));
        var receipt = Assert.Single(materialized.EffectReceipts);
        Assert.Equal("component.add", receipt.Type);
        Assert.Equal(actorId, receipt.EntityId);
        Assert.Equal("dnd2024.character.origin-selections", receipt.QualifiedTypeId);
        using var restored = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.character.origin-selections"))!.ValueJson);
        Assert.Equal("dnd2024.content.species.human.v1",
            restored.RootElement.GetProperty("speciesRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.content.background.soldier.v1",
            restored.RootElement.GetProperty("backgroundRef").GetProperty("entityId").GetString());
        Assert.Equal(creationRecord, (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.character-creation-record"))!.ValueJson);
    }

    [Theory]
    [MemberData(nameof(BasicClassCreationCases))]
    public async Task Basic_character_creation_supports_every_srd_level_one_class_model(
        string classKey,
        int hitDieSides,
        string[] savingThrows,
        string[] classSkills,
        string[] weaponCategories,
        string[] restrictedMartialProperties,
        string[] armorTraining,
        bool spellcastingPending)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var actorId = "actor.basic." + classKey;
        var classId = "dnd2024.content.class." + classKey + ".v1";
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
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", classId),
            input, 0, "1123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
        using var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.hit-points"))!.ValueJson);
        Assert.Equal(hitDieSides + 2,
            hitPoints.RootElement.GetProperty("maximum").GetInt32());
        using var proficiencies = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.creature.proficiencies"))!.ValueJson);
        Assert.Equal(savingThrows, ReadSavingThrowIds(proficiencies.RootElement).ToArray());
        Assert.Equal(new[] { "athletics", "intimidation" }.Concat(classSkills)
                .Order(StringComparer.Ordinal),
            ReadSkillIds(proficiencies.RootElement));
        Assert.Equal(weaponCategories, ReadWeaponCategoryIds(proficiencies.RootElement).ToArray());
        Assert.Equal(restrictedMartialProperties,
            ReadWeaponPropertyIds(proficiencies.RootElement).ToArray());
        Assert.Equal(armorTraining, ReadArmorTrainingIds(proficiencies.RootElement).ToArray());

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
        Assert.Equal(restrictedMartialProperties.Length > 0, pending.Any(value =>
            value.GetProperty("ownerDefinitionId").GetString() == classId &&
            value.GetProperty("entitlementKey").GetString()!.StartsWith(
                "weapon:restricted-martial-attack-enforcement:", StringComparison.Ordinal) &&
            value.GetProperty("reason").GetString() == "behavior-unimplemented"));
        Assert.DoesNotContain(pending, value =>
            value.GetProperty("ownerDefinitionId").GetString() == classId &&
            value.GetProperty("entitlementKey").GetString()!.StartsWith(
                "weapon:", StringComparison.Ordinal) &&
            value.GetProperty("reason").GetString() == "state-owner-unavailable");
        Assert.DoesNotContain(pending, value => value.GetProperty("entitlementKey").GetString()!
            .StartsWith("armor-training:", StringComparison.Ordinal));
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
        var classId = "dnd2024.content.class." + classKey + ".v1";

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
        var classId = "dnd2024.content.class." + classKey + ".v1";

        using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId,
            "dnd2024.class-creation-profile"))!.ValueJson);
        var primary = profile.RootElement.GetProperty("primaryAbilities");
        Assert.Equal(mode, primary.GetProperty("mode").GetString());
        Assert.Equal(abilities, primary.GetProperty("abilities").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
    }

    [Theory]
    [MemberData(nameof(BasicClassStartingCashCases))]
    public async Task Basic_character_creation_class_models_declare_exact_starting_cash(
        string classKey,
        int cashGp)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var classId = "dnd2024.content.class." + classKey + ".v1";

        using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId,
            "dnd2024.class-creation-profile"))!.ValueJson);

        Assert.Equal(cashGp,
            profile.RootElement.GetProperty("startingEquipmentCashGp").GetInt32());
        Assert.Equal("dnd2024.equipment.currency.gold-piece",
            profile.RootElement.GetProperty("startingEquipmentCurrencyDefinitionId").GetString());
    }

    [Fact]
    public async Task Basic_character_creation_background_models_preserve_exact_srd_declarations()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var cases = new[]
        {
            new
            {
                Key = "acolyte", EligibleAbilities = new[] { "int", "wis", "cha" },
                Skills = new[] { "insight", "religion" }, ToolKind = "fixed",
                ToolValue = "calligraphers-supplies",
                Feat = "dnd2024.feat.magic-initiate", Configuration = "cleric",
                CurrencyGp = 8,
                Entries = new[] { "calligraphers-supplies:1", "book-prayers:1", "holy-symbol:1", "parchment-sheet:10", "robe:1" }
            },
            new
            {
                Key = "criminal", EligibleAbilities = new[] { "dex", "con", "int" },
                Skills = new[] { "sleight-of-hand", "stealth" }, ToolKind = "fixed",
                ToolValue = "thieves-tools", Feat = "dnd2024.feat.alert",
                Configuration = "default", CurrencyGp = 16,
                Entries = new[] { "dagger:2", "thieves-tools:1", "crowbar:1", "pouch:2", "travelers-clothes:1" }
            },
            new
            {
                Key = "sage", EligibleAbilities = new[] { "con", "int", "wis" },
                Skills = new[] { "arcana", "history" }, ToolKind = "fixed",
                ToolValue = "calligraphers-supplies",
                Feat = "dnd2024.feat.magic-initiate", Configuration = "wizard",
                CurrencyGp = 8,
                Entries = new[] { "quarterstaff:1", "calligraphers-supplies:1", "book-history:1", "parchment-sheet:8", "robe:1" }
            },
            new
            {
                Key = "soldier", EligibleAbilities = new[] { "str", "dex", "con" },
                Skills = new[] { "athletics", "intimidation" }, ToolKind = "choice",
                ToolValue = "gaming-set", Feat = "dnd2024.feat.savage-attacker",
                Configuration = "default", CurrencyGp = 14,
                Entries = new[] { "spear:1", "shortbow:1", "arrow:20", "@background-tool:1", "healers-kit:1", "quiver:1", "travelers-clothes:1" }
            }
        };

        foreach (var background in cases)
        {
            var backgroundId = "dnd2024.content.background." + background.Key + ".v1";
            using var abilities = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, backgroundId,
                "dnd2024.background.ability-increase-options"))!.ValueJson);
            Assert.Equal(background.EligibleAbilities,
                abilities.RootElement.GetProperty("eligibleAbilities").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());
            Assert.Equal(new[] { "plus-2-plus-1", "plus-1-each" },
                abilities.RootElement.GetProperty("allowedPatterns").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());

            using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, backgroundId,
                "dnd2024.background-creation-profile"))!.ValueJson);
            Assert.Equal(background.Key, profile.RootElement.GetProperty("backgroundKey").GetString());
            Assert.Equal(background.Skills,
                profile.RootElement.GetProperty("skillProficiencies").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());
            var tool = profile.RootElement.GetProperty("toolProficiency");
            Assert.Equal(background.ToolKind, tool.GetProperty("kind").GetString());
            Assert.Equal(background.ToolValue,
                tool.GetProperty(background.ToolKind == "fixed" ? "toolId" : "optionFamily").GetString());
            var feat = profile.RootElement.GetProperty("originFeat");
            Assert.Equal(background.Feat, feat.GetProperty("definitionId").GetString());
            Assert.Equal(background.Configuration, feat.GetProperty("configurationKey").GetString());
            var equipment = profile.RootElement.GetProperty("startingEquipment");
            Assert.Equal(background.CurrencyGp, equipment.GetProperty("packageCurrencyGp").GetInt32());
            Assert.Equal(50, equipment.GetProperty("cashAlternativeGp").GetInt32());
            Assert.Equal(background.Entries,
                equipment.GetProperty("packageEntries").EnumerateArray().Select(value =>
                    value.GetProperty("kind").GetString() == "item"
                        ? value.GetProperty("itemKey").GetString() + ":" + value.GetProperty("quantity").GetInt32()
                        : "@" + value.GetProperty("selectionKey").GetString() + ":" + value.GetProperty("quantity").GetInt32())
                    .ToArray());
        }
    }

    [Fact]
    public async Task Basic_character_creation_composes_every_srd_background_and_class_pair()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var backgrounds = new[]
        {
            new { Key = "acolyte", PlusTwo = "int", PlusOne = "wis", Skills = new[] { "insight", "religion" }, FixedTool = (string?)"calligraphers-supplies", Feat = "dnd2024.feat.magic-initiate", Configuration = "cleric" },
            new { Key = "criminal", PlusTwo = "dex", PlusOne = "con", Skills = new[] { "sleight-of-hand", "stealth" }, FixedTool = (string?)"thieves-tools", Feat = "dnd2024.feat.alert", Configuration = "default" },
            new { Key = "sage", PlusTwo = "int", PlusOne = "wis", Skills = new[] { "arcana", "history" }, FixedTool = (string?)"calligraphers-supplies", Feat = "dnd2024.feat.magic-initiate", Configuration = "wizard" },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Skills = new[] { "athletics", "intimidation" }, FixedTool = (string?)null, Feat = "dnd2024.feat.savage-attacker", Configuration = "default" }
        };
        var classKeys = new[]
        {
            "barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger",
            "rogue", "sorcerer", "warlock", "wizard"
        };
        var operation = 0;

        foreach (var background in backgrounds)
        foreach (var classKey in classKeys)
        {
            operation++;
            var actorId = "actor.basic." + background.Key + "." + classKey;
            var backgroundId = "dnd2024.content.background." + background.Key + ".v1";
            var classId = "dnd2024.content.class." + classKey + ".v1";
            using var classProfile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, classId, "dnd2024.class-creation-profile"))!.ValueJson);
            var skillsProfile = classProfile.RootElement.GetProperty("skills");
            var classChoiceCount = skillsProfile.GetProperty("choiceCount").GetInt32();
            var classOptions = skillsProfile.GetProperty("options").EnumerateArray()
                .Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
            var classFixedTools = classProfile.RootElement.GetProperty("tools").GetProperty("fixed")
                .EnumerateArray().Select(value => value.GetString()!).ToArray();
            var hasClassToolChoice = classProfile.RootElement.GetProperty("tools")
                .GetProperty("choiceGroups").GetArrayLength() > 0;
            var expectedArmorTraining = classProfile.RootElement.GetProperty("armorTraining")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            var expectedWeaponRestrictions = classProfile.RootElement.GetProperty("weapons")
                .GetProperty("restrictedMartialProperties").EnumerateArray()
                .Select(value => value.GetString()).ToArray();
            var input = JsonSerializer.Serialize(new
            {
                characterId = actorId,
                name = background.Key + " " + classKey,
                ability = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [background.PlusTwo] = 2,
                        [background.PlusOne] = 1
                    }
                },
                speciesSelection = new { size = "medium" }
            });

            var result = await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create",
                BasicCreationRoles("world.character-creation.fixture",
                    "dnd2024.content.species.human.v1", classId, backgroundId),
                input, 0, operation.ToString("x32")));

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
            Assert.Equal(background.Key + "-" + classKey + "-level-1-v1",
                record.RootElement.GetProperty("templateKey").GetString());
            var selections = record.RootElement.GetProperty("selections");
            Assert.Equal(backgroundId, selections.GetProperty("backgroundDefinitionId").GetString());
            Assert.False(selections.TryGetProperty("classToolChoices", out _));
            Assert.False(selections.TryGetProperty("startingEquipmentChoices", out _));
            Assert.False(record.RootElement.TryGetProperty("createdItemIds", out _));
            Assert.Null(await harness.Entities.GetEntityAsync(
                DndHarness.StateSpaceId, "item.starting-gold." + actorId));
            var classSkills = selections.GetProperty("classSkillChoices").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            Assert.Equal(classChoiceCount, classSkills.Length);
            Assert.Equal(classChoiceCount, classSkills.Distinct(StringComparer.Ordinal).Count());
            Assert.All(classSkills, value => Assert.Contains(value, classOptions));
            Assert.DoesNotContain(classSkills, value => background.Skills.Contains(
                value, StringComparer.Ordinal));

            using var proficiencies = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
            Assert.Equal(background.Skills.Concat(classSkills).Order(StringComparer.Ordinal),
                ReadSkillIds(proficiencies.RootElement));
            using var languages = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.languages"))!.ValueJson);
            Assert.Equal(new[] { "common" }, ReadLanguageIds(languages.RootElement).ToArray());
            Assert.Equal(expectedArmorTraining,
                ReadArmorTrainingIds(proficiencies.RootElement).ToArray());
            Assert.Equal(expectedWeaponRestrictions,
                ReadWeaponPropertyIds(proficiencies.RootElement).ToArray());

            using var backgroundProfile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, backgroundId,
                "dnd2024.background-creation-profile"))!.ValueJson);
            using var classProgression = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, classId, "dnd2024.class-progression"))!.ValueJson);
            var expectedClassFeatures = classProgression.RootElement.GetProperty("levels")[0]
                .GetProperty("featureDefinitionIds").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            using var featureEntitlements = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId,
                "dnd2024.character.feature-entitlements"))!.ValueJson);
            var entitlements = featureEntitlements.RootElement.GetProperty("entitlements")
                .EnumerateArray().ToArray();
            Assert.Equal(expectedClassFeatures.Length + 1, entitlements.Length);
            Assert.Equal(expectedClassFeatures,
                entitlements.Where(value => value.GetProperty("grantKind").GetString() == "class-feature")
                    .Select(value => value.GetProperty("featureRef").GetProperty("entityId").GetString()));
            Assert.All(entitlements.Where(value =>
                    value.GetProperty("grantKind").GetString() == "class-feature"), value =>
                {
                    Assert.Equal(classId,
                        value.GetProperty("grantedByRef").GetProperty("entityId").GetString());
                    Assert.Equal(1, value.GetProperty("classLevel").GetInt32());
                    Assert.Equal(classProfile.RootElement.GetProperty("sourceRef").GetRawText(),
                        value.GetProperty("sourceRef").GetRawText());
                    Assert.False(value.TryGetProperty("behaviorStatus", out _));
                });
            var originEntitlement = Assert.Single(entitlements, value =>
                value.GetProperty("grantKind").GetString() == "origin-feat");
            Assert.Equal(background.Feat,
                originEntitlement.GetProperty("featureRef").GetProperty("entityId").GetString());
            Assert.Equal(backgroundId,
                originEntitlement.GetProperty("grantedByRef").GetProperty("entityId").GetString());
            Assert.Equal(background.Configuration,
                originEntitlement.GetProperty("configurationKey").GetString());
            Assert.Equal(backgroundProfile.RootElement.GetProperty("sourceRef").GetRawText(),
                originEntitlement.GetProperty("sourceRef").GetRawText());
            Assert.False(originEntitlement.TryGetProperty("behaviorStatus", out _));

            foreach (var entitlement in entitlements.Where(value =>
                         value.GetProperty("grantKind").GetString() == "class-feature"))
            {
                var definitionId = entitlement.GetProperty("featureRef")
                    .GetProperty("entityId").GetString()!;
                using var definition = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                    DndHarness.StateSpaceId, definitionId,
                    "dnd2024.character.content-definition"))!.ValueJson);
                Assert.Equal("feature", definition.RootElement.GetProperty("kind").GetString());
                Assert.Equal("active", definition.RootElement.GetProperty("status").GetString());
                Assert.Equal("dnd2024.source.srd-5.2.1",
                    definition.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
            }

            var expectedTools = classFixedTools
                .Concat(background.FixedTool is null ? [] : [background.FixedTool])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var toolState = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies");
            Assert.NotNull(toolState);
            using var toolProficiencies = JsonDocument.Parse(toolState!.ValueJson);
            if (expectedTools.Length == 0)
            {
                Assert.Empty(ReadToolIds(toolProficiencies.RootElement));
                Assert.DoesNotContain("tool", toolProficiencies.RootElement
                    .GetProperty("recordedFamilies").EnumerateArray()
                    .Select(value => value.GetString()));
            }
            else
            {
                Assert.Equal(expectedTools, ReadToolIds(toolProficiencies.RootElement).ToArray());
            }

            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == background.Feat &&
                value.GetProperty("entitlementKey").GetString() ==
                (background.Feat == "dnd2024.feat.alert"
                    ? "behavior:initiative-swap"
                    : background.Configuration == "default"
                    ? "behavior"
                    : "behavior:configuration:" + background.Configuration));
            Assert.All(expectedClassFeatures, featureId => Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == featureId &&
                value.GetProperty("entitlementKey").GetString() == "behavior" &&
                value.GetProperty("reason").GetString() == "behavior-unimplemented"));
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString() == "origin-language-choice:2:standard");
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "equipment:starting-package-or-50-gp", StringComparison.Ordinal));
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString() ==
                    "equipment:starting-package");
            Assert.Equal(background.Key == "soldier", pending.Any(value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString() == "tool-choice:1:gaming-set"));
            Assert.Equal(hasClassToolChoice, pending.Any(value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "tool-choice:", StringComparison.Ordinal)));
            Assert.Equal(expectedWeaponRestrictions.Length > 0, pending.Any(value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "weapon:restricted-martial-attack-enforcement:", StringComparison.Ordinal) &&
                value.GetProperty("reason").GetString() == "behavior-unimplemented"));
            Assert.DoesNotContain(pending, value => value.GetProperty("entitlementKey").GetString()!
                .StartsWith("armor-training:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Basic_character_creation_cash_alternative_composes_every_background_and_class_pair_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        var backgrounds = new[]
        {
            new { Key = "acolyte", PlusTwo = "int", PlusOne = "wis" },
            new { Key = "criminal", PlusTwo = "dex", PlusOne = "con" },
            new { Key = "sage", PlusTwo = "int", PlusOne = "wis" },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con" }
        };
        var classes = new[]
        {
            new { Key = "barbarian", CashGp = 75 },
            new { Key = "bard", CashGp = 90 },
            new { Key = "cleric", CashGp = 110 },
            new { Key = "druid", CashGp = 50 },
            new { Key = "fighter", CashGp = 155 },
            new { Key = "monk", CashGp = 50 },
            new { Key = "paladin", CashGp = 150 },
            new { Key = "ranger", CashGp = 150 },
            new { Key = "rogue", CashGp = 100 },
            new { Key = "sorcerer", CashGp = 50 },
            new { Key = "warlock", CashGp = 100 },
            new { Key = "wizard", CashGp = 55 }
        };
        var operation = 256;

        foreach (var background in backgrounds)
        foreach (var @class in classes)
        {
            operation++;
            var actorId = "actor.cash." + background.Key + "." + @class.Key;
            var itemId = "item.starting-gold." + actorId;
            var backgroundId = "dnd2024.content.background." + background.Key + ".v1";
            var classId = "dnd2024.content.class." + @class.Key + ".v1";
            var input = JsonSerializer.Serialize(new
            {
                characterId = actorId,
                name = background.Key + " cash " + @class.Key,
                ability = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [background.PlusTwo] = 2,
                        [background.PlusOne] = 1
                    }
                },
                speciesSelection = new { size = "medium" },
                equipmentChoices = new { background = "cash", @class = "cash" }
            });
            var roles = BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", classId, backgroundId);
            roles["currency"] = "dnd2024.equipment.currency.gold-piece";
            var request = harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create", roles, input, 0,
                operation.ToString("x32"));

            var created = await harness.Runner.RunAsync(request);
            var replay = await harness.Runner.RunAsync(request);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
            using var instance = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, itemId, "dnd2024.core.definition-link"))!.ValueJson);
            Assert.Equal("dnd2024.equipment.currency.gold-piece",
                instance.RootElement.GetProperty("definition").GetProperty("entityId").GetString());
            using var quantity = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, itemId, "dnd2024.item.quantity"))!.ValueJson);
            Assert.Equal(50 + @class.CashGp,
                quantity.RootElement.GetProperty("current").GetInt32());
            var containment = await harness.Edges.GetContainmentAsync(
                DndHarness.StateSpaceId, itemId);
            Assert.NotNull(containment);
            Assert.Equal(actorId, containment.ContainerEntityId);
            Assert.Equal("inventory.currency", containment.Slot);

            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId,
                "dnd2024.character-creation-record"))!.ValueJson);
            var choices = record.RootElement.GetProperty("selections")
                .GetProperty("startingEquipmentChoices");
            Assert.Equal("cash", choices.GetProperty("background").GetString());
            Assert.Equal("cash", choices.GetProperty("class").GetString());
            Assert.Equal(new[] { itemId }, record.RootElement.GetProperty("createdItemIds")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "equipment:", StringComparison.Ordinal));
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "equipment:", StringComparison.Ordinal));
            Assert.Contains(record.RootElement.GetProperty("sourceRefs").EnumerateArray(), value =>
                value.GetProperty("locator").GetString() ==
                    "Equipment > Coins > Coin Values > Gold Piece (SRD 5.2.1, pages 89-89)");
        }
    }

    [Fact]
    public async Task Basic_character_creation_cash_is_visible_to_existing_inventory_and_currency_readers()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.readers";
        const string itemId = "item.starting-gold.actor.cash.readers";
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1",
            "dnd2024.content.class.wizard.v1",
            "dnd2024.content.background.sage.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.readers\",\"name\":\"Cash Readers\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"int\":2,\"wis\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000001"));
        var inventory = await harness.EvaluateRolesAsync("dnd2024.mechanic.inventory.read",
            new Dictionary<string, string> { ["root"] = actorId }, "{}", 0);
        var currency = await harness.EvaluateRolesAsync("dnd2024.mechanic.currency-value.read",
            new Dictionary<string, string> { ["root"] = actorId }, "{}", 0);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        Assert.True(inventory.Ok,
            string.Join("; ", inventory.Problems.Append(inventory.Run?.Error ?? string.Empty)));
        using var inventoryData = JsonDocument.Parse(inventory.Run!.Output.Data);
        var visible = Assert.Single(inventoryData.RootElement.GetProperty("items")
            .EnumerateArray());
        Assert.Equal(itemId, visible.GetProperty("itemId").GetString());
        Assert.Equal(105, visible.GetProperty("quantity").GetInt32());
        Assert.Equal("inventory.currency", visible.GetProperty("slot").GetString());
        Assert.True(currency.Ok, currency.Run?.Error);
        using var currencyData = JsonDocument.Parse(currency.Run!.Output.Data);
        Assert.Equal(105, currencyData.RootElement.GetProperty("coinCount").GetInt32());
        Assert.Equal(10500, currencyData.RootElement.GetProperty("copperValue").GetInt32());
        var gp = Assert.Single(currencyData.RootElement.GetProperty("denominations")
            .EnumerateArray());
        Assert.Equal("gp", gp.GetProperty("code").GetString());
        Assert.Equal(105, gp.GetProperty("count").GetInt32());
        Assert.Empty(inventory.Run.Output.Effects);
        Assert.Empty(currency.Run.Output.Effects);
    }

    [Theory]
    [InlineData("{\"background\":\"cash\"}")]
    [InlineData("{\"class\":\"cash\"}")]
    [InlineData("{\"background\":\"cash\",\"class\":\"package\"}")]
    [InlineData("{\"background\":\"cash\",\"class\":\"cash\",\"amount\":205}")]
    public async Task Basic_character_creation_cash_rejects_partial_non_cash_or_derived_choices_unchanged(
        string equipmentChoices)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.invalid-choice";
        const string itemId = "item.starting-gold.actor.cash.invalid-choice";
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string prefix =
            "{\"characterId\":\"actor.cash.invalid-choice\",\"name\":\"Invalid Cash Choice\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles,
            prefix + equipmentChoices + "}", 0,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(equipmentChoices)))
                .ToLowerInvariant()[..32]));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("corrupt")]
    public async Task Basic_character_creation_cash_rejects_missing_wrong_or_corrupt_currency_role_unchanged(
        string invalidRole)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.invalid-role";
        const string itemId = "item.starting-gold.actor.cash.invalid-role";
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        if (invalidRole == "wrong")
        {
            await harness.AddItemDefinitionAsync("currency.test.wrong-gold.v1", "Wrong Gold",
                CurrencyItemDefinition("gp", 100));
            roles["currency"] = "currency.test.wrong-gold.v1";
        }
        else if (invalidRole == "corrupt")
        {
            roles["currency"] = "dnd2024.equipment.currency.gold-piece";
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.equipment.currency.gold-piece", "dnd2024.item.physical",
                "{\"weight\":{\"dimension\":\"mass\",\"value\":{\"numerator\":1,\"denominator\":1},\"unit\":{\"entityId\":\"dnd2024.vocabulary.mass-unit.kilogram\"}}}");
        }
        const string input =
            "{\"characterId\":\"actor.cash.invalid-role\",\"name\":\"Invalid Cash Role\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            invalidRole switch
            {
                "missing" => "cc3f1000000000000000000000000010",
                "wrong" => "cc3f1000000000000000000000000011",
                _ => "cc3f1000000000000000000000000012"
            }));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
    }

    [Fact]
    public async Task Basic_character_creation_legacy_class_profile_keeps_omitted_path_but_denies_cash()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string classId = "dnd2024.content.class.fighter.v1";
        var current = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId, "dnd2024.class-creation-profile");
        var legacy = current!.ValueJson.Replace(
            "\"startingEquipmentCashGp\":155,\"startingEquipmentCurrencyDefinitionId\":\"dnd2024.equipment.currency.gold-piece\",",
            "", StringComparison.Ordinal);
        Assert.NotEqual(current.ValueJson, legacy);
        await harness.ReplaceApplicationComponentRawAsync(
            classId, "dnd2024.class-creation-profile", legacy);
        var omittedRoles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        const string omittedInput =
            "{\"characterId\":\"actor.cash.legacy-omitted\",\"name\":\"Legacy Omitted\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var omitted = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", omittedRoles, omittedInput, 0,
            "cc3f1000000000000000000000000020"));
        var cashRoles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        cashRoles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string cashInput =
            "{\"characterId\":\"actor.cash.legacy-cash\",\"name\":\"Legacy Cash\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";
        var cash = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", cashRoles, cashInput, 0,
            "cc3f1000000000000000000000000021"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, omitted.Disposition);
        using var omittedRecord = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "actor.cash.legacy-omitted",
            "dnd2024.character-creation-record"))!.ValueJson);
        Assert.False(omittedRecord.RootElement.TryGetProperty("createdItemIds", out _));
        Assert.Contains(omittedRecord.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray(), value => value.GetProperty("ownerDefinitionId").GetString() ==
                classId && value.GetProperty("entitlementKey").GetString() ==
                "equipment:starting-package");
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, cash.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "actor.cash.legacy-cash"));
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "item.starting-gold.actor.cash.legacy-cash"));
    }

    [Theory]
    [InlineData("missing-cash")]
    [InlineData("missing-definition")]
    [InlineData("invalid-cash")]
    public async Task Basic_character_creation_cash_rejects_partial_or_invalid_class_cash_declaration(
        string invalidProfile)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string classId = "dnd2024.content.class.fighter.v1";
        var current = (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId, "dnd2024.class-creation-profile"))!.ValueJson;
        var invalid = invalidProfile switch
        {
            "missing-cash" => current.Replace("\"startingEquipmentCashGp\":155,", "",
                StringComparison.Ordinal),
            "missing-definition" => current.Replace(
                "\"startingEquipmentCurrencyDefinitionId\":\"dnd2024.equipment.currency.gold-piece\",",
                "", StringComparison.Ordinal),
            _ => current.Replace("\"startingEquipmentCashGp\":155",
                "\"startingEquipmentCashGp\":0", StringComparison.Ordinal)
        };
        Assert.NotEqual(current, invalid);
        await harness.ReplaceApplicationComponentRawAsync(
            classId, "dnd2024.class-creation-profile", invalid);
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.invalid-profile\",\"name\":\"Invalid Cash Profile\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            invalidProfile switch
            {
                "missing-cash" => "cc3f1000000000000000000000000030",
                "missing-definition" => "cc3f1000000000000000000000000031",
                _ => "cc3f1000000000000000000000000032"
            }));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "actor.cash.invalid-profile"));
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "item.starting-gold.actor.cash.invalid-profile"));
    }

    [Fact]
    public async Task Basic_character_creation_cash_rejects_invalid_background_cash_declaration()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string backgroundId = "dnd2024.content.background.soldier.v1";
        var current = (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, backgroundId,
            "dnd2024.background-creation-profile"))!.ValueJson;
        var invalid = current.Replace("\"cashAlternativeGp\":50",
            "\"cashAlternativeGp\":49", StringComparison.Ordinal);
        Assert.NotEqual(current, invalid);
        await harness.ReplaceApplicationComponentRawAsync(
            backgroundId, "dnd2024.background-creation-profile", invalid);
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.invalid-background\",\"name\":\"Invalid Cash Background\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000033"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "actor.cash.invalid-background"));
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId,
            "item.starting-gold.actor.cash.invalid-background"));
    }

    [Fact]
    public async Task Basic_character_creation_cash_rejects_overlong_derived_item_id_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        var actorId = "actor." + new string('a', 184);
        Assert.InRange(actorId.Length, 1, 200);
        Assert.True(("item.starting-gold." + actorId).Length > 200);
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Overlong Derived Item",
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new { str = 2, con = 1 }
            },
            speciesSelection = new { size = "medium" },
            equipmentChoices = new { background = "cash", @class = "cash" }
        });
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000040"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.DoesNotContain(await harness.Edges.ListContainmentsAsync(DndHarness.StateSpaceId),
            value => value.ContainedEntityId.StartsWith(
                "item.starting-gold.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Basic_character_creation_cash_collision_leaves_actor_and_existing_item_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.collision";
        const string itemId = "item.starting-gold.actor.cash.collision";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, itemId, "Existing Item");
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.collision\",\"name\":\"Cash Collision\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000041"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        var existing = await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId);
        Assert.NotNull(existing);
        Assert.Equal("Existing Item", existing.Name);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, itemId, "dnd2024.core.definition-link"));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
    }

    [Fact]
    public async Task Basic_character_creation_cash_rolls_back_item_and_containment_after_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.rollback";
        const string itemId = "item.starting-gold.actor.cash.rollback";
        const string worldId = "world.character-creation.fixture";
        var roles = BasicCreationRoles(worldId, "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.rollback\",\"name\":\"Cash Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000050"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
        var participationId = worldId + ".participation." + actorId;
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, participationId));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            worldId, participationId,
            "dnd2024.campaign.has-character-participation"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            participationId, actorId,
            "dnd2024.campaign.character-participation.for-actor"));
    }

    [Fact]
    public async Task Basic_character_creation_origin_choices_apply_languages_and_background_tools_and_replay()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var cases = new[]
        {
            new { Key = "acolyte", PlusTwo = "int", PlusOne = "wis", Languages = new[] { "elvish", "draconic" }, Tool = (string?)null, FixedTool = (string?)"calligraphers-supplies" },
            new { Key = "criminal", PlusTwo = "dex", PlusOne = "con", Languages = new[] { "orc", "dwarvish" }, Tool = (string?)null, FixedTool = (string?)"thieves-tools" },
            new { Key = "sage", PlusTwo = "int", PlusOne = "wis", Languages = new[] { "gnomish", "common-sign-language" }, Tool = (string?)null, FixedTool = (string?)"calligraphers-supplies" },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "goblin", "giant" }, Tool = (string?)"dice-set", FixedTool = (string?)null },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "halfling", "draconic" }, Tool = (string?)"dragonchess-set", FixedTool = (string?)null },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "elvish", "orc" }, Tool = (string?)"playing-cards", FixedTool = (string?)null },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "dwarvish", "gnomish" }, Tool = (string?)"three-dragon-ante", FixedTool = (string?)null }
        };
        var languageOrder = new[]
        {
            "common-sign-language", "draconic", "dwarvish", "elvish", "giant", "gnomish",
            "goblin", "halfling", "orc"
        };
        var operation = 0;

        foreach (var origin in cases)
        {
            operation++;
            var actorId = "actor.origin." + origin.Key + "." + operation;
            var backgroundId = "dnd2024.content.background." + origin.Key + ".v1";
            var originChoices = new Dictionary<string, object>
            {
                ["languages"] = origin.Languages
            };
            if (origin.Tool is not null) originChoices["backgroundTool"] = origin.Tool;
            var input = JsonSerializer.Serialize(new
            {
                characterId = actorId,
                name = "Complete " + origin.Key,
                ability = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [origin.PlusTwo] = 2,
                        [origin.PlusOne] = 1
                    }
                },
                speciesSelection = new { size = "medium" },
                originChoices
            });
            var request = harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create",
                BasicCreationRoles("world.character-creation.fixture",
                    "dnd2024.content.species.human.v1",
                    "dnd2024.content.class.rogue.v1", backgroundId),
                input, 0, (100 + operation).ToString("x32"));

            var created = await harness.Runner.RunAsync(request);
            var replay = await harness.Runner.RunAsync(request);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            var expectedLanguages = new[] { "common" }.Concat(origin.Languages
                .OrderBy(value => Array.IndexOf(languageOrder, value))).ToArray();
            using var languages = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.languages"))!.ValueJson);
            Assert.Equal(expectedLanguages, ReadLanguageIds(languages.RootElement).ToArray());

            var expectedTools = new[] { "thieves-tools", origin.FixedTool, origin.Tool }
                .Where(value => value is not null).Select(value => value!)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var tools = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
            Assert.Equal(expectedTools, ReadToolIds(tools.RootElement).ToArray());

            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
            var selections = record.RootElement.GetProperty("selections");
            Assert.Equal(origin.Languages.OrderBy(value => Array.IndexOf(languageOrder, value)),
                selections.GetProperty("languageChoices").EnumerateArray()
                    .Select(value => value.GetString()));
            Assert.Equal(origin.Tool is not null,
                selections.TryGetProperty("backgroundToolChoice", out var selectedTool));
            if (origin.Tool is not null) Assert.Equal(origin.Tool, selectedTool.GetString());
            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString() == "origin-language-choice:2:standard");
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "tool-choice:", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"draconic\",\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"abyssal\",\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"common\",\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"dice-set\"}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\"]}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"herbalism-kit\"}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\",\"orc\"],\"backgroundTool\":\"dice-set\"}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"dice-set\",\"extra\":true}")]
    public async Task Basic_character_creation_origin_choices_reject_invalid_or_cross_background_input(
        string backgroundKey,
        string plusTwo,
        string plusOne,
        string originChoicesJson)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.origin.invalid";
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Invalid Origin",
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new Dictionary<string, int> { [plusTwo] = 2, [plusOne] = 1 }
            },
            speciesSelection = new { size = "medium" },
            originChoices = JsonSerializer.Deserialize<JsonElement>(originChoicesJson)
        });

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1",
                backgroundId: "dnd2024.content.background." + backgroundKey + ".v1"),
            input, 0, "cc3b0000000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_origin_choices_roll_back_after_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.origin.rollback";
        const string input =
            "{\"characterId\":\"actor.origin.rollback\",\"name\":\"Origin Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"originChoices\":{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"dice-set\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1"),
            input, 0, "cc3b1000000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_class_tool_choices_apply_compose_and_replay()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var cases = new[]
        {
            new
            {
                ClassKey = "bard", BackgroundKey = "acolyte", PlusTwo = "int", PlusOne = "wis",
                Choices = new[] { "lyre", "flute", "bagpipes" },
                FixedBackgroundTool = (string?)"calligraphers-supplies",
                BackgroundTool = (string?)null, Languages = (string[]?)null
            },
            new
            {
                ClassKey = "monk", BackgroundKey = "acolyte", PlusTwo = "int", PlusOne = "wis",
                Choices = new[] { "calligraphers-supplies" },
                FixedBackgroundTool = (string?)"calligraphers-supplies",
                BackgroundTool = (string?)null, Languages = (string[]?)null
            },
            new
            {
                ClassKey = "monk", BackgroundKey = "criminal", PlusTwo = "dex", PlusOne = "con",
                Choices = new[] { "lute" }, FixedBackgroundTool = (string?)"thieves-tools",
                BackgroundTool = (string?)null, Languages = (string[]?)null
            },
            new
            {
                ClassKey = "bard", BackgroundKey = "soldier", PlusTwo = "str", PlusOne = "con",
                Choices = new[] { "shawm", "drum", "horn" }, FixedBackgroundTool = (string?)null,
                BackgroundTool = (string?)"dice-set", Languages = (string[]?)["draconic", "elvish"]
            }
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var testCase = cases[index];
            var actorId = "actor.class-tools." + testCase.ClassKey + "." + index;
            var classId = "dnd2024.content.class." + testCase.ClassKey + ".v1";
            var backgroundId = "dnd2024.content.background." + testCase.BackgroundKey + ".v1";
            var input = new Dictionary<string, object>
            {
                ["characterId"] = actorId,
                ["name"] = "Class Tool " + index,
                ["ability"] = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [testCase.PlusTwo] = 2,
                        [testCase.PlusOne] = 1
                    }
                },
                ["speciesSelection"] = new { size = "medium" },
                ["classToolChoices"] = testCase.Choices
            };
            if (testCase.Languages is not null)
            {
                input["originChoices"] = new
                {
                    languages = testCase.Languages,
                    backgroundTool = testCase.BackgroundTool
                };
            }

            var roles = BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", classId, backgroundId);
            var inputJson = JsonSerializer.Serialize(input);
            var evaluated = await harness.EvaluateRolesAsync(
                "dnd2024.mechanic.character.basic.create", roles, inputJson, 0);
            Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
            using var result = JsonDocument.Parse(evaluated.Run!.Output.Data);
            Assert.True(result.RootElement.GetProperty("classToolChoicesResolved").GetBoolean());
            var request = harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create", roles, inputJson, 0,
                (0xCC3E20 + index).ToString("x32"));
            var created = await harness.Runner.RunAsync(request);
            var replay = await harness.Runner.RunAsync(request);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId,
                "dnd2024.character-creation-record"))!.ValueJson);
            var selections = record.RootElement.GetProperty("selections");
            Assert.Equal(testCase.Choices.Order(StringComparer.Ordinal),
                selections.GetProperty("classToolChoices").EnumerateArray()
                    .Select(value => value.GetString()));
            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "tool-choice:", StringComparison.Ordinal));

            var expectedTools = testCase.Choices
                .Concat(testCase.FixedBackgroundTool is null ? [] : [testCase.FixedBackgroundTool])
                .Concat(testCase.BackgroundTool is null ? [] : [testCase.BackgroundTool])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var tools = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
            Assert.Equal(expectedTools, ReadToolIds(tools.RootElement).ToArray());
        }
    }

    [Theory]
    [InlineData("bard", "[]")]
    [InlineData("bard", "[\"lute\",\"flute\"]")]
    [InlineData("bard", "[\"lute\",\"flute\",\"drum\",\"viol\"]")]
    [InlineData("bard", "[\"lute\",\"lute\",\"drum\"]")]
    [InlineData("bard", "[\"lute\",\"flute\",\"smiths-tools\"]")]
    [InlineData("bard", "[\"lute\",\"flute\",\"kazoo\"]")]
    [InlineData("monk", "[\"lute\",\"flute\"]")]
    [InlineData("monk", "[\"dice-set\"]")]
    [InlineData("fighter", "[\"lute\"]")]
    public async Task Basic_character_creation_class_tool_choices_reject_invalid_or_cross_class_input(
        string classKey,
        string classToolChoicesJson)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.class-tools.invalid";
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Invalid Class Tools",
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new { str = 2, con = 1 }
            },
            speciesSelection = new { size = "medium" },
            classToolChoices = JsonSerializer.Deserialize<JsonElement>(classToolChoicesJson)
        });

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1",
                "dnd2024.content.class." + classKey + ".v1"),
            input, 0, "cc3e2100000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_class_tool_choices_roll_back_after_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.class-tools.rollback";
        const string input =
            "{\"characterId\":\"actor.class-tools.rollback\",\"name\":\"Class Tool Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"classToolChoices\":[\"bagpipes\",\"flute\",\"viol\"]}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", "dnd2024.content.class.bard.v1"),
            input, 0, "cc3e2200000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_supports_fixed_size_species_and_source_speed()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.goliath";
        var roles = BasicCreationRoles(
            "world.character-creation.fixture", "dnd2024.content.species.goliath.v1");
        const string input =
            "{\"characterId\":\"actor.basic.goliath\",\"name\":\"Kava\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":1,\"dex\":1,\"con\":1}},\"speciesSelection\":{}}";

        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, long.MaxValue,
            "b123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        using var size = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.body"))!.ValueJson);
        using var speed = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.movement"))!.ValueJson);
        Assert.Equal("dnd2024.vocabulary.size.medium",
            size.RootElement.GetProperty("sizeRef").GetProperty("entityId").GetString());
        var walk = speed.RootElement.GetProperty("speeds")
            .GetProperty("dnd2024.vocabulary.movement-mode.walk").GetProperty("distance");
        Assert.Equal(35 * 381, walk.GetProperty("value").GetProperty("numerator").GetInt32());
        Assert.Equal(1250, walk.GetProperty("value").GetProperty("denominator").GetInt32());
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
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
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
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
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
    [InlineData("class-armor-order-drift")]
    [InlineData("class-weapon-restriction-order-drift")]
    [InlineData("background-profile-drift")]
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
            await harness.ReplaceApplicationComponentRawAsync("dnd2024.content.species.human.v1",
                "dnd2024.species-profile",
                "{\"contentKey\":\"human\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.drifted\",\"locator\":\"Character Origins > Character Species > Human\"},\"creatureType\":\"humanoid\",\"allowedSizes\":[\"small\",\"medium\"],\"baseSpeed\":{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0},\"traitKeys\":[\"resourceful\",\"skillful\",\"versatile\"],\"choiceFamilies\":[]}");
        }
        else if (invalidState == "class-profile-drift")
        {
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.class.fighter.v1", "dnd2024.class-creation-profile",
                "{\"classKey\":\"wizard\",\"primaryAbilities\":{\"mode\":\"all\",\"abilities\":[\"int\"]},\"savingThrows\":[\"int\",\"wis\"],\"skills\":{\"choiceCount\":2,\"options\":[\"arcana\",\"history\",\"insight\",\"investigation\",\"medicine\",\"nature\",\"religion\"],\"fixedChoices\":[\"arcana\",\"investigation\"]},\"weapons\":{\"categories\":[\"simple\"],\"restrictedMartialProperties\":[]},\"armorTraining\":[],\"tools\":{\"fixed\":[],\"choiceGroups\":[]},\"spellcasting\":{\"kind\":\"full\",\"ability\":\"int\",\"cantrips\":3,\"preparedSpells\":4,\"spellbookSpells\":6,\"level1Slots\":2,\"slotLevel\":1},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Classes > Wizard, PDF pages 77–78\"}}");
        }
        else if (invalidState == "class-armor-order-drift")
        {
            var current = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "dnd2024.content.class.fighter.v1",
                "dnd2024.class-creation-profile");
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.class.fighter.v1", "dnd2024.class-creation-profile",
                current!.ValueJson.Replace(
                    "[\"light\",\"medium\",\"heavy\",\"shield\"]",
                    "[\"shield\",\"light\",\"medium\",\"heavy\"]",
                    StringComparison.Ordinal));
        }
        else if (invalidState == "class-weapon-restriction-order-drift")
        {
            var current = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "dnd2024.content.class.fighter.v1",
                "dnd2024.class-creation-profile");
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.class.fighter.v1", "dnd2024.class-creation-profile",
                current!.ValueJson.Replace(
                    "\"categories\":[\"simple\",\"martial\"],\"restrictedMartialProperties\":[]",
                    "\"categories\":[\"simple\"],\"restrictedMartialProperties\":[\"light\",\"finesse\"]",
                    StringComparison.Ordinal));
        }
        else
        {
            var current = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "dnd2024.content.background.soldier.v1",
                "dnd2024.background-creation-profile");
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.background.soldier.v1", "dnd2024.background-creation-profile",
                current!.ValueJson.Replace("Soldier, PDF p. 83", "Soldier, PDF p. 82",
                    StringComparison.Ordinal));
        }

        const string input =
            "{\"characterId\":\"actor.basic.invalid-state\",\"name\":\"Invalid State\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
            invalidState switch
            {
                "inactive-world" => "e123456789abcdef0123456789abcdf0",
                "source-drift" => "f123456789abcdef0123456789abcdf0",
                "class-profile-drift" => "a223456789abcdef0123456789abcdf0",
                "class-armor-order-drift" => "a323456789abcdef0123456789abcdf0",
                "class-weapon-restriction-order-drift" =>
                    "a423456789abcdef0123456789abcdf0",
                _ => "b223456789abcdef0123456789abcdf0"
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
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
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
        await harness.ReplaceCoreComponentRawAsync("subject.high", "dnd2024.creature.ability-scores",
            "{\"str\":16,\"dex\":14,\"con\":14,\"int\":10,\"wis\":15,\"cha\":8}");
        await harness.AddProficiencyStateAsync("subject.high", 1, ["athletics", "perception"]);
        await harness.AddSavingThrowStateAsync("subject.high", ["str", "con"]);

        var first = await harness.EvaluateAsync("subject.high", "{}", 1,
            "dnd2024.mechanic.character-sheet.read");
        var otherSeed = await harness.EvaluateAsync("subject.high", "{}", long.MaxValue,
            "dnd2024.mechanic.character-sheet.read");

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
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");
        var request = harness.ActionFor("dnd2024.mechanic.character-sheet.read", "subject.high",
            "{}", 99, "a123456789abcdef0123456789abcde0");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");
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
        await harness.ReplaceCoreComponentRawAsync("subject.low", "dnd2024.creature.ability-scores",
            "{\"str\":1,\"dex\":30,\"con\":2,\"int\":3,\"wis\":30,\"cha\":1}");
        await harness.AddProficiencyStateAsync("subject.low", 20, []);
        await harness.AddSavingThrowStateAsync("subject.low", ["cha", "wis", "int", "con", "dex", "str"]);

        var result = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.character-sheet.read");

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
            "dnd2024.mechanic.character-sheet.read");

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
            "dnd2024.mechanic.character-sheet.read");
        Assert.False(injected.Ok);
        Assert.Contains("empty object", injected.Run?.Error, StringComparison.Ordinal);

        await harness.ReplaceCoreComponentRawAsync("subject.high", "dnd2024.creature.proficiencies",
            "{\"entries\":{\"dnd2024.vocabulary.skill.perception\":{\"rankRef\":{\"entityId\":\"dnd2024.vocabulary.proficiency-rank.invalid\"},\"sourceRefs\":[{\"entityId\":\"dnd2024.source.srd-5.2.1\"}]}},\"recordedFamilies\":[\"saving-throw\",\"skill\"]}");
        var duplicate = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-sheet.read");
        Assert.False(duplicate.Ok);
        Assert.Contains("invalid rank or source list", duplicate.Run?.Error, StringComparison.Ordinal);

        await harness.ReplaceClassMembershipRawAsync("subject.high",
            "{\"classRef\":{\"entityId\":\"content.extension.class.invalid.v1\"},\"level\":1}");
        var drifted = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-sheet.read");
        Assert.False(drifted.Ok);
        Assert.True(drifted.Run is null || drifted.Run.Output.Effects.Count == 0);
    }

    [Fact]
    public async Task Character_sheet_javascript_rejects_a_malformed_raw_projection()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "catalog", "applications",
            "dnd2024", "mechanics", "proficiency", "dnd2024.mechanic.character-sheet.read.js"));
        var valid = new Dictionary<string, string>
        {
            ["dnd2024.creature.ability-scores"] = "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":10,\"dnd2024.vocabulary.ability.dexterity\":10,\"dnd2024.vocabulary.ability.constitution\":10,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}",
            ["dnd2024.creature.proficiencies"] =
                "{\"entries\":{},\"recordedFamilies\":[\"saving-throw\",\"skill\"]}"
        };
        static MechanicProjection Projection(IReadOnlyDictionary<string, string> components) => new()
        {
            Roles = new Dictionary<string, EntityProjection>
            {
                ["subject"] = new("subject", "Subject", components)
            },
            Children = new Dictionary<string, IReadOnlyList<ChildMechanicResult>>
            {
                ["level"] =
                [
                    new("dnd2024.mechanic.character-level.read", 1, 0,
                        new Dictionary<string, string> { ["subject"] = "subject" },
                        new MechanicOutput
                        {
                            Data = "{\"test\":\"character-level-read\",\"subjectId\":\"subject\",\"present\":true,\"valid\":true,\"problem\":null,\"totalLevel\":1,\"proficiencyBonus\":2,\"membershipCount\":1}",
                            HasData = true
                        }, [], 0)
                ]
            },
            Input = "{}",
            Seed = 0
        };
        var cases = new[]
        {
            (Component: "dnd2024.creature.proficiencies", Value: "{",
                Error: "missing or malformed"),
            (Component: "dnd2024.creature.ability-scores",
                Value: "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":31,\"dnd2024.vocabulary.ability.dexterity\":10,\"dnd2024.vocabulary.ability.constitution\":10,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}",
                Error: "1 through 30"),
            (Component: "dnd2024.creature.proficiencies",
                Value: "{\"entries\":{},\"recordedFamilies\":[\"skill\"]}",
                Error: "must be recorded")
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
        var first = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
            "{\"count\":2,\"sides\":6,\"modifier\":3}", 4242);
        var replay = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
            "{\"count\":2,\"sides\":6,\"modifier\":3}", 4242);
        var defaults = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles, "{}", 7);
        var invalid = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
            "{\"count\":101}", 7);
        var extra = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
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
                "dnd2024.mechanic.initiative.roll");
            var low = await harness.EvaluateAsync("subject.low", "{}", DeriveSeed(seed, 1),
                "dnd2024.mechanic.initiative.roll");
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
                participationIds = EncounterParticipationIds(),
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

    private static Dictionary<string, string> EncounterParticipationIds() => new()
    {
        ["subject.high"] = "encounter.participation.high",
        ["subject.low"] = "encounter.participation.low"
    };

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

    private static IEnumerable<string> ReadLanguageIds(JsonElement root)
        => root.GetProperty("languages").EnumerateObject()
            .Select(property => property.Name.Replace("dnd2024.vocabulary.language.", "",
                StringComparison.Ordinal));

    private static IEnumerable<string> ReadProficiencyIds(JsonElement root, string prefix)
        => root.GetProperty("entries").EnumerateObject()
            .Where(property => property.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(property => property.Name[prefix.Length..]);

    private static IEnumerable<string> ReadSkillIds(JsonElement root)
        => ReadProficiencyIds(root, "dnd2024.vocabulary.skill.").Order(StringComparer.Ordinal);

    private static IEnumerable<string> ReadArmorTrainingIds(JsonElement root)
    {
        var order = new[] { "light", "medium", "heavy", "shield" };
        var present = ReadProficiencyIds(root, "dnd2024.equipment.armor-category.")
            .ToHashSet(StringComparer.Ordinal);
        return order.Where(present.Contains);
    }

    private static IEnumerable<string> ReadSavingThrowIds(JsonElement root)
    {
        var mappings = new[]
        {
            ("str", "strength"), ("dex", "dexterity"), ("con", "constitution"),
            ("int", "intelligence"), ("wis", "wisdom"), ("cha", "charisma")
        };
        var present = ReadProficiencyIds(root, "dnd2024.vocabulary.ability.")
            .ToHashSet(StringComparer.Ordinal);
        return mappings.Where(value => present.Contains(value.Item2)).Select(value => value.Item1);
    }

    private static IEnumerable<string> ReadWeaponCategoryIds(JsonElement root)
    {
        var present = ReadProficiencyIds(root, "dnd2024.equipment.weapon-category.")
            .ToHashSet(StringComparer.Ordinal);
        return new[] { "simple", "martial" }.Where(present.Contains);
    }

    private static IEnumerable<string> ReadWeaponPropertyIds(JsonElement root)
    {
        var present = ReadProficiencyIds(root, "dnd2024.equipment.weapon-property.")
            .ToHashSet(StringComparer.Ordinal);
        return new[] { "finesse", "light" }.Where(present.Contains);
    }

    private static IEnumerable<string> ReadToolIds(JsonElement root)
        => ReadProficiencyIds(root, "dnd2024.equipment.tool.")
            .Select(value => value switch
            {
                "dice" => "dice-set",
                "dragonchess" => "dragonchess-set",
                _ => value
            }).Order(StringComparer.Ordinal);

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
        private readonly RegisteredComponentTypeVersion _proficiencies;
        private readonly RegisteredComponentTypeVersion _hitPoints;
        private readonly RegisteredComponentTypeVersion _speed;
        private readonly IReadOnlyDictionary<string, RegisteredComponentTypeVersion> _additionalTypes;
        public IReadOnlySet<string> ActiveSourcePaths { get; }

        private DndHarness(
            SqliteFixture fixture,
            DantesRoleplayDbContext db,
            ActivatedApplicationCatalogProvider catalogs,
            RegisteredComponentTypeVersion abilities,
            RegisteredComponentTypeVersion proficiencies,
            RegisteredComponentTypeVersion hitPoints,
            RegisteredComponentTypeVersion speed,
            IReadOnlyDictionary<string, RegisteredComponentTypeVersion> additionalTypes,
            IReadOnlySet<string> activeSourcePaths,
            SqliteEntityComponentStore entities,
            SqliteStateSpaceEdgeStore edges,
            ApplicationActionRunner runner,
            IApplicationReadModelService readModels)
        {
            _fixture = fixture;
            _db = db;
            _catalogs = catalogs;
            _abilities = abilities;
            _proficiencies = proficiencies;
            _hitPoints = hitPoints;
            _speed = speed;
            _additionalTypes = additionalTypes;
            ActiveSourcePaths = activeSourcePaths;
            Entities = entities;
            Edges = edges;
            Runner = runner;
            ReadModels = readModels;
        }

        public SqliteEntityComponentStore Entities { get; }
        public SqliteStateSpaceEdgeStore Edges { get; }
        public ApplicationActionRunner Runner { get; }
        public IApplicationReadModelService ReadModels { get; }

        public Task<IReadOnlyList<EventSummary>> EventsAsync(string rootOperationId) =>
            new EventLedger(_db).FindAsync(rootOperationId: rootOperationId);

        /// <summary>
        /// One prepared database per source set, kept for the process.
        ///
        /// Building this scans and fingerprints every file under the D&D application catalog,
        /// previews and activates it, registers its component types from disk, and seeds the
        /// subjects — about ten seconds of work that is byte-for-byte identical every time. This
        /// class has 345 test cases and xunit runs a class on one thread, so paying it per test
        /// was most of a forty-minute suite, on one core of twenty-four. Tests still get their own
        /// database: the template is cloned, never shared.
        /// </summary>
        private sealed record Template(
            SqliteFixture Fixture,
            RegisteredComponentTypeVersion Abilities,
            RegisteredComponentTypeVersion Proficiencies,
            RegisteredComponentTypeVersion HitPoints,
            RegisteredComponentTypeVersion Speed,
            IReadOnlyDictionary<string, RegisteredComponentTypeVersion> AdditionalTypes,
            IReadOnlySet<string> ActiveSourcePaths);

        private static readonly SemaphoreSlim TemplateGate = new(1, 1);
        private static readonly Dictionary<bool, Template> Templates = [];

        private static async Task<Template> TemplateAsync(bool includeLegacyEquipmentExtension)
        {
            await TemplateGate.WaitAsync();
            try
            {
                if (Templates.TryGetValue(includeLegacyEquipmentExtension, out var cached)) return cached;
                var built = await BuildTemplateAsync(includeLegacyEquipmentExtension);
                Templates[includeLegacyEquipmentExtension] = built;
                return built;
            }
            finally
            {
                TemplateGate.Release();
            }
        }

        public static async Task<DndHarness> CreateAsync(
            bool includeLegacyEquipmentExtension = false,
            bool failTransactionAfterEffects = false)
        {
            var template = await TemplateAsync(includeLegacyEquipmentExtension);
            var fixture = SqliteFixture.CloneOf(template.Fixture.Connection);
            var db = fixture.CreateContext();
            var applications = new SqliteApplicationRegistry(db);
            var sources = new SqliteSourceRegistry(db);
            var roots = new WorkspaceRoot();
            var operations = new OperationLog(db);
            var activations = new ApplicationActivationService(
                db,
                new ApplicationPreviewService(applications, sources,
                    new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
                    new SourceOverlayResolver()),
                new EmptyImpact(), operations);
            var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var entities = new SqliteEntityComponentStore(db, types, schemas);
            var materializer = new ActivatedApplicationCatalogMaterializer(applications, activations, sources, roots);
            _ = materializer.BuildFeatureSnapshot(Application);
            var catalogs = new ActivatedApplicationCatalogProvider(
                new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
                materializer,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("dnd2024-ability-check-cursor-key")));
            var evaluator = new ApplicationMechanicEvaluator(
                catalogs, new ApplicationMechanicProjectionResolver(db, stateSpaces), new JintMechanicEngine());
            var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
            var mappings = new ApplicationMechanicProjectionMappingResolver(
                catalogs, stateSpaces, types, edges);
            var clockParticipant = new ApplicationClockEventTransactionParticipant(
                new EventTypeStore(db), new EventLedger(db), schemas);
            IReadOnlyList<IApplicationEcsTransactionParticipant> participants =
                failTransactionAfterEffects
                    ? [clockParticipant, new RejectAfterEffectsTransactionParticipant()]
                    : [clockParticipant];
            var applier = new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges,
                participants);
            var runner = new ApplicationActionRunner(
                catalogs, activations, stateSpaces, types, entities, edges,
                mappings,
                evaluator, applier, operations);
            var readModels = new ApplicationReadModelService(
                catalogs, activations, stateSpaces, mappings, evaluator, schemas);
            return new(fixture, db, catalogs, template.Abilities, template.Proficiencies,
                template.HitPoints, template.Speed, template.AdditionalTypes,
                template.ActiveSourcePaths, entities, edges, runner, readModels);
        }

        private static async Task<Template> BuildTemplateAsync(bool includeLegacyEquipmentExtension)
        {
            var fixture = new SqliteFixture();
            await using var db = fixture.CreateContext();
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
            stateSpaces.Create(new(StateSpaceId, revision,
                activation.Activation.ActivationFingerprint,
                activation.Activation.ResolutionFingerprint));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var abilityDefinition = await DefinitionAsync("abilities/dnd2024.creature.ability-scores");
            var proficiencyDefinition = await DefinitionAsync("proficiency/dnd2024.creature.proficiencies");
            var hitPointsDefinition = await DefinitionAsync("combat/dnd2024.creature.hit-points");
            var speedDefinition = await DefinitionAsync("movement/dnd2024.creature.movement");
            var abilities = types.Define(new(Application, abilityDefinition.Id, abilityDefinition.Schema));
            var proficiencies = types.Define(new(Application, proficiencyDefinition.Id, proficiencyDefinition.Schema));
            var hitPoints = types.Define(new(Application, hitPointsDefinition.Id, hitPointsDefinition.Schema));
            var speed = types.Define(new(Application, speedDefinition.Id, speedDefinition.Schema));
            var additionalTypes = new Dictionary<string, RegisteredComponentTypeVersion>(StringComparer.Ordinal);
            var primaryTypeIds = new HashSet<string>(
                [abilityDefinition.Id, proficiencyDefinition.Id, hitPointsDefinition.Id, speedDefinition.Id],
                StringComparer.Ordinal);
            var applicationComponentDirectory = Path.Combine(
                RepositoryRoot(), "catalog", "applications", "dnd2024", "components");
            foreach (var path in Directory.EnumerateFiles(
                         applicationComponentDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(path => !path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
            {
                var componentId = Path.GetFileNameWithoutExtension(path);
                if (primaryTypeIds.Contains(componentId)) continue;

                var definition = await DefinitionAsync(componentId);
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
            await new EventTypeStore(db).WriteAsync(new()
            {
                Id = "game.core.world.clock.advanced",
                Category = "game.core.world.time",
                Name = "World clock advanced",
                Scope = "world",
                Status = EventTypeStatus.Active,
                PayloadSchema = await File.ReadAllTextAsync(Path.Combine(
                    RepositoryRoot(), "catalog", "event-types", "game", "core", "world",
                    "clock", "advanced.schema.json"))
            });
            await entities.CreateEntityAsync(StateSpaceId,
                "dnd2024.content.defense.unarmored.v1", "Unarmored Defense (ordinary, D&D 2024)");
            var defenseBasis = additionalTypes["dnd2024.creature.defense-basis"];
            await entities.AddComponentAsync(new(StateSpaceId,
                "dnd2024.content.defense.unarmored.v1",
                new(defenseBasis.QualifiedId, defenseBasis.Version, defenseBasis.SchemaHash),
                "{\"armorClass\":{\"mechanicId\":\"dnd2024.mechanic.armor-class.unarmored\",\"inputBindings\":{\"abilityRef\":\"dnd2024.vocabulary.ability.dexterity\",\"base\":10}},\"damageResponses\":[]}", 0));
            await AddSubjectAsync(entities, abilities,
                additionalTypes["dnd2024.character.feature-entitlements"], "subject.high",
                "{\"str\":30,\"dex\":10,\"con\":10,\"int\":10,\"wis\":10,\"cha\":10}");
            await AddSubjectAsync(entities, abilities,
                additionalTypes["dnd2024.character.feature-entitlements"], "subject.low",
                "{\"str\":1,\"dex\":10,\"con\":10,\"int\":10,\"wis\":10,\"cha\":10}");

            return new(fixture, abilities, proficiencies, hitPoints, speed, additionalTypes,
                activation.Activation.Winners.Select(value => value.RelativePath).ToHashSet(StringComparer.Ordinal));
        }

        public async Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
            string subjectId, string input, long seed, string localMechanicId = "dnd2024.mechanic.check.ability")
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
                    ["dnd2024.creature.ability-scores"] = new(_abilities.QualifiedId, _abilities.Version, _abilities.SchemaHash),
                    ["dnd2024.creature.proficiencies"] = new(_proficiencies.QualifiedId, _proficiencies.Version, _proficiencies.SchemaHash),
                    ["dnd2024.creature.hit-points"] = new(_hitPoints.QualifiedId, _hitPoints.Version, _hitPoints.SchemaHash),
                    ["dnd2024.creature.movement"] = new(_speed.QualifiedId, _speed.Version, _speed.SchemaHash)
                };
            foreach (var (componentId, type) in _additionalTypes)
                if (includeGameBaseMapping || type.Owner != GameApplication)
                    componentMapping[componentId] = new(
                        type.QualifiedId, type.Version, type.SchemaHash);
            var mapping = new ApplicationMechanicProjectionMapping(componentMapping,
                new Dictionary<string, string>
                {
                    ["rest.world"] = "dnd2024.rest.world",
                    ["campaign.has-character-participation"] =
                        "dnd2024.campaign.has-character-participation",
                    ["campaign.character-participation.for-actor"] =
                        "dnd2024.campaign.character-participation.for-actor",
                    ["character.has-class-membership"] =
                        "dnd2024.character.has-class-membership",
                    ["encounter.has-participation"] = "dnd2024.encounter.has-participation",
                    ["encounter.participation.for-actor"] = "dnd2024.encounter.participation.for-actor",
                    ["encounter.has-round"] = "dnd2024.encounter.has-round",
                    ["encounter.has-turn"] = "dnd2024.encounter.has-turn",
                    ["encounter.round.has-turn"] = "dnd2024.encounter.round.has-turn",
                    ["encounter.active-round"] = "dnd2024.encounter.active-round",
                    ["encounter.active-turn"] = "dnd2024.encounter.active-turn"
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
            => ActionFor("dnd2024.mechanic.check.ability", subjectId, input, seed, operationId);

        public ApplicationActionExecutionRequest ActionFor(
            string localMechanicId, string subjectId, string input, long seed, string operationId)
        {
            var record = Record(localMechanicId);
            var subject = record.Summary.QualifiedId + "\n" + subjectId + "\n" + input + "\n" + seed;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
            return new(StateSpaceId, Application, record.Summary.QualifiedId, record.Summary.Version,
                record.Summary.ContentFingerprint,
                new Dictionary<string, string> { ["subject"] = subjectId }, input, seed,
                new(operationId, fingerprint));
        }

        public ApplicationActionExecutionRequest ActionForRoles(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed, string operationId)
        {
            var record = Record(localMechanicId);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Summary.QualifiedId + "\n" + input + "\n" + seed)));
            return new(StateSpaceId, Application, record.Summary.QualifiedId, record.Summary.Version,
                record.Summary.ContentFingerprint,
                roles, input, seed, new(operationId, fingerprint));
        }

        public async Task AddProficiencyStateAsync(string subjectId, int level, IReadOnlyList<string> skills)
        {
            await AddCharacterLevelAsync(subjectId, level);
            await MergeProficiencyFamilyAsync(subjectId, "skill",
                skills.ToDictionary(
                    skill => "dnd2024.vocabulary.skill." + skill,
                    _ => (object)new
                    {
                        rankRef = new { entityId = "dnd2024.vocabulary.proficiency-rank.proficiency" },
                        sourceRefs = new[] { new { entityId = "dnd2024.source.srd-5.2.1" } }
                    }, StringComparer.Ordinal));
        }

        private async Task MergeProficiencyFamilyAsync(
            string subjectId, string family, IReadOnlyDictionary<string, object> additions)
        {
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleOrDefaultAsync(value =>
                value.StateSpaceId == StateSpaceId && value.EntityId == subjectId
                && value.QualifiedTypeId == _proficiencies.QualifiedId);
            var entries = new Dictionary<string, object?>(StringComparer.Ordinal);
            var families = new HashSet<string>(StringComparer.Ordinal);
            if (row is not null)
            {
                using var document = JsonDocument.Parse(row.Data);
                foreach (var property in document.RootElement.GetProperty("entries").EnumerateObject())
                    entries[property.Name] = property.Value.Clone();
                foreach (var item in document.RootElement.GetProperty("recordedFamilies").EnumerateArray())
                    families.Add(item.GetString()!);
            }
            foreach (var (entityId, entry) in additions) entries[entityId] = entry;
            families.Add(family);
            var familyOrder = new[] { "armor-training", "saving-throw", "skill", "tool", "weapon" };
            var data = JsonSerializer.Serialize(new
            {
                entries,
                recordedFamilies = familyOrder.Where(families.Contains).ToArray()
            });
            if (row is null)
                await Entities.AddComponentAsync(new(StateSpaceId, subjectId,
                    new(_proficiencies.QualifiedId, _proficiencies.Version, _proficiencies.SchemaHash), data, 0));
            else
            {
                row.Data = data;
                await _db.SaveChangesAsync();
            }
        }

        public async Task AddCharacterLevelAsync(string subjectId, int level)
            => await AddClassMembershipAsync(subjectId, "fighter",
                "dnd2024.content.class.fighter.v1", level);

        public async Task AddClassMembershipAsync(
            string subjectId,
            string membershipKey,
            string classId,
            int level)
        {
            var membershipId = subjectId + ".class-membership." + membershipKey;
            await Entities.CreateEntityAsync(StateSpaceId, membershipId,
                subjectId + " " + membershipKey + " membership");
            var membership = _additionalTypes["dnd2024.character.class-membership"];
            await Entities.AddComponentAsync(new(StateSpaceId, membershipId,
                new(membership.QualifiedId, membership.Version, membership.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    classRef = new { entityId = classId },
                    level
                }), 0));
            await Edges.SetRelationshipAsync(StateSpaceId, subjectId, membershipId,
                "dnd2024.character.has-class-membership", "{}", 0);
        }

        public async Task ReplaceClassMembershipRawAsync(string subjectId, string valueJson)
        {
            var membership = _additionalTypes["dnd2024.character.class-membership"];
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId && value.EntityId == ClassMembershipId(subjectId)
                && value.QualifiedTypeId == membership.QualifiedId);
            row.Data = valueJson;
            await _db.SaveChangesAsync();
        }

        private static string ClassMembershipId(string subjectId) =>
            subjectId + ".class-membership.fighter";

        public async Task AddSavingThrowStateAsync(string subjectId, IReadOnlyList<string> abilities)
        {
            var references = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["str"] = "dnd2024.vocabulary.ability.strength",
                ["dex"] = "dnd2024.vocabulary.ability.dexterity",
                ["con"] = "dnd2024.vocabulary.ability.constitution",
                ["int"] = "dnd2024.vocabulary.ability.intelligence",
                ["wis"] = "dnd2024.vocabulary.ability.wisdom",
                ["cha"] = "dnd2024.vocabulary.ability.charisma"
            };
            await MergeProficiencyFamilyAsync(subjectId, "saving-throw",
                abilities.ToDictionary(
                    ability => references[ability],
                    _ => (object)new
                    {
                        rankRef = new { entityId = "dnd2024.vocabulary.proficiency-rank.proficiency" },
                        sourceRefs = new[] { new { entityId = "dnd2024.source.srd-5.2.1" } }
                    }, StringComparer.Ordinal));
        }

        public async Task AddCombatFixturesAsync()
        {
            await AddProficiencyStateAsync("subject.high", 5, []);
            await MergeProficiencyFamilyAsync("subject.high", "weapon",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["dnd2024.equipment.weapon-category.simple"] = new
                    {
                        rankRef = new { entityId = "dnd2024.vocabulary.proficiency-rank.proficiency" },
                        sourceRefs = new[] { new { entityId = "dnd2024.source.srd-5.2.1" } }
            }
                });
            await Entities.CreateEntityAsync(StateSpaceId, "weapon.fixture", "Dagger");
            await AddApplicationComponentAsync("weapon.fixture", "dnd2024.item.weapon",
                "{\"category\":{\"entityId\":\"dnd2024.equipment.weapon-category.simple\"},\"properties\":[{\"entityId\":\"dnd2024.equipment.weapon-property.finesse\"},{\"entityId\":\"dnd2024.equipment.weapon-property.light\"},{\"entityId\":\"dnd2024.equipment.weapon-property.thrown\"}],\"masteryProperty\":{\"entityId\":\"dnd2024.equipment.weapon-mastery.nick\"}}");
            await AddApplicationComponentAsync("weapon.fixture", "dnd2024.activity.membership",
                "{\"activities\":[{\"entityId\":\"activity.weapon.fixture\",\"expectedArchetype\":\"dnd2024.archetype.activity-definition\"}]}");
            await Entities.CreateEntityAsync(StateSpaceId, "activity.weapon.fixture", "Dagger Melee Attack");
            await AddApplicationComponentAsync("activity.weapon.fixture", "dnd2024.core.version",
                "{\"revision\":1,\"status\":\"active\"}");
            await AddApplicationComponentAsync("activity.weapon.fixture", "dnd2024.activity.activation",
                "{\"economy\":\"none\"}");
            await AddApplicationComponentAsync("activity.weapon.fixture", "dnd2024.activity.attack",
                "{\"mode\":\"melee\",\"abilityOptions\":[{\"entityId\":\"dnd2024.vocabulary.ability.strength\"},{\"entityId\":\"dnd2024.vocabulary.ability.dexterity\"}]}");
            await AddApplicationComponentAsync("activity.weapon.fixture", "dnd2024.activity.damage",
                "{\"parts\":[{\"amount\":{\"count\":1,\"dieRef\":{\"entityId\":\"dnd2024.vocabulary.die.d4\"},\"modifier\":0},\"damageType\":{\"entityId\":\"dnd2024.vocabulary.damage-type.piercing\"}}],\"delivery\":\"on-hit\",\"criticalBehavior\":\"eligible\"}");
            await AddApplicationComponentAsync("activity.weapon.fixture", "dnd2024.activity.range",
                "{\"range\":{\"kind\":\"distance\",\"normal\":{\"dimension\":\"distance\",\"value\":{\"numerator\":381,\"denominator\":250},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.meter\"}}}}");
            await Entities.CreateEntityAsync(StateSpaceId, "target.fixture", "Target");
            await Entities.AddComponentAsync(new(StateSpaceId, "target.fixture",
                new(_abilities.QualifiedId, _abilities.Version, _abilities.SchemaHash),
                "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":10,\"dnd2024.vocabulary.ability.dexterity\":1,\"dnd2024.vocabulary.ability.constitution\":10,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}", 0));
            var defenses = _additionalTypes["dnd2024.creature.defenses"];
            await Entities.AddComponentAsync(new(StateSpaceId, "target.fixture",
                new(defenses.QualifiedId, defenses.Version, defenses.SchemaHash),
                "{\"armorClassSource\":{\"entityId\":\"dnd2024.content.defense.unarmored.v1\"},\"damageResponses\":[]}", 0));
            await Entities.AddComponentAsync(new(StateSpaceId, "target.fixture", new(_hitPoints.QualifiedId, _hitPoints.Version, _hitPoints.SchemaHash), "{\"current\":20,\"maximum\":20}", 0));
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
                "content", "entities", "character-options", "feats");
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
            await AddCharacterCreationFeatFixturesAsync();
            await Entities.CreateEntityAsync(StateSpaceId, worldId, "Character Creation World");
            await AddApplicationComponentAsync(worldId, "game.core.world.root",
                "{\"status\":\"active\",\"summary\":\"A source-bound basic character creation fixture.\",\"visibility\":\"party\"}");

            var directory = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
                "content", "entities", "character-progression");
            foreach (var path in Directory.GetFiles(directory, "dnd2024.content.class.*.json")
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
            foreach (var path in Directory.GetFiles(directory, "dnd2024.content.feature.*.json")
                         .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');
                Assert.Contains(relative, ActiveSourcePaths);
                var featureEntity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
                await Entities.CreateEntityAsync(StateSpaceId, featureEntity.Id, featureEntity.Name);
                foreach (var component in featureEntity.Components)
                    await AddApplicationComponentAsync(
                        featureEntity.Id, component.DefinitionId, component.Data);
            }
        }

        public async Task AddCanonicalGoldDefinitionFixtureAsync()
            => await AddCanonicalCurrencyDefinitionFixtureAsync("gold-piece");

        public async Task AddCanonicalCurrencyDefinitionFixtureAsync(string denomination)
        {
            var relative =
                $"catalog/applications/dnd2024/content/entities/equipment/base/equipment.currency.{denomination}.json";
            Assert.Contains(relative, ActiveSourcePaths);
            var path = Path.Combine(RepositoryRoot(),
                relative.Replace('/', Path.DirectorySeparatorChar));
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            Assert.Equal($"dnd2024.equipment.currency.{denomination}", entity.Id);
            await Entities.CreateEntityAsync(StateSpaceId, entity.Id, entity.Name);
            foreach (var component in entity.Components)
                await AddApplicationComponentAsync(
                    entity.Id, component.DefinitionId, component.Data);
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
                    maximum = 10
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
                "catalog/applications/dnd2024/content/entities/character-creation/rest/dnd2024.content.rest-policy.standard.v1.json";
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
                    maximum
                }), 0));
            if (mitigationJson is not null)
                await AddApplicationComponentAsync(targetId, "dnd2024.creature.defenses", mitigationJson);
        }

        public async Task AddHitPointsAsync(string entityId, int current, int maximum)
            => await Entities.AddComponentAsync(new(StateSpaceId, entityId,
                new(_hitPoints.QualifiedId, _hitPoints.Version, _hitPoints.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    current,
                    maximum
                }), 0));

        public async Task AddEncounterFixturesAsync()
        {
            await Entities.CreateEntityAsync(StateSpaceId, "encounter.fixture", "Encounter");
            foreach (var subjectId in new[] { "subject.high", "subject.low" })
            {
                await Entities.AddComponentAsync(new(StateSpaceId, subjectId,
                    new(_speed.QualifiedId, _speed.Version, _speed.SchemaHash),
                    NormalizePrototypeComponentFixture("dnd2024.creature.movement",
                        "{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}"), 0));
            }
            var edges = new SqliteStateSpaceEdgeStore(_db,
                new SqliteStateSpaceRegistry(_db, new SqliteApplicationRegistry(_db)));
            await edges.MoveContainmentAsync(StateSpaceId, "subject.high", "encounter.fixture", "participant", 0);
            await edges.MoveContainmentAsync(StateSpaceId, "subject.low", "encounter.fixture", "participant", 0);
        }

        public async Task AddExplicitTurnAsync(
            string turnId = "turn.fixture",
            string participantId = "encounter.participation.high",
            string status = "active")
        {
            await Entities.CreateEntityAsync(StateSpaceId, turnId, "Explicit Turn");
            var turn = _additionalTypes["dnd2024.encounter.turn"];
            await Entities.AddComponentAsync(new(StateSpaceId, turnId,
                new(turn.QualifiedId, turn.Version, turn.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    encounter = new { entityId = "encounter.fixture" },
                    round = new { entityId = "encounter.round.1" },
                    participant = new { entityId = participantId },
                    ordinal = 0,
                    status
                }), 0));
        }

        public async Task AddItemDefinitionAsync(string definitionId, string name, string definitionJson,
            string? activityJson = null)
        {
            await Entities.CreateEntityAsync(StateSpaceId, definitionId, name);
            var definition = _additionalTypes["dnd2024.item-definition"];
            await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                new(definition.QualifiedId, definition.Version, definition.SchemaHash), definitionJson, 0));

            using var document = JsonDocument.Parse(definitionJson);
            var root = document.RootElement;
            var version = _additionalTypes["dnd2024.core.version"];
            await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                new(version.QualifiedId, version.Version, version.SchemaHash),
                "{\"revision\":1,\"status\":\"active\"}", 0));
            var legacySource = root.GetProperty("sourceRef");
            var source = _additionalTypes["dnd2024.core.source"];
            await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                new(source.QualifiedId, source.Version, source.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    citations = new[]
                    {
                        new
                        {
                            sourceRef = new { entityId = legacySource.GetProperty("sourceId").GetString() },
                            locator = legacySource.GetProperty("locator").GetString()
                        }
                    }
                }), 0));
            var pounds = root.GetProperty("massPounds");
            var physical = _additionalTypes["dnd2024.item.physical"];
            await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                new(physical.QualifiedId, physical.Version, physical.SchemaHash),
                JsonSerializer.Serialize(new
                {
                    weight = new
                    {
                        dimension = "mass",
                        value = new
                        {
                            numerator = pounds.GetProperty("numerator").GetInt64() * 45359237L,
                            denominator = pounds.GetProperty("denominator").GetInt64() * 100000000L
                        },
                        unit = new { entityId = "dnd2024.vocabulary.mass-unit.kilogram" }
                    }
                }), 0));
            if (root.TryGetProperty("capacity", out var legacyCapacity)
                && legacyCapacity.TryGetProperty("weightPounds", out var weightPounds))
            {
                var container = _additionalTypes["dnd2024.item.container"];
                await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                    new(container.QualifiedId, container.Version, container.SchemaHash),
                    JsonSerializer.Serialize(new
                    {
                        maximumWeight = new
                        {
                            dimension = "mass",
                            value = new
                            {
                                numerator = weightPounds.GetProperty("numerator").GetInt64()
                                    * 45359237L,
                                denominator = weightPounds.GetProperty("denominator").GetInt64()
                                    * 100000000L
                            },
                            unit = new { entityId = "dnd2024.vocabulary.mass-unit.kilogram" }
                        }
                    }), 0));
            }
            if (root.TryGetProperty("equipmentModes", out var equipmentModes))
            {
                var equippable = _additionalTypes["dnd2024.item.equippable"];
                var slots = equipmentModes.EnumerateArray().Select(mode =>
                    new
                    {
                        entityId = mode.GetString() == "held"
                            ? "dnd2024.equipment-slot.main-hand"
                            : "dnd2024.equipment-slot.body"
                    }).DistinctBy(value => value.entityId, StringComparer.Ordinal).ToArray();
                await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                    new(equippable.QualifiedId, equippable.Version, equippable.SchemaHash),
                    JsonSerializer.Serialize(new { equipmentSlots = slots }), 0));
            }
            if (activityJson is not null)
            {
                var activity = _additionalTypes["dnd2024.item-activity"];
                await Entities.AddComponentAsync(new(StateSpaceId, definitionId,
                    new(activity.QualifiedId, activity.Version, activity.SchemaHash), activityJson, 0));
            }
        }

        public async Task AddCatalogEntityAsync(EntityFile entity)
        {
            await Entities.CreateEntityAsync(StateSpaceId, entity.Id, entity.Name);
            foreach (var component in entity.Components)
                await AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
        }

        public async Task AddApplicationComponentAsync(string entityId, string componentId,
            string valueJson)
        {
            var type = componentId == "dnd2024.creature.proficiencies"
                ? _proficiencies
                : _additionalTypes[componentId];
            valueJson = NormalizePrototypeComponentFixture(componentId, valueJson);
            await Entities.AddComponentAsync(new(StateSpaceId, entityId,
                new(type.QualifiedId, type.Version, type.SchemaHash), valueJson, 0));
        }

        public async Task ReplaceApplicationComponentRawAsync(string entityId, string componentId,
            string valueJson)
        {
            var type = componentId == "dnd2024.creature.proficiencies"
                ? _proficiencies
                : _additionalTypes[componentId];
            valueJson = NormalizePrototypeComponentFixture(componentId, valueJson);
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
                "dnd2024.creature.ability-scores" => _abilities,
                "dnd2024.creature.proficiencies" => _proficiencies,
                "dnd2024.creature.hit-points" => _hitPoints,
                _ => throw new ArgumentOutOfRangeException(nameof(componentId), componentId,
                    "Not a registered core component.")
            };
            valueJson = NormalizePrototypeComponentFixture(componentId, valueJson);
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId && value.EntityId == entityId
                && value.QualifiedTypeId == type.QualifiedId);
            row.Data = valueJson;
            await _db.SaveChangesAsync();
        }

        public async Task AddPhysicalItemAsync(string itemId, string name, string definitionId,
            string? containerId = null, string slot = "carried", int? quantity = null,
            string? equipmentState = null, bool includeQuantity = true)
        {
            await Entities.CreateEntityAsync(StateSpaceId, itemId, name);
            var instance = _additionalTypes["dnd2024.core.definition-link"];
            await Entities.AddComponentAsync(new(StateSpaceId, itemId,
                new(instance.QualifiedId, instance.Version, instance.SchemaHash),
                JsonSerializer.Serialize(new { definition = new { entityId = definitionId } }), 0));
            if (includeQuantity)
            {
                var quantityType = _additionalTypes["dnd2024.item.quantity"];
                await Entities.AddComponentAsync(new(StateSpaceId, itemId,
                    new(quantityType.QualifiedId, quantityType.Version, quantityType.SchemaHash),
                    JsonSerializer.Serialize(new { current = quantity ?? 1 }), 0));
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
            data = NormalizePrototypeComponentFixture("dnd2024.creature.movement", data);
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId
                && value.EntityId == subjectId
                && value.QualifiedTypeId == _speed.QualifiedId);
            row.Data = data;
            await _db.SaveChangesAsync();
        }

        public async Task ReplaceConditionsRawAsync(string subjectId, string data)
        {
            var conditions = _additionalTypes["dnd2024.conditions"];
            var row = await _db.Set<ApplicationEcsComponentRecord>().SingleAsync(value =>
                value.StateSpaceId == StateSpaceId
                && value.EntityId == subjectId
                && value.QualifiedTypeId == conditions.QualifiedId);
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
            => Record("dnd2024.mechanic.check.ability");

        private CatalogRecordView Record(string localMechanicId)
        {
            Assert.True(_catalogs.TryGet(Application, out var catalog));
            var recordId = localMechanicId.StartsWith(Application.Value + ".", StringComparison.Ordinal)
                ? localMechanicId
                : Application.Value + "." + localMechanicId;
            return catalog.Inspect(new(Application, Application.Value, recordId));
        }

        private static async Task AddSubjectAsync(
            SqliteEntityComponentStore entities,
            RegisteredComponentTypeVersion abilities,
            RegisteredComponentTypeVersion featureEntitlements,
            string id,
            string scores)
        {
            await entities.CreateEntityAsync(StateSpaceId, id, id);
            try
            {
                var legacy = JsonSerializer.Deserialize<Dictionary<string, int>>(scores);
                var keys = new[] { "str", "dex", "con", "int", "wis", "cha" };
                if (legacy is not null && legacy.Count == keys.Length && keys.All(legacy.ContainsKey))
                {
                    var references = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["dnd2024.vocabulary.ability.strength"] = legacy["str"],
                        ["dnd2024.vocabulary.ability.dexterity"] = legacy["dex"],
                        ["dnd2024.vocabulary.ability.constitution"] = legacy["con"],
                        ["dnd2024.vocabulary.ability.intelligence"] = legacy["int"],
                        ["dnd2024.vocabulary.ability.wisdom"] = legacy["wis"],
                        ["dnd2024.vocabulary.ability.charisma"] = legacy["cha"]
                    };
                    scores = JsonSerializer.Serialize(new { scores = references });
                }
            }
            catch (JsonException)
            {
                // Preserve malformed fixtures so the target schema can reject them.
            }
            await entities.AddComponentAsync(new(StateSpaceId, id,
                new(abilities.QualifiedId, abilities.Version, abilities.SchemaHash), scores, 0));
            await entities.AddComponentAsync(new(StateSpaceId, id,
                new(featureEntitlements.QualifiedId, featureEntitlements.Version,
                    featureEntitlements.SchemaHash), "{\"entitlements\":[]}", 0));
        }

        private static string NormalizePrototypeComponentFixture(string componentId, string valueJson)
        {
            try
            {
                using var document = JsonDocument.Parse(valueJson);
                var root = document.RootElement;
                if (componentId == "dnd2024.creature.ability-scores"
                    && root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("str", out var str)
                    && root.TryGetProperty("dex", out var dex)
                    && root.TryGetProperty("con", out var con)
                    && root.TryGetProperty("int", out var intel)
                    && root.TryGetProperty("wis", out var wis)
                    && root.TryGetProperty("cha", out var cha)
                    && str.ValueKind == JsonValueKind.Number && dex.ValueKind == JsonValueKind.Number
                    && con.ValueKind == JsonValueKind.Number && intel.ValueKind == JsonValueKind.Number
                    && wis.ValueKind == JsonValueKind.Number && cha.ValueKind == JsonValueKind.Number)
                {
                    return JsonSerializer.Serialize(new
                    {
                        scores = new Dictionary<string, int>
                        {
                            ["dnd2024.vocabulary.ability.strength"] = str.GetInt32(),
                            ["dnd2024.vocabulary.ability.dexterity"] = dex.GetInt32(),
                            ["dnd2024.vocabulary.ability.constitution"] = con.GetInt32(),
                            ["dnd2024.vocabulary.ability.intelligence"] = intel.GetInt32(),
                            ["dnd2024.vocabulary.ability.wisdom"] = wis.GetInt32(),
                            ["dnd2024.vocabulary.ability.charisma"] = cha.GetInt32()
                        }
                    });
                }

                if (componentId == "dnd2024.creature.proficiencies"
                    && root.ValueKind == JsonValueKind.Object
                    && !root.TryGetProperty("entries", out _))
                {
                    var entries = new Dictionary<string, object?>(StringComparer.Ordinal);
                    object Entry() => new
                    {
                        rankRef = new { entityId = "dnd2024.vocabulary.proficiency-rank.proficiency" },
                        sourceRefs = new[] { new { entityId = "dnd2024.source.srd-5.2.1" } }
                    };
                    if (root.TryGetProperty("skills", out var skills)
                        && skills.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var skill in skills.EnumerateArray())
                            if (skill.ValueKind == JsonValueKind.String)
                                entries["dnd2024.vocabulary.skill." + skill.GetString()] = Entry();
                            else return valueJson;
                        return JsonSerializer.Serialize(new { entries, recordedFamilies = new[] { "skill" } });
                    }
                    if (root.TryGetProperty("abilities", out var saves)
                        && saves.ValueKind == JsonValueKind.Array)
                    {
                        var abilityRefs = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["str"] = "dnd2024.vocabulary.ability.strength",
                            ["dex"] = "dnd2024.vocabulary.ability.dexterity",
                            ["con"] = "dnd2024.vocabulary.ability.constitution",
                            ["int"] = "dnd2024.vocabulary.ability.intelligence",
                            ["wis"] = "dnd2024.vocabulary.ability.wisdom",
                            ["cha"] = "dnd2024.vocabulary.ability.charisma"
                        };
                        foreach (var save in saves.EnumerateArray())
                            if (save.ValueKind == JsonValueKind.String
                                && abilityRefs.TryGetValue(save.GetString()!, out var abilityRef))
                                entries[abilityRef] = Entry();
                            else return valueJson;
                        return JsonSerializer.Serialize(new { entries, recordedFamilies = new[] { "saving-throw" } });
                    }
                    if (root.TryGetProperty("tools", out var tools)
                        && tools.ValueKind == JsonValueKind.Array)
                    {
                        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["dice-set"] = "dice", ["dragonchess-set"] = "dragonchess",
                            ["playing-cards"] = "playing-cards", ["three-dragon-ante"] = "three-dragon-ante"
                        };
                        foreach (var tool in tools.EnumerateArray())
                            if (tool.ValueKind == JsonValueKind.String)
                            {
                                var id = tool.GetString()!;
                                entries["dnd2024.equipment.tool." + aliases.GetValueOrDefault(id, id)] = Entry();
                            }
                            else return valueJson;
                        return JsonSerializer.Serialize(new { entries, recordedFamilies = new[] { "tool" } });
                    }
                    if (root.TryGetProperty("categories", out var categories)
                        && categories.ValueKind == JsonValueKind.Array)
                    {
                        var values = categories.EnumerateArray().Select(item => item.GetString()).ToArray();
                        var locator = root.TryGetProperty("sourceRef", out var sourceRef)
                            && sourceRef.ValueKind == JsonValueKind.Object
                            && sourceRef.TryGetProperty("locator", out var locatorValue)
                            && locatorValue.ValueKind == JsonValueKind.String
                                ? locatorValue.GetString()
                                : null;
                        var weapon = locator == "Equipment > Weapons > Weapon Proficiency"
                            || (locator != "Rules Glossary > Armor Training"
                                && values.Length > 0
                                && values.All(value => value is "simple" or "martial"));
                        foreach (var value in values)
                        {
                            if (value is null) return valueJson;
                            entries[(weapon ? "dnd2024.equipment.weapon-category." :
                                "dnd2024.equipment.armor-category.") + value] = Entry();
                        }
                        if (weapon && root.TryGetProperty("restrictedMartialProperties", out var properties)
                            && properties.ValueKind == JsonValueKind.Array)
                            foreach (var property in properties.EnumerateArray())
                                if (property.ValueKind == JsonValueKind.String)
                                    entries["dnd2024.equipment.weapon-property." + property.GetString()] = Entry();
                                else return valueJson;
                        return JsonSerializer.Serialize(new
                        {
                            entries,
                            recordedFamilies = new[] { weapon ? "weapon" : "armor-training" }
                        });
                    }
                }

                if (componentId == "dnd2024.creature.body"
                    && root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("size", out var size)
                    && size.ValueKind == JsonValueKind.String)
                    return JsonSerializer.Serialize(new
                    {
                        sizeRef = new { entityId = "dnd2024.vocabulary.size." + size.GetString() }
                    });

                if (componentId == "dnd2024.creature.languages"
                    && root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("languages", out var languages)
                    && languages.ValueKind == JsonValueKind.Array)
                {
                    var state = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var language in languages.EnumerateArray())
                    {
                        if (language.ValueKind != JsonValueKind.String) return valueJson;
                        var id = language.GetString();
                        state["dnd2024.vocabulary.language." + id] = new
                        {
                            understands = true,
                            communicates = true,
                            reads = true,
                            writes = true,
                            sourceRefs = new[] { new { entityId = "dnd2024.source.srd-5.2.1" } }
                        };
                    }
                    return JsonSerializer.Serialize(new { languages = state });
                }

                if (componentId == "dnd2024.creature.defenses"
                    && root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("resistances", out var resistances)
                    && root.TryGetProperty("immunities", out var immunities)
                    && root.TryGetProperty("vulnerabilities", out var vulnerabilities)
                    && resistances.ValueKind == JsonValueKind.Array
                    && immunities.ValueKind == JsonValueKind.Array
                    && vulnerabilities.ValueKind == JsonValueKind.Array)
                {
                    var entries = new List<object>();
                    AddResponses(entries, resistances, "resistance");
                    AddResponses(entries, immunities, "immunity");
                    AddResponses(entries, vulnerabilities, "vulnerability");
                    entries.Sort((left, right) =>
                        string.CompareOrdinal(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right)));
                    var result = new Dictionary<string, object?> { ["damageResponses"] = entries };
                    if (root.TryGetProperty("armorClassSource", out var armorClassSource))
                        result["armorClassSource"] = armorClassSource.Clone();
                    else if (root.TryGetProperty("sourceRef", out var sourceRef)
                        && sourceRef.ValueKind == JsonValueKind.Object
                        && sourceRef.TryGetProperty("sourceId", out var sourceId))
                        result["armorClassSource"] = new { entityId = sourceId.GetString() };
                    return JsonSerializer.Serialize(result);
                }

                if (componentId == "dnd2024.creature.movement"
                    && root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("walkFeet", out var walk))
                {
                    var speeds = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var (mode, property) in new[]
                    {
                        ("walk", "walkFeet"), ("burrow", "burrowFeet"), ("climb", "climbFeet"),
                        ("fly", "flyFeet"), ("swim", "swimFeet")
                    })
                    {
                        if (!root.TryGetProperty(property, out var feet) || feet.ValueKind != JsonValueKind.Number
                            || !feet.TryGetInt32(out var amount)) return valueJson;
                        speeds["dnd2024.vocabulary.movement-mode." + mode] = new
                        {
                            distance = new
                            {
                                dimension = "distance",
                                value = new { numerator = amount * 381, denominator = 1250 },
                                unit = new { entityId = "dnd2024.vocabulary.distance-unit.meter" }
                            },
                            enabled = amount > 0,
                            sourceRefs = new[] { new { entityId = "dnd2024.source.srd-5.2.1" } }
                        };
                    }
                    return JsonSerializer.Serialize(new { speeds });
                }
            }
            catch (JsonException)
            {
                // Preserve malformed fixtures so the mechanic can reject them.
            }
            return valueJson;
        }

        private static void AddResponses(List<object> entries, JsonElement values, string response)
        {
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String) continue;
                entries.Add(new
                {
                    damageTypeRef = new { entityId = "dnd2024.vocabulary.damage-type." + value.GetString() },
                    responseRef = new { entityId = "dnd2024.vocabulary.damage-response." + response },
                    sourceRef = new { entityId = "dnd2024.source.srd-5.2.1" }
                });
            }
        }

        private static async Task<ComponentDefinitionFile> DefinitionAsync(string relative)
        {
            var componentId = relative[(relative.LastIndexOf('/') + 1)..];
            var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "components",
                componentId + ".json");
            var definition = ComponentDefinitionFile.Parse(await File.ReadAllTextAsync(path), componentId + ".json",
                await File.ReadAllTextAsync(Path.ChangeExtension(path, ".schema.json")));
            var compilation = new BoundedJsonSchemaValidator().Compile(definition.Schema);
            Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
            return definition;
        }

        private static async Task<ComponentDefinitionFile> GameDefinitionAsync(string componentId)
        {
            var catalog = Path.Combine(RepositoryRoot(), "catalog");
            var path = CatalogLayout.ToFileSystemPath(catalog, CatalogLayout.Component(componentId));
            var schemaPath = CatalogLayout.ToFileSystemPath(catalog, CatalogLayout.ComponentSchema(componentId));
            var definition = ComponentDefinitionFile.Parse(await File.ReadAllTextAsync(path),
                CatalogLayout.Component(componentId),
                await File.ReadAllTextAsync(schemaPath));
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
        string classId = "dnd2024.content.class.fighter.v1",
        string backgroundId = "dnd2024.content.background.soldier.v1") =>
        new(StringComparer.Ordinal)
        {
            ["world"] = worldId,
            ["policy"] = "dnd2024.content.ability-assignment.standard-array.v1",
            ["background"] = backgroundId,
            ["species"] = speciesId,
            ["class"] = classId
        };

    private static string AlertGrantState(
        string configurationKey = "default",
        string grantKind = "origin-feat",
        string grantedByDefinitionId = "dnd2024.content.background.criminal.v1",
        string locator = "Feats > Origin Feats > Alert, PDF page 87",
        bool duplicate = false,
        bool extraProperty = false)
    {
        var entitlement = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["featureRef"] = new { entityId = "dnd2024.feat.alert" },
            ["grantedByRef"] = new { entityId = grantedByDefinitionId },
            ["grantKind"] = grantKind,
            ["sourceRef"] = new
            {
                sourceId = "dnd2024.source.srd-5.2.1",
                locator
            }
        };
        if (grantKind == "origin-feat") entitlement["configurationKey"] = configurationKey;
        else entitlement["classLevel"] = 1;
        if (extraProperty) entitlement["behaviorStatus"] = "implemented";
        var entitlements = new List<Dictionary<string, object>> { entitlement };
        if (duplicate)
        {
            var second = new Dictionary<string, object>(entitlement, StringComparer.Ordinal)
            {
                ["grantedByRef"] = new
                {
                    entityId = "content.extension.background.investigator.v1"
                },
                ["sourceRef"] = new
                {
                    sourceId = "dnd2024.source.srd-5.2.1",
                    locator = "Extension > Investigator > Alert Grant"
                }
            };
            entitlements.Add(second);
        }
        return JsonSerializer.Serialize(new
        {
            entitlements
        });
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
