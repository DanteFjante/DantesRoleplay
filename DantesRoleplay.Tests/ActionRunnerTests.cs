using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class ActionRunnerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Runs_the_first_active_match_applies_the_exact_effects_and_records_replay_data()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);
        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", "{\"vigour\":10}");

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.check.vigour",
            Category = "check",
            Name = "Spend vigour",
            Matches = "push\nspend vigour",
            Requirements = "{\"roles\":{\"subject\":{\"components\":[\"stats\"]}}}",
            Status = MechanicStatus.Active,
            Source = """
                var stats = JSON.parse(ctx.roles.subject.components.stats);
                return {
                  narration: 'Vigour spent.',
                  data: { remaining: stats.vigour - ctx.input.cost },
                  effects: [{
                    type: 'component.merge',
                    entityId: ctx.roles.subject.id,
                    definitionId: 'stats',
                    data: JSON.stringify({ vigour: stats.vigour - ctx.input.cost })
                  }]
                };
                """
        });

        var runner = CreateRunner(db, world, store);
        var result = await runner.RunAsync(new ActionRequest
        {
            Intent = "push through the gate",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = "orban" },
            Input = "{\"cost\":4}",
            Seed = 42,
            ProceduresUsed = ["procedure.mechanic.run", "procedure.mechanic.projection"]
        });

        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("mechanic.check.vigour", result.Mechanic?.Id);
        Assert.Equal(42, result.Seed);
        Assert.Equal(1, result.AppliedCount);
        Assert.Contains("orban", result.AffectedEntityIds);
        Assert.Contains("query(kind: \"entities\"", result.NextSteps.Single());

        var after = await world.GetEntityAsync("orban");
        Assert.NotNull(after);
        Assert.Contains("\"vigour\":6", after.Components.Single().Data);

        var operation = db.Operations.Single(o => o.Id == result.OperationId);
        Assert.Equal("mechanic.check.vigour", operation.MechanicId);
        Assert.Equal(1, operation.MechanicVersion);
        Assert.Equal(42, operation.Seed);
        Assert.Contains("orban", operation.ProjectionJson);
    }

    [Fact]
    public async Task A_draft_mechanic_is_not_executable()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.draft.test",
            Category = "test",
            Name = "Draft test",
            Matches = "draft action",
            Status = MechanicStatus.Draft,
            Source = "return { effects: [] };"
        });

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "draft action"
        });

        Assert.False(result.Ok);
        Assert.Equal("NO_ACTIVE_MECHANIC", result.Error?.Code);
        Assert.Equal("orient()", result.Error?.Fix);
        Assert.Single(await db.Operations.Where(o => o.Tool == "commit").ToListAsync());
    }

    [Fact]
    public async Task Projection_failure_records_failure_and_does_not_run_the_mechanic()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.requires.subject",
            Category = "test",
            Name = "Requires subject",
            Matches = "needs subject",
            Requirements = "{\"roles\":{\"subject\":{\"components\":[]}}}",
            Status = MechanicStatus.Active,
            Source = "throw new Error('must not run');"
        });

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "needs subject"
        });

        Assert.False(result.Ok);
        Assert.Equal("PROJECTION_FAILED", result.Error?.Code);
        // The recovery has to be the call that gets the caller unstuck, not a kernel contract
        // whose own first line tells an action caller to read a different one.
        Assert.StartsWith("query(kind: \"mechanics\", id: ", result.Error?.Fix);
        Assert.Contains("roleEntityIds", result.Error?.Fix);
        Assert.Contains("MISSING_REQUIRED_ROLE", result.Error?.Why);
        Assert.Empty(await world.FindEntitiesAsync());
    }

    [Fact]
    public async Task Invalid_effects_are_dry_run_rejected_and_leave_the_world_unchanged()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);
        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", "{\"vigour\":10}");

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.bad.effects",
            Category = "test",
            Name = "Bad effects",
            Matches = "bad effects",
            Requirements = "{\"roles\":{\"subject\":{\"components\":[\"stats\"]}}}",
            Status = MechanicStatus.Active,
            Source = """
                return { effects: [
                  { type: 'component.merge', entityId: 'orban', definitionId: 'stats', data: '{"vigour":9}' },
                  { type: 'containment.move', entityId: 'orban', toEntityId: 'missing-place' }
                ] };
                """
        });

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "bad effects",
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = "orban" },
            Seed = 7
        });

        Assert.False(result.Ok);
        Assert.Equal("INVALID_EFFECTS", result.Error?.Code);
        Assert.Equal("query(kind: \"procedures\", id: \"procedure.world.change\")", result.Error?.Fix);
        Assert.Contains("Unknown entity", result.Error?.Why);

        var after = await world.GetEntityAsync("orban");
        Assert.NotNull(after);
        Assert.Contains("\"vigour\":10", after.Components.Single().Data);

        var operation = db.Operations.Single(o => o.Id == result.OperationId);
        Assert.False(operation.Success);
        Assert.Equal(7, operation.Seed);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("4")]
    [InlineData("true")]
    public async Task Invalid_input_is_rejected_before_mechanic_selection(string input)
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.input.must.not.run",
            Category = "test",
            Name = "Input must not run",
            Matches = "anything",
            Status = MechanicStatus.Active,
            Source = "throw new Error('must not run');"
        });

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "anything",
            Input = input
        });

        Assert.False(result.Ok);
        Assert.Equal("INVALID_INPUT", result.Error?.Code);
        Assert.Contains("JSON object", result.Error?.Why);
        Assert.Equal(
            "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"same intent\\\",\\\"roleEntityIds\\\":{},\\\"input\\\":\\\"{}\\\"}\")",
            result.Error?.Fix);
        Assert.Empty(result.Candidates);
        Assert.Single(await db.Operations.Where(o => o.Tool == "commit").ToListAsync());
    }

    [Fact]
    public async Task An_omitted_or_explicit_empty_object_input_still_reaches_a_mechanic()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.input.empty-object",
            Category = "test",
            Name = "Empty object input",
            Matches = "empty input",
            Status = MechanicStatus.Active,
            Source = "return { narration: String(Object.keys(ctx.input).length), effects: [] };"
        });

        var runner = CreateRunner(db, world, store);
        var omitted = await runner.RunAsync(new ActionRequest { Intent = "empty input", Seed = 12 });
        var explicitObject = await runner.RunAsync(new ActionRequest
        {
            Intent = "empty input",
            Input = "{}",
            Seed = 12
        });

        Assert.True(omitted.Ok, omitted.Error?.Why);
        Assert.True(explicitObject.Ok, explicitObject.Error?.Why);
        Assert.Equal("{}", omitted.Projection!.Input);
        Assert.Equal("{}", explicitObject.Projection!.Input);
        Assert.Equal("0", omitted.Output!.Narration);
        Assert.Equal("0", explicitObject.Output!.Narration);
    }

    [Fact]
    public async Task Cancellation_before_transaction_start_is_still_audited()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await CreateRunner(db, world, store).RunAsync(
            new ActionRequest { Intent = "cancelled action" },
            cancellation.Token);

        Assert.False(result.Ok);
        Assert.Equal("CANCELLED", result.Error?.Code);
        Assert.Equal("query(kind: \"history\", failuresOnly: true)", result.Error?.Fix);
        Assert.Single(await db.Operations.Where(o => o.Tool == "commit").ToListAsync());
    }

    [Fact]
    public async Task Declared_children_run_before_the_parent_and_are_frozen_and_audited()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);
        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Encounter", "encounter");
        await world.CreateEntityAsync("Borin", "borin");
        await world.CreateEntityAsync("Alia", "alia");
        await world.SetComponentAsync("alia", "stats", "{\"score\":3}");
        await world.SetComponentAsync("borin", "stats", "{\"score\":2}");
        await world.MoveAsync("alia", "encounter");
        await world.MoveAsync("borin", "encounter");

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.child.roll",
            Category = "test",
            Name = "Child roll",
            Matches = "child roll",
            Requirements = """{"roles":{"subject":{"components":["stats"]}}}""",
            Status = MechanicStatus.Active,
            Source = """
                var stats = JSON.parse(ctx.roles.subject.components.stats);
                return {
                  narration: ctx.roles.subject.name + ':' + (stats.score + ctx.input.bonus),
                  data: { subject: ctx.roles.subject.id },
                  effects: []
                };
                """
        });

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.parent.roster",
            Category = "test",
            Name = "Parent roster",
            Matches = "run roster",
            Requirements = """
                {
                  "roles":{"encounter":{"components":[],"includeContents":true}},
                  "children":{
                    "rolls":{
                      "mechanicId":"mechanic.test.child.roll",
                      "forEachContentsOf":"encounter",
                      "roleBindings":{"subject":"$item"},
                      "inheritInput":false,
                      "inputFromParentProperty":"initiativeInputs",
                      "inputForEachItem":true
                    }
                  }
                }
                """,
            Status = MechanicStatus.Active,
            Source = """
                var outcomes = ctx.children.rolls.map(function (child) { return child.output.narration; });
                return {
                  narration: outcomes.join(',') + '|frozen=' +
                    Object.isFrozen(ctx.children) + ':' + Object.isFrozen(ctx.children.rolls[0].output),
                  effects: []
                };
                """
        });

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "run roster",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = "encounter" },
            Input = "{\"initiativeInputs\":{\"alia\":{\"bonus\":4},\"borin\":{\"bonus\":4}},\"parentOnly\":true}",
            Seed = 99
        });

        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("Alia:7,Borin:6|frozen=true:true", result.Output.Narration);
        var children = result.Projection!.Children["rolls"];
        Assert.Equal(2, children.Count);
        Assert.All(children, child => Assert.Equal("mechanic.test.child.roll", child.MechanicId));
        Assert.Equal("alia", children[0].RoleEntityIds["subject"]);
        Assert.Equal("borin", children[1].RoleEntityIds["subject"]);
        Assert.NotEqual(99, children[0].Seed);
        Assert.NotEqual(children[0].Seed, children[1].Seed);

        var operation = db.Operations.Single(o => o.Id == result.OperationId);
        Assert.Contains("mechanic.test.child.roll", operation.ProjectionJson);
        Assert.Contains("Children", operation.ProjectionJson);
    }

    [Fact]
    public async Task Declared_children_can_compose_recursively_without_exposing_a_host_callback()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.grandchild",
            Category = "test",
            Name = "Grandchild",
            Matches = "grandchild",
            Status = MechanicStatus.Active,
            Source = "return { narration: 'leaf', effects: [] };"
        });

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.child.with-child",
            Category = "test",
            Name = "Child with child",
            Matches = "child with child",
            Requirements = """
                {"children":{"leaf":{"mechanicId":"mechanic.test.grandchild","roleBindings":{}}}}
                """,
            Status = MechanicStatus.Active,
            Source = "return { narration: ctx.children.leaf[0].output.narration + '-child', effects: [] };"
        });

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.parent.with-child",
            Category = "test",
            Name = "Parent with child",
            Matches = "parent with child",
            Requirements = """
                {"children":{"child":{"mechanicId":"mechanic.test.child.with-child","roleBindings":{}}}}
                """,
            Status = MechanicStatus.Active,
            Source = "return { narration: ctx.children.child[0].output.narration + '-parent', effects: [] };"
        });

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "parent with child",
            Seed = 13
        });

        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("leaf-child-parent", result.Output.Narration);
        Assert.Equal("mechanic.test.child.with-child", result.Projection!.Children["child"].Single().MechanicId);
    }

    [Fact]
    public async Task A_failed_child_stops_the_parent_before_any_effect_is_applied()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.parent.missing-child",
            Category = "test",
            Name = "Parent missing child",
            Matches = "parent missing child",
            Requirements = """
                {"children":{"missing":{"mechanicId":"mechanic.test.not-active","roleBindings":{}}}}
                """,
            Status = MechanicStatus.Active,
            Source = "throw new Error('the parent must never run');"
        });

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "parent missing child",
            Seed = 3
        });

        Assert.False(result.Ok);
        Assert.Equal("COMPOSITION_FAILED", result.Error?.Code);
        Assert.Contains("CHILD_NOT_ACTIVE", result.Error?.Why);
        Assert.Empty(await world.FindEntitiesAsync());
    }

    private static ActionRunner CreateRunner(
        DantesRoleplayDbContext db,
        WorldStore world,
        MechanicStore store)
    {
        var projections = new ProjectionResolver(db);
        var engine = new JintMechanicEngine();
        return new ActionRunner(
            db,
            store,
            projections,
            engine,
            new EffectApplier(db, world),
            new OperationLog(db),
            new MechanicComposer(store, projections, engine));
    }
}
