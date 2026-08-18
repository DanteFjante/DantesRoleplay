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
        Assert.Contains("get_entities", result.NextSteps.Single());

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
        Assert.Single(await db.Operations.Where(o => o.Tool == "run_action").ToListAsync());
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
        Assert.Equal("get_procedure(id: \"procedure.mechanic.projection\")", result.Error?.Fix);
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
        Assert.Equal("get_procedure(id: \"procedure.world.change\")", result.Error?.Fix);
        Assert.Contains("Unknown entity", result.Error?.Why);

        var after = await world.GetEntityAsync("orban");
        Assert.NotNull(after);
        Assert.Contains("\"vigour\":10", after.Components.Single().Data);

        var operation = db.Operations.Single(o => o.Id == result.OperationId);
        Assert.False(operation.Success);
        Assert.Equal(7, operation.Seed);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_before_mechanic_selection()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        var result = await CreateRunner(db, world, store).RunAsync(new ActionRequest
        {
            Intent = "anything",
            Input = "{not json"
        });

        Assert.False(result.Ok);
        Assert.Equal("INVALID_INPUT", result.Error?.Code);
        Assert.Equal("run_action(intent: \"same intent\", roleEntityIds: {}, input: \"{}\")", result.Error?.Fix);
        Assert.Empty(result.Candidates);
        Assert.Single(await db.Operations.Where(o => o.Tool == "run_action").ToListAsync());
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
        Assert.Equal("history(tool: \"run_action\", failuresOnly: true)", result.Error?.Fix);
        Assert.Single(await db.Operations.Where(o => o.Tool == "run_action").ToListAsync());
    }

    private static ActionRunner CreateRunner(
        DantesRoleplayDbContext db,
        WorldStore world,
        MechanicStore store) =>
        new(
            db,
            store,
            new ProjectionResolver(db),
            new JintMechanicEngine(),
            new EffectApplier(db, world),
            new OperationLog(db));
}
