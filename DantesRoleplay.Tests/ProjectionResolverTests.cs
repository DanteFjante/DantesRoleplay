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

    // ---- the caller's own arguments --------------------------------------------------

    [Fact]
    public async Task Input_reaches_the_mechanic_and_malformed_input_becomes_an_empty_object()
    {
        await using var db = _fixture.CreateContext();
        await WorldAsync(db);
        var resolver = new ProjectionResolver(db);

        var good = await resolver.ResolveAsync(
            Requires("{}"), new Dictionary<string, string>(), """{"cost":4}""", seed: 7);

        // Not an error: the harness does JSON.parse on this, and failing here with a worse message
        // than the one the caller already got would help nobody.
        var bad = await resolver.ResolveAsync(
            Requires("{}"), new Dictionary<string, string>(), "not json at all");

        Assert.Contains("cost", good.Projection!.Input);
        Assert.Equal(7, good.Projection.Seed);
        Assert.Equal("{}", bad.Projection!.Input);
    }

    // ---- the whole chain -----------------------------------------------------------------

    [Fact]
    public async Task Resolve_then_run_then_apply_is_the_shape_run_action_will_have()
    {
        await using var db = _fixture.CreateContext();
        var world = await WorldAsync(db);
        var resolver = new ProjectionResolver(db);
        var applier = new EffectApplier(db, world);
        var engine = new RuleAccess.JintMechanicEngine();

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
