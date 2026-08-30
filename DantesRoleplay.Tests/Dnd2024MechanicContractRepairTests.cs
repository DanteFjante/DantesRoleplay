using System.Text.Json;
using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024MechanicContractRepairTests
{
    [Fact]
    public async Task Weapon_damage_application_uses_current_hp_and_temporary_hp_only()
    {
        var subject = new EntityProjection(
            "character.hero",
            "Hero",
            new Dictionary<string, string>
            {
                ["dnd2024.creature.ability-scores"] =
                    "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":16}}"
            });
        var weapon = new EntityProjection("item.club", "Club", new Dictionary<string, string>());
        var target = new EntityProjection(
            "creature.target",
            "Target",
            new Dictionary<string, string>
            {
                ["dnd2024.creature.hit-points"] = "{\"current\":10,\"maximum\":10}",
                ["dnd2024.creature.temporary-hit-points"] =
                    "{\"amount\":3,\"sourceRef\":{\"entityId\":\"dnd2024.source.srd-5.2.1\"}}"
            });
        var damageOutput = new MechanicOutput
        {
            Data =
                "{\"test\":\"weapon-damage\",\"subjectId\":\"character.hero\",\"weaponId\":\"item.club\",\"ability\":\"str\",\"critical\":false,\"type\":\"bludgeoning\",\"damage\":8}",
            HasData = true
        };
        var mitigationOutput = new MechanicOutput
        {
            Data =
                "{\"test\":\"damage-mitigation-profile\",\"defenderId\":\"creature.target\",\"mitigationKnown\":true,\"conditionsKnown\":true,\"immunities\":[],\"resistances\":[\"bludgeoning\"],\"vulnerabilities\":[],\"petrified\":false}",
            HasData = true
        };
        var result = await RunAsync("combat/dnd2024.mechanic.weapon-damage.apply", new MechanicProjection
        {
            Input = "{\"ability\":\"str\",\"critical\":false}",
            Roles = new() { ["subject"] = subject, ["weapon"] = weapon, ["target"] = target },
            Children = new()
            {
                ["damage"] =
                [
                    new ChildMechanicResult(
                        "dnd2024.mechanic.weapon-damage.roll", 1, 0,
                        new Dictionary<string, string>
                        {
                            ["subject"] = subject.Id,
                            ["weapon"] = weapon.Id
                        },
                        damageOutput, [], 0)
                ],
                ["mitigation"] =
                [
                    new ChildMechanicResult(
                        "dnd2024.mechanic.damage.resolve", 1, 0,
                        new Dictionary<string, string> { ["defender"] = target.Id },
                        mitigationOutput, [], 0)
                ]
            }
        });

        Assert.True(result.Ok, result.Error);
        Assert.Contains(result.Output.Effects,
            effect => effect.Type == "component.remove" &&
                      effect.DefinitionId == "dnd2024.creature.temporary-hit-points");
        Assert.Contains(result.Output.Effects,
            effect => effect.Type == "component.set" &&
                      effect.DefinitionId == "dnd2024.creature.hit-points" &&
                      effect.Data == "{\"current\":9,\"maximum\":10}");
        using var data = JsonDocument.Parse(result.Output.Data);
        Assert.Equal(4, data.RootElement.GetProperty("damage").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("restInterruption").ValueKind);
    }

    [Fact]
    public async Task Currency_value_is_derived_from_canonical_coin_ids_without_stored_ratios()
    {
        static ContainedProjection Coins(string id, string definitionId, int count) => new(
            id,
            id,
            "currency",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.core.definition-link"] =
                    "{\"definition\":{\"entityId\":\"" + definitionId + "\"}}",
                ["dnd2024.item.quantity"] = "{\"current\":" + count + "}"
            });
        var result = await RunAsync("data/dnd2024.mechanic.currency-value.read", new MechanicProjection
        {
            Input = "{}",
            Roles = new()
            {
                ["root"] = new EntityProjection(
                    "inventory.hero",
                    "Hero inventory",
                    new Dictionary<string, string>(),
                    Contains:
                    [
                        Coins("coins.silver", "dnd2024.equipment.currency.silver-piece", 7),
                        Coins("coins.gold", "dnd2024.equipment.currency.gold-piece", 3),
                        Coins("item.club", "dnd2024.equipment.weapon.club", 1)
                    ])
            }
        });

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Output.Effects);
        using var data = JsonDocument.Parse(result.Output.Data);
        Assert.Equal(10, data.RootElement.GetProperty("coinCount").GetInt32());
        Assert.Equal(370, data.RootElement.GetProperty("copperValue").GetInt32());
        Assert.Equal(new[] { "sp", "gp" },
            data.RootElement.GetProperty("denominations").EnumerateArray()
                .Select(row => row.GetProperty("code").GetString()).ToArray());
    }

    [Fact]
    public async Task Initiative_uses_canonical_alert_entitlement_and_keeps_rest_separate()
    {
        var subject = new EntityProjection(
            "character.hero",
            "Hero",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.creature.ability-scores"] =
                    "{\"scores\":{\"dnd2024.vocabulary.ability.dexterity\":16}}",
                ["dnd2024.character.feature-entitlements"] =
                    "{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.feat.alert\"},\"sourceRef\":{\"entityId\":\"dnd2024.background.guard\"}}]}"
            });
        var levelOutput = new MechanicOutput
        {
            Data =
                "{\"test\":\"character-level-read\",\"subjectId\":\"character.hero\",\"present\":true,\"valid\":true,\"problem\":null,\"membershipCount\":1,\"totalLevel\":5,\"proficiencyBonus\":3}",
            HasData = true
        };
        var result = await RunAsync("checks/dnd2024.mechanic.initiative.roll", new MechanicProjection
        {
            Input = "{\"useAlertInitiativeProficiency\":true}",
            Seed = 42,
            Roles = new() { ["subject"] = subject },
            Children = new()
            {
                ["level"] =
                [
                    new ChildMechanicResult(
                        "dnd2024.mechanic.character-level.read",
                        1,
                        0,
                        new Dictionary<string, string> { ["subject"] = subject.Id },
                        levelOutput,
                        [],
                        0)
                ]
            }
        });

        Assert.True(result.Ok, result.Error);
        using var data = JsonDocument.Parse(result.Output.Data);
        Assert.True(data.RootElement.GetProperty("alertInitiativeProficiency").GetProperty("used").GetBoolean());
        Assert.Equal(3,
            data.RootElement.GetProperty("alertInitiativeProficiency").GetProperty("bonus").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("restInterruption").ValueKind);
    }

    [Fact]
    public async Task Species_selection_preserves_canonical_size_movement_and_grants()
    {
        var species = new EntityProjection(
            "dnd2024.species.human",
            "Human",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.core.version"] = "{\"revision\":1,\"status\":\"active\"}",
                ["dnd2024.advancement.species"] =
                    "{\"grantRefs\":[{\"entityId\":\"dnd2024.feature.human.resourceful\"}]}",
                ["dnd2024.creature.classification"] =
                    "{\"creatureTypeRef\":{\"entityId\":\"dnd2024.vocabulary.creature-type.humanoid\"},\"descriptiveTagRefs\":[]}",
                ["dnd2024.creature.body-basis"] =
                    "{\"allowedSizeRefs\":[{\"entityId\":\"dnd2024.vocabulary.size.small\"},{\"entityId\":\"dnd2024.vocabulary.size.medium\"}]}",
                ["dnd2024.creature.movement-basis"] =
                    "{\"speeds\":{\"dnd2024.vocabulary.movement-mode.walk\":{\"distance\":{\"dimension\":\"distance\",\"value\":{\"numerator\":1143,\"denominator\":125},\"unit\":{\"entityId\":\"dnd2024.vocabulary.distance-unit.meter\"}}}}}"
            });
        var result = await RunAsync("data/dnd2024.mechanic.species-selection.resolve", new MechanicProjection
        {
            Input = "{\"sizeRef\":\"dnd2024.vocabulary.size.medium\"}",
            Roles = new() { ["species"] = species }
        });

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Output.Effects);
        using var data = JsonDocument.Parse(result.Output.Data);
        Assert.Equal("dnd2024.vocabulary.size.medium",
            data.RootElement.GetProperty("selectedSizeRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.feature.human.resourceful",
            data.RootElement.GetProperty("grantRefs")[0].GetProperty("entityId").GetString());
        Assert.Equal(1143,
            data.RootElement.GetProperty("movementBasis").GetProperty("speeds")
                .GetProperty("dnd2024.vocabulary.movement-mode.walk").GetProperty("distance")
                .GetProperty("value").GetProperty("numerator").GetInt32());
    }

    [Fact]
    public async Task Item_create_and_quantity_operations_use_complete_canonical_instances()
    {
        var definition = ActiveDefinition("dnd2024.equipment.weapon.club");
        var destination = new EntityProjection("inventory.hero", "Hero inventory", new Dictionary<string, string>());
        var created = await RunAsync("data/dnd2024.mechanic.item-instance.create-and-place", new MechanicProjection
        {
            Input = "{\"itemId\":\"item.club\",\"name\":\"Club\",\"slot\":\"carried\"}",
            Roles = new() { ["definition"] = definition, ["destination"] = destination }
        });
        Assert.True(created.Ok, created.Error);
        Assert.Equal(4, created.Output.Effects.Count);
        var quantityEffect = Assert.Single(created.Output.Effects,
            effect => effect.DefinitionId == "dnd2024.item.quantity");
        Assert.Equal("component.add", quantityEffect.Type);
        Assert.Equal("{\"current\":1}", quantityEffect.Data);

        var source = ItemRole("item.club-stack", "Clubs", definition.Id, 5, destination.Id, "carried");
        var split = await RunAsync("data/dnd2024.mechanic.item-stack.split", new MechanicProjection
        {
            Input = "{\"count\":2,\"itemId\":\"item.club-split\",\"name\":\"Clubs\"}",
            Roles = new() { ["source"] = source, ["definition"] = definition }
        });
        Assert.True(split.Ok, split.Error);
        Assert.Contains(split.Output.Effects,
            effect => effect.Type == "component.set" && effect.Data == "{\"current\":3}");
        Assert.Contains(split.Output.Effects,
            effect => effect.Type == "component.add" &&
                      effect.DefinitionId == "dnd2024.item.quantity" &&
                      effect.Data == "{\"current\":2}");

        var consume = await RunAsync("data/dnd2024.mechanic.item-stack.consume", new MechanicProjection
        {
            Input = "{\"count\":5}",
            Roles = new() { ["item"] = source, ["definition"] = definition }
        });
        Assert.True(consume.Ok, consume.Error);
        Assert.Single(consume.Output.Effects);
        Assert.Equal("entity.delete", consume.Output.Effects[0].Type);
    }

    [Fact]
    public async Task Equipment_and_inventory_use_canonical_slots_and_definition_facets()
    {
        var holder = new EntityProjection("creature.hero", "Hero", new Dictionary<string, string>());
        var item = ItemRole("item.club", "Club", "dnd2024.equipment.weapon.club", 1, holder.Id, "carried");
        var references = new Dictionary<string, ReferencedEntityProjection>(StringComparer.Ordinal)
        {
            ["dnd2024.equipment.weapon.club"] = new(
                "dnd2024.equipment.weapon.club",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dnd2024.item.equippable"] =
                        "{\"equipmentSlots\":[{\"entityId\":\"dnd2024.equipment-slot.main-hand\"}]}",
                    ["dnd2024.item.weapon"] =
                        "{\"category\":{\"entityId\":\"dnd2024.equipment.weapon-category.simple\"},\"properties\":[]}",
                    ["dnd2024.item.physical"] = "{}"
                })
        };
        var equipped = await RunAsync("data/dnd2024.mechanic.item.equip", new MechanicProjection
        {
            Input = "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}",
            Roles = new() { ["item"] = item, ["holder"] = holder },
            References = references
        });
        Assert.True(equipped.Ok, equipped.Error);
        var equipmentEffect = Assert.Single(equipped.Output.Effects);
        Assert.Equal("dnd2024.item.equipment", equipmentEffect.DefinitionId);
        using (var state = JsonDocument.Parse(equipmentEffect.Data))
        {
            Assert.Equal(holder.Id, state.RootElement.GetProperty("equippedBy").GetProperty("entityId").GetString());
        }

        const string equipment =
            "{\"equippedBy\":{\"entityId\":\"creature.hero\"},\"slots\":[{\"entityId\":\"dnd2024.equipment-slot.main-hand\"}]}";
        var contained = new ContainedProjection(
            item.Id,
            item.Name,
            "carried",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.core.definition-link"] =
                    "{\"definition\":{\"entityId\":\"dnd2024.equipment.weapon.club\"}}",
                ["dnd2024.item.quantity"] = "{\"current\":1}",
                ["dnd2024.item.equipment"] = equipment
            });
        var inventory = await RunAsync("data/dnd2024.mechanic.inventory.read", new MechanicProjection
        {
            Input = "{}",
            Roles = new()
            {
                ["root"] = new EntityProjection(holder.Id, holder.Name, holder.Components, Contains: [contained])
            },
            References = references
        });
        Assert.True(inventory.Ok, inventory.Error);
        using var inventoryData = JsonDocument.Parse(inventory.Output.Data);
        var view = inventoryData.RootElement.GetProperty("items")[0];
        Assert.Equal(1, view.GetProperty("quantity").GetInt32());
        Assert.Contains("dnd2024.item.weapon",
            view.GetProperty("definitionComponentIds").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(holder.Id,
            view.GetProperty("equipment").GetProperty("equippedBy").GetProperty("entityId").GetString());

        var equippedRole = item with
        {
            Components = new Dictionary<string, string>(item.Components, StringComparer.Ordinal)
            {
                ["dnd2024.item.equipment"] = equipment
            }
        };
        var unequipped = await RunAsync("data/dnd2024.mechanic.item.unequip", new MechanicProjection
        {
            Input = "{}",
            Roles = new() { ["item"] = equippedRole, ["holder"] = holder }
        });
        Assert.True(unequipped.Ok, unequipped.Error);
        Assert.Equal("component.remove", Assert.Single(unequipped.Output.Effects).Type);
    }

    [Fact]
    public async Task Class_progression_resolves_canonical_level_grants()
    {
        var classRole = new EntityProjection(
            "dnd2024.class.fighter",
            "Fighter",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.advancement.class"] =
                    "{\"primaryAbilityRefs\":[{\"entityId\":\"dnd2024.vocabulary.ability.strength\"}],\"hitDieRef\":{\"entityId\":\"dnd2024.vocabulary.die.d10\"},\"progressionRef\":{\"entityId\":\"dnd2024.class-progression.fighter\"}}"
            });
        var references = new Dictionary<string, ReferencedEntityProjection>(StringComparer.Ordinal)
        {
            ["dnd2024.class-progression.fighter"] = new(
                "dnd2024.class-progression.fighter",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dnd2024.advancement.progression"] =
                        "{\"levels\":{\"1\":{\"grantRefs\":[{\"entityId\":\"dnd2024.feature.fighter.second-wind\"}]},\"2\":{}}}"
                })
        };

        var supported = await RunAsync("proficiency/dnd2024.mechanic.class-progression.read", new MechanicProjection
        {
            Input = "{\"classLevel\":1}",
            Roles = new() { ["class"] = classRole },
            References = references
        });
        Assert.True(supported.Ok, supported.Error);
        Assert.Empty(supported.Output.Effects);
        using (var result = JsonDocument.Parse(supported.Output.Data))
        {
            Assert.Equal("supported", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("dnd2024.class-progression.fighter",
                result.RootElement.GetProperty("progressionId").GetString());
            Assert.Equal("dnd2024.vocabulary.die.d10",
                result.RootElement.GetProperty("hitDieRef").GetProperty("entityId").GetString());
            Assert.Equal("dnd2024.feature.fighter.second-wind",
                result.RootElement.GetProperty("grantRefs")[0].GetProperty("entityId").GetString());
        }

        var unsupported = await RunAsync("proficiency/dnd2024.mechanic.class-progression.read", new MechanicProjection
        {
            Input = "{\"classLevel\":3}",
            Roles = new() { ["class"] = classRole },
            References = references
        });
        Assert.True(unsupported.Ok, unsupported.Error);
        using var unsupportedResult = JsonDocument.Parse(unsupported.Output.Data);
        Assert.Equal("unsupported-level", unsupportedResult.RootElement.GetProperty("status").GetString());
        Assert.Empty(unsupportedResult.RootElement.GetProperty("grantRefs").EnumerateArray());
    }

    [Fact]
    public async Task Class_progression_fails_closed_when_declared_reference_is_unavailable()
    {
        var result = await RunAsync("proficiency/dnd2024.mechanic.class-progression.read", new MechanicProjection
        {
            Input = "{\"classLevel\":1}",
            Roles = new()
            {
                ["class"] = new EntityProjection(
                    "dnd2024.class.fighter",
                    "Fighter",
                    new Dictionary<string, string>
                    {
                        ["dnd2024.advancement.class"] =
                            "{\"primaryAbilityRefs\":[{\"entityId\":\"dnd2024.vocabulary.ability.strength\"}],\"hitDieRef\":{\"entityId\":\"dnd2024.vocabulary.die.d10\"},\"progressionRef\":{\"entityId\":\"dnd2024.class-progression.fighter\"}}"
                    })
            }
        });

        Assert.False(result.Ok);
        Assert.Empty(result.Output.Effects);
    }

    [Fact]
    public async Task Burden_and_capacity_use_canonical_definition_weight_and_exact_kilograms()
    {
        var item = new ContainedProjection(
            "item.chain-mail",
            "Chain Mail stack",
            "carried",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.core.definition-link"] =
                    "{\"definition\":{\"entityId\":\"dnd2024.equipment.armor.chain-mail\"}}",
                ["dnd2024.item.quantity"] = "{\"current\":3}"
            });
        var creature = new EntityProjection(
            "creature.hero",
            "Hero",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.creature.ability-scores"] =
                    "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":10}}",
                ["dnd2024.creature.body"] =
                    "{\"sizeRef\":{\"entityId\":\"dnd2024.vocabulary.size.medium\"}}"
            },
            Contains: [item]);
        var references = new Dictionary<string, ReferencedEntityProjection>(StringComparer.Ordinal)
        {
            ["dnd2024.equipment.armor.chain-mail"] = new(
                "dnd2024.equipment.armor.chain-mail",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dnd2024.item.physical"] =
                        "{\"weight\":{\"dimension\":\"mass\",\"value\":{\"numerator\":5,\"denominator\":2},\"unit\":{\"entityId\":\"dnd2024.vocabulary.mass-unit.kilogram\"}}}"
                })
        };

        var burden = await RunAsync("data/dnd2024.mechanic.item-burden.read", new MechanicProjection
        {
            Input = "{}",
            Roles = new() { ["root"] = creature },
            References = references
        });
        Assert.True(burden.Ok, burden.Error);
        Assert.Empty(burden.Output.Effects);
        using (var result = JsonDocument.Parse(burden.Output.Data))
        {
            Assert.Equal("item-burden-read", result.RootElement.GetProperty("test").GetString());
            var mass = result.RootElement.GetProperty("mass");
            Assert.Equal("mass", mass.GetProperty("dimension").GetString());
            Assert.Equal(15, mass.GetProperty("value").GetProperty("numerator").GetInt64());
            Assert.Equal(2, mass.GetProperty("value").GetProperty("denominator").GetInt64());
            Assert.Equal("dnd2024.vocabulary.mass-unit.kilogram",
                mass.GetProperty("unit").GetProperty("entityId").GetString());
        }

        var capacity = await RunAsync("data/dnd2024.mechanic.carrying-capacity.read", new MechanicProjection
        {
            Input = "{}",
            Roles = new() { ["creature"] = creature },
            Children = new()
            {
                ["burden"] =
                [
                    new ChildMechanicResult(
                        "dnd2024.mechanic.item-burden.read",
                        1,
                        0,
                        new Dictionary<string, string> { ["root"] = creature.Id },
                        burden.Output,
                        [],
                        0)
                ]
            }
        });
        Assert.True(capacity.Ok, capacity.Error);
        Assert.Empty(capacity.Output.Effects);
        using var capacityResult = JsonDocument.Parse(capacity.Output.Data);
        var carrying = capacityResult.RootElement.GetProperty("carryingCapacity").GetProperty("value");
        Assert.Equal(136077711, carrying.GetProperty("numerator").GetInt64());
        Assert.Equal(2000000, carrying.GetProperty("denominator").GetInt64());
        Assert.True(capacityResult.RootElement.GetProperty("withinCarryingCapacity").GetBoolean());
    }

    [Theory]
    [InlineData("{\"current\":0}", "dnd2024.vocabulary.mass-unit.kilogram")]
    [InlineData("{\"current\":1}", "dnd2024.vocabulary.mass-unit.pound")]
    public async Task Burden_fails_closed_for_nonphysical_quantity_or_noncanonical_weight(
        string quantity,
        string unit)
    {
        var item = new ContainedProjection(
            "item.invalid",
            "Invalid item",
            "carried",
            new Dictionary<string, string>
            {
                ["dnd2024.core.definition-link"] =
                    "{\"definition\":{\"entityId\":\"definition.invalid\"}}",
                ["dnd2024.item.quantity"] = quantity
            });
        var result = await RunAsync("data/dnd2024.mechanic.item-burden.read", new MechanicProjection
        {
            Input = "{}",
            Roles = new()
            {
                ["root"] = new EntityProjection("root", "Root", new Dictionary<string, string>(), Contains: [item])
            },
            References = new()
            {
                ["definition.invalid"] = new ReferencedEntityProjection(
                    "definition.invalid",
                    new Dictionary<string, string>
                    {
                        ["dnd2024.item.physical"] =
                            "{\"weight\":{\"dimension\":\"mass\",\"value\":{\"numerator\":1,\"denominator\":1},\"unit\":{\"entityId\":\"" + unit + "\"}}}"
                    })
            }
        });
        Assert.False(result.Ok);
        Assert.Empty(result.Output.Effects);
    }

    private static async Task<MechanicRunResult> RunAsync(string relative, MechanicProjection projection)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "catalog", "applications", "dnd2024", "mechanics",
            relative.Replace('/', Path.DirectorySeparatorChar) + ".js"));
        return await new JintMechanicEngine().RunAsync(source, projection, ExecutionLimits.Default);
    }

    private static EntityProjection ActiveDefinition(string id) => new(
        id,
        id,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dnd2024.core.version"] = "{\"revision\":1,\"status\":\"active\"}"
        });

    private static EntityProjection ItemRole(
        string id,
        string name,
        string definitionId,
        int quantity,
        string? containerId = null,
        string slot = "") => new(
            id,
            name,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dnd2024.core.definition-link"] =
                    "{\"definition\":{\"entityId\":\"" + definitionId + "\"}}",
                ["dnd2024.item.quantity"] = "{\"current\":" + quantity + "}"
            },
            containerId,
            slot);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
