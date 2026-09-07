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

public sealed class Dnd2024CharacterCreationAndRestTests : Dnd2024TestBase
{
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
        Assert.Equal(3, evaluated.Projection!.Objects.Count);
        Assert.All(evaluated.Projection.Roles.Values, role => Assert.Empty(role.Components));
        Assert.Equal(2, evaluated.Run!.Output.Effects.Count);
        Assert.Single(evaluated.Run.Output.Events);
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

    [Fact]
    public async Task Character_creation_rest_begin_rolls_back_object_reducer_effects_and_event_on_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddRestBeginFixturesAsync(currentHitPoints: 1, currentMinute: 321);

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", RestBeginRoles(), "{\"kind\":\"short\"}", 0,
            "f223456789abcdef0123456789abcdee"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high", "dnd2024.rest.world"));
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
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
        if (result.Run is not null)
            Assert.Empty(result.Run.Output.Effects);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
    }

    [Fact]
    public async Task Character_creation_rest_begin_no_longer_requires_legacy_component_projection_mapping()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync();

        var result = await harness.EvaluateRolesWithoutGameBaseMappingAsync(
            "dnd2024.mechanic.rest.begin", RestBeginRoles(), "{\"kind\":\"short\"}", 0);

        Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems));
        Assert.Equal(3, result.Projection!.Objects.Count);
        Assert.All(result.Projection.Roles.Values, role => Assert.Empty(role.Components));
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
        Assert.Single(finalEvaluation.Run!.Output.Events);
        Assert.Empty(finalEvaluation.Run.Output.Notifications);
        Assert.Contains("\"benefitsGranted\":false", finalEvaluation.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, final.Disposition);
        using var finalState = JsonDocument.Parse(ready!.ValueJson);
        Assert.Equal("ready", finalState.RootElement.GetProperty("status").GetString());
        Assert.Equal(60, finalState.RootElement.GetProperty("lightActivityMinutes").GetInt32());
        Assert.Equal(3, ready.Revision);
        Assert.Equal(2, (await harness.EventsAsync(first.OperationId)).Count);
        Assert.Equal(2, (await harness.EventsAsync(final.OperationId)).Count);
    }

    [Theory]
    [InlineData("short", 60, 1, 0)]
    [InlineData("long", 480, 10, 9)]
    public async Task Duration_ready_rest_completes_benefits_events_and_replay_atomically(
        string kind, int duration, int expectedHitPoints, int expectedRestored)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentHitPoints: 1, currentMinute: 100);
        if (kind == "long")
        {
            await harness.AddClassMembershipAsync(
                "subject.high", "fighter", "dnd2024.content.class.fighter.v1", 3);
            await harness.AddApplicationComponentAsync("subject.high", "dnd2024.character.hit-dice",
                "{\"pools\":{\"subject.high.class-membership.fighter\":{\"dieRef\":{\"entityId\":\"dnd2024.vocabulary.die.d10\"},\"spent\":2}}}");
            await harness.AddApplicationComponentAsync("subject.high", "dnd2024.conditions",
                "{\"entries\":[{\"condition\":\"exhaustion\",\"level\":2}],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary\"}}");
            await harness.ReplaceCoreComponentRawAsync("subject.high",
                "dnd2024.creature.ability-scores",
                "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":30,\"dnd2024.vocabulary.ability.dexterity\":10,\"dnd2024.vocabulary.ability.constitution\":8,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}");
            await harness.AddApplicationComponentAsync("subject.high",
                "dnd2024.creature.ability-score-basis",
                "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":30,\"dnd2024.vocabulary.ability.dexterity\":10,\"dnd2024.vocabulary.ability.constitution\":10,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}");
            await harness.Entities.CreateEntityAsync(
                DndHarness.StateSpaceId, "resource.definition.fixture", "Long-rest resource");
            await harness.AddApplicationComponentAsync("resource.definition.fixture",
                "dnd2024.resource.definition",
                "{\"usageLimit\":{\"maximum\":3,\"recharge\":{\"kind\":\"long-rest\"}}}");
            await harness.Entities.CreateEntityAsync(
                DndHarness.StateSpaceId, "resource.pool.fixture", "Resource pool");
            await harness.AddApplicationComponentAsync("resource.pool.fixture", "dnd2024.resource.pool",
                "{\"resourceDefinition\":{\"entityId\":\"resource.definition.fixture\"},\"expended\":2}");
            await harness.Edges.MoveContainmentAsync(DndHarness.StateSpaceId,
                "resource.pool.fixture", "subject.high", "resource", 0);
        }
        var roles = RestBeginRoles();
        var started = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"" + kind + "\"}", 0,
            "31200000000000000000000000000001"));
        ApplicationActionExecutionResult progressed;
        if (kind == "long")
        {
            await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", roles,
                "{\"activity\":\"sleep\",\"minutes\":360}", 0,
                "31200000000000000000000000000002"));
            progressed = await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", roles,
                "{\"activity\":\"light\",\"minutes\":120}", 0,
                "31200000000000000000000000000003"));
        }
        else
        {
            progressed = await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.rest.progress", roles,
                "{\"activity\":\"light\",\"minutes\":60}", 0,
                "31200000000000000000000000000003"));
        }
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.rest.complete", roles, "{\"hitDice\":[]}", 0,
            "31200000000000000000000000000004");

        var completed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var hp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points");
        var completion = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-completion");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, started.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, progressed.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, completed.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        using (var hitPoints = JsonDocument.Parse(hp!.ValueJson))
            Assert.Equal(expectedHitPoints, hitPoints.RootElement.GetProperty("current").GetInt32());
        using (var terminal = JsonDocument.Parse(completion!.ValueJson))
        {
            Assert.Equal("complete", terminal.RootElement.GetProperty("status").GetString());
            Assert.Equal(kind, terminal.RootElement.GetProperty("kind").GetString());
            Assert.Equal(100 + duration,
                terminal.RootElement.GetProperty("completedAtMinute").GetInt32());
            Assert.Equal(expectedRestored, terminal.RootElement.GetProperty("benefits")
                .GetProperty("hitPointsRestored").GetInt32());
            if (kind == "long")
            {
                var benefits = terminal.RootElement.GetProperty("benefits");
                Assert.Equal(2, benefits.GetProperty("hitDiceRecovered").GetInt32());
                Assert.Equal(1, benefits.GetProperty("resourcePoolsRestored").GetInt32());
                Assert.Equal(1, benefits.GetProperty("exhaustionLevelsReduced").GetInt32());
                Assert.Equal(1, benefits.GetProperty("abilityScoresRestored").GetInt32());
            }
        }
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high", "dnd2024.rest.world"));
        Assert.Single(await harness.EventsAsync(started.OperationId));
        Assert.Equal(2, (await harness.EventsAsync(progressed.OperationId)).Count);
        Assert.Single(await harness.EventsAsync(completed.OperationId));
        Assert.Equal("dnd2024.rest.completed",
            Assert.Single(await harness.EventsAsync(completed.OperationId)).TypeId);
        if (kind == "long")
        {
            using var dice = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "subject.high", "dnd2024.character.hit-dice"))!.ValueJson);
            Assert.Equal(0, dice.RootElement.GetProperty("pools")
                .GetProperty("subject.high.class-membership.fighter").GetProperty("spent").GetInt32());
            using var conditions = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions"))!.ValueJson);
            Assert.Equal(1, Assert.Single(conditions.RootElement.GetProperty("entries").EnumerateArray())
                .GetProperty("level").GetInt32());
            using var abilities = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores"))!.ValueJson);
            Assert.Equal(10, abilities.RootElement.GetProperty("scores")
                .GetProperty("dnd2024.vocabulary.ability.constitution").GetInt32());
            using var resource = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "resource.pool.fixture", "dnd2024.resource.pool"))!.ValueJson);
            Assert.Equal(0, resource.RootElement.GetProperty("expended").GetInt32());
        }
    }

    [Fact]
    public async Task Rest_completion_rolls_back_benefits_terminal_state_and_event_on_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddRestBeginFixturesAsync(currentHitPoints: 1, currentMinute: 580);
        await harness.AddApplicationComponentAsync("subject.high", "dnd2024.rest-episode",
            "{\"policyEntityId\":\"dnd2024.content.rest-policy.standard.v1\",\"kind\":\"long\",\"worldId\":\"world.rest.fixture\",\"startedAtMinute\":100,\"observedAtMinute\":580,\"observedClockRevision\":7,\"requiredMinutes\":480,\"sleepMinutes\":360,\"lightActivityMinutes\":120,\"interruptionCount\":0,\"status\":\"ready\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary > Long Rest, PDF page 185\"}}");
        await harness.Edges.SetRelationshipAsync(DndHarness.StateSpaceId,
            "world.rest.fixture", "subject.high", "dnd2024.rest.world", "{}", 0);

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.complete", RestBeginRoles(), "{\"hitDice\":[]}", 0,
            "31300000000000000000000000000001"));
        var hp = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points");

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        using (var hitPoints = JsonDocument.Parse(hp!.ValueJson))
            Assert.Equal(1, hitPoints.RootElement.GetProperty("current").GetInt32());
        Assert.NotNull(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-completion"));
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(
            DndHarness.StateSpaceId, "world.rest.fixture", "subject.high", "dnd2024.rest.world"));
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
    }

    [Fact]
    public async Task Rest_completion_rejects_stale_policy_before_any_benefit()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddRestBeginFixturesAsync(currentHitPoints: 1, currentMinute: 100);
        var roles = RestBeginRoles();
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.begin", roles, "{\"kind\":\"short\"}", 0,
            "31400000000000000000000000000001"));
        await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.progress", roles,
            "{\"activity\":\"light\",\"minutes\":60}", 0,
            "31400000000000000000000000000002"));
        var policy = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "dnd2024.content.rest-policy.standard.v1", "dnd2024.rest-policy");
        await harness.ReplaceApplicationComponentRawAsync(
            "dnd2024.content.rest-policy.standard.v1", "dnd2024.rest-policy",
            policy!.ValueJson.Replace("\"policyVersion\":1", "\"policyVersion\":2",
                StringComparison.Ordinal));

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.rest.complete", roles, "{\"hitDice\":[]}", 0,
            "31400000000000000000000000000003"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.NotNull(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-episode"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.rest-completion"));
        using var hp = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points"))!.ValueJson);
        Assert.Equal(1, hp.RootElement.GetProperty("current").GetInt32());
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
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
}
