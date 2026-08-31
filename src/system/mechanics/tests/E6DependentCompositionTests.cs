using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;

namespace DantesRoleplay.Tests;

/// <summary>Slice 1 proof for closed, typed data handoff between sibling mechanics.</summary>
public sealed class E6DependentCompositionTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_later_dependency_receives_a_producers_object_data_in_topological_order()
    {
        await using var db = _fixture.CreateContext();
        var mechanics = new MechanicStore(db);
        var world = new WorldStore(db);

        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.producer",
            "return { data: { context: 'fixture' }, effects: [] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.consumer",
            "return { data: { received: ctx.input.context }, effects: [] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.parent",
            "return { narration: JSON.parse(ctx.children.consumer[0].output.data).received, effects: [] };",
            """
            {
              "children": {
                "consumer": {
                  "mechanicId": "mechanic.test.e6.consumer",
                  "roleBindings": {},
                  "inheritInput": false,
                  "inputFromChildData": { "resultKey": "producer" }
                },
                "producer": {
                  "mechanicId": "mechanic.test.e6.producer",
                  "roleBindings": {},
                  "inheritInput": false,
                  "input": "{}"
                }
              }
            }
            """));

        var result = Runner(db, world, mechanics).RunAsync(new ActionRequest
        {
            Intent = "run mechanic.test.e6.parent",
            Seed = 27
        });

        var completed = await result;
        Assert.True(completed.Ok, completed.Error?.Why);
        Assert.Equal("fixture", completed.Output.Narration);
        Assert.Equal("mechanic.test.e6.producer", completed.Projection!.Children["producer"].Single().MechanicId);
        Assert.Equal("mechanic.test.e6.consumer", completed.Projection.Children["consumer"].Single().MechanicId);
        Assert.NotEqual(completed.Projection.Children["producer"].Single().Seed,
            completed.Projection.Children["consumer"].Single().Seed);
    }

    [Fact]
    public async Task A_scalar_producer_data_aborts_before_the_consumer_or_parent_can_run()
    {
        await using var db = _fixture.CreateContext();
        var mechanics = new MechanicStore(db);
        var world = new WorldStore(db);

        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.scalar-producer",
            "return { data: 'not-an-object', effects: [] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.scalar-consumer",
            "throw new Error('consumer should not run');",
            """
            { "children": {
              "consumer": {
                "mechanicId": "mechanic.test.e6.scalar-consumer",
                "roleBindings": {}, "inheritInput": false,
                "inputFromChildData": { "resultKey": "producer" }
              },
              "producer": {
                "mechanicId": "mechanic.test.e6.scalar-producer",
                "roleBindings": {}, "inheritInput": false, "input": "{}"
              }
            }}
            """));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.scalar-parent",
            "throw new Error('parent should not run');",
            """
            { "children": {
              "consumer": {
                "mechanicId": "mechanic.test.e6.scalar-consumer",
                "roleBindings": {}, "inheritInput": false,
                "inputFromChildData": { "resultKey": "producer" }
              },
              "producer": {
                "mechanicId": "mechanic.test.e6.scalar-producer",
                "roleBindings": {}, "inheritInput": false, "input": "{}"
              }
            }}
            """));

        // The second setup mechanic verifies that invalid dependent data fails only when the
        // parent actually composes the sibling pair.
        var result = await Runner(db, world, mechanics).RunAsync(new ActionRequest
        {
            Intent = "run mechanic.test.e6.scalar-parent",
            Seed = 28
        });

        Assert.False(result.Ok);
        Assert.Equal("COMPOSITION_FAILED", result.Error?.Code);
        Assert.Contains("CHILD_INPUT_FROM_DATA_FAILED", result.Error?.Why);
        Assert.Empty(result.Output.Effects);
    }

    [Fact]
    public async Task An_omitted_producer_data_aborts_before_the_consumer_or_parent_can_run()
    {
        await using var db = _fixture.CreateContext();
        var mechanics = new MechanicStore(db);
        var world = new WorldStore(db);

        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.no-data-producer",
            "return { effects: [] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.no-data-consumer",
            "throw new Error('consumer should not run');"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.no-data-parent",
            "throw new Error('parent should not run');",
            """
            { "children": {
              "consumer": {
                "mechanicId": "mechanic.test.e6.no-data-consumer",
                "roleBindings": {}, "inheritInput": false,
                "inputFromChildData": { "resultKey": "producer" }
              },
              "producer": {
                "mechanicId": "mechanic.test.e6.no-data-producer",
                "roleBindings": {}, "inheritInput": false, "input": "{}"
              }
            }}
            """));

        var result = await Runner(db, world, mechanics).RunAsync(new ActionRequest
        {
            Intent = "run mechanic.test.e6.no-data-parent",
            Seed = 29
        });

        Assert.False(result.Ok);
        Assert.Equal("COMPOSITION_FAILED", result.Error?.Code);
        Assert.Contains("did not return data", result.Error?.Why);
    }

    [Fact]
    public async Task Child_proposals_join_the_root_action_in_topological_and_recursive_order()
    {
        await using var db = _fixture.CreateContext();
        var mechanics = new MechanicStore(db);
        var world = new WorldStore(db);

        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.alpha",
            "return { effects: [{ type: 'entity.create', entityId: 'e6-alpha', name: 'Alpha' }] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.beta",
            "return { effects: [{ type: 'entity.create', entityId: 'e6-beta', name: 'Beta' }] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.leaf",
            "return { effects: [{ type: 'entity.create', entityId: 'e6-leaf', name: 'Leaf' }] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.nested",
            "return { effects: [{ type: 'entity.create', entityId: 'e6-nested', name: 'Nested' }] };",
            """{ "children": { "leaf": { "mechanicId": "mechanic.test.e6.leaf", "roleBindings": {} } } }"""));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.aggregate-parent",
            "return { effects: [{ type: 'entity.create', entityId: 'e6-parent', name: 'Parent' }] };",
            """
            { "children": {
              "nested": { "mechanicId": "mechanic.test.e6.nested", "roleBindings": {} },
              "beta": { "mechanicId": "mechanic.test.e6.beta", "roleBindings": {} },
              "alpha": { "mechanicId": "mechanic.test.e6.alpha", "roleBindings": {} }
            }}
            """));

        var result = await Runner(db, world, mechanics).RunAsync(new ActionRequest
        {
            Intent = "run mechanic.test.e6.aggregate-parent",
            Seed = 30
        });

        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal(5, result.AppliedCount);
        Assert.Equal(
            ["e6-alpha", "e6-beta", "e6-leaf", "e6-nested", "e6-parent"],
            result.Output.Effects.Select(effect => effect.EntityId));
        foreach (var effect in result.Output.Effects)
            Assert.NotNull(await world.GetEntityAsync(effect.EntityId));
    }

    [Fact]
    public async Task An_invalid_child_proposal_rolls_back_the_entire_root_action()
    {
        await using var db = _fixture.CreateContext();
        var mechanics = new MechanicStore(db);
        var world = new WorldStore(db);

        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.invalid-child",
            "return { effects: [{ type: 'containment.move', entityId: 'missing-entity', toEntityId: 'missing-place' }] };"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.test.e6.atomic-parent",
            "return { effects: [{ type: 'entity.create', entityId: 'e6-should-not-exist', name: 'Nope' }] };",
            """{ "children": { "child": { "mechanicId": "mechanic.test.e6.invalid-child", "roleBindings": {} } } }"""));

        var result = await Runner(db, world, mechanics).RunAsync(new ActionRequest
        {
            Intent = "run mechanic.test.e6.atomic-parent",
            Seed = 31
        });

        Assert.False(result.Ok);
        Assert.Equal("INVALID_EFFECTS", result.Error?.Code);
        Assert.Null(await world.GetEntityAsync("e6-should-not-exist"));
    }

    [Theory]
    [InlineData("""{"children":{"consumer":{"mechanicId":"mechanic.test.child","roleBindings":{},"inheritInput":true,"inputFromChildData":{"resultKey":"producer"}},"producer":{"mechanicId":"mechanic.test.child","roleBindings":{}}}}""", "cannot combine")]
    [InlineData("""{"children":{"consumer":{"mechanicId":"mechanic.test.child","roleBindings":{},"inheritInput":false,"inputFromChildData":{"resultKey":"missing"}}}}""", "unknown child")]
    [InlineData("""{"children":{"a":{"mechanicId":"mechanic.test.child","roleBindings":{},"inheritInput":false,"inputFromChildData":{"resultKey":"b"}},"b":{"mechanicId":"mechanic.test.child","roleBindings":{},"inheritInput":false,"inputFromChildData":{"resultKey":"a"}}}}""", "acyclic")]
    public void Invalid_dependent_declarations_are_rejected_before_execution(string requirementsJson, string expectedProblem)
    {
        var problems = MechanicRequirements.Parse(requirementsJson).CompositionProblems();

        Assert.Contains(problems, problem => problem.Contains(expectedProblem, StringComparison.OrdinalIgnoreCase));
    }

    private static WriteMechanicRequest Mechanic(string id, string source, string requirements = "{}") => new()
    {
        Id = id,
        Category = "test.e6",
        Name = id,
        Description = "E6 generic fixture.",
        Matches = id,
        Requirements = requirements,
        Source = source,
        Status = MechanicStatus.Active
    };

    private static ActionRunner Runner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics)
    {
        var projections = new ProjectionResolver(db);
        var engine = new JintMechanicEngine();
        return new ActionRunner(
            db,
            mechanics,
            projections,
            engine,
            new EffectApplier(db, world),
            new OperationLog(db),
            new MechanicComposer(mechanics, projections, engine));
    }
}
