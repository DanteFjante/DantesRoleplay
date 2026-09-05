using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Knowledge.Tests;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Blobs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Media;
using DantesRoleplay.MCPServer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class ItemDetailsProjectionTests
{
    private const string MechanicId = "dnd2024.mechanic.inventory-item-details.project";
    private const string QueryId = "dnd2024.query.inventory-item-details";
    private static readonly BoundedJsonSchemaValidator Schemas = new();
    private static string Root
    {
        get
        {
            for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
                if (File.Exists(Path.Combine(path.FullName, "DantesRoleplay.slnx"))) return path.FullName;
            throw new DirectoryNotFoundException();
        }
    }
    private static string App => Path.Combine(Root, "catalog", "applications", "dnd2024");
    private static MechanicFile Mechanic => MechanicFile.Parse(
        File.ReadAllText(Path.Combine(App, "mechanics", "data", MechanicId + ".md")), MechanicId,
        File.ReadAllText(Path.Combine(App, "mechanics", "data", MechanicId + ".js")));
    private static ApplicationQueryContract Query => ApplicationQueryContract.Parse(
        File.ReadAllText(Path.Combine(App, "queries", "data", QueryId + ".json")), ApplicationIdentifier.Parse("dnd2024"));
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static object Ref(string id) => new { entityId = id };
    private static JsonElement Property(JsonElement data, string label) =>
        Assert.Single(data.GetProperty("properties").EnumerateArray(), value => value.GetProperty("label").GetString() == label);

    [Fact]
    public void Registered_query_pins_exact_authored_mechanic_and_closed_output_with_no_children_or_writes()
    {
        var mechanic = Mechanic;
        var hash = ApplicationCatalogRecordContent.Fingerprint(ApplicationCatalogRecordContent.MechanicJson(mechanic));
        var query = Query;
        var output = Schemas.Compile(query.OutputSchemaJson);
        Assert.True(output.IsAccepted, Json(output.Diagnostics));
        Assert.True(hash == query.ProjectionContentHash && output.SchemaHash == query.OutputSchemaHash,
            $"Mechanic hash: {hash}; Schema hash: {output.SchemaHash}");
        Assert.Equal(MechanicId, query.ProjectionQualifiedId);
        Assert.Equal(ApplicationQueryExposure.BindingOnly, query.Exposure);
        var requirements = MechanicRequirements.Parse(mechanic.Requirements);
        Assert.NotNull(requirements.AuthorizedContext);
        Assert.Empty(requirements.Children);
        Assert.Empty(requirements.EffectComponentIds);
        Assert.Equal(Schemas.Compile(query.InputSchemaJson!).SchemaHash, Schemas.Compile(requirements.InputSchema!.Value.GetRawText()).SchemaHash);
        var registered = ApplicationQueryContract.Parse(ApplicationCatalogRecordContent.QueryJson(query), ApplicationIdentifier.Parse("dnd2024"));
        Assert.Equal(Schemas.Compile(query.InputSchemaJson!).SchemaHash, Schemas.Compile(registered.InputSchemaJson!).SchemaHash);
        var content = ApplicationCatalogRecordContent.QueryJson(query);
        var descriptor = ApplicationCapabilityContractAdapter.Create(ApplicationIdentifier.Parse("dnd2024"),
            new("dnd2024", "query", QueryId, query.Name, query.Description, [], [], "queries/data", "active", 1,
                content, ApplicationCatalogRecordContent.Fingerprint(content), "fixture", QueryId + ".json"));
        Assert.Equal(Schemas.Compile(query.InputSchemaJson!).SchemaHash, Schemas.Compile(descriptor.Input.SchemaJson).SchemaHash);
        Assert.All(descriptor.Examples, example => Assert.Equal(example.ExpectedValid ? SchemaValueStatus.Valid : SchemaValueStatus.Invalid,
            Schemas.Validate(Schemas.Compile(descriptor.Input.SchemaJson).NormalizedSchema, example.InputJson).Status));
    }

    [Fact]
    public void Legacy_query_serialization_and_discovery_keep_empty_input_and_exact_fingerprint()
    {
        var query = ApplicationQueryContract.Parse(File.ReadAllText(Path.Combine(App, "queries", "character", "dnd2024.query.character-sheet.json")), ApplicationIdentifier.Parse("dnd2024"));
        var content = ApplicationCatalogRecordContent.QueryJson(query);
        using var serialized = JsonDocument.Parse(content);
        Assert.False(serialized.RootElement.TryGetProperty("inputSchema", out _));
        var roundTrip = ApplicationQueryContract.Parse(content, ApplicationIdentifier.Parse("dnd2024"));
        Assert.Null(roundTrip.InputSchemaJson);
        Assert.Equal(content, ApplicationCatalogRecordContent.QueryJson(roundTrip));
    }

    [Fact]
    public void Display_vocabulary_matches_authored_entity_names_and_never_guesses_reference_labels()
    {
        var match = Regex.Match(Mechanic.Source, @"var labels = (\{[\s\S]*?\});");
        using var labels = JsonDocument.Parse(match.Groups[1].Value);
        var expected = labels.RootElement.EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString());
        foreach (var path in Directory.EnumerateFiles(Path.Combine(App, "content", "entities"), "*.json", SearchOption.AllDirectories))
        {
            using var entity = JsonDocument.Parse(File.ReadAllText(path));
            if (entity.RootElement.TryGetProperty("id", out var id) && expected.Remove(id.GetString()!, out var name))
                Assert.Equal(name, entity.RootElement.GetProperty("name").GetString());
        }
        Assert.Empty(expected);
    }

    [Fact]
    public async Task Instances_share_definition_measurements_but_keep_names_stacks_and_modified_durability()
    {
        using var fixture = await Fixture.Create();
        await fixture.Item("item.second", "Dented keepsake", 2);
        await fixture.Component(fixture.ItemId, "dnd2024.object.durability", new { currentHitPoints = 0, destroyed = true });
        await fixture.Component("item.second", "dnd2024.object.durability", new { currentHitPoints = 7, destroyed = false });
        // A stray instance physical facet does not override the existing definition owner.
        await fixture.Component("item.second", "dnd2024.item.physical", new { weight = Fixture.Weight(99) });
        using var first = await fixture.Details(dm: true);
        using var second = await fixture.Details("item.second", dm: true);
        Assert.Equal("Personal keepsake", first.RootElement.GetProperty("name").GetString());
        Assert.Equal("Dented keepsake", second.RootElement.GetProperty("name").GetString());
        Assert.Equal(6, first.RootElement.GetProperty("quantity").GetInt32());
        Assert.Equal(2, second.RootElement.GetProperty("quantity").GetInt32());
        Assert.Equal("1/2", Property(first.RootElement, "Weight per item").GetProperty("value").GetString());
        Assert.Equal("1/2", Property(second.RootElement, "Weight per item").GetProperty("value").GetString());
        Assert.Equal("Kilogram", Property(first.RootElement, "Weight per item").GetProperty("unit").GetString());
        Assert.Equal(0, Property(first.RootElement, "Current hit points").GetProperty("value").GetInt32());
        Assert.Equal(7, Property(second.RootElement, "Current hit points").GetProperty("value").GetInt32());
        Assert.DoesNotContain("sourceRef", first.RootElement.GetRawText());
    }

    [Fact]
    public async Task Confirmed_observer_and_GM_preview_match_and_unknown_override_hides_identity_and_definition()
    {
        using var fixture = await Fixture.Create();
        await fixture.Know(fixture.ItemId);
        await fixture.Know(fixture.Definition);
        using var actor = await fixture.Details();
        fixture.Policy.IsDm = true;
        using var preview = await fixture.Details();
        Assert.Equal(actor.RootElement.GetRawText(), preview.RootElement.GetRawText());
        Assert.Equal("Personal keepsake", actor.RootElement.GetProperty("name").GetString());
        Assert.Equal(fixture.Definition, actor.RootElement.GetProperty("definitionId").GetString());
        await fixture.Game.RelateAsync(fixture.Game.World, fixture.ItemId, fixture.Game.Binding.BaselineRelationshipKind, "{\"inheritance\":\"current\"}");
        await fixture.Game.Edges.SetRelationshipAsync(fixture.Game.Campaign, fixture.Game.Actor, fixture.ItemId,
            fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"unknown\"}", 1);
        using var hidden = await fixture.Details();
        Assert.Equal("Item", hidden.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, hidden.RootElement.GetProperty("definitionId").ValueKind);
        Assert.Empty(hidden.RootElement.GetProperty("properties").EnumerateArray());
        Assert.DoesNotContain(fixture.Definition, hidden.RootElement.GetRawText());
        Assert.DoesNotContain("Personal keepsake", hidden.RootElement.GetRawText());
        Assert.True(fixture.Policy.IsDm);
    }

    [Theory]
    [InlineData("familiar")]
    [InlineData("suspected")]
    [InlineData("unknown")]
    public async Task Unconfirmed_identity_never_expands_the_definition(string stance)
    {
        using var fixture = await Fixture.Create();
        await fixture.Know(fixture.ItemId, stance);
        await fixture.Know(fixture.Definition);
        using var result = await fixture.Details();
        Assert.Equal("Item", result.RootElement.GetProperty("name").GetString());
        Assert.Empty(result.RootElement.GetProperty("properties").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("observerKnowledge").ValueKind);
    }

    [Fact]
    public async Task Authorized_uncertain_statement_keeps_its_stance_without_revealing_hidden_statement_or_raw_facets()
    {
        using var fixture = await Fixture.Create();
        await fixture.Game.AddKnowledgeAsync("fact.allowed", "It may resist fire.", fixture.ItemId);
        await fixture.Game.AddKnowledgeAsync("fact.hidden", "Private curse description", fixture.ItemId);
        await fixture.Know("fact.allowed", "suspected");
        await fixture.Component(fixture.ItemId, "dnd2024.magic-item.curse", new { curse = Ref("private.curse") });
        using var result = await fixture.Details();
        var property = Property(result.RootElement, "Recorded knowledge");
        Assert.Equal("It may resist fire.", property.GetProperty("value").GetString());
        Assert.Equal("suspected", property.GetProperty("sources")[0].GetProperty("knowledgeState").GetString());
        Assert.DoesNotContain("private", result.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fact.allowed", result.RootElement.GetRawText());
        Assert.DoesNotContain("Personal keepsake", result.RootElement.GetRawText());
        Assert.Empty(result.RootElement.GetProperty("reasons").EnumerateArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validated_discovery_can_restrict_identity_but_never_grants_a_hidden_curse(bool identityKnown)
    {
        using var fixture = await Fixture.Create();
        await fixture.Know(fixture.ItemId);
        await fixture.Know(fixture.Definition);
        await fixture.Component(fixture.ItemId, "dnd2024.magic-item.knowledge", new
        {
            knowledgeRelationship = new { stateSpaceId = fixture.Game.Campaign, fromEntityId = fixture.Game.Actor,
                toEntityId = fixture.ItemId, qualifiedKind = fixture.Game.Binding.ExplicitStateRelationshipKind },
            identityKnown, curseKnown = false, knownProperties = Array.Empty<object>()
        });
        await fixture.Component(fixture.ItemId, "dnd2024.magic-item.curse", new { curse = Ref("private.curse") });
        using var result = await fixture.Details();
        Assert.Equal(identityKnown ? "Personal keepsake" : "Item", result.RootElement.GetProperty("name").GetString());
        Assert.DoesNotContain("curse", result.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", result.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_metadata_is_read_without_conversion_and_missing_quantity_is_null_not_zero()
    {
        using var fixture = await Fixture.Create();
        var resolved = await fixture.Resolve(dm: true);
        var projection = resolved.Projection!;
        var components = projection.References[fixture.Definition].Components.ToDictionary();
        components.Remove("dnd2024.core.version");
        components.Remove("dnd2024.item.physical");
        components["dnd2024.item-definition"] = Json(new { definitionVersion = 1, kind = "shield", stackPolicy = "separate",
            massPounds = new { numerator = 6, denominator = 1 }, sourceRef = new { sourceId = "private.source", locator = "Private source locator" },
            armorProfile = new { category = "shield", armorClassBonus = 0, stealthDisadvantage = false } });
        projection.References[fixture.Definition] = projection.References[fixture.Definition] with { Components = components };
        var subject = projection.Roles["subject"];
        var item = subject.Contains!.Single();
        projection.Roles["subject"] = subject with { Contains = [item with
            { Components = item.Components!.Where(pair => pair.Key != "dnd2024.item.quantity").ToDictionary() }] };
        using var result = await Run(projection);
        Assert.Equal(6, Property(result.RootElement, "Weight per item").GetProperty("value").GetInt32());
        Assert.Equal("Pound", Property(result.RootElement, "Weight per item").GetProperty("unit").GetString());
        Assert.Equal(0, Property(result.RootElement, "Armor class bonus").GetProperty("value").GetInt32());
        Assert.False(Property(result.RootElement, "Stealth disadvantage").GetProperty("value").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("quantity").ValueKind);
        Assert.DoesNotContain("private", result.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Known_but_unresolved_property_or_curse_reference_reports_partial_without_exposing_its_identifier(bool curse)
    {
        using var fixture = await Fixture.Create();
        await fixture.Know(fixture.ItemId);
        await fixture.Component(fixture.ItemId, "dnd2024.magic-item.knowledge", new
        {
            knowledgeRelationship = new { stateSpaceId = fixture.Game.Campaign, fromEntityId = fixture.Game.Actor,
                toEntityId = fixture.ItemId, qualifiedKind = fixture.Game.Binding.ExplicitStateRelationshipKind },
            identityKnown = true, curseKnown = curse, knownProperties = curse ? Array.Empty<object>() : new[] { Ref("private.property") }
        });
        using var result = await fixture.Details();
        Assert.Equal("partial", result.RootElement.GetProperty("state").GetString());
        Assert.Contains(result.RootElement.GetProperty("reasons").EnumerateArray(), value => value.GetString() == "dependency-unavailable");
        Assert.DoesNotContain("private.property", result.RootElement.GetRawText());
    }

    [Fact]
    public async Task Nested_containment_is_exact_for_DM_and_does_not_leak_parent_identity_to_Player()
    {
        using var fixture = await Fixture.Create();
        await fixture.Item("item.bag", "Secret bag name", 1);
        await fixture.Game.Edges.MoveContainmentAsync(fixture.Game.Campaign, fixture.ItemId, "item.bag", "inside", 1);
        using var dm = await fixture.Details(dm: true);
        Assert.Equal("item.bag", dm.RootElement.GetProperty("container").GetProperty("itemId").GetString());
        using var player = await fixture.Details();
        Assert.Equal(JsonValueKind.Null, player.RootElement.GetProperty("container").ValueKind);
        Assert.DoesNotContain("Secret bag name", player.RootElement.GetRawText());
    }

    [Fact]
    public async Task Missing_required_definition_fails_closed_and_absent_optional_facets_are_not_fabricated()
    {
        using var fixture = await Fixture.Create();
        using var present = await fixture.Details(dm: true);
        Assert.Single(present.RootElement.GetProperty("properties").EnumerateArray());
        Assert.Equal("ready", present.RootElement.GetProperty("state").GetString());
        await fixture.Item("item.broken", "Broken reference", 1, "definition.missing");
        var missing = await fixture.Resolve("item.broken", dm: true);
        Assert.Null(missing.Projection);
        Assert.Equal(["READ_MODEL_UNAVAILABLE"], missing.Problems);
    }

    [Fact]
    public async Task Zero_measurement_and_recorded_false_survive_but_reference_only_charge_count_is_unavailable()
    {
        using var fixture = await Fixture.Create(weightNumerator: 0);
        await fixture.Component(fixture.ItemId, "dnd2024.magic-item.attunement", new { attunedBy = Ref(fixture.Game.Actor), active = false });
        await fixture.Component(fixture.ItemId, "dnd2024.magic-item.charges", new { resourceDefinition = Ref("private.resource") });
        using var result = await fixture.Details(dm: true);
        Assert.Equal("0/2", Property(result.RootElement, "Weight per item").GetProperty("value").GetString());
        Assert.False(Property(result.RootElement, "Attunement active").GetProperty("value").GetBoolean());
        Assert.Contains(result.RootElement.GetProperty("reasons").EnumerateArray(), value => value.GetString() == "dependency-unavailable");
        Assert.DoesNotContain("private.resource", result.RootElement.GetRawText());
        Assert.DoesNotContain(result.RootElement.GetProperty("properties").EnumerateArray(), value => value.GetProperty("label").GetString() == "Charges");
    }

    [Fact]
    public async Task Equipment_slots_and_supported_definition_facets_use_canonical_labels_and_authored_scalars()
    {
        using var fixture = await Fixture.Create();
        await fixture.Component(fixture.ItemId, "dnd2024.item.equipment", new { equippedBy = Ref(fixture.Game.Actor), slots = new[] { Ref("dnd2024.equipment-slot.main-hand") } });
        await fixture.Component(fixture.Definition, "dnd2024.item.weapon", new { category = Ref("dnd2024.equipment.weapon-category.simple"), properties = new[] { Ref("dnd2024.equipment.weapon-property.light") } });
        await fixture.Component(fixture.Definition, "dnd2024.item.consumable", new { unitsConsumedPerUse = 1 });
        using var result = await fixture.Details(dm: true);
        Assert.Equal("Main Hand", result.RootElement.GetProperty("equipmentSlots")[0].GetString());
        Assert.Equal("Simple", Property(result.RootElement, "Weapon category").GetProperty("value").GetString());
        Assert.Equal("Light", Property(result.RootElement, "Weapon property").GetProperty("value").GetString());
        Assert.Equal(1, Property(result.RootElement, "Units consumed per use").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Depth_bound_is_reported_for_selected_item_and_rejects_deeper_selection()
    {
        using var fixture = await Fixture.Create();
        var parent = fixture.ItemId;
        for (var depth = 2; depth <= 5; depth++)
        {
            var id = "item.depth." + depth;
            await fixture.Item(id, "Nested", 1);
            await fixture.Game.Edges.MoveContainmentAsync(fixture.Game.Campaign, id, parent, "inside", 1);
            parent = id;
        }
        using var selected = await fixture.Details(dm: true);
        Assert.Contains(selected.RootElement.GetProperty("reasons").EnumerateArray(), value => value.GetString() == "inventory-bound");
        Assert.Equal(["READ_MODEL_SELECTION_UNAVAILABLE"], (await fixture.Resolve(parent)).Problems);
    }

    [Fact]
    public async Task Bounded_known_statements_report_partial_and_oversized_single_source_fails_without_silent_truncation()
    {
        using var fixture = await Fixture.Create();
        var projection = await fixture.Resolve(dm: true);
        for (var i = 0; i < 40; i++) projection.Projection!.References["fact." + i.ToString("D2")] =
            new("fact." + i, new Dictionary<string, string> { ["authorized-knowledge"] = Json(new
                { subjectId = fixture.ItemId, displayText = new string('é', 512), presentationKind = "statement", state = "known" }) });
        using var result = await Run(projection.Projection!);
        Assert.Equal(32, result.RootElement.GetProperty("properties").GetArrayLength());
        Assert.Equal("partial", result.RootElement.GetProperty("state").GetString());
        Assert.True(Encoding.UTF8.GetByteCount(result.RootElement.GetRawText()) <= 65536);
        projection.Projection!.References["fact.00"] = new("fact.00", new Dictionary<string, string> { ["authorized-knowledge"] = Json(new
            { subjectId = fixture.ItemId, displayText = new string('x', 2049), presentationKind = "statement", state = "known" }) });
        var invalid = await new JintMechanicEngine().RunAsync(Mechanic.Source, projection.Projection, ExecutionLimits.Default);
        Assert.False(invalid.Ok);
    }

    private static async Task<JsonDocument> Run(MechanicProjection projection)
    {
        var run = await new JintMechanicEngine().RunAsync(Mechanic.Source, projection, ExecutionLimits.Default);
        Assert.True(run.Ok, run.Error);
        Assert.Empty(run.Output.Effects);
        Assert.Empty(run.Output.Events);
        Assert.Empty(run.Output.Notifications);
        var schema = Schemas.Compile(Query.OutputSchemaJson);
        var validation = Schemas.Validate(schema.NormalizedSchema, run.Output.Data);
        Assert.True(validation.Status == SchemaValueStatus.Valid, Json(validation) + "\n" + run.Output.Data);
        return JsonDocument.Parse(run.Output.Data);
    }

    [Fact]
    public async Task Media_uses_instance_roles_before_permitted_definition_inheritance_and_Player_preview_matches_Actor()
    {
        using var fixture = await Fixture.Create();
        fixture.Media.Add(fixture.ItemId, "instance", "illustration", "Instance illustration");
        fixture.Media.Add(fixture.ItemId, "secret", "handout", "Secret caption and image", player: false);
        fixture.Media.Add(fixture.Definition, "inherited", "illustration", "Definition illustration");
        fixture.Media.Add(fixture.Definition, "icon", "icon", "Definition icon");
        fixture.Media.Add(fixture.Definition, "handout", "handout", "Definition handout");
        using var unknown = await fixture.Details();
        Assert.Empty(unknown.RootElement.GetProperty("media").EnumerateArray());
        Assert.DoesNotContain("illustration", unknown.RootElement.GetRawText());
        await fixture.Know(fixture.ItemId);
        await fixture.Know(fixture.Definition);
        using var actor = await fixture.Details();
        Assert.Equal(["Instance illustration", "Definition icon"], actor.RootElement.GetProperty("media").EnumerateArray().Select(value => value.GetProperty("alt").GetString()));
        fixture.Policy.IsDm = true;
        using var preview = await fixture.Details();
        Assert.Equal(actor.RootElement.GetRawText(), preview.RootElement.GetRawText());
        Assert.DoesNotContain("Secret", preview.RootElement.GetRawText());
        Assert.DoesNotContain(new string('a', 64), preview.RootElement.GetRawText());
        Assert.DoesNotContain("private/source", preview.RootElement.GetRawText());
        fixture.Media.Items[fixture.ItemId].Clear();
        using var inherited = await fixture.Details();
        Assert.Equal(["Definition illustration", "Definition icon"], inherited.RootElement.GetProperty("media").EnumerateArray().Select(value => value.GetProperty("alt").GetString()));
    }

    [Fact]
    public async Task View_bound_image_reauthorizes_knowledge_possession_and_visibility_without_ambient_GM_enrichment()
    {
        using var fixture = await Fixture.Create();
        fixture.Media.Add(fixture.ItemId, "instance", "illustration", "Safe art");
        fixture.Media.Add(fixture.ItemId, "secret", "illustration", "Private art", player: false);
        await fixture.Know(fixture.ItemId);
        using var actor = await fixture.Details();
        var playerUrl = actor.RootElement.GetProperty("media")[0].GetProperty("contentUrl").GetString()!;
        Assert.Equal(200, await fixture.OpenImage(playerUrl));
        Assert.Equal(EntityMediaAudience.Player, fixture.Media.LastOpenedAudience);
        fixture.Policy.IsDm = true;
        Assert.Equal(200, await fixture.OpenImage(playerUrl));
        Assert.Equal(EntityMediaAudience.Player, fixture.Media.LastOpenedAudience);
        using var dm = await fixture.Details(dm: true);
        var dmUrl = dm.RootElement.GetProperty("media")[1].GetProperty("contentUrl").GetString()!;
        fixture.Policy.IsDm = false;
        Assert.Equal(404, await fixture.OpenImage(dmUrl));
        Assert.Equal(404, await fixture.OpenImage(playerUrl, "actor.other"));
        Assert.Equal(404, await fixture.OpenImage(playerUrl, query: "?perspective=dm"));
        fixture.Media.Items[fixture.ItemId][0] = fixture.Media.Items[fixture.ItemId][0] with { Visibility = [EntityMediaAudience.GameMaster] };
        Assert.Equal(404, await fixture.OpenImage(playerUrl));
        fixture.Media.Items[fixture.ItemId][0] = fixture.Media.Items[fixture.ItemId][0] with { Visibility = [EntityMediaAudience.Player, EntityMediaAudience.GameMaster] };
        await fixture.Game.Edges.SetRelationshipAsync(fixture.Game.Campaign, fixture.Game.Actor, fixture.ItemId,
            fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"unknown\"}", 1);
        fixture.Policy.IsDm = true;
        Assert.Equal(404, await fixture.OpenImage(playerUrl));
        await fixture.Game.Edges.MoveContainmentAsync(fixture.Game.Campaign, fixture.ItemId, fixture.Game.World, "elsewhere", 1);
        Assert.Equal(404, await fixture.OpenImage(dmUrl));
    }

    [Fact]
    public async Task Missing_assets_and_changed_metadata_do_not_leave_a_usable_old_content_link()
    {
        using var fixture = await Fixture.Create();
        fixture.Media.Add(fixture.ItemId, "instance", "illustration", "Safe art");
        await fixture.Know(fixture.ItemId);
        using var view = await fixture.Details();
        var url = view.RootElement.GetProperty("media")[0].GetProperty("contentUrl").GetString()!;
        fixture.Media.Missing = true;
        using var missing = await fixture.Details();
        Assert.Empty(missing.RootElement.GetProperty("media").EnumerateArray());
        Assert.Equal(404, await fixture.OpenImage(url));
        fixture.Media.Missing = false;
        fixture.Media.Items[fixture.ItemId][0] = fixture.Media.Items[fixture.ItemId][0] with { Caption = "Changed caption" };
        Assert.Equal(404, await fixture.OpenImage(url));
        using var refreshed = await fixture.Details();
        var fresh = refreshed.RootElement.GetProperty("media")[0].GetProperty("contentUrl").GetString()!;
        Assert.NotEqual(url, fresh);
        Assert.Equal(200, await fixture.OpenImage(fresh));
        fixture.Media.Unavailable = true;
        using var unavailable = await fixture.Details();
        Assert.Empty(unavailable.RootElement.GetProperty("media").EnumerateArray());
        Assert.Equal("Personal keepsake", unavailable.RootElement.GetProperty("name").GetString());
        Assert.Equal(404, await fixture.OpenImage(fresh));
    }

    [Fact]
    public async Task Permission_change_during_content_open_denies_the_stream_and_disposes_it()
    {
        using var fixture = await Fixture.Create();
        fixture.Media.Add(fixture.ItemId, "instance", "illustration", "Safe art");
        await fixture.Know(fixture.ItemId);
        using var view = await fixture.Details();
        var url = view.RootElement.GetProperty("media")[0].GetProperty("contentUrl").GetString()!;
        fixture.Media.OnOpen = () => fixture.Policy.Revision = "changed";
        Assert.Equal(404, await fixture.OpenImage(url));
        Assert.False(fixture.Media.LastStream!.CanRead);
    }

    [Fact]
    public void Media_links_expire_and_remain_bounded_without_becoming_blob_addresses()
    {
        var clock = new Clock();
        var links = new ReadModelMediaLinkStore(clock);
        var request = new ApplicationReadModelRequest("space", ApplicationIdentifier.Parse("fixture"), "fixture.query.details",
            new Dictionary<string, string> { ["subject"] = "actor" }, new("player"), "{\"itemId\":\"item\"}");
        var ticket = new ReadModelMediaTicket(request, "campaign", "actor", "item", "visual-0", "hash");
        var first = links.GetOrCreate(ticket);
        Assert.Equal(first, links.GetOrCreate(ticket));
        Assert.NotNull(links.Find(first.Split('/')[3]));
        clock.Now = clock.Now.AddMinutes(11);
        Assert.Null(links.Find(first.Split('/')[3]));
        var next = links.GetOrCreate(ticket);
        Assert.NotEqual(first, next);
        for (var i = 0; i < 4096; i++) links.GetOrCreate(ticket with { MediaId = "image." + i });
        Assert.Null(links.Find(next.Split('/')[3]));
        Assert.Null(links.Find("../private"));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MediaSource : IEntityMediaService
    {
        public Dictionary<string, List<EntityMediaAttachment>> Items = [];
        public bool Missing;
        public bool Unavailable;
        public EntityMediaAudience? LastOpenedAudience;
        public Action? OnOpen;
        public MemoryStream? LastStream;
        public void Add(string owner, string id, string role, string alt, bool player = true)
        {
            if (!Items.TryGetValue(owner, out var items)) Items[owner] = items = [];
            items.Add(new(id, role, player ? [EntityMediaAudience.Player, EntityMediaAudience.GameMaster] : [EntityMediaAudience.GameMaster],
                new string('a', 64), "image/png", 1, 1, alt, alt, items.Count, new("original", "Private credit", "private/source", "2026-09-05", 1)));
        }
        public Task<EntityMediaDiscoveryResult> DiscoverAsync(ApplicationIdentifier applicationId, string stateSpaceId, string entityId,
            EntityMediaAudience audience, bool diagnostics = false, CancellationToken cancellationToken = default) =>
            Unavailable ? throw new IOException("Private storage location must not escape.") : Task.FromResult(new EntityMediaDiscoveryResult(applicationId.Value, stateSpaceId, entityId, "resolution",
                Missing ? [] : (Items.GetValueOrDefault(entityId) ?? []).Where(value => value.Visibility.Contains(audience)).ToArray(), []));
        public async Task<EntityMediaReadResult?> OpenReadAsync(ApplicationIdentifier applicationId, string stateSpaceId, string entityId,
            string mediaId, EntityMediaAudience audience, CancellationToken cancellationToken = default)
        {
            LastOpenedAudience = audience;
            var attachment = (await DiscoverAsync(applicationId, stateSpaceId, entityId, audience)).Attachments.SingleOrDefault(value => value.MediaId == mediaId);
            if (attachment is null) return null;
            OnOpen?.Invoke();
            LastStream = new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10]);
            return new(attachment, new(new(attachment.Sha256, "image/png", 8, DateTimeOffset.UtcNow), LastStream));
        }
    }

    private sealed class Policy(KnowledgeCoreTests.KnowledgeFixture game) : IAuthorizedKnowledgeAudiencePolicy
    {
        public bool IsDm;
        public string Revision = "policy";
        public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeAudienceResolution(new("principal", game.Campaign,
                IsDm ? KnowledgeAudienceRole.GameMaster : KnowledgeAudienceRole.Actor, IsDm ? null : game.Actor, Revision)));
    }
    private sealed class Binding(KnowledgeApplicationBinding value) : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) => Task.FromResult<KnowledgeApplicationBinding?>(value);
    }
    private sealed class Fixture : IDisposable, IApplicationReadModelService
    {
        public KnowledgeCoreTests.KnowledgeFixture Game { get; } = new();
        public string ItemId => "item.first";
        public string Definition => "definition.shared";
        public Policy Policy { get; }
        public MediaSource Media { get; } = new();
        public ReadModelMediaLinkStore Links { get; } = new();
        private readonly Dictionary<string, string> types = [];
        private readonly MechanicRequirements requirements = MechanicRequirements.Parse(Mechanic.Requirements);
        private readonly ApplicationMechanicProjectionMapping mapping;
        private readonly ApplicationAuthorizedProjectionResolver resolver;
        private Fixture()
        {
            Policy = new(Game);
            var components = new Dictionary<string, EcsComponentReference>();
            foreach (var id in requirements.AllComponentIds())
            {
                var type = "fixture-knowledge.detail-" + types.Count;
                types[id] = type;
                components[id] = Game.DefineComponent(type);
            }
            mapping = new(components, new Dictionary<string, string>());
            resolver = new(Game.Db, Policy, new Binding(Game.Binding), new ApplicationKnowledgeActorParticipationVerifier(Game.Entities, Game.Edges), Game.Source, Game.States, Media, Links);
        }
        public static object Weight(int numerator) => new { dimension = "mass", value = new { numerator, denominator = 2 }, unit = Ref("dnd2024.vocabulary.mass-unit.kilogram") };
        public static async Task<Fixture> Create(int weightNumerator = 1)
        {
            var value = new Fixture();
            await value.Game.AddCoreAsync();
            await value.Game.AddParticipationAsync();
            await value.Game.AddEntityAsync(value.Definition, "Shared definition");
            await value.Component(value.Definition, "dnd2024.core.version", new { revision = 1, status = "active" });
            await value.Component(value.Definition, "dnd2024.item.physical", new { weight = Weight(weightNumerator) });
            await value.Item(value.ItemId, "Personal keepsake", 6);
            return value;
        }
        public async Task Item(string id, string name, int quantity, string? definition = null)
        {
            await Game.AddEntityAsync(id, name);
            await Component(id, "dnd2024.core.definition-link", new { definition = Ref(definition ?? Definition) });
            await Component(id, "dnd2024.item.quantity", new { current = quantity });
            await Game.Edges.MoveContainmentAsync(Game.Campaign, id, Game.Actor, "pack", 0);
        }
        public async Task Component(string entity, string id, object data)
        {
            var schema = Schemas.Compile(File.ReadAllText(Path.Combine(App, "components", id + ".schema.json")));
            Assert.True(schema.IsAccepted, Json(schema.Diagnostics));
            Assert.Equal(SchemaValueStatus.Valid, Schemas.Validate(schema.NormalizedSchema, Json(data)).Status);
            await Game.ComponentAsync(entity, types[id], Json(data));
        }
        public Task Know(string id, string state = "known") => Game.RelateAsync(Game.Actor, id, Game.Binding.ExplicitStateRelationshipKind, Json(new { stance = state }));
        public Task<ProjectionResult> Resolve(string? item = null, bool dm = false)
        {
            if (dm) Policy.IsDm = true;
            return resolver.ResolveAsync(new(Game.Campaign, ApplicationIdentifier.Parse(Game.ApplicationId), MechanicId, new string('A', 64), mapping,
                new Dictionary<string, string> { ["subject"] = Game.Actor, ["campaign"] = Game.Campaign },
                Json(new { itemId = item ?? ItemId }), 0, Audience: new(dm ? "dm" : "player"), ReadModelQueryId: QueryId), requirements);
        }
        public async Task<JsonDocument> Details(string? item = null, bool dm = false)
        {
            var before = Game.Db.ChangeTracker.Entries().Count();
            var projection = await Resolve(item, dm);
            Assert.True(projection.Ok, string.Join(';', projection.Problems));
            var data = await Run(projection.Projection!);
            Assert.Equal(before, Game.Db.ChangeTracker.Entries().Count());
            Assert.DoesNotContain(Game.Db.ChangeTracker.Entries(), entry => entry.State is Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified or Microsoft.EntityFrameworkCore.EntityState.Deleted);
            return data;
        }
        public void Dispose() => Game.Dispose();
        public async Task<ApplicationReadModelResult> ReadAsync(ApplicationReadModelRequest request, CancellationToken cancellationToken = default)
        {
            using var input = JsonDocument.Parse(request.InputJson);
            using var data = await Details(input.RootElement.GetProperty("itemId").GetString(), request.Audience?.Perspective == "dm");
            return new(Game.ApplicationId, Game.Campaign, QueryId, "state", "resolution", Query.OutputSchemaHash, "result", "source", data.RootElement.GetRawText());
        }
        public async Task<int> OpenImage(string url, string? actor = null, string query = "")
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = new QueryString(query);
            context.Response.Body = new MemoryStream();
            using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
            context.RequestServices = services;
            var seat = new Seats(new(true, "principal", Game.ApplicationId, Game.Campaign, Policy.IsDm ? null : actor ?? Game.Actor,
                Policy.IsDm ? KnowledgeAudienceRole.GameMaster : KnowledgeAudienceRole.Actor, ["fixture-source"]));
            var result = await ReadModelMediaWebEndpoint.ReadAsync(url.Split('/')[3], context, seat, Links, this, Policy, Media, CancellationToken.None);
            await result.ExecuteAsync(context);
            Assert.Equal("private, no-store", context.Response.Headers.CacheControl.ToString());
            Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
            return context.Response.StatusCode;
        }
    }
    private sealed class Seats(LocalKnowledgeSeatSnapshot seat) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => seat with { SourceIds = seat.SourceIds?.ToArray() };
    }
}
