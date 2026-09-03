using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024ApplicationReadViewTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("dnd2024");
    private static readonly JintMechanicEngine Engine = new();
    private static readonly BoundedJsonSchemaValidator Schemas = new();

    public static TheoryData<string, string> ReadViews => new()
    {
        { "campaign/dnd2024.query.campaign-resume.json", "campaign/dnd2024.mechanic.campaign.resume.project" },
        { "campaign/dnd2024.query.current-scene.json", "campaign/dnd2024.mechanic.campaign.current-scene.project" },
        { "campaign/dnd2024.query.actor-context.json", "campaign/dnd2024.mechanic.campaign.actor-context.project" },
        { "play/dnd2024.query.unresolved-decisions.json", "play/dnd2024.mechanic.play.unresolved-decisions.project" },
        { "campaign/dnd2024.query.recent-consequences.json", "campaign/dnd2024.mechanic.campaign.recent-consequences.project" },
        { "character/dnd2024.query.character-sheet-v2.json", "data/dnd2024.mechanic.character-sheet-v2.project" },
        { "character/dnd2024.query.character-dossier-v1.json", "data/dnd2024.mechanic.character-dossier-v1.project" }
    };

    [Theory]
    [MemberData(nameof(ReadViews))]
    public void Read_view_contract_pins_the_exact_catalog_mechanic_and_closed_schema(
        string queryPath, string mechanicPath)
    {
        var query = ApplicationQueryContract.Parse(File.ReadAllText(Query(queryPath)), Application);
        var mechanic = ReadMechanic(mechanicPath);
        var content = JsonSerializer.Serialize(new
        {
            id = mechanic.Id,
            category = mechanic.Category,
            name = mechanic.Name,
            description = mechanic.Description,
            matches = mechanic.Matches,
            requirements = mechanic.Requirements,
            source = mechanic.Source,
            scope = mechanic.Scope,
            status = mechanic.Status.ToString().ToLowerInvariant()
        });
        var compilation = Schemas.Compile(query.OutputSchemaJson);

        Assert.Equal(query.ProjectionQualifiedId, mechanic.Id);
        var mechanicHash = Hash(content);
        Assert.True(mechanicHash == query.ProjectionContentHash,
            $"Mechanic hash for {query.Id}: {mechanicHash}");
        Assert.True(compilation.IsAccepted,
            string.Join("; ", compilation.Diagnostics.Select(value => value.Code + ": " + value.Message)));
        Assert.True(compilation.SchemaHash == query.OutputSchemaHash,
            $"Schema hash for {query.Id}: {compilation.SchemaHash}");
        Assert.Equal(ApplicationQueryExposure.ModelVisible, query.Exposure);
        Assert.Equal("active", query.Status);
    }

    [Fact]
    public async Task Campaign_resume_is_bounded_party_visible_and_schema_valid()
    {
        var campaign = Entity("campaign", "Measure of Mercy", new()
        {
            ["game.core.campaign.root"] = Json(new { status = "active", title = "Measure of Mercy", premise = "Choose what justice costs.", partyGoals = new[] { "Find the missing envoy." }, toneAndBoundaries = new[] { "Hopeful intrigue." }, rulesetScope = "dnd2024", creationMethod = "manual", reviewFingerprint = new string('a', 64) }),
            ["game.core.campaign.current-scene"] = Json(new { location = Ref("market") }),
            ["game.core.campaign.scene-affordances"] = Json(new { scene = new { location = Ref("market") }, items = new object[] { new { key = "ask-guard", label = "Ask the guard", summary = "Learn what happened.", visibility = "party" }, new { key = "secret-door", label = "Secret door", summary = "GM-only route.", visibility = "gm" } } })
        }, related:
        [
            Related("arc", "Mercy", "game.core.campaign.has-arc", "game.core.campaign.arc", new { status = "active", title = "The Broken Scale", partyStake = "Keep the city from choosing vengeance.", gmContext = "Hidden culprit." }),
            Related("chapter", "Missing Envoy", "game.core.campaign.has-chapter", "game.core.campaign.chapter", new { status = "active", title = "Missing Envoy", partyQuestion = "Who benefits?", gmContext = "Secret patron." }),
            Related("session-2", "Session 2", "game.core.campaign.has-session", new Dictionary<string, string>
            {
                ["game.core.campaign.session"] = Json(new { status = "active", ordinal = 2 })
            }),
            Related("session-1", "Session 1", "game.core.campaign.has-session", new Dictionary<string, string>
            {
                ["game.core.campaign.session"] = Json(new { status = "ended", ordinal = 1 }),
                ["game.core.campaign.session-recap"] = Recap()
            }),
            Related("participation", "Ganji participation", "game.core.campaign.has-character-participation", "game.core.campaign.character-participation", new { status = "active" })
        ]);

        using var output = await Run("campaign/dnd2024.mechanic.campaign.resume.project", "campaign", campaign);
        var data = output.RootElement;

        Assert.Equal(1, data.GetProperty("party").GetProperty("activeMemberCount").GetInt32());
        Assert.Equal("ask-guard", Assert.Single(data.GetProperty("affordances").EnumerateArray()).GetProperty("key").GetString());
        Assert.DoesNotContain("Hidden culprit", data.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Secret patron", data.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("GM-only route", data.GetRawText(), StringComparison.Ordinal);
        AssertSchema("campaign/dnd2024.query.campaign-resume.json", data.GetRawText());
    }

    [Fact]
    public async Task Current_scene_rejects_gm_locations_and_filters_gm_affordances()
    {
        var campaign = Entity("campaign", "Campaign", new()
        {
            ["game.core.campaign.root"] = Root(),
            ["game.core.campaign.current-scene"] = Json(new { location = Ref("market") }),
            ["game.core.campaign.scene-affordances"] = Json(new { scene = new { location = Ref("market") }, items = new object[] { new { key = "look", label = "Look around", summary = "Survey the market.", visibility = "party" }, new { key = "trap", label = "Hidden trap", summary = "GM secret.", visibility = "gm" } } })
        });
        var references = new Dictionary<string, ReferencedEntityProjection>
        {
            ["market"] = new("market", new Dictionary<string, string>
            {
                ["game.core.world.location"] = Json(new { kind = "settlement", status = "active", summary = "A crowded market.", visibility = "party" })
            })
        };

        using var output = await Run("campaign/dnd2024.mechanic.campaign.current-scene.project",
            new MechanicProjection { Input = "{}", Roles = { ["campaign"] = campaign }, References = references });
        Assert.Equal("look", Assert.Single(output.RootElement.GetProperty("affordances").EnumerateArray()).GetProperty("key").GetString());
        Assert.DoesNotContain("GM secret", output.RootElement.GetRawText(), StringComparison.Ordinal);
        AssertSchema("campaign/dnd2024.query.current-scene.json", output.RootElement.GetRawText());

        var gmReferences = new Dictionary<string, ReferencedEntityProjection>
        {
            ["market"] = new("market", new Dictionary<string, string>
            {
                ["game.core.world.location"] = Json(new { kind = "site", status = "active", summary = "A concealed lair.", visibility = "gm" })
            })
        };
        var rejected = await RunRaw("campaign/dnd2024.mechanic.campaign.current-scene.project",
            new MechanicProjection { Input = "{}", Roles = { ["campaign"] = campaign }, References = gmReferences });
        Assert.False(rejected.Ok);
        Assert.Contains("not party-visible", rejected.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actor_context_requires_exact_participation_and_excludes_private_identity_fields()
    {
        var participation = Related("participation", "Participation", "game.core.campaign.has-character-participation",
            "game.core.campaign.character-participation", new { status = "active" });
        var campaign = Entity("campaign", "Campaign", new()
        {
            ["game.core.campaign.root"] = Root(),
            ["game.core.campaign.current-scene"] = Json(new { location = Ref("market") })
        }, related: [participation]);
        var actor = Entity("ganji", "Ganji", new()
        {
            ["dnd2024.character.identity"] = Json(new { pronouns = "they/them", appearance = "Travel-stained robes.", biography = "Private history.", playerNotes = "Private note." }),
            ["dnd2024.creature.hit-points"] = Json(new { current = 7, maximum = 10 }),
            ["dnd2024.creature.temporary-hit-points"] = Json(new { amount = 3, sourceRef = Ref("spell") }),
            ["dnd2024.conditions"] = Json(new { entries = new object[] { new { condition = "prone" } }, sourceRef = new { sourceId = "dnd2024.source.srd-5.2.1", locator = "Rules Glossary" } })
        }, containerId: "market", containerSlot: "party", relationships:
        [new("participation", "ganji", "game.core.campaign.character-participation.for-actor", "{}")]);
        var projection = new MechanicProjection { Input = "{}", Roles = { ["campaign"] = campaign, ["actor"] = actor } };

        using var output = await Run("campaign/dnd2024.mechanic.campaign.actor-context.project", projection);
        var raw = output.RootElement.GetRawText();
        Assert.True(output.RootElement.GetProperty("presence").GetProperty("presentInCurrentScene").GetBoolean());
        Assert.Equal("party", output.RootElement.GetProperty("presence").GetProperty("slot").GetString());
        Assert.DoesNotContain("Private history", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Private note", raw, StringComparison.Ordinal);
        AssertSchema("campaign/dnd2024.query.actor-context.json", raw);
    }

    [Fact]
    public async Task Unresolved_decisions_returns_only_live_authoritative_decisions()
    {
        var scene = Entity("scene", "Market scene", new()
        {
            ["dnd2024.play.scene-state"] = Json(new { scene = Ref("scene"), status = "active", pillar = Ref("exploration"), currentDecisionPoint = Ref("scene") }),
            ["dnd2024.play.decision-point"] = Json(new { status = "declared", eligibleParticipants = new[] { Ref("participant") }, declaredIntents = new[] { new { participant = Ref("participant"), actor = Ref("ganji"), text = "Question the guard." } } })
        });
        using var output = await Run("play/dnd2024.mechanic.play.unresolved-decisions.project", "scene", scene);
        Assert.Equal("Question the guard.", Assert.Single(output.RootElement.GetProperty("items").EnumerateArray()).GetProperty("declaredIntents")[0].GetProperty("text").GetString());
        AssertSchema("play/dnd2024.query.unresolved-decisions.json", output.RootElement.GetRawText());

        var resolved = scene with { Components = scene.Components.ToDictionary(value => value.Key, value => value.Key == "dnd2024.play.decision-point" ? Json(new { status = "resolved", eligibleParticipants = new[] { Ref("participant") }, declaredIntents = Array.Empty<object>() }) : value.Value) };
        using var empty = await Run("play/dnd2024.mechanic.play.unresolved-decisions.project", "scene", resolved);
        Assert.Empty(empty.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Recent_consequences_are_bounded_and_do_not_expose_gm_context()
    {
        var campaign = Entity("campaign", "Campaign", new() { ["game.core.campaign.root"] = Root() }, related:
        [
            Related("session-1", "Session 1", "game.core.campaign.has-session", new Dictionary<string, string> { ["game.core.campaign.session"] = Json(new { status = "ended", ordinal = 1 }), ["game.core.campaign.session-recap"] = Recap() }),
            Related("chapter-1", "Chapter 1", "game.core.campaign.has-chapter", "game.core.campaign.chapter", new { status = "closed", title = "Arrival", partyQuestion = "Who can be trusted?", closingSummary = "The envoy vanished.", gmContext = "Secret suspect." }),
            Related("arc-1", "Arc 1", "game.core.campaign.has-arc", "game.core.campaign.arc", new { status = "resolved", title = "The Arrival", partyStake = "Protect the envoy.", closingSummary = "The party saved the delegation.", gmContext = "Secret sponsor." }),
            Related("visit-1", "Market visit", "game.core.campaign.has-location-visit", "game.core.campaign.location-visit", new { firstVisitedMinute = 10, lastVisitedMinute = 20, visitCount = 2, status = "departed", summary = "The party searched the market.", memory = "A guard offered help.", gmContext = "The guard lied." })
        ]);

        using var output = await Run("campaign/dnd2024.mechanic.campaign.recent-consequences.project", "campaign", campaign);
        var raw = output.RootElement.GetRawText();
        Assert.Single(output.RootElement.GetProperty("endedSessions").EnumerateArray());
        Assert.DoesNotContain("Secret suspect", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret sponsor", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("The guard lied", raw, StringComparison.Ordinal);
        AssertSchema("campaign/dnd2024.query.recent-consequences.json", raw);
    }

    [Fact]
    public async Task Origin_materializer_is_idempotent_and_refuses_conflicting_evidence()
    {
        const string record = """
            {"status":"basic-playable","selections":{"speciesDefinitionId":"species.half-elf","backgroundDefinitionId":"background.acolyte"}}
            """;
        var subject = Entity("actor.ganji", "Ganji", new()
        {
            ["dnd2024.character-creation-record"] = record
        });
        var references = new Dictionary<string, ReferencedEntityProjection>
        {
            ["species.half-elf"] = Definition("species.half-elf", "Half-Elf", "species", "half-elf"),
            ["background.acolyte"] = Definition("background.acolyte", "Acolyte", "background", "acolyte")
        };

        var projection = new MechanicProjection
        {
            Input = "{}",
            Roles = { ["subject"] = subject },
            References = references
        };
        var materialized = await RunRaw("data/dnd2024.mechanic.character.origin.materialize", projection);
        Assert.True(materialized.Ok, materialized.Error);
        var effect = Assert.Single(materialized.Output.Effects);
        Assert.Equal("component.add", effect.Type);
        Assert.Equal("dnd2024.character.origin-selections", effect.DefinitionId);

        var matching = new MechanicProjection
        {
            Input = "{}",
            Roles = { ["subject"] = Entity("actor.ganji", "Ganji", new()
            {
                ["dnd2024.character-creation-record"] = record,
                ["dnd2024.character.origin-selections"] =
                    Json(new { speciesRef = Ref("species.half-elf"), backgroundRef = Ref("background.acolyte") })
            }) },
            References = references
        };
        var replay = await RunRaw("data/dnd2024.mechanic.character.origin.materialize", matching);
        Assert.True(replay.Ok, replay.Error);
        Assert.Empty(replay.Output.Effects);
        Assert.Contains("already-materialized", replay.Output.Data, StringComparison.Ordinal);

        var conflicting = new MechanicProjection
        {
            Input = "{}",
            Roles = { ["subject"] = Entity("actor.ganji", "Ganji", new()
            {
                ["dnd2024.character-creation-record"] = record,
                ["dnd2024.character.origin-selections"] =
                    Json(new { speciesRef = Ref("species.elf"), backgroundRef = Ref("background.acolyte") })
            }) },
            References = references
        };
        var rejected = await RunRaw("data/dnd2024.mechanic.character.origin.materialize", conflicting);
        Assert.False(rejected.Ok);
        Assert.Contains("conflict", rejected.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Character_sheet_v2_preserves_named_labels_nested_inventory_and_exact_wallet()
    {
        var coins = new ContainedProjection("item.coins", "Gold Coins", "contents", null, []);
        var pouch = new ContainedProjection("item.pouch", "Belt Pouch", "contents", null, [coins]);
        var backpack = new ContainedProjection("item.backpack", "Backpack", "inventory", null, [pouch]);
        var subject = Entity("actor.aric", "Aric", new(), related: null) with
        {
            Contains = [backpack]
        };
        const string legacy = """
        {"version":1,"subject":{"id":"actor.aric","name":"Aric"},"origin":{"speciesId":"dnd2024.content.species.human.v1","backgroundId":"dnd2024.content.background.soldier.v2"},"classes":[{"id":"membership.fighter","name":"Fighter membership","classId":"dnd2024.content.class.fighter.v1","level":3,"subclassId":null}],"inventory":{"items":[{"id":"item.backpack","name":"Backpack","definitionId":"dnd2024.equipment.container.backpack.v1","quantity":1,"slot":"inventory","depth":1,"equipmentSlots":[]},{"id":"item.pouch","name":"Belt Pouch","definitionId":"dnd2024.equipment.container.pouch.v2","quantity":1,"slot":"contents","depth":2,"equipmentSlots":[]},{"id":"item.coins","name":"Gold Coins","definitionId":"dnd2024.equipment.currency.gold-piece","quantity":25,"slot":"contents","depth":3,"equipmentSlots":[]}],"contentsDepth":4,"mayOmitDeeperContents":true}}
        """;
        const string currency = """
        {"test":"currency-value-read","rootId":"actor.aric","coinCount":25,"copperValue":2500,"denominations":[{"denominationId":"dnd2024.equipment.currency.gold-piece","code":"gp","count":25,"copperValuePerCoin":100,"totalCopperValue":2500}],"boundedDepth":4}
        """;
        var projection = new MechanicProjection
        {
            Input = "{}",
            Roles = { ["subject"] = subject },
            Children =
            {
                ["legacy"] = [Child("dnd2024.mechanic.character-sheet.project", "subject", subject.Id, legacy)],
                ["currency"] = [Child("dnd2024.mechanic.currency-value.read", "root", subject.Id, currency)]
            }
        };

        using var output = await Run("data/dnd2024.mechanic.character-sheet-v2.project", projection);
        var data = output.RootElement;
        var items = data.GetProperty("inventory").GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(2, data.GetProperty("version").GetInt32());
        Assert.Null(items[0].GetProperty("parentItemId").GetString());
        Assert.Equal("item.backpack", items[1].GetProperty("parentItemId").GetString());
        Assert.Equal("item.pouch", items[2].GetProperty("parentItemId").GetString());
        Assert.Equal(new[] { 0, 0, 0 }, items.Select(value => value.GetProperty("order").GetInt32()));
        Assert.Equal(25, data.GetProperty("wallet").GetProperty("gpCount").GetInt32());
        Assert.Equal(2500, data.GetProperty("wallet").GetProperty("copperValue").GetInt32());
        Assert.Equal("Fighter", data.GetProperty("classes")[0].GetProperty("class").GetProperty("label").GetString());
        var labels = Labels(data).ToArray();
        Assert.DoesNotContain(labels, value => value is "V1" or "V2" || value.Contains('.'));
        AssertSchema("character/dnd2024.query.character-sheet-v2.json", data.GetRawText());
    }

    [Fact]
    public async Task Character_sheet_v2_rejects_duplicate_nodes_and_depth_overflow_without_output()
    {
        var duplicate = new ContainedProjection("item.same", "Duplicate", "contents", null, []);
        var firstParent = new ContainedProjection("item.first-parent", "First Parent", "inventory", null, [duplicate]);
        var secondParent = new ContainedProjection("item.second-parent", "Second Parent", "inventory", null, [duplicate]);
        var subject = Entity("actor.aric", "Aric", new()) with { Contains = [firstParent, secondParent] };
        var projection = V2Projection(subject, """
            {"version":1,"subject":{"id":"actor.aric","name":"Aric"},"inventory":{"items":[],"contentsDepth":4,"mayOmitDeeperContents":true}}
            """);
        var duplicateResult = await RunRaw("data/dnd2024.mechanic.character-sheet-v2.project", projection);
        Assert.False(duplicateResult.Ok);
        Assert.Contains("duplicate", duplicateResult.Error, StringComparison.OrdinalIgnoreCase);

        var level5 = new ContainedProjection("item.5", "Five", "contents", null, []);
        var level4 = new ContainedProjection("item.4", "Four", "contents", null, [level5]);
        var level3 = new ContainedProjection("item.3", "Three", "contents", null, [level4]);
        var level2 = new ContainedProjection("item.2", "Two", "contents", null, [level3]);
        var level1 = new ContainedProjection("item.1", "One", "inventory", null, [level2]);
        var deepSubject = subject with { Contains = [level1] };
        var depthResult = await RunRaw("data/dnd2024.mechanic.character-sheet-v2.project",
            V2Projection(deepSubject, """
            {"version":1,"subject":{"id":"actor.aric","name":"Aric"},"inventory":{"items":[],"contentsDepth":4,"mayOmitDeeperContents":true}}
            """));
        Assert.False(depthResult.Ok);
        Assert.Contains("bounded depth", depthResult.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Character_dossier_joins_recorded_origin_features_inventory_and_provenance_without_private_rules_text()
    {
        const string speciesId = "dnd2024.content.species.half-elf.v1";
        const string backgroundId = "dnd2024.content.background.acolyte.v1";
        const string classId = "dnd2024.content.class.monk.v1";
        const string martialArtsId = "dnd2024.content.feature.monk.martial-arts.v1";
        const string unarmoredDefenseId = "dnd2024.content.feature.monk.unarmored-defense.v1";
        const string originFeatId = "dnd2024.feat.magic-initiate";
        var subject = Entity("actor.caldris.ganji", "Ganji", new()
        {
            ["dnd2024.character-creation-record"] = Json(new
            {
                status = "basic-playable",
                selections = new { speciesDefinitionId = speciesId, backgroundDefinitionId = backgroundId, classDefinitionId = classId },
                unresolvedEntitlements = new object[]
                {
                    new { ownerDefinitionId = speciesId, entitlementKey = "trait:darkvision", reason = "application-deferred" },
                    new { ownerDefinitionId = originFeatId, entitlementKey = "behavior:configuration:cleric", reason = "behavior-unimplemented" }
                }
            }),
            ["dnd2024.character.origin-selections"] = Json(new { speciesRef = Ref(speciesId), backgroundRef = Ref(backgroundId) }),
            ["dnd2024.character.feature-entitlements"] = Json(new
            {
                entitlements = new object[]
                {
                    new { featureRef = Ref(martialArtsId), grantedByRef = Ref(classId), grantKind = "class-feature", classLevel = 1 },
                    new { featureRef = Ref(unarmoredDefenseId), grantedByRef = Ref(classId), grantKind = "class-feature", classLevel = 1 },
                    new { featureRef = Ref(originFeatId), grantedByRef = Ref(backgroundId), grantKind = "origin-feat", configurationKey = "cleric" }
                }
            })
        }) with
        {
            Contains =
            [
                new ContainedProjection("item.ganji.quarterstaff", "Ganji's Quarterstaff", "inventory",
                    new Dictionary<string, string>
                    {
                        ["dnd2024.core.definition-link"] = Json(new { definition = "dnd2024.equipment.weapon.quarterstaff" })
                    }, []),
                new ContainedProjection("item.ganji.carving-knife", "Ganji's Carving Knife", "inventory",
                    new Dictionary<string, string>
                    {
                        ["dnd2024.core.definition-link"] = Json(new { definition = "dnd2024.equipment.tool.carving-knife" })
                    }, [])
            ]
        };
        var references = new Dictionary<string, ReferencedEntityProjection>
        {
            [speciesId] = Definition(speciesId, "Half-Elf (Caldris Homebrew, content v1)", "species", "half-elf"),
            [backgroundId] = Definition(backgroundId, "Acolyte (SRD 5.2.1, content v1)", "background", "acolyte"),
            [classId] = Definition(classId, "Monk (SRD 5.2.1, content v1)", "class", "monk"),
            [martialArtsId] = Definition(martialArtsId, "Martial Arts (SRD 5.2.1, content v1)", "feature", "martial-arts"),
            [unarmoredDefenseId] = Definition(unarmoredDefenseId, "Unarmored Defense (SRD 5.2.1, content v1)", "feature", "unarmored-defense"),
            [originFeatId] = Definition(originFeatId, "Magic Initiate (SRD 5.2.1, content v1)", "feature", "magic-initiate"),
            ["dnd2024.equipment.weapon.quarterstaff"] = new("dnd2024.equipment.weapon.quarterstaff", new Dictionary<string, string>(), "Quarterstaff"),
            ["dnd2024.equipment.tool.carving-knife"] = new("dnd2024.equipment.tool.carving-knife", new Dictionary<string, string>
            {
                ["game.core.rules.readable"] = Json(new { visibility = "gm", presentationStatus = "published", summary = "Private GM equipment history." })
            }, "Carving Knife")
        };
        const string sheet = """
        {"version":2,"subject":{"id":"actor.caldris.ganji","label":"Ganji"},"identity":{"biography":"A quiet acolyte seeking a merciful path."},"origin":{"species":{"id":"dnd2024.content.species.half-elf.v1","label":"Half Elf V1"},"background":{"id":"dnd2024.content.background.acolyte.v1","label":"Acolyte V1"}},"classes":[{"id":"membership.ganji.monk","name":"Monk membership","class":{"id":"dnd2024.content.class.monk.v1","label":"Monk V1"},"level":1,"subclass":null}],"level":1,"proficiencyBonus":2,"abilities":[{"ability":{"id":"str","label":"Strength"},"score":10,"modifier":0}],"savingThrows":[],"skills":[],"hitPoints":{"current":9,"maximum":9,"maximumReduction":0},"armorClass":{"value":15},"features":[{"feature":{"id":"dnd2024.content.feature.monk.martial-arts.v1","label":"Martial Arts V1"},"grantedBy":{"id":"dnd2024.content.class.monk.v1","label":"Monk V1"},"grantKind":{"id":"class-feature","label":"Class Feature"},"classLevel":1},{"feature":{"id":"dnd2024.content.feature.monk.unarmored-defense.v1","label":"Unarmored Defense V1"},"grantedBy":{"id":"dnd2024.content.class.monk.v1","label":"Monk V1"},"grantKind":{"id":"class-feature","label":"Class Feature"},"classLevel":1},{"feature":{"id":"dnd2024.feat.magic-initiate","label":"Magic Initiate"},"grantedBy":{"id":"dnd2024.content.background.acolyte.v1","label":"Acolyte V1"},"grantKind":{"id":"origin-feat","label":"Origin Feat"},"classLevel":null}],"inventory":{"items":[{"id":"item.ganji.quarterstaff","name":"Ganji's Quarterstaff","definition":{"id":"dnd2024.equipment.weapon.quarterstaff","label":"Quarterstaff V1"},"quantity":1,"slot":"inventory","parentItemId":null,"order":0,"depth":1,"childCount":0,"deeperContentsOmitted":false,"equipmentSlots":[]},{"id":"item.ganji.carving-knife","name":"Ganji's Carving Knife","definition":{"id":"dnd2024.equipment.tool.carving-knife","label":"Carving Knife V1"},"quantity":1,"slot":"inventory","parentItemId":null,"order":1,"depth":1,"childCount":0,"deeperContentsOmitted":false,"equipmentSlots":[]}],"contentsDepth":4,"mayOmitDeeperContents":true},"wallet":{"coinCount":0,"copperValue":0,"gpCount":0,"denominations":[]}}
        """;
        var projection = new MechanicProjection
        {
            Input = "{}",
            Roles = { ["subject"] = subject },
            References = references,
            Children = { ["sheet"] = [Child("dnd2024.mechanic.character-sheet-v2.project", "subject", subject.Id, sheet)] }
        };

        using var output = await Run("data/dnd2024.mechanic.character-dossier-v1.project", projection);
        var data = output.RootElement;
        Assert.Equal("Half-Elf", data.GetProperty("origin").GetProperty("species").GetProperty("label").GetString());
        Assert.Equal("Acolyte", data.GetProperty("origin").GetProperty("background").GetProperty("label").GetString());
        Assert.Equal("Monk", data.GetProperty("classes")[0].GetProperty("definition").GetProperty("label").GetString());
        Assert.Equal(3, data.GetProperty("features").GetArrayLength());
        Assert.Equal(2, data.GetProperty("sheet").GetProperty("inventory").GetProperty("items").GetArrayLength());
        Assert.Equal(4, data.GetProperty("provenance").GetProperty("inventoryDepth").GetInt32());
        Assert.DoesNotContain("Private GM equipment history", data.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(Labels(data), value => value.EndsWith(" V1", StringComparison.Ordinal));
        AssertSchema("character/dnd2024.query.character-dossier-v1.json", data.GetRawText());
    }

    private static async Task<JsonDocument> Run(string mechanicPath, string role, EntityProjection entity) =>
        await Run(mechanicPath, new MechanicProjection { Input = "{}", Roles = { [role] = entity } });

    private static async Task<JsonDocument> Run(string mechanicPath, MechanicProjection projection)
    {
        var result = await Engine.RunAsync(ReadMechanic(mechanicPath).Source, projection, ExecutionLimits.Default);
        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Output.Effects);
        Assert.Empty(result.Output.Events);
        Assert.Empty(result.Output.Notifications);
        return JsonDocument.Parse(result.Output.Data);
    }

    private static Task<MechanicRunResult> RunRaw(string mechanicPath, MechanicProjection projection) =>
        Engine.RunAsync(ReadMechanic(mechanicPath).Source, projection, ExecutionLimits.Default);

    private static MechanicProjection V2Projection(EntityProjection subject, string legacy) => new()
    {
        Input = "{}",
        Roles = { ["subject"] = subject },
        Children =
        {
            ["legacy"] = [Child("dnd2024.mechanic.character-sheet.project", "subject", subject.Id, legacy)],
            ["currency"] = [Child("dnd2024.mechanic.currency-value.read", "root", subject.Id,
                $$"""{"test":"currency-value-read","rootId":"{{subject.Id}}","coinCount":0,"copperValue":0,"denominations":[],"boundedDepth":4}""")]
        }
    };

    private static ChildMechanicResult Child(string mechanicId, string role, string entityId, string data) =>
        new(mechanicId, 1, 0, new Dictionary<string, string> { [role] = entityId },
            new MechanicOutput { Data = data }, [], 0);

    private static IEnumerable<string> Labels(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.NameEquals("label") && property.Value.ValueKind == JsonValueKind.String)
                    yield return property.Value.GetString()!;
                foreach (var nested in Labels(property.Value)) yield return nested;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                foreach (var nested in Labels(item)) yield return nested;
    }

    private static void AssertSchema(string queryPath, string data)
    {
        var contract = ApplicationQueryContract.Parse(File.ReadAllText(Query(queryPath)), Application);
        var compiled = Schemas.Compile(contract.OutputSchemaJson);
        var validation = Schemas.Validate(compiled.ProfileId, compiled.NormalizedSchema, data);
        Assert.True(validation.Status == SchemaValueStatus.Valid,
            string.Join("; ", validation.Diagnostics.Select(value => value.Pointer + ": " + value.Message)));
    }

    private static EntityProjection Entity(string id, string name, Dictionary<string, string> components,
        string? containerId = null, string containerSlot = "",
        IReadOnlyList<RelationshipProjection>? relationships = null,
        IReadOnlyList<RelatedEntityProjection>? related = null) =>
        new(id, name, components, containerId, containerSlot, null, relationships, related);

    private static RelatedEntityProjection Related(string id, string name, string kind, string componentId, object value) =>
        Related(id, name, kind, new Dictionary<string, string> { [componentId] = Json(value) });

    private static RelatedEntityProjection Related(string id, string name, string kind, Dictionary<string, string> components) =>
        new(id, name, "campaign", id, kind, "{}", components);

    private static string Root() => Json(new { status = "active", title = "Measure of Mercy", premise = "Choose what justice costs.", partyGoals = new[] { "Find the envoy." }, toneAndBoundaries = new[] { "Hopeful intrigue." }, rulesetScope = "dnd2024", creationMethod = "manual", reviewFingerprint = new string('a', 64) });
    private static string Recap() => Json(new { protocolVersion = "session.s0.c3-only.v1", chapter = new { id = "chapter", status = "active", title = "Missing Envoy", partyQuestion = "Who benefits?" }, arc = new { id = "arc", status = "active", title = "The Broken Scale", partyStake = "Keep the city from choosing vengeance." }, milestones = new[] { new { chapterId = "chapter-0", title = "Arrival", closingSummary = "The party reached the city.", timestamp = "2026-09-01T18:00:00Z", sequence = 1 } } });
    private static object Ref(string entityId) => new { entityId };
    private static ReferencedEntityProjection Definition(
        string id, string name, string kind, string contentKey) =>
        new(id, new Dictionary<string, string>
        {
            ["dnd2024.character.content-definition"] = Json(new
            {
                kind,
                contentKey,
                contentVersion = 1,
                status = "active",
                sourceRef = new { sourceId = "source", locator = "Source > Definition" }
            })
        }, name);
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static MechanicFile ReadMechanic(string relative)
    {
        var path = Path.Combine(Mechanics(), relative.Replace('/', Path.DirectorySeparatorChar));
        return MechanicFile.Parse(File.ReadAllText(path + ".md"), relative + ".md", File.ReadAllText(path + ".js"));
    }

    private static string Query(string relative) => Path.Combine(Queries(), relative.Replace('/', Path.DirectorySeparatorChar));
    private static string Mechanics() => Path.Combine(Catalog(), "applications", "dnd2024", "mechanics");
    private static string Queries() => Path.Combine(Catalog(), "applications", "dnd2024", "queries");
    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

}
