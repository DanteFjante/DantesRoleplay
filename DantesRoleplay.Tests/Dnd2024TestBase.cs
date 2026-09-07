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

public abstract class Dnd2024TestBase
{
    protected static Dictionary<string, string> RestBeginRoles() => new()
    {
        ["creature"] = "subject.high",
        ["world"] = "world.rest.fixture",
        ["policy"] = "dnd2024.content.rest-policy.standard.v1"
    };

    protected static string SeparateItemDefinition(string? equipmentModes = null)
        => "{\"definitionVersion\":1,\"kind\":\"adventuring-gear\",\"stackPolicy\":\"separate\",\"massPounds\":{\"numerator\":1,\"denominator\":1}" +
           (equipmentModes is null ? "" : ",\"equipmentModes\":" + equipmentModes) +
           ",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Adventuring Gear\"}}";

    protected static string FungibleItemDefinition()
        => "{\"definitionVersion\":1,\"kind\":\"ammunition\",\"stackPolicy\":\"fungible\",\"massPounds\":{\"numerator\":1,\"denominator\":20},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Ammunition\"}}";

    protected static string ContainerItemDefinition(int maximumWeightPounds)
        => "{\"definitionVersion\":1,\"kind\":\"adventuring-gear\",\"stackPolicy\":\"separate\",\"massPounds\":{\"numerator\":1,\"denominator\":1},\"capacity\":{\"weightPounds\":{\"numerator\":" + maximumWeightPounds + ",\"denominator\":1}},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Adventuring Gear\"}}";

    protected static string CurrencyItemDefinition(string denomination, int copperValue)
        => "{\"definitionVersion\":1,\"kind\":\"currency\",\"stackPolicy\":\"fungible\",\"massPounds\":{\"numerator\":1,\"denominator\":50},\"currency\":{\"denomination\":\"" + denomination + "\",\"copperValue\":" + copperValue + ",\"coinsPerPound\":50},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Equipment > Coins\"}}";

    protected static int Roll(ApplicationMechanicEvaluationResult result)
    {
        Assert.True(result.Ok, result.Run?.Error);
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        return data.RootElement.GetProperty("roll").GetInt32();
    }

    protected static async Task<(string Input, long Seed)> EncounterOrderWithHighFirstAsync(DndHarness harness)
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

    protected static int Initiative(ApplicationMechanicEvaluationResult result)
    {
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        return data.RootElement.GetProperty("initiative").GetInt32();
    }

    protected static Dictionary<string, string> EncounterParticipationIds() => new()
    {
        ["subject.high"] = "encounter.participation.high",
        ["subject.low"] = "encounter.participation.low"
    };

    protected static long DeriveSeed(long parentSeed, int ordinal)
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

    protected static bool Succeeded(ApplicationMechanicEvaluationResult result)
    {
        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        return data.RootElement.GetProperty("succeeded").GetBoolean();
    }

    protected static IEnumerable<string> ReadLanguageIds(JsonElement root)
        => root.GetProperty("languages").EnumerateObject()
            .Select(property => property.Name.Replace("dnd2024.vocabulary.language.", "",
                StringComparison.Ordinal));

