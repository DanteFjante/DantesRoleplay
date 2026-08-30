using System.Text.Json;
using System.Runtime.CompilerServices;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024WeaponActivityRepairTests
{
    private const string Strength = "dnd2024.vocabulary.ability.strength";
    private const string Dexterity = "dnd2024.vocabulary.ability.dexterity";
    private const string Proficiency = "dnd2024.vocabulary.proficiency-rank.proficiency";
    private const string Simple = "dnd2024.equipment.weapon-category.simple";
    private const string Martial = "dnd2024.equipment.weapon-category.martial";
    private const string Finesse = "dnd2024.equipment.weapon-property.finesse";
    private const string Light = "dnd2024.equipment.weapon-property.light";

    [Fact]
    public void Every_weapon_has_one_or_more_unique_schema_valid_exact_metre_activities()
    {
        var weaponPaths = Directory.GetFiles(WeaponRoot(), "*.json", SearchOption.TopDirectoryOnly);
        var activityPaths = Directory.GetFiles(ActivityRoot(), "*.json", SearchOption.TopDirectoryOnly);
        Assert.Equal(38, weaponPaths.Length);
        Assert.Equal(51, activityPaths.Length);

        var activityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in activityPaths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var id = root.GetProperty("id").GetString()!;
            Assert.True(activityIds.Add(id), "Duplicate activity ID: " + id);
            Assert.Equal("dnd2024.archetype.activity-definition", root.GetProperty("archetype").GetString());
            Assert.DoesNotContain("distance-unit.foot", root.GetRawText(), StringComparison.Ordinal);

            foreach (var component in root.GetProperty("components").EnumerateObject())
                AssertCurrentSchema(component.Name, component.Value.GetRawText());

            var range = root.GetProperty("components").GetProperty("dnd2024.activity.range")
                .GetProperty("range");
            Assert.Equal("distance", range.GetProperty("kind").GetString());
            AssertExactIntegerFeet(range.GetProperty("normal"));
            if (range.TryGetProperty("long", out var longRange)) AssertExactIntegerFeet(longRange);
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in weaponPaths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("dnd2024.archetype.weapon-definition", root.GetProperty("archetype").GetString());
            var components = root.GetProperty("components");
            AssertCurrentSchema("dnd2024.item.weapon",
                components.GetProperty("dnd2024.item.weapon").GetRawText());
            var membership = components.GetProperty("dnd2024.activity.membership");
            AssertCurrentSchema("dnd2024.activity.membership", membership.GetRawText());
            var activities = membership.GetProperty("activities").EnumerateArray().ToArray();
            Assert.NotEmpty(activities);
            foreach (var activity in activities)
            {
                var id = activity.GetProperty("entityId").GetString()!;
                Assert.Equal("dnd2024.archetype.activity-definition",
                    activity.GetProperty("expectedArchetype").GetString());
                Assert.Contains(id, activityIds);
                Assert.True(referenced.Add(id), "Activity is referenced more than once: " + id);
            }
        }

        Assert.Equal(activityIds.Order(StringComparer.Ordinal), referenced.Order(StringComparer.Ordinal));
        AssertRange("dnd2024.equipment.weapon.dagger.attack.melee", 381, 250);
        AssertRange("dnd2024.equipment.weapon.glaive.attack", 381, 125);
        AssertRange("dnd2024.equipment.weapon.dagger.attack.thrown", 762, 125, 2286, 125);
        AssertRange("dnd2024.equipment.weapon.longbow.attack", 1143, 25, 4572, 25);
    }

    [Fact]
    public void Retained_weapon_contracts_use_current_owners_and_existing_categories()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["weapon-profile.write"] = "ruleset.dnd2024.core.data.weapon-profile",
            ["weapon-attack"] = "ruleset.dnd2024.core.gameplay.weapon-attacks",
            ["weapon-damage.roll"] = "ruleset.dnd2024.core.gameplay.weapon-damage"
        };
        foreach (var (name, category) in expected)
        {
            var contract = File.ReadAllText(MechanicPath(name) + ".md");
            Assert.Contains("category: " + category, contract, StringComparison.Ordinal);
            Assert.DoesNotContain("\"dnd2024.weapon-profile\"", contract, StringComparison.Ordinal);
            using var requirements = JsonDocument.Parse(RequirementsJson(contract));
            Assert.True(requirements.RootElement.TryGetProperty("roles", out _));
        }
    }

    [Fact]
    public async Task Writer_record_mode_runs_through_projection_when_normalized_facets_are_absent()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var world = new WorldStore(db);
        foreach (var componentId in new[]
                 {
                     "dnd2024.item.weapon", "dnd2024.activity.membership", "dnd2024.core.version",
                     "dnd2024.activity.activation", "dnd2024.activity.attack",
                     "dnd2024.activity.damage", "dnd2024.activity.range"
                 })
            await world.DefineComponentAsync(componentId, componentId, componentId);
        await world.CreateEntityAsync("Unrecorded weapon", "weapon.unrecorded");
        await world.CreateEntityAsync("New attack", "activity.new-attack");
        await world.SetComponentAsync("activity.new-attack", "dnd2024.core.version",
            "{\"revision\":1,\"status\":\"active\"}");

        var contract = File.ReadAllText(MechanicPath("weapon-profile.write") + ".md");
        var requirements = MechanicRequirements.Parse(RequirementsJson(contract));
        const string input =
            "{\"mode\":\"record\",\"categoryId\":\"dnd2024.equipment.weapon-category.simple\",\"attackMode\":\"ranged\",\"abilityIds\":[\"dnd2024.vocabulary.ability.dexterity\"],\"damage\":{\"kind\":\"dice\",\"count\":1,\"dieId\":\"dnd2024.vocabulary.die.d6\",\"typeId\":\"dnd2024.vocabulary.damage-type.piercing\"},\"range\":{\"normalFeet\":30,\"longFeet\":120}}";
        var resolved = await new ProjectionResolver(db).ResolveAsync(requirements,
            new Dictionary<string, string>
            {
                ["weapon"] = "weapon.unrecorded", ["activity"] = "activity.new-attack"
            }, input, seed: 71);

        Assert.True(resolved.Ok, string.Join("; ", resolved.Problems));
        Assert.Empty(resolved.Projection!.Roles["weapon"].Components);
        Assert.Equal(new[] { "dnd2024.core.version" },
            resolved.Projection.Roles["activity"].Components.Keys);
        var result = await RunAsync("weapon-profile.write", resolved.Projection);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(6, result.Output.Effects.Count);
        foreach (var effect in result.Output.Effects) AssertCurrentSchema(effect.DefinitionId, effect.Data);

        var rangeEffect = Assert.Single(result.Output.Effects,
            effect => effect.DefinitionId == "dnd2024.activity.range");
        using var range = JsonDocument.Parse(rangeEffect.Data);
        AssertRational(range.RootElement.GetProperty("range").GetProperty("normal"), 1143, 125);
        AssertRational(range.RootElement.GetProperty("range").GetProperty("long"), 4572, 125);
    }

    [Fact]
    public async Task Damage_roll_uses_selected_member_activity_for_dice_and_fixed_blowgun_damage()
    {
        var dagger = await RunDamageAsync("dagger", "dnd2024.equipment.weapon.dagger.attack.melee",
            "dex", critical: true);
        Assert.True(dagger.Ok, dagger.Error);
        using (var data = JsonDocument.Parse(dagger.Output.Data))
        {
            var root = data.RootElement;
            Assert.Equal("dnd2024.equipment.weapon.dagger.attack.melee",
                root.GetProperty("activityId").GetString());
            Assert.Equal("dice", root.GetProperty("amountKind").GetString());
            Assert.Equal(2, root.GetProperty("rolls").GetArrayLength());
            Assert.Equal(4, root.GetProperty("abilityModifier").GetInt32());
            Assert.Equal(root.GetProperty("subtotal").GetInt32() + 4,
                root.GetProperty("damage").GetInt32());
        }

        var blowgun = await RunDamageAsync("blowgun", "dnd2024.equipment.weapon.blowgun.attack",
            "dex", critical: true);
        Assert.True(blowgun.Ok, blowgun.Error);
        using (var data = JsonDocument.Parse(blowgun.Output.Data))
        {
            var root = data.RootElement;
            Assert.Equal("fixed", root.GetProperty("amountKind").GetString());
            Assert.Empty(root.GetProperty("rolls").EnumerateArray());
            Assert.Equal(1, root.GetProperty("subtotal").GetInt32());
            Assert.Equal(0, root.GetProperty("abilityModifier").GetInt32());
            Assert.Equal(1, root.GetProperty("damage").GetInt32());
        }

        var mismatch = await RunDamageAsync("dagger", "dnd2024.equipment.weapon.club.attack",
            "str", critical: false);
        Assert.False(mismatch.Ok);
        Assert.Empty(mismatch.Output.Effects);
    }

    [Fact]
    public async Task Attack_proficiency_honors_full_category_and_only_qualifying_martial_properties()
    {
        var category = await RunAttackAsync("rapier", "dnd2024.equipment.weapon.rapier.attack", "dex",
            Martial);
        var qualifying = await RunAttackAsync("rapier", "dnd2024.equipment.weapon.rapier.attack", "dex",
            Finesse);
        var nonqualifying = await RunAttackAsync("rapier", "dnd2024.equipment.weapon.rapier.attack", "dex",
            Light);
        var simplePropertyOnly = await RunAttackAsync("dagger",
            "dnd2024.equipment.weapon.dagger.attack.melee", "dex", Finesse);
        var redundant = await RunAttackAsync("rapier", "dnd2024.equipment.weapon.rapier.attack", "dex",
            Martial, Finesse);

        AssertAttackProficiency(category, true, Martial);
        AssertAttackProficiency(qualifying, true, Finesse);
        AssertAttackProficiency(nonqualifying, false, null);
        AssertAttackProficiency(simplePropertyOnly, false, null);
        Assert.False(redundant.Ok);
        Assert.Empty(redundant.Output.Effects);
    }

    [Fact]
    public async Task Attack_and_damage_reject_nonmember_or_noncanonical_activity_ability_choices()
    {
        var attackMismatch = await RunAttackAsync("dagger", "dnd2024.equipment.weapon.club.attack",
            "str", Simple);
        var damageAbility = await RunDamageAsync("club", "dnd2024.equipment.weapon.club.attack",
            "dex", critical: false);

        Assert.False(attackMismatch.Ok);
        Assert.Empty(attackMismatch.Output.Effects);
        Assert.False(damageAbility.Ok);
        Assert.Empty(damageAbility.Output.Effects);
    }

    [Fact]
    public async Task Damage_application_binds_and_reports_the_selected_activity_identity()
    {
        var valid = await RunApplyAsync("activity.club", "activity.club");
        Assert.True(valid.Ok, valid.Error);
        using (var data = JsonDocument.Parse(valid.Output.Data))
        {
            Assert.Equal("activity.club", data.RootElement.GetProperty("activityId").GetString());
            Assert.Equal("weapon.club", data.RootElement.GetProperty("weaponId").GetString());
            Assert.Equal("target", data.RootElement.GetProperty("targetId").GetString());
        }

        var mismatch = await RunApplyAsync("activity.club", "activity.other");
        Assert.False(mismatch.Ok);
        Assert.Empty(mismatch.Output.Effects);
    }

    private static async Task<MechanicRunResult> RunDamageAsync(
        string weaponSlug, string activityId, string ability, bool critical)
    {
        var subject = Subject(Proficiencies());
        return await RunAsync("weapon-damage.roll", new MechanicProjection
        {
            Input = JsonSerializer.Serialize(new { ability, critical }),
            Seed = 4242,
            Roles = new(StringComparer.Ordinal)
            {
                ["subject"] = subject,
                ["weapon"] = Weapon(weaponSlug),
                ["activity"] = Activity(activityId)
            }
        });
    }

    private static async Task<MechanicRunResult> RunAttackAsync(
        string weaponSlug, string activityId, string ability, params string[] proficiencyIds)
    {
        var subject = Subject(Proficiencies(proficiencyIds));
        var target = new EntityProjection("target", "Target",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.creature.defenses"] = "{\"baseArmorClass\":15}"
            });
        return await RunAsync("weapon-attack", new MechanicProjection
        {
            Input = JsonSerializer.Serialize(new { ability }),
            Seed = 4242,
            Roles = new(StringComparer.Ordinal)
            {
                ["subject"] = subject,
                ["weapon"] = Weapon(weaponSlug),
                ["activity"] = Activity(activityId),
                ["target"] = target
            },
            Children = new(StringComparer.Ordinal)
            {
                ["level"] =
                [
                    Child("mechanic.dnd2024.character-level.read", "subject", subject.Id,
                        "{\"test\":\"character-level-read\",\"subjectId\":\"hero\",\"present\":true,\"valid\":true,\"problem\":null,\"membershipCount\":1,\"totalLevel\":5,\"proficiencyBonus\":3}")
                ],
                ["armorClass"] =
                [
                    Child("mechanic.dnd2024.armor-class.read", "subject", target.Id,
                        "{\"test\":\"armor-class-read\",\"subjectId\":\"target\",\"sourceEntityId\":\"target\",\"armorClass\":15,\"calculationMechanicId\":\"mechanic.dnd2024.armor-class.read\"}")
                ]
            }
        });
    }

    private static async Task<MechanicRunResult> RunApplyAsync(string roleActivityId, string childActivityId)
    {
        var subject = Subject(Proficiencies());
        var weapon = new EntityProjection("weapon.club", "Club", new Dictionary<string, string>());
        var activity = new EntityProjection(roleActivityId, "Club attack", new Dictionary<string, string>());
        var target = new EntityProjection("target", "Target",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.creature.hit-points"] = "{\"current\":10,\"maximum\":10}",
                ["dnd2024.creature.temporary-hit-points"] =
                    "{\"amount\":3,\"sourceRef\":{\"entityId\":\"source.dnd2024.srd-5.2.1\"}}"
            });
        return await RunAsync("weapon-damage.apply", new MechanicProjection
        {
            Input = "{\"ability\":\"str\",\"critical\":false}",
            Roles = new(StringComparer.Ordinal)
            {
                ["subject"] = subject, ["weapon"] = weapon, ["activity"] = activity, ["target"] = target
            },
            Children = new(StringComparer.Ordinal)
            {
                ["damage"] =
                [
                    Child("mechanic.dnd2024.weapon-damage.roll",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["subject"] = subject.Id, ["weapon"] = weapon.Id,
                            ["activity"] = childActivityId
                        },
                        "{\"test\":\"weapon-damage\",\"subjectId\":\"hero\",\"weaponId\":\"weapon.club\",\"activityId\":\"" + childActivityId + "\",\"ability\":\"str\",\"critical\":false,\"type\":\"bludgeoning\",\"damage\":8}")
                ],
                ["mitigation"] =
                [
                    Child("mechanic.dnd2024.damage.resolve", "defender", target.Id,
                        "{\"test\":\"damage-mitigation-profile\",\"defenderId\":\"target\",\"mitigationKnown\":true,\"conditionsKnown\":true,\"immunities\":[],\"resistances\":[\"bludgeoning\"],\"vulnerabilities\":[],\"petrified\":false}")
                ]
            }
        });
    }

    private static void AssertAttackProficiency(MechanicRunResult result, bool expected, string? sourceId)
    {
        Assert.True(result.Ok, result.Error);
        using var data = JsonDocument.Parse(result.Output.Data);
        var root = data.RootElement;
        Assert.Equal(expected, root.GetProperty("proficient").GetBoolean());
        Assert.Equal(expected ? 3 : 0, root.GetProperty("proficiencyBonusApplied").GetInt32());
        var source = root.GetProperty("proficiencySourceRef");
        if (sourceId is null) Assert.Equal(JsonValueKind.Null, source.ValueKind);
        else Assert.Equal(sourceId, source.GetProperty("entityId").GetString());
    }

    private static ChildMechanicResult Child(string mechanicId, string role, string entityId, string data) =>
        Child(mechanicId, new Dictionary<string, string>(StringComparer.Ordinal) { [role] = entityId }, data);

    private static ChildMechanicResult Child(
        string mechanicId, Dictionary<string, string> roleEntityIds, string data) =>
        new(mechanicId, 1, 0, roleEntityIds,
            new MechanicOutput { Data = data, HasData = true }, [], 0);

    private static EntityProjection Subject(string proficiencies) => new(
        "hero", "Hero", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dnd2024.creature.ability-scores"] =
                "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":16,\"dnd2024.vocabulary.ability.dexterity\":18,\"dnd2024.vocabulary.ability.constitution\":10,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}",
            ["dnd2024.creature.proficiencies"] = proficiencies
        });

    private static string Proficiencies(params string[] ids)
    {
        var entries = string.Join(",", ids.Select(id => JsonSerializer.Serialize(id) +
            ":{\"rankRef\":{\"entityId\":\"" + Proficiency +
            "\"},\"sourceRefs\":[{\"entityId\":\"source.dnd2024.srd-5.2.1\"}]}"));
        return "{\"entries\":{" + entries + "},\"recordedFamilies\":[\"weapon\"]}";
    }

    private static EntityProjection Weapon(string slug) => Definition(
        Path.Combine(WeaponRoot(), "equipment.weapon." + slug + ".json"));

    private static EntityProjection Activity(string id) => Definition(
        Path.Combine(ActivityRoot(), id + ".json"));

    private static EntityProjection Definition(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return new EntityProjection(
            root.GetProperty("id").GetString()!, root.GetProperty("name").GetString()!,
            root.GetProperty("components").EnumerateObject().ToDictionary(
                value => value.Name, value => value.Value.GetRawText(), StringComparer.Ordinal));
    }

    private static async Task<MechanicRunResult> RunAsync(string name, MechanicProjection projection)
    {
        var source = await File.ReadAllTextAsync(MechanicPath(name) + ".js");
        return await new JintMechanicEngine().RunAsync(source, projection, ExecutionLimits.Default);
    }

    private static void AssertCurrentSchema(string componentId, string valueJson)
    {
        var schemaPath = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
            "components", componentId + ".schema.json");
        Assert.True(File.Exists(schemaPath), "Missing schema for " + componentId);
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(File.ReadAllText(schemaPath));
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, valueJson).Status);
    }

    private static void AssertExactIntegerFeet(JsonElement measure)
    {
        Assert.Equal("distance", measure.GetProperty("dimension").GetString());
        Assert.Equal("dnd2024.vocabulary.distance-unit.meter",
            measure.GetProperty("unit").GetProperty("entityId").GetString());
        var value = measure.GetProperty("value");
        var numerator = value.GetProperty("numerator").GetInt64();
        var denominator = value.GetProperty("denominator").GetInt64();
        Assert.Equal(0, numerator * 1250 % (denominator * 381));
        Assert.True(numerator * 1250 / (denominator * 381) > 0);
    }

    private static void AssertRange(
        string id, int normalNumerator, int normalDenominator,
        int? longNumerator = null, int? longDenominator = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ActivityRoot(), id + ".json")));
        var range = document.RootElement.GetProperty("components")
            .GetProperty("dnd2024.activity.range").GetProperty("range");
        AssertRational(range.GetProperty("normal"), normalNumerator, normalDenominator);
        if (longNumerator is not null && longDenominator is not null)
            AssertRational(range.GetProperty("long"), longNumerator.Value, longDenominator.Value);
        else
            Assert.False(range.TryGetProperty("long", out _));
    }

    private static void AssertRational(JsonElement measure, int numerator, int denominator)
    {
        var value = measure.GetProperty("value");
        Assert.Equal(numerator, value.GetProperty("numerator").GetInt32());
        Assert.Equal(denominator, value.GetProperty("denominator").GetInt32());
        Assert.Equal("dnd2024.vocabulary.distance-unit.meter",
            measure.GetProperty("unit").GetProperty("entityId").GetString());
    }

    private static string RequirementsJson(string contract)
    {
        var start = contract.IndexOf("```json", StringComparison.Ordinal);
        var end = contract.IndexOf("```", start + 7, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return contract[(start + 7)..end].Trim();
    }

    private static string WeaponRoot() => Path.Combine(RepositoryRoot(), "catalog", "applications",
        "dnd2024", "content", "entities", "equipment", "weapon");

    private static string ActivityRoot() => Path.Combine(RepositoryRoot(), "catalog", "applications",
        "dnd2024", "content", "entities", "equipment", "weapon-activities");

    private static string MechanicPath(string name) => Path.Combine(RepositoryRoot(), "catalog",
        "applications", "dnd2024", "mechanics", "combat", "mechanic.dnd2024." + name);

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
    {
        var sourceDirectory = Directory.GetParent(sourcePath)?.Parent;
        if (sourceDirectory is not null &&
            File.Exists(Path.Combine(sourceDirectory.FullName, "DantesRoleplay.slnx")))
            return sourceDirectory.FullName;
        var workingDirectory = new DirectoryInfo(Environment.CurrentDirectory);
        if (File.Exists(Path.Combine(workingDirectory.FullName, "DantesRoleplay.slnx")))
            return workingDirectory.FullName;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
