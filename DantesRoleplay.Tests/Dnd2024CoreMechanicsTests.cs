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

public sealed class Dnd2024CoreMechanicsTests : Dnd2024TestBase
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
    public async Task Tactical_board_places_and_moves_through_public_direct_actions_atomically_and_idempotently()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddEncounterFixturesAsync();
        var encounterRoles = new Dictionary<string, string> { ["encounter"] = "encounter.fixture" };
        var orderRequest = await EncounterOrderWithHighFirstAsync(harness);
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-initiative-order", encounterRoles,
            orderRequest.Input, orderRequest.Seed, "1023456789abcdef0123456789abcdf0")));
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter-turn.start", encounterRoles,
            "{\"roundId\":\"encounter.round.1\",\"turnId\":\"encounter.turn.1.0\"}", 0,
            "2023456789abcdef0123456789abcdf0")));

        await harness.AddApplicationComponentAsync("encounter.fixture", "dnd2024.encounter.board",
            "{\"revision\":3,\"status\":\"active\",\"visibility\":\"public\",\"columns\":12,\"rows\":8,\"feetPerSquare\":5,\"terrain\":[],\"obstacles\":[]}");
        await harness.AddApplicationComponentAsync("encounter.participation.low", "dnd2024.combat.position",
            "{\"encounter\":{\"entityId\":\"encounter.fixture\"},\"anchor\":{\"x\":8,\"y\":2},\"footprint\":{\"width\":1,\"height\":1},\"elevationFeet\":0,\"visibility\":\"public\",\"revision\":1}");

        var placementRoles = new Dictionary<string, string>
        {
            ["encounter"] = "encounter.fixture",
            ["participation"] = "encounter.participation.high"
        };
        var placed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.encounter.board.place", placementRoles,
            "{\"expectedBoardRevision\":3,\"expectedPositionRevision\":null,\"position\":{\"anchor\":{\"x\":1,\"y\":1},\"footprint\":{\"width\":1,\"height\":1},\"elevationFeet\":0,\"visibility\":\"public\"}}",
            0, "3023456789abcdef0123456789abcdf0"));
        AssertSucceeded(placed);

        var movementRoles = new Dictionary<string, string>
        {
            ["subject"] = "subject.high",
            ["encounter"] = "encounter.fixture",
            ["participation"] = "encounter.participation.high"
        };
        var moveRequest = harness.ActionForRoles(
            "dnd2024.mechanic.encounter.board.move", movementRoles,
            "{\"expectedBoardRevision\":3,\"expectedPositionRevision\":1,\"expectedTurnId\":\"encounter.turn.1.0\",\"path\":[{\"x\":2,\"y\":1}],\"spend\":{\"resource\":\"movement\",\"distance\":{\"dimension\":\"distance\",\"value\":{\"numerator\":381,\"denominator\":250},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.meter\"}}}}",
            0, "4023456789abcdef0123456789abcdf0");
        var moved = await harness.Runner.RunAsync(moveRequest);
        var replay = await harness.Runner.RunAsync(moveRequest);
        AssertSucceeded(moved);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(moved.OperationId, replay.OperationId);

        var position = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.participation.high", "dnd2024.combat.position");
        using var positionJson = JsonDocument.Parse(position!.ValueJson);
        Assert.Equal(2, positionJson.RootElement.GetProperty("anchor").GetProperty("x").GetInt32());
        Assert.Equal(2, positionJson.RootElement.GetProperty("revision").GetInt32());
        var budget = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "encounter.turn.1.0", "dnd2024.combat.turn-budget");
        using var budgetJson = JsonDocument.Parse(budget!.ValueJson);
        Assert.Single(budgetJson.RootElement.GetProperty("movementSpent").EnumerateArray());
    }

}