    protected static IEnumerable<string> ReadProficiencyIds(JsonElement root, string prefix)
        => root.GetProperty("entries").EnumerateObject()
            .Where(property => property.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(property => property.Name[prefix.Length..]);

    protected static IEnumerable<string> ReadSkillIds(JsonElement root)
        => ReadProficiencyIds(root, "dnd2024.vocabulary.skill.").Order(StringComparer.Ordinal);

    protected static IEnumerable<string> ReadArmorTrainingIds(JsonElement root)
    {
        var order = new[] { "light", "medium", "heavy", "shield" };
        var present = ReadProficiencyIds(root, "dnd2024.equipment.armor-category.")
            .ToHashSet(StringComparer.Ordinal);
        return order.Where(present.Contains);
    }

    protected static IEnumerable<string> ReadSavingThrowIds(JsonElement root)
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

    protected static IEnumerable<string> ReadWeaponCategoryIds(JsonElement root)
    {
        var present = ReadProficiencyIds(root, "dnd2024.equipment.weapon-category.")
            .ToHashSet(StringComparer.Ordinal);
        return new[] { "simple", "martial" }.Where(present.Contains);
    }

    protected static IEnumerable<string> ReadWeaponPropertyIds(JsonElement root)
    {
        var present = ReadProficiencyIds(root, "dnd2024.equipment.weapon-property.")
            .ToHashSet(StringComparer.Ordinal);
        return new[] { "finesse", "light" }.Where(present.Contains);
    }

    protected static IEnumerable<string> ReadToolIds(JsonElement root)
        => ReadProficiencyIds(root, "dnd2024.equipment.tool.")
            .Select(value => value switch
            {
                "dice" => "dice-set",
                "dragonchess" => "dragonchess-set",
                _ => value
            }).Order(StringComparer.Ordinal);

    protected static void AssertRollMode(
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

    protected static async Task AddDowntimeFixturesAsync(DndHarness harness)
    {
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId,
            "world.downtime.fixture", "Downtime World");
        await harness.AddApplicationComponentAsync("world.downtime.fixture", "game.core.world.root",
            "{\"status\":\"active\",\"summary\":\"Bounded downtime fixture.\",\"visibility\":\"party\"}");
        await harness.AddApplicationComponentAsync("world.downtime.fixture", "game.core.world.clock",
            "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":7}");
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId,
            "downtime.definition.crafting.fixture", "Fixture crafting downtime");
        await harness.AddApplicationComponentAsync("downtime.definition.crafting.fixture",
            "dnd2024.core.version", "{\"revision\":1,\"status\":\"active\"}");
        await harness.AddApplicationComponentAsync("downtime.definition.crafting.fixture",
            "dnd2024.downtime.definition", "{\"revision\":1,\"fingerprint\":\"" + Fingerprint
            + "\",\"kind\":\"crafting\",\"totalMinutes\":60,"
            + "\"prerequisiteKeys\":[\"tool.smith\"],\"reservations\":[{"
            + "\"definitionId\":\"item.definition.downtime.material\",\"quantity\":2,"
            + "\"purpose\":\"material\"}],\"cancellationPolicy\":\"refund\","
            + "\"output\":{\"definitionId\":\"item.definition.downtime.output\"}}");
        foreach (var kind in new[] { "recovery", "service", "training", "lifestyle" })
        {
            var definitionId = "downtime.definition." + kind + ".fixture";
            await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId,
                definitionId, "Fixture " + kind + " downtime");
            await harness.AddApplicationComponentAsync(definitionId, "dnd2024.core.version",
                "{\"revision\":1,\"status\":\"active\"}");
            await harness.AddApplicationComponentAsync(definitionId, "dnd2024.downtime.definition",
                "{\"revision\":1,\"fingerprint\":\"" + Fingerprint + "\",\"kind\":\""
                + kind + "\",\"totalMinutes\":5,\"prerequisiteKeys\":[],\"reservations\":[],"
                + "\"cancellationPolicy\":\"retain\"}");
        }
        foreach (var (id, name) in new[]
                 {
                     ("item.definition.downtime.material", "Fixture material definition"),
                     ("item.definition.downtime.output", "Fixture output definition")
                 })
        {
            await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, id, name);
            await harness.AddApplicationComponentAsync(id, "dnd2024.core.version",
                "{\"revision\":1,\"status\":\"active\"}");
        }
        await harness.AddPhysicalItemAsync("item.downtime.material.fixture", "Fixture material",
            "item.definition.downtime.material", "subject.high", "inventory.materials", 3);
        await harness.AddPhysicalItemAsync("item.downtime.cancel-material.fixture", "Refundable fixture material",
            "item.definition.downtime.material", "subject.low", "inventory.materials", 2);
    }

    protected static async Task AddSocialFixturesAsync(DndHarness harness)
    {
        await harness.Entities.CreateEntityAsync(
            DndHarness.StateSpaceId, "actor.social.target", "Social target");
        await harness.Entities.CreateEntityAsync(
            DndHarness.StateSpaceId, "campaign.social.fixture", "Social campaign");
        await harness.AddApplicationComponentAsync("campaign.social.fixture", "game.core.campaign.root",
            "{\"status\":\"active\",\"title\":\"Social fixture\",\"premise\":\"Test explicit social consequences.\"," +
            "\"partyGoals\":[\"Preserve selected state.\"],\"toneAndBoundaries\":[\"No invented judgment.\"]," +
            "\"rulesetScope\":\"dnd2024\",\"creationMethod\":\"manual\",\"reviewFingerprint\":\"" +
            new string('a', 64) + "\"}");
    }

    protected static Dictionary<string, string> SocialRoles(string targetId) => new(StringComparer.Ordinal)
    {
        ["source"] = "subject.high",
        ["target"] = targetId,
        ["campaign"] = "campaign.social.fixture"
    };

    protected static string SocialInput(
        string relationshipId,
        string? previousAttitudeId,
        string nextAttitudeId,
        string visibility,
        int expectedRevision,
        IReadOnlyList<(string Id, string Summary, string Visibility)> reasons,
        IReadOnlyList<string>? evidence = null,
        (string Id, string Summary, string Visibility)? consequence = null,
        string? creativeDecision = null)
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["relationshipId"] = relationshipId,
            ["previousAttitudeId"] = previousAttitudeId,
            ["nextAttitudeId"] = nextAttitudeId,
            ["visibility"] = visibility,
            ["reasonFacts"] = reasons.Select(value => new
            {
                factId = value.Id,
                kind = "reason",
                summary = value.Summary,
                provenance = "DM-selected Slice 13 test evidence",
                visibility = value.Visibility
            }).ToArray(),
            ["evidenceReceiptIds"] = evidence ?? [],
            ["expectedRevision"] = expectedRevision
        };
        if (consequence is { } selected)
            input["consequence"] = new
            {
                factId = selected.Id,
                kind = "consequence",
                summary = selected.Summary,
                provenance = "DM-selected Slice 13 consequence",
                visibility = selected.Visibility
            };
        if (creativeDecision is not null) input["dialogueAndAcceptance"] = creativeDecision;
        return JsonSerializer.Serialize(input);
    }

    protected static Dictionary<string, string> ObjectDurabilityRoles(string objectId, string definitionId) =>
        new(StringComparer.Ordinal) { ["object"] = objectId, ["definition"] = definitionId };

    protected static string ObjectVersionInput() =>
        "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\"}";

    protected static string ObjectDamageInput(int amount, string damageType) =>
        "{\"amount\":" + amount + ",\"damageType\":\"" + damageType
        + "\",\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\"}";

    protected static string ObjectRepairInput(int amount, string authority, string receipts) =>
        "{\"hitPointsRestored\":" + amount + ",\"repairAuthority\":\"" + authority
        + "\",\"settlementReceiptIds\":" + receipts
        + ",\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\"}";

    protected static async Task AddObjectDurabilityFixturesAsync(DndHarness harness, bool initialized = false)
    {
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId,
            "object.definition.fixture", "Fixture destructible object definition");
        await harness.AddApplicationComponentAsync("object.definition.fixture", "dnd2024.core.version",
            "{\"revision\":1,\"status\":\"active\"}");
        await harness.AddApplicationComponentAsync("object.definition.fixture", "dnd2024.core.source",
            "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Slice 12 fixture\"}]}");
        await harness.AddApplicationComponentAsync("object.definition.fixture", "dnd2024.object.durability-basis",
            "{\"armorClass\":15,\"maximumHitPoints\":20,\"damageThreshold\":5,\"damageResponses\":["
            + "{\"damageType\":{\"entityId\":\"dnd2024.vocabulary.damage-type.acid\"},\"response\":{\"entityId\":\"dnd2024.vocabulary.damage-response.vulnerability\"}},"
            + "{\"damageType\":{\"entityId\":\"dnd2024.vocabulary.damage-type.cold\"},\"response\":{\"entityId\":\"dnd2024.vocabulary.damage-response.immunity\"}},"
            + "{\"damageType\":{\"entityId\":\"dnd2024.vocabulary.damage-type.fire\"},\"response\":{\"entityId\":\"dnd2024.vocabulary.damage-response.resistance\"}}]}");
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId,
            "object.durability.fixture", "Fixture destructible object");
        await harness.AddApplicationComponentAsync("object.durability.fixture", "dnd2024.core.definition-link",
            "{\"definition\":{\"entityId\":\"object.definition.fixture\"},\"definitionRevision\":1}");
        await harness.AddApplicationComponentAsync("object.durability.fixture", "dnd2024.item.equipment",
            "{\"equippedBy\":{\"entityId\":\"subject.high\"},\"slots\":[{\"entityId\":\"slot.fixture\"}]}");
        await harness.AddApplicationComponentAsync("object.durability.fixture", "game.core.world.route.availability",
            "{\"status\":\"open\"}");
        await harness.AddApplicationComponentAsync("object.durability.fixture", "dnd2024.hazard.trap-state",
            "{\"phase\":{\"entityId\":\"dnd2024.hazard.trap-phase.armed\"},\"activationCount\":0}");
        if (initialized)
            await harness.AddApplicationComponentAsync("object.durability.fixture", "dnd2024.object.durability",
                "{\"currentHitPoints\":20,\"destroyed\":false,\"stabilized\":true,\"basisRevision\":1,"
                + "\"basisFingerprint\":\"" + Fingerprint + "\",\"activeDamageEffects\":[]}");

        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId,
            "object.resilient.definition.fixture", "Fixture resilient object definition");
        await harness.AddApplicationComponentAsync("object.resilient.definition.fixture", "dnd2024.core.version",
            "{\"revision\":1,\"status\":\"active\"}");
        await harness.AddApplicationComponentAsync("object.resilient.definition.fixture", "dnd2024.core.source",
            "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Slice 12 resilient fixture\"}]}");
        await harness.AddApplicationComponentAsync("object.resilient.definition.fixture", "dnd2024.object.durability-basis",
            "{\"armorClass\":15,\"maximumHitPoints\":20,\"damageThreshold\":0,\"damageResponses\":[]}");
        await harness.AddApplicationComponentAsync("object.resilient.definition.fixture", "dnd2024.magic-item.resilience",
            "{\"destructionRequirement\":{\"operator\":\"predicate\",\"predicateId\":\"predicate.always\",\"arguments\":[]}}");
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId,
            "object.resilient.fixture", "Fixture resilient object");
        await harness.AddApplicationComponentAsync("object.resilient.fixture", "dnd2024.core.definition-link",
            "{\"definition\":{\"entityId\":\"object.resilient.definition.fixture\"},\"definitionRevision\":1}");
    }

    protected const string Fingerprint = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";

    protected static void AssertSucceeded(ApplicationActionExecutionResult result) =>
        Assert.True(result.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            result.Disposition + ": " + string.Join("; ", result.Problems.Select(value =>
                value.Code + " " + value.SafeMessage)));

    protected static Dictionary<string, string> PoisonApplyRoles(string definitionId, string? activityId)
    {
        var roles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "item.affliction.source.fixture",
            ["target"] = "subject.high",
            ["definition"] = definitionId,
            ["world"] = "world.affliction.fixture"
        };
        if (activityId is not null) roles["activity"] = activityId;
        return roles;
    }

    protected static Dictionary<string, string> PoisonProgressRoles(string applicationId) => new(StringComparer.Ordinal)
    {
        ["application"] = applicationId,
        ["target"] = "subject.high",
        ["definition"] = "hazard.poison.delayed.fixture",
        ["activity"] = "activity.poison.save.success",
        ["world"] = "world.affliction.fixture"
    };

    protected static Dictionary<string, string> PoisonRecoverRoles(string applicationId) => new(StringComparer.Ordinal)
    {
        ["application"] = applicationId,
        ["target"] = "subject.high",
        ["definition"] = "hazard.poison.delayed.fixture",
        ["activity"] = "activity.poison.recover.fixture"
    };

    protected static string PoisonApplyInput(string applicationId, int? dc, string damage, string conditions) =>
        "{\"applicationId\":\"" + applicationId
        + "\",\"appliedAtEventId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
        + Fingerprint + "\",\"expectedClockRevision\":7,\"deliveryMethod\":\"dnd2024.vocabulary.poison-delivery.injury\""
        + (dc is null ? "" : ",\"check\":{\"ability\":\"con\",\"dc\":" + dc + "}")
        + ",\"damageEffects\":" + damage + ",\"conditionEffects\":" + conditions + "}";

    protected static string PoisonProgressInput(int clockRevision) =>
        "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint
        + "\",\"expectedClockRevision\":" + clockRevision
        + ",\"check\":{\"ability\":\"con\",\"dc\":0},\"damageEffects\":[{\"amount\":2,\"damageType\":\"poison\",\"saveSucceeded\":true,\"successfulSaveBehavior\":\"none\"}],\"conditionEffects\":[]}";

    protected static string AfflictionCheckInput(int dc) =>
        "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint
        + "\",\"check\":{\"ability\":\"con\",\"dc\":" + dc + "}}";

    protected static Dictionary<string, string> ContagionExposeRoles() => new(StringComparer.Ordinal)
    {
        ["source"] = "item.affliction.source.fixture",
        ["target"] = "subject.high",
        ["definition"] = "hazard.contagion.fixture",
        ["world"] = "world.affliction.fixture"
    };

    protected static Dictionary<string, string> ContagionProgressRoles(string applicationId) => new(StringComparer.Ordinal)
    {
        ["application"] = applicationId,
        ["target"] = "subject.high",
        ["definition"] = "hazard.contagion.fixture",
        ["world"] = "world.affliction.fixture"
    };

    protected static Dictionary<string, string> ContagionTransmitRoles() => new(StringComparer.Ordinal)
    {
        ["sourceApplication"] = "affliction.contagion.source.fixture",
        ["sourceHost"] = "subject.high",
        ["target"] = "subject.low",
        ["definition"] = "hazard.contagion.fixture",
        ["world"] = "world.affliction.fixture"
    };

    protected static Dictionary<string, string> ContagionRecoverRoles() => new(StringComparer.Ordinal)
    {
        ["application"] = "affliction.contagion.source.fixture",
        ["target"] = "subject.high",
        ["definition"] = "hazard.contagion.fixture",
        ["activity"] = "activity.contagion.recover.fixture"
    };

    protected static string ContagionStartInput(string applicationId, int clockRevision) =>
        "{\"applicationId\":\"" + applicationId
        + "\",\"appliedAtEventId\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
        + Fingerprint + "\",\"expectedClockRevision\":" + clockRevision
        + ",\"triggerEvent\":\"contagion.fixture.contact\"}";

    protected static Dictionary<string, string> CurseBindRoles() => new(StringComparer.Ordinal)
    {
        ["source"] = "item.curse.source.fixture",
        ["target"] = "subject.high",
        ["definition"] = "hazard.curse.fixture",
        ["world"] = "world.affliction.fixture"
    };

    protected static Dictionary<string, string> CurseDiscoverRoles() => new(StringComparer.Ordinal)
    {
        ["application"] = "affliction.curse.fixture",
        ["source"] = "item.curse.source.fixture",
        ["target"] = "subject.high",
        ["observer"] = "subject.low",
        ["definition"] = "hazard.curse.fixture",
        ["activity"] = "activity.curse.discover.fixture"
    };

    protected static string CurseDiscoveryInput() =>
        "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint
        + "\",\"knowledgeId\":\"knowledge.curse.fixture\",\"discoveryEventId\":\"cccccccccccccccccccccccccccccccc\",\"check\":{\"ability\":\"con\",\"dc\":0}}";

    protected static Dictionary<string, string> CurseRemoveRoles() => new(StringComparer.Ordinal)
    {
        ["application"] = "affliction.curse.fixture",
        ["target"] = "subject.high",
        ["definition"] = "hazard.curse.fixture",
        ["activity"] = "activity.curse.remove.fixture"
    };

    protected static Dictionary<string, string> TravelRoles() => new(StringComparer.Ordinal)
    {
        ["traveller"] = "subject.high",
        ["origin"] = "location.travel.origin",
        ["destination"] = "location.travel.destination",
        ["route"] = "route.travel.fixture",
        ["world"] = "world.travel.fixture"
    };

    protected static string TravelInput(string pace, string? extraProperty = null)
    {
        var input = "{\"travel\":{\"journeyId\":\"journey.slice9\",\"exposureScheduleId\":\"exposure.slice9\",\"mode\":\"walk\",\"pace\":\""
                    + pace
                    + "\",\"expectedRouteRevision\":1,\"expectedRouteFingerprint\":\""
                    + new string('A', 64)
                    + "\",\"expectedClockRevision\":7}}";
        return input.Insert(1, extraProperty is null ? "" : extraProperty + ",");
    }

    protected static Dictionary<string, string> TrapDetectRoles() => new(StringComparer.Ordinal)
    {
        ["observer"] = "subject.high",
        ["trap"] = "hazard.trap.instance.fixture",
        ["definition"] = "hazard.trap.definition.fixture",
        ["activity"] = "activity.hazard.detect.fixture"
    };

    protected static Dictionary<string, string> TrapDisarmRoles() => new(StringComparer.Ordinal)
    {
        ["actor"] = "subject.high",
        ["trap"] = "hazard.trap.instance.fixture",
        ["definition"] = "hazard.trap.definition.fixture",
        ["activity"] = "activity.hazard.disarm.fixture"
    };

    protected static Dictionary<string, string> TrapTriggerRoles() => new(StringComparer.Ordinal)
    {
        ["trap"] = "hazard.trap.instance.fixture",
        ["definition"] = "hazard.trap.definition.fixture",
        ["target"] = "subject.high"
    };

    protected static Dictionary<string, string> TrapLifecycleRoles() => new(StringComparer.Ordinal)
    {
        ["trap"] = "hazard.trap.instance.fixture",
        ["definition"] = "hazard.trap.definition.fixture"
    };

    protected static Dictionary<string, string> ExposureBeginRoles() => new(StringComparer.Ordinal)
    {
        ["subject"] = "subject.high",
        ["definition"] = "hazard.environment.definition.fixture"
    };

    protected static Dictionary<string, string> ExposureProgressRoles(string exposureId) => new(StringComparer.Ordinal)
    {
        ["exposure"] = exposureId,
        ["definition"] = "hazard.environment.definition.fixture",
        ["subject"] = "subject.high",
        ["activity"] = "activity.hazard.exposure.fixture",
        ["world"] = "world.hazard.fixture"
    };

    protected static Dictionary<string, string> ExposureResolveRoles(string exposureId) => new(StringComparer.Ordinal)
    {
        ["exposure"] = exposureId,
        ["definition"] = "hazard.environment.definition.fixture",
        ["subject"] = "subject.high",
        ["activity"] = "activity.hazard.exposure.fixture"
    };

    protected static Dictionary<string, string> ExposureRecoverRoles(string exposureId) => new(StringComparer.Ordinal)
    {
        ["exposure"] = exposureId,
        ["definition"] = "hazard.environment.definition.fixture",
        ["subject"] = "subject.high"
    };

    protected static string FailedExposureInput(int expectedClockRevision) =>
        "{\"minutes\":60,\"expectedClockRevision\":" + expectedClockRevision
        + ",\"expectedDefinitionRevision\":1,\"check\":{\"ability\":\"con\",\"dc\":100},"
        + "\"damageEffects\":[{\"amount\":2,\"damageType\":\"cold\",\"saveSucceeded\":false,\"successfulSaveBehavior\":\"none\"}],"
        + "\"conditionEffects\":[{\"mode\":\"apply\",\"conditions\":[\"prone\"]}]}";

    protected sealed class DndHarness : IAsyncDisposable
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

        public CatalogSearchResult Search(string query)
        {
            Assert.True(_catalogs.TryGet(Application, out var catalog));
            return catalog.Search(new(Application, query,
                Kinds: ["mechanic"], PageSize: 10));
        }

        public bool HasActiveQuery(string queryId)
        {
            if (!_catalogs.TryGet(Application, out var catalog)) return false;
            try
            {
                var record = catalog.Inspect(new(Application, Application.Value, queryId));
                return record.Summary.Kind == ApplicationQueryContract.CatalogKind
                       && record.Summary.Status == "active";
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

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
            var projectionRegistry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
            var materializer = new ActivatedApplicationCatalogMaterializer(applications, activations, sources, roots,
                projections: projectionRegistry);
            _ = materializer.BuildFeatureSnapshot(Application);
            var catalogs = new ActivatedApplicationCatalogProvider(
                new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
                materializer,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("dnd2024-ability-check-cursor-key")));
            var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
            var projectionTransactions = new SqliteProjectionReadTransaction(db);
            var sourceSnapshots = new SqliteProjectionSourceSnapshotReader(db, stateSpaces, entities);
            var objectMaterializer = new ProjectionMaterializer(projectionRegistry, entities,
                stateSpaces, schemas, snapshots: sourceSnapshots);
            var objectCollections = new ProjectionCollectionMaterializer(projectionRegistry,
                objectMaterializer, edges, entities, entities, schemas, projectionTransactions);
            var objectProjections = new ApplicationMechanicObjectProjectionResolver(
                projectionRegistry, objectMaterializer, objectCollections, entities);
            var evaluator = new ApplicationMechanicEvaluator(
                catalogs, new ApplicationMechanicProjectionResolver(db, stateSpaces),
                new JintMechanicEngine(), objectProjections: objectProjections);
            var mappings = new ApplicationMechanicProjectionMappingResolver(
                catalogs, stateSpaces, types, edges);
            var clockParticipant = new ApplicationClockEventTransactionParticipant(
                new EventTypeStore(db), new EventLedger(db), schemas);
            var declaredEventParticipant = new ApplicationDeclaredEventTransactionParticipant(
                db, new EventLedger(db));
            IReadOnlyList<IApplicationEcsTransactionParticipant> participants =
                failTransactionAfterEffects
                    ? [clockParticipant, declaredEventParticipant,
                        new RejectAfterEffectsTransactionParticipant()]
                    : [clockParticipant, declaredEventParticipant];
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
            var retainedObjectVersions = Directory.EnumerateFiles(Path.Combine(
                    RepositoryRoot(), "catalog", "applications", "dnd2024", "objects"), "*.json", SearchOption.AllDirectories)
                .Select(path => ApplicationObjectDocument.Parse(File.ReadAllText(path), Application))
                .SelectMany(value => value.ComponentInputs).GroupBy(value => value.Type.QualifiedTypeId)
                .ToDictionary(group => group.Key, group => group.Max(value => value.Type.TypeVersion) - 1);
            foreach (var path in Directory.EnumerateFiles(
                         applicationComponentDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(path => !path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
            {
                var componentId = Path.GetFileNameWithoutExtension(path);
                if (primaryTypeIds.Contains(componentId)) continue;

                var definition = await DefinitionAsync(componentId);
                var retainedVersions = retainedObjectVersions.GetValueOrDefault(componentId);
                RegisterPriorComponentVersions(types, definition.Id, retainedVersions);
                additionalTypes[definition.Id] = types.Define(new(Application, definition.Id, definition.Schema));
            }
            foreach (var componentId in new[]
                     {
                         "game.core.world.root", "game.core.world.clock",
                         "game.core.world.fact", "game.core.campaign.root",
                         "game.core.campaign.character-participation",
                         "game.core.world.location", "game.core.world.faction", "game.core.world.route",
                         "game.core.world.route.availability", "game.core.world.traveller",
                         "game.core.media.visual", "game.core.world.media.visual", "game.core.rules.readable"
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
            foreach (var eventName in new[] { "started", "progressed", "interrupted", "completed" })
            {
                await new EventTypeStore(db).WriteAsync(new()
                {
                    Id = "dnd2024.rest." + eventName,
                    Category = "dnd2024.ruleset.core.gameplay.rest",
                    Name = "Rest " + eventName,
                    Scope = "world",
                    Status = EventTypeStatus.Active,
                    PayloadSchema = await File.ReadAllTextAsync(Path.Combine(
                        RepositoryRoot(), "catalog", "event-types", "dnd2024", "rest",
                        eventName + ".schema.json"))
                });
            }
            foreach (var eventName in new[] { "journey-recorded", "arrived" })
            {
                await new EventTypeStore(db).WriteAsync(new()
                {
                    Id = "dnd2024.travel." + eventName,
                    Category = "dnd2024.ruleset.core.gameplay.exploration",
                    Name = "Travel " + eventName,
                    Scope = "world",
                    Status = EventTypeStatus.Active,
                    PayloadSchema = await File.ReadAllTextAsync(Path.Combine(
                        RepositoryRoot(), "catalog", "event-types", "dnd2024", "travel",
                        eventName + ".schema.json"))
                });
            }
            foreach (var eventName in new[]
                     {
                         "trap-detected", "trap-disarmed", "trap-triggered", "trap-reset",
                         "trap-cleared", "exposure-started", "exposure-resolved", "exposure-recovered",
                         "poison-applied", "poison-progressed", "poison-recovered",
                         "contagion-exposed", "contagion-transmitted", "contagion-progressed",
                         "contagion-recovered", "curse-bound", "curse-discovered", "curse-removed"
                     })
            {
                await new EventTypeStore(db).WriteAsync(new()
                {
                    Id = "dnd2024.hazard." + eventName,
                    Category = "dnd2024.ruleset.core.gameplay.hazards",
                    Name = "Hazard " + eventName,
                    Scope = "world",
                    Status = EventTypeStatus.Active,
                    PayloadSchema = await File.ReadAllTextAsync(Path.Combine(
                        RepositoryRoot(), "catalog", "event-types", "dnd2024", "hazard",
                        eventName + ".schema.json"))
                });
            }
            foreach (var eventName in new[] { "damaged", "destroyed", "repaired" })
            {
                await new EventTypeStore(db).WriteAsync(new()
                {
                    Id = "dnd2024.object." + eventName,
                    Category = "dnd2024.ruleset.core.gameplay.objects",
                    Name = "Object " + eventName,
                    Scope = "world",
                    Status = EventTypeStatus.Active,
                    PayloadSchema = await File.ReadAllTextAsync(Path.Combine(
                        RepositoryRoot(), "catalog", "event-types", "dnd2024", "object",
                        eventName + ".schema.json"))
                });
            }
            await new EventTypeStore(db).WriteAsync(new()
            {
                Id = "dnd2024.social.attitude-changed",
                Category = "dnd2024.ruleset.core.gameplay.social",
                Name = "Social attitude changed",
                Scope = "world",
                Status = EventTypeStatus.Active,
                PayloadSchema = await File.ReadAllTextAsync(Path.Combine(
                    RepositoryRoot(), "catalog", "event-types", "dnd2024", "social",
                    "attitude-changed.schema.json"))
            });
            foreach (var eventName in new[] { "started", "progressed", "completed", "cancelled" })
            {
                await new EventTypeStore(db).WriteAsync(new()
                {
                    Id = "dnd2024.downtime." + eventName,
                    Category = "dnd2024.ruleset.core.gameplay.downtime",
                    Name = "Downtime " + eventName,
                    Scope = "world",
                    Status = EventTypeStatus.Active,
                    PayloadSchema = await File.ReadAllTextAsync(Path.Combine(
                        RepositoryRoot(), "catalog", "event-types", "dnd2024", "downtime",
                        eventName + ".schema.json"))
                });
            }
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

        private static void RegisterPriorComponentVersions(
            SqliteComponentTypeRegistry types,
            string componentId,
            int count)
        {
            for (var version = 1; version <= count; version++)
                types.Define(new(Application, componentId,
                    $$"""{"type":"object","title":"retained-prior-v{{version}}"}"""));
        }

        public async Task<ApplicationMechanicEvaluationResult> EvaluateAsync(
            string subjectId, string input, long seed, string localMechanicId = "dnd2024.mechanic.check.ability")
            => await EvaluateRolesAsync(localMechanicId, new Dictionary<string, string> { ["subject"] = subjectId }, input, seed);

        public async Task<ApplicationMechanicEvaluationResult> EvaluateRolesAsync(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed,
            MechanicAudienceContext? audience = null)
            => await EvaluateRolesWithMappingAsync(localMechanicId, roles, input, seed,
                includeGameBaseMapping: true, audience);

        public async Task<ApplicationMechanicEvaluationResult> EvaluateRolesWithoutGameBaseMappingAsync(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed)
            => await EvaluateRolesWithMappingAsync(localMechanicId, roles, input, seed,
                includeGameBaseMapping: false);

        private async Task<ApplicationMechanicEvaluationResult> EvaluateRolesWithMappingAsync(
            string localMechanicId, IReadOnlyDictionary<string, string> roles, string input, long seed,
            bool includeGameBaseMapping, MechanicAudienceContext? audience = null)
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
                    ["encounter.active-turn"] = "dnd2024.encounter.active-turn",
                    ["hazard.detected"] = "dnd2024.hazard.detected",
                    ["hazard.exposure.subject"] = "dnd2024.hazard.exposure.subject",
                    ["hazard.exposure.schedule"] = "dnd2024.hazard.exposure.schedule",
                    ["hazard.exposure.recovered"] = "dnd2024.hazard.exposure.recovered"
                });
            var applications = new SqliteApplicationRegistry(_db);
            var stateSpaces = new SqliteStateSpaceRegistry(_db, applications);
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(_db, schemas);
            var registry = new SqliteProjectionDefinitionRegistry(_db, types, schemas, applications);
            var transactions = new SqliteProjectionReadTransaction(_db);
            var materializer = new ProjectionMaterializer(registry, Entities, stateSpaces, schemas,
                snapshots: new SqliteProjectionSourceSnapshotReader(_db, stateSpaces, Entities));
            var collections = new ProjectionCollectionMaterializer(registry, materializer, Edges,
                Entities, Entities, schemas, transactions);
            var objectResolver = new ApplicationMechanicObjectProjectionResolver(
                registry, materializer, collections, Entities);
            return await new ApplicationMechanicEvaluator(
                _catalogs, new ApplicationMechanicProjectionResolver(_db, stateSpaces),
                new JintMechanicEngine(), objectProjections: objectResolver).EvaluateAsync(new(
                    StateSpaceId, Application, record.Summary.QualifiedId, record.Summary.ContentFingerprint,
                mapping, roles, input, seed, Audience: audience));
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

        public async Task AddTravelFixturesAsync(
            int terrainMultiplier = 1,
            int visibilityMultiplier = 1,
            bool navigationRequired = false,
            int navigationDc = 10,
            int? exposureCadenceMinutes = null)
        {
            await Entities.CreateEntityAsync(StateSpaceId, "world.travel.fixture", "Travel World");
            await AddApplicationComponentAsync("world.travel.fixture", "game.core.world.root",
                "{\"status\":\"active\",\"summary\":\"A bounded travel test world.\",\"visibility\":\"party\"}");
            await AddApplicationComponentAsync("world.travel.fixture", "game.core.world.clock",
                "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":7}");

            foreach (var (id, name) in new[]
                     {
                         ("location.travel.origin", "Travel Origin"),
                         ("location.travel.destination", "Travel Destination")
                     })
            {
                await Entities.CreateEntityAsync(StateSpaceId, id, name);
                await AddApplicationComponentAsync(id, "game.core.world.location",
                    "{\"kind\":\"site\",\"status\":\"active\",\"summary\":\"A travel fixture location.\",\"visibility\":\"party\"}");
                await Edges.MoveContainmentAsync(StateSpaceId, id, "world.travel.fixture", "location", 0);
            }
            await AddApplicationComponentAsync("subject.high", "game.core.world.traveller",
                "{\"status\":\"active\"}");
            await Edges.MoveContainmentAsync(StateSpaceId, "subject.high",
                "location.travel.origin", "presence", 0);

            await Entities.CreateEntityAsync(StateSpaceId, "terrain.travel.fixture", "Travel Terrain");
            await AddApplicationComponentAsync("terrain.travel.fixture", "dnd2024.exploration.terrain",
                "{\"terrainType\":{\"entityId\":\"dnd2024.vocabulary.terrain-type.plains\"},\"maximumPace\":{\"entityId\":\"dnd2024.vocabulary.travel-pace.fast\"}}");
            await Entities.CreateEntityAsync(StateSpaceId, "visibility.travel.fixture", "Travel Visibility");
            await AddApplicationComponentAsync("visibility.travel.fixture", "dnd2024.exploration.visibility",
                "{\"basis\":{\"entityId\":\"dnd2024.vocabulary.visibility.clear\"}}");

            await Entities.CreateEntityAsync(StateSpaceId, "route.travel.fixture", "Travel Route");
            await AddApplicationComponentAsync("route.travel.fixture", "game.core.world.route",
                "{\"status\":\"active\",\"summary\":\"A six-mile authored route.\",\"visibility\":\"party\",\"mode\":\"on-foot\",\"durationMinutes\":120}");
            await AddApplicationComponentAsync("route.travel.fixture", "game.core.world.route.availability",
                "{\"status\":\"open\"}");
            var navigation = navigationRequired
                ? JsonSerializer.Serialize(new
                {
                    required = true,
                    ability = "wis",
                    skill = "survival",
                    dc = navigationDc,
                    rollCircumstances = Array.Empty<object>()
                })
                : "{\"required\":false}";
            var exposure = exposureCadenceMinutes is int cadence
                ? JsonSerializer.Serialize(new
                {
                    enabled = true,
                    cadenceMinutes = cadence,
                    hazard = new { entityId = "dnd2024.hazard.environment.travel-fixture" }
                })
                : "{\"enabled\":false}";
            var profile = "{\"revision\":1,\"fingerprint\":\"" + new string('A', 64)
                          + "\",\"world\":{\"entityId\":\"world.travel.fixture\"},\"origin\":{\"entityId\":\"location.travel.origin\"},\"destination\":{\"entityId\":\"location.travel.destination\"},\"distance\":{\"dimension\":\"distance\",\"value\":{\"numerator\":6,\"denominator\":1},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.mile\"}},\"allowedModes\":[{\"entityId\":\"dnd2024.vocabulary.movement-mode.walk\"}],\"terrain\":{\"entityId\":\"terrain.travel.fixture\"},\"visibility\":{\"entityId\":\"visibility.travel.fixture\"},\"terrainDurationMultiplier\":{\"numerator\":"
                          + terrainMultiplier
                          + ",\"denominator\":1},\"visibilityDurationMultiplier\":{\"numerator\":"
                          + visibilityMultiplier
                          + ",\"denominator\":1},\"navigation\":" + navigation
                          + ",\"exposure\":" + exposure
                          + ",\"arrivalPolicy\":\"move-record-and-visit\"}";
            await AddApplicationComponentAsync("route.travel.fixture",
                "dnd2024.exploration.route-profile", profile);
        }

        public async Task AddHazardFixturesAsync(
            int detectionDc = 0,
            int disarmDc = 0,
            int exposureDc = 100)
        {
            await AddProficiencyStateAsync("subject.high", 1, ["perception", "sleight-of-hand"]);
            await AddSavingThrowStateAsync("subject.high", ["dex", "con"]);
            await AddHitPointsAsync("subject.high", 10, 10);
            await AddApplicationComponentAsync("subject.high", "dnd2024.creature.defenses",
                "{\"damageResponses\":[]}");
            await AddApplicationComponentAsync("subject.high", "dnd2024.conditions",
                "{\"entries\":[],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary\"}}");

            foreach (var (id, name, ability, skill, dc) in new[]
                     {
                         ("activity.hazard.detect.fixture", "Detect Fixture Trap", "wisdom", "perception", detectionDc),
                         ("activity.hazard.disarm.fixture", "Disarm Fixture Trap", "dexterity", "sleight-of-hand", disarmDc),
                         ("activity.hazard.exposure.fixture", "Endure Fixture Exposure", "constitution", null, exposureDc)
                     })
            {
                await Entities.CreateEntityAsync(StateSpaceId, id, name);
                await AddApplicationComponentAsync(id, "dnd2024.core.version",
                    "{\"revision\":1,\"status\":\"active\"}");
                await AddApplicationComponentAsync(id, "dnd2024.activity.check",
                    JsonSerializer.Serialize(new
                    {
                        abilityOptions = new[]
                        {
                            new { entityId = "dnd2024.vocabulary.ability." + ability }
                        },
                        proficiencySources = skill is null
                            ? Array.Empty<object>()
                            : new object[] { new { entityId = "dnd2024.vocabulary.skill." + skill } },
                        difficulty = dc
                    }));
            }

            await Entities.CreateEntityAsync(StateSpaceId, "hazard.trap.definition.fixture", "Fixture Needle Trap");
            await AddApplicationComponentAsync("hazard.trap.definition.fixture", "dnd2024.core.version",
                "{\"revision\":1,\"status\":\"active\"}");
            await AddApplicationComponentAsync("hazard.trap.definition.fixture", "dnd2024.core.source",
                "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Slice 10 focused fixture\"}]}");
            await AddApplicationComponentAsync("hazard.trap.definition.fixture", "dnd2024.hazard.trap",
                JsonSerializer.Serialize(new
                {
                    category = new { entityId = "dnd2024.hazard.category.trap" },
                    trigger = new { @event = "hazard.fixture.entered", timing = "when" },
                    duration = new { kind = "instantaneous" },
                    detectionActivity = new { entityId = "activity.hazard.detect.fixture" },
                    disarmActivity = new { entityId = "activity.hazard.disarm.fixture" },
                    triggerEffects = new object[]
                    {
                        new
                        {
                            effect = new { entityId = "dnd2024.mechanic.hazard.damage.apply" },
                            parameters = new Dictionary<string, object>
                            {
                                ["amount"] = 3,
                                ["damageType"] = new { entityId = "dnd2024.vocabulary.damage-type.piercing" },
                                ["saveAbility"] = "dex",
                                ["saveDc"] = 100,
                                ["successfulSaveBehavior"] = "none"
                            }
                        },
                        new
                        {
                            effect = new { entityId = "dnd2024.mechanic.conditions.write" },
                            parameters = new Dictionary<string, object>
                            {
                                ["condition"] = "prone",
                                ["saveAbility"] = "dex",
                                ["saveDc"] = 100,
                                ["successfulSaveBehavior"] = "none"
                            }
                        }
                    },
                    reset = new { kind = "dawn" }
                }));
            await Entities.CreateEntityAsync(StateSpaceId, "hazard.trap.instance.fixture", "Hidden Fixture Needle Trap");
            await AddApplicationComponentAsync("hazard.trap.instance.fixture", "dnd2024.core.definition-link",
                "{\"definition\":{\"entityId\":\"hazard.trap.definition.fixture\"},\"definitionRevision\":1}");
            await AddApplicationComponentAsync("hazard.trap.instance.fixture", "dnd2024.hazard.trap-state",
                "{\"phase\":{\"entityId\":\"dnd2024.hazard.trap-phase.armed\"},\"activationCount\":0}");

            await Entities.CreateEntityAsync(StateSpaceId, "hazard.environment.definition.fixture", "Fixture Bitter Cold");
            await AddApplicationComponentAsync("hazard.environment.definition.fixture", "dnd2024.core.version",
                "{\"revision\":1,\"status\":\"active\"}");
            await AddApplicationComponentAsync("hazard.environment.definition.fixture", "dnd2024.core.source",
                "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Slice 10 focused fixture\"}]}");
            await AddApplicationComponentAsync("hazard.environment.definition.fixture", "dnd2024.hazard.environment",
                JsonSerializer.Serialize(new
                {
                    category = new { entityId = "dnd2024.hazard.category.environment" },
                    exposureTrigger = new { @event = "hazard.fixture.exposed", timing = "when" },
                    checkActivity = new { entityId = "activity.hazard.exposure.fixture" },
                    exposureInterval = new
                    {
                        kind = "measured", amount = 60,
                        unit = new { entityId = "dnd2024.vocabulary.time-unit.minute" }
                    },
                    effects = new object[]
                    {
                        new
                        {
                            effect = new { entityId = "dnd2024.mechanic.hazard.damage.apply" },
                            parameters = new Dictionary<string, object>
                            {
                                ["amount"] = 2,
                                ["damageType"] = new { entityId = "dnd2024.vocabulary.damage-type.cold" },
                                ["successfulSaveBehavior"] = "none"
                            }
                        },
                        new
                        {
                            effect = new { entityId = "dnd2024.mechanic.conditions.write" },
                            parameters = new Dictionary<string, object>
                            {
                                ["condition"] = "prone",
                                ["successfulSaveBehavior"] = "none"
                            }
                        }
                    },
                    mitigations = new[]
                    {
                        new
                        {
                            @operator = "predicate", predicateId = "predicate.hazard.fixture.shelter",
                            arguments = new[] { "shelter" }
                        }
                    }
                }));

            await Entities.CreateEntityAsync(StateSpaceId, "world.hazard.fixture", "Hazard World");
            await AddApplicationComponentAsync("world.hazard.fixture", "game.core.world.root",
                "{\"status\":\"active\",\"summary\":\"A bounded hazard test world.\",\"visibility\":\"party\"}");
            await AddApplicationComponentAsync("world.hazard.fixture", "game.core.world.clock",
                "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":7}");
        }

        public async Task AddExposureRecordAsync(string exposureId = "hazard.exposure.fixture")
        {
            await Entities.CreateEntityAsync(StateSpaceId, exposureId, "Fixture environmental exposure");
            await AddApplicationComponentAsync(exposureId, "dnd2024.core.definition-link",
                "{\"definition\":{\"entityId\":\"hazard.environment.definition.fixture\"},\"definitionRevision\":1}");
            await AddApplicationComponentAsync(exposureId, "dnd2024.hazard.environment-exposure",
                "{\"accumulatedExposure\":{\"dimension\":\"time\",\"value\":{\"numerator\":0,\"denominator\":1},\"unit\":{\"entityId\":\"dnd2024.vocabulary.time-unit.minute\"}},\"exposureCount\":0,\"failedChecks\":0}");
            await Edges.SetRelationshipAsync(StateSpaceId, exposureId, "subject.high",
                "dnd2024.hazard.exposure.subject", "{}", 0);
        }

        public async Task AddAfflictionFixturesAsync()
        {
            await AddSavingThrowStateAsync("subject.high", ["con"]);
            await AddSavingThrowStateAsync("subject.low", ["con"]);
            await AddHitPointsAsync("subject.high", 10, 10);
            await AddHitPointsAsync("subject.low", 10, 10);
            foreach (var subjectId in new[] { "subject.high", "subject.low" })
            {
                await AddApplicationComponentAsync(subjectId, "dnd2024.creature.defenses",
                    "{\"damageResponses\":[]}");
                await AddApplicationComponentAsync(subjectId, "dnd2024.conditions",
                    "{\"entries\":[],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary\"}}");
            }

            await Entities.CreateEntityAsync(StateSpaceId, "world.affliction.fixture", "Affliction World");
            await AddApplicationComponentAsync("world.affliction.fixture", "game.core.world.root",
                "{\"status\":\"active\",\"summary\":\"A bounded affliction test world.\",\"visibility\":\"party\"}");
            await AddApplicationComponentAsync("world.affliction.fixture", "game.core.world.clock",
                "{\"calendarId\":\"calendar.fixture\",\"currentMinute\":100,\"revision\":7}");
            await Entities.CreateEntityAsync(StateSpaceId, "item.affliction.source.fixture", "Affliction Source");
            await Entities.CreateEntityAsync(StateSpaceId, "item.curse.source.fixture", "Ordinary Looking Ring");
            await AddApplicationComponentAsync("item.curse.source.fixture", "dnd2024.magic-item.curse",
                "{\"curse\":{\"entityId\":\"hazard.curse.fixture\"}}");

            foreach (var (id, name, dc) in new[]
                     {
                         ("activity.poison.save.fail", "Resist Fixture Poison", 100),
                         ("activity.poison.save.success", "Resist Delayed Fixture Poison", 0),
                         ("activity.poison.recover.fixture", "Recover from Fixture Poison", 0),
                         ("activity.contagion.recover.fixture", "Recover from Fixture Contagion", 0),
                         ("activity.curse.discover.fixture", "Discover Fixture Curse", 0),
                         ("activity.curse.remove.fixture", "Remove Fixture Curse", 0)
                     })
            {
                await Entities.CreateEntityAsync(StateSpaceId, id, name);
                await AddApplicationComponentAsync(id, "dnd2024.core.version",
                    "{\"revision\":1,\"status\":\"active\"}");
                await AddApplicationComponentAsync(id, "dnd2024.activity.check",
                    JsonSerializer.Serialize(new
                    {
                        abilityOptions = new[]
                        {
                            new { entityId = "dnd2024.vocabulary.ability.constitution" }
                        },
                        proficiencySources = Array.Empty<object>(),
                        difficulty = dc
                    }));
            }

            var poisonEffects = new object[]
            {
                new
                {
                    effect = new { entityId = "dnd2024.mechanic.hazard.damage.apply" },
                    parameters = new Dictionary<string, object>
                    {
                        ["amount"] = 2,
                        ["damageType"] = new { entityId = "dnd2024.vocabulary.damage-type.poison" },
                        ["successfulSaveBehavior"] = "none"
                    }
                },
                new
                {
                    effect = new { entityId = "dnd2024.mechanic.conditions.write" },
                    parameters = new Dictionary<string, object>
                    {
                        ["condition"] = "poisoned",
                        ["successfulSaveBehavior"] = "none"
                    }
                }
            };
            foreach (var (id, name, activityId, onset) in new[]
                     {
                         ("hazard.poison.immediate.fixture", "Immediate Fixture Poison",
                             "activity.poison.save.fail", (object)new { kind = "instantaneous" }),
                         ("hazard.poison.success.fixture", "Avoidable Fixture Poison",
                             "activity.poison.save.success", (object)new { kind = "instantaneous" }),
                         ("hazard.poison.delayed.fixture", "Delayed Fixture Poison",
                             "activity.poison.save.success", (object)new
                             {
                                 kind = "measured", amount = 5,
                                 unit = new { entityId = "dnd2024.vocabulary.time-unit.minute" }
                             })
                     })
            {
                await Entities.CreateEntityAsync(StateSpaceId, id, name);
                await AddApplicationComponentAsync(id, "dnd2024.core.version",
                    "{\"revision\":1,\"status\":\"active\"}");
                await AddApplicationComponentAsync(id, "dnd2024.core.source",
                    "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Slice 11 focused poison fixture\"}]}");
                await AddApplicationComponentAsync(id, "dnd2024.hazard.poison",
                    JsonSerializer.Serialize(new
                    {
                        category = new { entityId = "dnd2024.hazard-category.poison" },
                        deliveryMethods = new[]
                        {
                            new { entityId = "dnd2024.vocabulary.poison-delivery.injury" }
                        },
                        onset,
                        duration = new { kind = "special" },
                        savingThrowActivity = new { entityId = activityId },
                        effects = poisonEffects,
                        recoveryActivities = new[]
                        {
                            new { entityId = "activity.poison.recover.fixture" }
                        }
                    }));
            }

            await Entities.CreateEntityAsync(StateSpaceId, "hazard.contagion.fixture", "Fixture Contagion");
            await AddApplicationComponentAsync("hazard.contagion.fixture", "dnd2024.core.version",
                "{\"revision\":1,\"status\":\"active\"}");
            await AddApplicationComponentAsync("hazard.contagion.fixture", "dnd2024.core.source",
                "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Slice 11 focused contagion fixture\"}]}");
            await AddApplicationComponentAsync("hazard.contagion.fixture", "dnd2024.hazard.contagion",
                JsonSerializer.Serialize(new
                {
                    transmissionTriggers = new[]
                    {
                        new { @event = "contagion.fixture.contact", timing = "when" }
                    },
                    incubation = new
                    {
                        kind = "measured", amount = 5,
                        unit = new { entityId = "dnd2024.vocabulary.time-unit.minute" }
                    },
                    activeEffects = new object[]
                    {
                        new
                        {
                            effect = new { entityId = "dnd2024.mechanic.hazard.damage.apply" },
                            parameters = new Dictionary<string, object>
                            {
                                ["amount"] = 1,
                                ["damageType"] = new { entityId = "dnd2024.vocabulary.damage-type.poison" }
                            }
                        },
                        new
                        {
                            effect = new { entityId = "dnd2024.mechanic.conditions.write" },
                            parameters = new Dictionary<string, object> { ["condition"] = "poisoned" }
                        }
                    },
                    recoveryActivity = new { entityId = "activity.contagion.recover.fixture" },
                    recoveryRequirement = new
                    {
                        @operator = "predicate", predicateId = "predicate.always",
                        arguments = Array.Empty<object>()
                    },
                    postRecoveryImmunity = new { kind = "special" }
                }));

            await Entities.CreateEntityAsync(StateSpaceId, "hazard.curse.fixture", "Fixture Curse");
            await AddApplicationComponentAsync("hazard.curse.fixture", "dnd2024.core.version",
                "{\"revision\":1,\"status\":\"active\"}");
            await AddApplicationComponentAsync("hazard.curse.fixture", "dnd2024.core.source",
                "{\"citations\":[{\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"},\"locator\":\"Slice 11 focused curse fixture\"}]}");
            await AddApplicationComponentAsync("hazard.curse.fixture", "dnd2024.hazard.curse",
                JsonSerializer.Serialize(new
                {
                    bindingTrigger = new { @event = "curse.fixture.bound", timing = "when" },
                    duration = new { kind = "permanent" },
                    effects = new object[]
                    {
                        new
                        {
                            effect = new { entityId = "dnd2024.mechanic.conditions.write" },
                            parameters = new Dictionary<string, object> { ["condition"] = "frightened" }
                        }
                    },
                    removalRequirement = new
                    {
                        @operator = "predicate", predicateId = "predicate.always",
                        arguments = Array.Empty<object>()
                    },
                    removalActivities = new[]
                    {
                        new { entityId = "activity.curse.remove.fixture" }
                    },
                    discoveryActivity = new { entityId = "activity.curse.discover.fixture" }
                }));
        }

        public async Task SetAfflictionClockAsync(int currentMinute, int revision)
            => await ReplaceApplicationComponentRawAsync("world.affliction.fixture", "game.core.world.clock",
                JsonSerializer.Serialize(new
                {
                    calendarId = "calendar.fixture", currentMinute, revision
                }));

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

    protected static Dictionary<string, string> BasicCreationRoles(
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

    protected static string AlertGrantState(
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

    protected static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
