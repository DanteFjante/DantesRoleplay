using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using Fixture = DantesRoleplay.Tests.ItemViewAudienceTests.Fixture;

namespace DantesRoleplay.Tests;

// IV06 reviews existing links with disposable records. This is not the IV07
// recipe projection or an eligibility evaluator, and registers no runtime IDs.
public sealed class ItemRecipeAssociationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Root
    {
        get
        {
            for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
                if (File.Exists(Path.Combine(path.FullName, "DantesRoleplay.slnx"))) return path.FullName;
            throw new DirectoryNotFoundException();
        }
    }
    private static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, path)))!;
    private static AuthorizedAssociationSource Associations => Read("docs/current/item-view-contracts/recipes.requirements.draft.json")
        ["proposedAuthorizedContext"]!["sourceSets"]!["associations"]!.Deserialize<AuthorizedAssociationSource>(Json)!;
    private static string RecipeType => Associations.CandidateComponentId;
    private static JsonObject Link(string definition) => new() { ["definition"] = new JsonObject { ["entityId"] = definition }, ["quantity"] = 1 };
    private static JsonObject Recipe(string[] outputs, string[] materials) => new()
    {
        ["outputs"] = new JsonArray(outputs.Select(id => (JsonNode)Link(id)).ToArray()),
        ["materialRequirements"] = new JsonArray(materials.Select(id => (JsonNode)Link(id)).ToArray()),
        ["workDuration"] = new JsonObject { ["kind"] = "special" },
        ["toolRequirement"] = JsonNode.Parse("""{"operator":"predicate","predicateId":"predicate.proficiency.tool","arguments":[{"entityId":"fixture.tool"}]}"""),
        ["crafterRequirement"] = JsonNode.Parse("""{"operator":"predicate","predicateId":"predicate.proficiency.crafter","arguments":[{"entityId":"fixture.crafter"}]}""")
    };
    private static void ValidRecipe(JsonNode recipe)
    {
        var validator = new BoundedJsonSchemaValidator();
        var schema = validator.Compile(Read("catalog/applications/dnd2024/components/dnd2024.crafting.recipe.schema.json").ToJsonString());
        Assert.True(schema.IsAccepted);
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(schema.NormalizedSchema, recipe.ToJsonString()).Status);
    }
    // Generic reference-path traversal for reviewing the declared field contract.
    // Production association semantics stay with the future catalog projection.
    private static IEnumerable<string> Targets(JsonNode node, string path)
    {
        IEnumerable<JsonNode> nodes = [node];
        foreach (var part in path.Split('.'))
            nodes = part.EndsWith("[]", StringComparison.Ordinal)
                ? nodes.SelectMany(value => value[part[..^2]]?.AsArray().OfType<JsonNode>() ?? [])
                : nodes.Select(value => value[part]).OfType<JsonNode>();
        return nodes.Select(value => value["entityId"]!.GetValue<string>());
    }
    private static string[] Linked(JsonNode recipe, string definition) => Associations.ReferencePaths
        .Where(pair => Targets(recipe, pair.Value).Contains(definition, StringComparer.Ordinal)).Select(pair => pair.Key).Order().ToArray();
    private static async Task AddRecipe(Fixture fixture, string id, JsonNode recipe, string? stance = "known")
    {
        ValidRecipe(recipe);
        await fixture.Game.AddEntityAsync(id, "Recipe label is not an association");
        await fixture.Game.ComponentAsync(id, fixture.AssociationType, recipe.ToJsonString());
        await fixture.Game.AddKnowledgeAsync("fact." + id, "Reviewed recipe instructions", id);
        if (stance is not null) await SetKnowledge(fixture, id, stance);
    }
    private static async Task SetKnowledge(Fixture fixture, string id, string stance)
    {
        var game = fixture.Game;
        var kind = game.Binding.ExplicitStateRelationshipKind;
        var current = (await game.Edges.ListRelationshipsAsync(game.Campaign)).SingleOrDefault(edge =>
            edge.FromEntityId == game.Actor && edge.ToEntityId == "fact." + id && edge.QualifiedKind == kind);
        await game.Edges.SetRelationshipAsync(game.Campaign, game.Actor, "fact." + id, kind,
            JsonSerializer.Serialize(new { stance }), current?.Revision ?? 0);
    }
    private static async Task<MechanicProjection> ReadProjection(Fixture fixture, string? observer = null, string? item = null)
    {
        var result = await fixture.Read(observer: observer, item: item);
        Assert.True(result.Ok, string.Join(';', result.Problems));
        return result.Projection!;
    }

    [Fact]
    public void Existing_schema_and_reviewed_paths_support_output_ingredient_both_and_no_association()
    {
        Assert.Equal("dnd2024.crafting.recipe", RecipeType);
        Assert.Equal("outputs[].definition", Associations.ReferencePaths["makes"]);
        Assert.Equal("materialRequirements[].definition", Associations.ReferencePaths["uses"]);
        Assert.True(Associations.RequireKnownCandidate);
        Assert.Equal("selected-definition", Associations.Target);
        foreach (var (outputs, materials, expected) in new (string[], string[], string[])[] {
            (["definition.shared"], ["definition.other"], ["makes"]),
            (["definition.other"], ["definition.shared"], ["uses"]),
            (["definition.shared", "definition.shared"], ["definition.shared"], ["makes", "uses"]),
            (["definition.other"], [], []) })
        {
            var recipe = Recipe(outputs, materials);
            ValidRecipe(recipe);
            Assert.Equal(expected, Linked(recipe, "definition.shared"));
            Assert.Empty(Linked(recipe, "item.first")); // Instance IDs do not replace definition links.
        }
        var binding = Read("catalog/applications/dnd2024/metadata/authorized-knowledge.json")["binding"]!;
        Assert.Equal("game.core.world.knowledge.about", binding["knowledgeAboutRelationshipKind"]!.GetValue<string>());
        Assert.Equal("game.core.world.knowledge.state", binding["explicitStateRelationshipKind"]!.GetValue<string>());
    }

    [Fact]
    public async Task Known_recipe_records_supply_separate_groups_and_unknown_candidates_are_not_hydrated()
    {
        using var fixture = await Fixture.Create(associations: Associations);
        await AddRecipe(fixture, "recipe.makes", Recipe([fixture.Definition], ["definition.other"]));
        await AddRecipe(fixture, "recipe.uses", Recipe(["definition.other"], [fixture.Definition]));
        await AddRecipe(fixture, "recipe.both", Recipe([fixture.Definition, fixture.Definition], [fixture.Definition]));
        await AddRecipe(fixture, "recipe.hidden", Recipe([fixture.Definition], []), stance: null);
        var projection = await ReadProjection(fixture);
        foreach (var (group, expected) in new[] {
            ("makes", new[] { "recipe.both", "recipe.makes" }), ("uses", new[] { "recipe.both", "recipe.uses" }) })
            Assert.Equal(expected, projection.References.Where(pair => pair.Value.Components.ContainsKey(RecipeType) &&
                Targets(JsonNode.Parse(pair.Value.Components[RecipeType])!, Associations.ReferencePaths[group]).Contains(fixture.Definition))
                .Select(pair => pair.Key).Order().ToArray());
        Assert.DoesNotContain("recipe.hidden", projection.References.Keys);
        Assert.DoesNotContain("fact.recipe.hidden", projection.References.Keys);
        Assert.DoesNotContain(fixture.Game.Db.ChangeTracker.Entries(), entry => entry.State is
            Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified or Microsoft.EntityFrameworkCore.EntityState.Deleted);
    }

    [Fact]
    public async Task Shared_definition_and_inventory_do_not_share_recipe_knowledge_between_observers()
    {
        using var fixture = await Fixture.Create(associations: Associations);
        await AddRecipe(fixture, "recipe.first-only", Recipe([fixture.Definition], []));
        await fixture.AddSecondObserver();
        fixture.Policy.Grant = fixture.Policy.Grant with { Role = KnowledgeAudienceRole.GameMaster, ActorId = null };
        Assert.Contains("recipe.first-only", (await ReadProjection(fixture)).References.Keys);
        Assert.DoesNotContain("recipe.first-only", (await ReadProjection(fixture, "actor.second", "item.second")).References.Keys);
    }

    [Theory]
    [InlineData("unknown", false)]
    [InlineData("familiar", false)]
    [InlineData("suspected", true)]
    [InlineData("believed", true)]
    public async Task Recipe_fact_state_controls_hydration_and_retains_uncertainty(string stance, bool visible)
    {
        using var fixture = await Fixture.Create(associations: Associations);
        await AddRecipe(fixture, "recipe.state", Recipe([fixture.Definition], []), stance);
        var projection = await ReadProjection(fixture);
        Assert.Equal(visible, projection.References.ContainsKey("recipe.state"));
        if (visible)
        {
            var record = JsonNode.Parse(projection.References["fact.recipe.state"].Components["authorized-knowledge"])!;
            Assert.Equal("recipe.state", record["subjectId"]!.GetValue<string>());
            Assert.Equal(stance, record["state"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Knowing_item_or_definition_does_not_teach_recipe_and_learning_or_forgetting_invalidates()
    {
        using var fixture = await Fixture.Create(associations: Associations);
        await AddRecipe(fixture, "recipe.learned", Recipe([fixture.Definition], [fixture.Definition]), stance: null);
        foreach (var subject in new[] { fixture.Item, fixture.Definition })
        {
            await fixture.Game.AddKnowledgeAsync("fact." + subject, "I know this item", subject);
            await fixture.Game.RelateAsync(fixture.Game.Actor, "fact." + subject, fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"known\"}");
        }
        var before = await ReadProjection(fixture);
        Assert.DoesNotContain("recipe.learned", before.References.Keys);
        await SetKnowledge(fixture, "recipe.learned", "known");
        var learned = await ReadProjection(fixture);
        Assert.Contains("recipe.learned", learned.References.Keys);
        Assert.NotEqual(before.AuthorizedSourceRevision, learned.AuthorizedSourceRevision);
        await SetKnowledge(fixture, "recipe.learned", "unknown");
        var forgotten = await ReadProjection(fixture);
        Assert.DoesNotContain("recipe.learned", forgotten.References.Keys);
        Assert.NotEqual(learned.AuthorizedSourceRevision, forgotten.AuthorizedSourceRevision);
    }

    [Fact]
    public async Task Known_incomplete_recipe_preserves_ingredient_link_without_inventing_an_output()
    {
        using var fixture = await Fixture.Create(associations: Associations);
        await AddRecipe(fixture, "recipe.incomplete", Recipe([], [fixture.Definition]));
        var projection = await ReadProjection(fixture);
        var recipe = JsonNode.Parse(projection.References["recipe.incomplete"].Components[RecipeType])!;
        Assert.Empty(recipe["outputs"]!.AsArray());
        Assert.Equal(["uses"], Linked(recipe, fixture.Definition));
        // IV07 must retain this known Uses association as definition-incomplete,
        // not claim craftability or invent a Makes entry from its display name.
    }

    [Theory]
    [InlineData("equipment.recipe.potion-of-healing")]
    [InlineData("equipment.recipe.nonmagical-item")]
    [InlineData("equipment.recipe.spell-scroll.level-1")]
    public void Incomplete_authored_recipes_do_not_provide_resolved_output_or_parameter_bindings(string file)
    {
        var recipe = Read($"catalog/applications/dnd2024/content/entities/equipment/crafting/{file}.json")["components"]![RecipeType]!;
        ValidRecipe(recipe);
        Assert.Empty(recipe["outputs"]!.AsArray());
        Assert.Empty(recipe["toolRequirement"]!["arguments"]!.AsArray());
        Assert.Empty(recipe["crafterRequirement"]!["arguments"]!.AsArray());
        Assert.Empty(Linked(recipe, "dnd2024.equipment.potion-of-healing"));
        // Schema validity does not establish semantic completeness. Current
        // downtime execution consumes a separately authored downtime.definition;
        // it supplies no recipe parameter-resolution contract for these records.
    }

    [Fact]
    public async Task Ambiguous_recipe_subject_fails_without_hydrating_either_candidate()
    {
        using var fixture = await Fixture.Create(associations: Associations);
        await AddRecipe(fixture, "recipe.ambiguous", Recipe([fixture.Definition], []));
        await fixture.Game.RelateAsync("fact.recipe.ambiguous", fixture.Definition, fixture.Game.Binding.KnowledgeAboutRelationshipKind, "{}");
        var result = await fixture.Read();
        Assert.Null(result.Projection);
        Assert.Equal(["READ_MODEL_UNAVAILABLE"], result.Problems);
    }

    [Fact]
    public async Task Changed_known_material_association_invalidates_its_source_revision()
    {
        using var fixture = await Fixture.Create(associations: Associations);
        await AddRecipe(fixture, "recipe.changed", Recipe(["definition.other"], [fixture.Definition]));
        var before = await ReadProjection(fixture);
        var current = (await fixture.Game.Entities.GetComponentAsync(fixture.Game.Campaign, "recipe.changed", fixture.AssociationType))!;
        await fixture.Game.Entities.SetComponentAsync(new(fixture.Game.Campaign, "recipe.changed", current.Type,
            Recipe(["definition.other"], []).ToJsonString(), current.Revision));
        var after = await ReadProjection(fixture);
        Assert.NotEqual(before.AuthorizedSourceRevision, after.AuthorizedSourceRevision);
        Assert.Empty(Linked(JsonNode.Parse(after.References["recipe.changed"].Components[RecipeType])!, fixture.Definition));
    }
}
