using DantesRoleplay.DataAccess;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

public sealed class ProjectionResolverTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static async Task<WorldStore> WorldAsync(DantesRoleplayDbContext db)
    {
        var world = new WorldStore(db);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.DefineComponentAsync("marks", "Marks", "Lasting effects.");
        await world.DefineComponentAsync("secrets", "Secrets", "Referee-only notes.");

        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", """{"vigour":10}""");
        await world.SetComponentAsync("orban", "marks", """{"weary":true}""");
        await world.SetComponentAsync("orban", "secrets", """{"trueName":"Orbannon"}""");

        return world;
    }

    private static MechanicRequirements Requires(string json) => MechanicRequirements.Parse(json);

    // ---- the containment rule: only what was declared ------------------------------------

    [Fact]
    public async Task A_mechanic_receives_only_the_components_it_declared()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"]}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok, string.Join("; ", result.Problems));

        var subject = result.Projection!.Roles["subject"];

        Assert.Equal(["stats"], subject.Components.Keys);

        // Orban carries marks and secrets too. A rule that declared only stats cannot see them —
        // which is what makes the declaration an honest answer to "what does this rule touch?"
        // rather than a hopeful one.
        Assert.False(subject.Components.ContainsKey("marks"));
        Assert.False(subject.Components.ContainsKey("secrets"));
    }

    [Fact]
    public async Task Declaring_several_components_materialises_all_of_them()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats","marks"]}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok);
        Assert.Equal(2, result.Projection!.Roles["subject"].Components.Count);
        Assert.Contains("vigour", result.Projection.Roles["subject"].Components["stats"]);
    }

    // ---- roles -------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_required_role_says_what_it_is_for_and_how_to_supply_it()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""
                {"roles":{
                  "subject":{"components":["stats"]},
                  "other":{"components":["stats"],"description":"The one being acted upon."}
                }}
                """),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.False(result.Ok);
        Assert.Single(result.Problems);
        Assert.Contains("'other'", result.Problems[0]);
        Assert.Contains("The one being acted upon.", result.Problems[0]);
        Assert.Contains("<entityId>", result.Problems[0]);
    }

    [Fact]
    public async Task An_optional_role_that_is_absent_is_simply_absent()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""
                {"roles":{
                  "subject":{"components":["stats"]},
                  "witness":{"components":["stats"],"optional":true}
                }}
                """),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.True(result.Projection!.Roles.ContainsKey("subject"));

        // The mechanic checks for it with a plain `if (ctx.roles.witness)`. One rule instead of two.
        Assert.False(result.Projection.Roles.ContainsKey("witness"));
    }

    [Fact]
    public async Task Supplying_a_role_the_mechanic_does_not_have_is_reported_rather_than_ignored()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"]}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban", ["target"] = "orban" });

        Assert.False(result.Ok);

        // Usually means the wrong mechanic was chosen. Dropping it silently turns a findable
        // mistake into a puzzling result.
        Assert.Contains("does not have a role called 'target'", result.Problems[0]);
        Assert.Contains("subject", result.Problems[0]);
    }

    [Fact]
    public async Task An_entity_that_does_not_exist_names_the_role_that_asked_for_it()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"]}}}"""),
            new Dictionary<string, string> { ["subject"] = "nobody" });

        Assert.False(result.Ok);
        Assert.Contains("'subject'", result.Problems[0]);
        Assert.Contains("nobody", result.Problems[0]);
        Assert.Contains("get_entities", result.Problems[0]);
    }

    [Fact]
    public async Task A_deleted_entity_is_not_a_participant()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.DeleteEntityAsync("orban");

        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"]}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Every_fault_comes_back_at_once()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"]},"other":{"components":["stats"]}}}"""),
            new Dictionary<string, string> { ["stranger"] = "orban" });

        // An unknown role AND two unsupplied ones. Reporting the first only would cost three
        // round trips to learn three things the system already knew.
        Assert.Equal(3, result.Problems.Count);
    }

    // ---- containment ---------------------------------------------------------------------

    [Fact]
    public async Task Contents_are_materialised_only_when_the_mechanic_asked_for_them()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("Lantern", "lantern");
        await world.MoveAsync("lantern", "orban", "carried");

        var resolver = new ProjectionResolver(db);

        var without = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"]}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        var with = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"],"includeContents":true}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.Null(without.Projection!.Roles["subject"].Contains);

        Assert.NotNull(with.Projection!.Roles["subject"].Contains);
        Assert.Single(with.Projection.Roles["subject"].Contains!);
        Assert.Equal("carried", with.Projection.Roles["subject"].Contains![0].Slot);

        // Existing direct-content mechanics receive the exact old node shape. In particular,
        // they cannot mistake an omitted descendant component request for an empty component.
        var sandbox = await new JintMechanicEngine().RunAsync("""
            return {
              data: JSON.stringify({
                hasComponents: typeof ctx.roles.subject.contains[0].components !== 'undefined',
                hasNestedContents: typeof ctx.roles.subject.contains[0].contains !== 'undefined'
              })
            };
            """, with.Projection, ExecutionLimits.Default);

        Assert.True(sandbox.Ok, sandbox.Error);
        using var data = System.Text.Json.JsonDocument.Parse(sandbox.Output.Data);
        Assert.False(data.RootElement.GetProperty("hasComponents").GetBoolean());
        Assert.False(data.RootElement.GetProperty("hasNestedContents").GetBoolean());
    }

    [Fact]
    public async Task Direct_contents_keep_the_existing_identity_only_javascript_shape()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("Lantern", "lantern");
        await world.SetComponentAsync("lantern", "secrets", """{"trueName":"Dawn"}""");
        await world.MoveAsync("lantern", "orban", "carried");
        var resolver = new ProjectionResolver(db);

        var resolved = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":[],"includeContents":true}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(resolved.Ok, string.Join("; ", resolved.Problems));
        var run = await new JintMechanicEngine().RunAsync("""
            return {
              narration: 'direct contents',
              data: JSON.stringify({
                components: typeof ctx.roles.subject.contains[0].components,
                contains: typeof ctx.roles.subject.contains[0].contains
              })
            };
            """, resolved.Projection!, ExecutionLimits.Default);

        Assert.True(run.Ok, run.Error);
        using var data = System.Text.Json.JsonDocument.Parse(run.Output.Data);
        Assert.Equal("undefined", data.RootElement.GetProperty("components").GetString());
        Assert.Equal("undefined", data.RootElement.GetProperty("contains").GetString());
    }

    [Fact]
    public async Task Contents_can_opt_in_to_a_bounded_nested_component_projection()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("Backpack", "pack");
        await world.CreateEntityAsync("Pouch", "pouch");
        await world.CreateEntityAsync("Gem", "gem");
        await world.CreateEntityAsync("String", "string");
        await world.SetComponentAsync("pack", "marks", """{"container":true}""");
        await world.SetComponentAsync("pouch", "marks", """{"container":true}""");
        await world.SetComponentAsync("pouch", "secrets", """{"hidden":true}""");
        await world.SetComponentAsync("gem", "marks", """{"weight":1}""");
        await world.SetComponentAsync("gem", "secrets", """{"owner":"GM"}""");
        await world.MoveAsync("pack", "orban", "carried");
        await world.MoveAsync("pouch", "pack", "inside");
        await world.MoveAsync("gem", "pouch", "inside");
        await world.MoveAsync("string", "pack", "inside");
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":[],"includeContents":true,"contentsDepth":3,"contentComponentIds":["marks"]}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        var pack = Assert.Single(result.Projection!.Roles["subject"].Contains!);
        Assert.Equal("pack", pack.Id);
        Assert.Equal("true", System.Text.Json.JsonDocument.Parse(pack.Components!["marks"]).RootElement.GetProperty("container").GetRawText());
        var pouch = pack.Contains!.Single(item => item.Id == "pouch");
        Assert.Equal("pouch", pouch.Id);
        Assert.False(pouch.Components!.ContainsKey("secrets"));
        var gem = Assert.Single(pouch.Contains!);
        Assert.Equal("gem", gem.Id);
        Assert.False(gem.Components!.ContainsKey("secrets"));
        var stringItem = pack.Contains!.Single(item => item.Id == "string");
        Assert.NotNull(stringItem.Components);
        Assert.Empty(stringItem.Components!);
    }

    [Fact]
    public async Task Nested_content_projection_fails_closed_at_its_node_limit()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        for (var index = 0; index <= ProjectionLimits.MaxContainedNodes; index++)
        {
            var id = $"item-{index}";
            await world.CreateEntityAsync($"Item {index:D3}", id);
            await world.MoveAsync(id, "orban");
        }

        var resolver = new ProjectionResolver(db);
        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":[],"includeContents":true,"contentsDepth":1}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.False(result.Ok);
        Assert.Null(result.Projection);
        Assert.Contains(result.Problems, problem => problem.StartsWith("CONTAINMENT_PROJECTION_LIMIT:"));
    }

    [Fact]
    public async Task Where_an_entity_is_comes_for_free_because_a_rule_almost_always_needs_it()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("The cellar", "cellar");
        await world.MoveAsync("orban", "cellar");

        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":[]}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok);
        Assert.Equal("cellar", result.Projection!.Roles["subject"].ContainerId);
    }

    // ---- relationships -----------------------------------------------------------------

    [Fact]
    public async Task Relationships_are_materialised_only_for_roles_that_explicitly_request_them()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("Bridge", "bridge");
        await world.CreateEntityAsync("Road", "road");
        await world.RelateAsync("orban", "bridge", "z-link", """{"order":2}""");
        await world.RelateAsync("road", "orban", "a-link", """{"order":1}""");
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""
                {"roles":{
                  "withRelationships":{"components":["stats"],"includeRelationships":true},
                  "withoutRelationships":{"components":["stats"]}
                }}
                """),
            new Dictionary<string, string>
            {
                ["withRelationships"] = "orban",
                ["withoutRelationships"] = "orban"
            });

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        var withRelationships = result.Projection!.Roles["withRelationships"];
        var withoutRelationships = result.Projection.Roles["withoutRelationships"];
        Assert.Null(withoutRelationships.Relationships);
        Assert.Collection(withRelationships.Relationships!,
            first =>
            {
                Assert.Equal("a-link", first.Kind);
                Assert.Equal("road", first.FromEntityId);
                Assert.Equal("orban", first.ToEntityId);
                Assert.Equal("""{"order":1}""", first.Data);
            },
            second =>
            {
                Assert.Equal("z-link", second.Kind);
                Assert.Equal("orban", second.FromEntityId);
                Assert.Equal("bridge", second.ToEntityId);
                Assert.Equal("""{"order":2}""", second.Data);
            });

        var sandbox = await new JintMechanicEngine().RunAsync("""
            return {
              narration: 'relationship projection',
              data: JSON.stringify({
                optedInKinds: ctx.roles.withRelationships.relationships.map(edge => edge.kind),
                optOutIsUndefined: typeof ctx.roles.withoutRelationships.relationships === 'undefined'
              })
            };
            """, result.Projection, ExecutionLimits.Default);

        Assert.True(sandbox.Ok, sandbox.Error);
        using var data = System.Text.Json.JsonDocument.Parse(sandbox.Output.Data);
        Assert.Equal(["a-link", "z-link"], data.RootElement.GetProperty("optedInKinds").EnumerateArray().Select(item => item.GetString()));
        Assert.True(data.RootElement.GetProperty("optOutIsUndefined").GetBoolean());
    }

    [Fact]
    public async Task An_opted_in_role_with_no_relationships_receives_an_explicit_empty_list()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":["stats"],"includeRelationships":true}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.NotNull(result.Projection!.Roles["subject"].Relationships);
        Assert.Empty(result.Projection.Roles["subject"].Relationships!);
    }

    [Fact]
    public async Task Relationship_endpoint_components_are_bounded_by_kind_direction_and_allow_list()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("Bridge", "bridge");
        await world.SetComponentAsync("bridge", "marks", """{"stable":true}""");
        await world.SetComponentAsync("bridge", "secrets", """{"owner":"keeper"}""");
        await world.CreateEntityAsync("Road", "road");
        await world.SetComponentAsync("road", "marks", """{"stable":false}""");
        await world.RelateAsync("orban", "bridge", "knows", """{"strength":2}""");
        await world.RelateAsync("road", "orban", "knows", """{"strength":1}""");
        await world.RelateAsync("orban", "road", "avoids", "{}");

        var result = await new ProjectionResolver(db).ResolveAsync(
            Requires("""
                {"roles":{"subject":{"components":[],"includeRelationships":true,
                  "relationshipComponents":[{"kind":"knows","direction":"outgoing",
                    "targetComponentIds":["marks"]}]}}}
                """),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        var subject = result.Projection!.Roles["subject"];
        var related = Assert.Single(subject.Related!);
        Assert.Equal("bridge", related.Id);
        Assert.Equal("orban", related.FromEntityId);
        Assert.Equal("bridge", related.ToEntityId);
        Assert.Equal("knows", related.Kind);
        Assert.Equal(["marks"], related.Components.Keys);
        Assert.DoesNotContain("secrets", related.Components.Keys);
        Assert.Equal(3, subject.Relationships!.Count);
    }

    [Fact]
    public async Task Relationship_endpoint_declarations_fail_closed_when_state_is_incomplete()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("Bridge", "bridge");
        await world.RelateAsync("orban", "bridge", "knows");

        var result = await new ProjectionResolver(db).ResolveAsync(
            Requires("""
                {"roles":{"subject":{"components":[],"includeRelationships":true,
                  "relationshipComponents":[{"kind":"knows","direction":"outgoing",
                    "targetComponentIds":["marks"]}]}}}
                """),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.False(result.Ok);
        Assert.Null(result.Projection);
        Assert.Contains(result.Problems, problem => problem.StartsWith(
            "RELATIONSHIP_COMPONENT_TARGET_MISSING:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("false", "outgoing")]
    [InlineData("true", "sideways")]
    public void Relationship_endpoint_declarations_validate_the_projection_boundary(
        string includeRelationships,
        string direction)
    {
        var requirements = Requires("{\"roles\":{\"subject\":{\"components\":[],\"includeRelationships\":"
            + includeRelationships
            + ",\"relationshipComponents\":[{\"kind\":\"knows\",\"direction\":\""
            + direction + "\",\"targetComponentIds\":[\"marks\"]}]}}}");

        Assert.NotEmpty(requirements.ProjectionProblems());
    }

    [Fact]
    public void Component_references_may_originate_from_an_optional_root_component()
    {
        var requirements = Requires("""
            {"roles":{"subject":{"components":[],"optionalComponents":["defenses"],
              "componentReferences":[{"sourceComponentId":"defenses","field":"basis",
                "targetComponentIds":["defense-basis"]}]}}}
            """);

        Assert.Empty(requirements.ProjectionProblems());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Include_relationships_requires_a_boolean(string invalidValue)
    {
        var requirements = "{\"roles\":{\"subject\":{\"components\":[],\"includeRelationships\":" + invalidValue + "}}}";

        Assert.Throws<System.Text.Json.JsonException>(() => Requires(requirements));
    }

    [Fact]
    public async Task Relationships_with_a_deleted_opposite_endpoint_are_not_projected()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        await world.CreateEntityAsync("Bridge", "bridge");
        await world.RelateAsync("orban", "bridge", "crosses");
        await world.DeleteEntityAsync("bridge");
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("""{"roles":{"subject":{"components":[],"includeRelationships":true}}}"""),
            new Dictionary<string, string> { ["subject"] = "orban" });

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.Empty(result.Projection!.Roles["subject"].Relationships!);
    }

    // ---- the caller's own arguments --------------------------------------------------

    [Fact]
    public async Task A_valid_object_input_reaches_the_mechanic_unchanged()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        const string input = "{ \"cost\" : 4 }";
        var good = await resolver.ResolveAsync(
            Requires("{}"), new Dictionary<string, string>(), input, seed: 7);

        Assert.True(good.Ok, string.Join("; ", good.Problems));
        Assert.Equal(input, good.Projection!.Input);
        Assert.Equal(7, good.Projection.Seed);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("4")]
    [InlineData("true")]
    public async Task An_invalid_input_root_is_rejected_without_a_projection(string input)
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var result = await resolver.ResolveAsync(
            Requires("{}"), new Dictionary<string, string>(), input, seed: 7);

        Assert.False(result.Ok);
        Assert.Null(result.Projection);
        Assert.Single(result.Problems);
        Assert.StartsWith("INVALID_INPUT:", result.Problems[0]);
    }

    // ---- the whole chain -----------------------------------------------------------------

    [Fact]
    public async Task Resolve_then_run_then_apply_is_the_shape_run_action_will_have()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        var resolver = new ProjectionResolver(db);
        var applier = new EffectApplier(db, world);
        var engine = new JintMechanicEngine();

        var requirements = Requires("""{"roles":{"subject":{"components":["stats"]}}}""");

        var resolved = await resolver.ResolveAsync(
            requirements,
            new Dictionary<string, string> { ["subject"] = "orban" },
            """{"cost":3}""",
            seed: 99);

        Assert.True(resolved.Ok, string.Join("; ", resolved.Problems));

        var run = await engine.RunAsync("""
            var stats = JSON.parse(ctx.roles.subject.components.stats);
            return {
              narration: ctx.roles.subject.name + ' spends ' + ctx.input.cost + '.',
              effects: [{ type: 'component.merge', entityId: ctx.roles.subject.id,
                          definitionId: 'stats',
                          data: JSON.stringify({ vigour: stats.vigour - ctx.input.cost }) }]
            };
            """, resolved.Projection!, ExecutionLimits.Default);

        Assert.True(run.Ok, run.Error);
        Assert.Equal("Orban spends 3.", run.Output.Narration);

        var applied = await applier.ApplyAsync(run.Output.Effects);

        Assert.True(applied.Valid);

        var after = await world.GetEntityAsync("orban");
        Assert.Contains("\"vigour\":7", after!.Components.Single(c => c.DefinitionId == "stats").Data);
    }
}
