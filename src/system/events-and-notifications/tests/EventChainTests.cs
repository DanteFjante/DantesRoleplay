using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 5b: chains run to arbitrary depth, and the limits are what stop them.
///
/// A chain terminates only if the rules somebody wrote happen to terminate, which is not a property
/// anything can check in advance. Two rules reacting to each other are not a bug either author
/// would notice — each is reasonable alone — so these bounds are not a safety net over a rare case.
/// They are the only thing between a plausible pair of rules and a transaction that never ends.
/// </summary>
public sealed class EventChainTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ---- the budget itself ------------------------------------------------------------------

    [Fact]
    public void Each_bound_reports_its_own_cause()
    {
        Assert.Null(new ChainBudget().CheckDepth(ChainBudget.MaxDepth));
        Assert.Equal("EVENT_DEPTH_LIMIT", new ChainBudget().CheckDepth(ChainBudget.MaxDepth + 1));

        var events = new ChainBudget();
        Assert.Null(events.CountEvents(ChainBudget.MaxEvents));
        Assert.Equal("EVENT_COUNT_LIMIT", events.CountEvents(1));

        var executions = new ChainBudget();
        for (var i = 0; i < ChainBudget.MaxExecutions; i++)
        {
            Assert.Null(executions.CountExecution($"subscription.{i}", 8));
        }

        Assert.Equal("EXECUTION_COUNT_LIMIT", executions.CountExecution("subscription.one-more", 8));

        var perSubscription = new ChainBudget();
        Assert.Null(perSubscription.CountExecution("subscription.a", 2));
        Assert.Null(perSubscription.CountExecution("subscription.a", 2));
        Assert.Equal("SUBSCRIPTION_EXECUTION_LIMIT", perSubscription.CountExecution("subscription.a", 2));

        // A different subscription has its own allowance.
        Assert.Null(perSubscription.CountExecution("subscription.b", 2));
    }

    /// <summary>
    /// A limit of zero would mean "registered but never runs", which is what disabling a
    /// subscription is for. Reading it as unbounded keeps a misconfigured registration visible
    /// rather than silently dead.
    /// </summary>
    [Fact]
    public void A_per_chain_limit_of_zero_is_unbounded_not_dead()
    {
        var budget = new ChainBudget();

        for (var i = 0; i < 20; i++)
        {
            Assert.Null(budget.CountExecution("subscription.unbounded", 0));
        }
    }

    // ---- chains that run ---------------------------------------------------------------------

    /// <summary>
    /// Two rules answering each other, three links deep. Causation names the event each answers,
    /// depth climbs by one, and sequence keeps counting across the whole chain.
    /// </summary>
    [Fact]
    public async Task A_chain_runs_past_depth_one_and_records_how_it_got_there()
    {
        await using var db = await WorldAsync();

        // Stops itself at depth 3, so this proves a chain ENDING rather than being cut off.
        await SeedReactionAsync(db, "moves", "world.component.replaced", MovesEntity, maxPerChain: 8);
        await SeedReactionAsync(db, "marks", "world.containment.moved", SetsStatsUntilDepth(3), maxPerChain: 8);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = "spark", DefinitionId = "stats", Data = """{"n":1}""" }]);

        Assert.True(result.Applied, result.BlockReason);

        var events = await new EventLedger(db).FindAsync(correlationId: result.CorrelationId, limit: 200);

        // replaced(0) -> moved(1) -> replaced(2) -> moved(3), then the condition ends it.
        Assert.Equal(4, events.Count);
        Assert.Equal(Enumerable.Range(0, events.Count), events.Select(e => e.Sequence));
        Assert.Equal(Enumerable.Range(0, events.Count), events.Select(e => e.Depth));

        for (var i = 1; i < events.Count; i++)
        {
            Assert.Equal(events[i - 1].Id, events[i].CausationId);
        }

        Assert.Equal(string.Empty, events[0].CausationId);
    }

    // ---- chains that are stopped ---------------------------------------------------------------

    /// <summary>
    /// Two subscriptions alternating reach depth faster than either reaches its own allowance, so
    /// this is the arrangement that actually exercises the depth bound. One self-triggering
    /// subscription cannot: its per-chain limit caps at 8, which it hits first.
    /// </summary>
    [Fact]
    public async Task Depth_stops_two_rules_that_answer_each_other()
    {
        await using var db = await WorldAsync();
        // Neither rule stops itself, and each runs once per cycle — so depth climbs twice as fast
        // as either one's own allowance, and depth is what stops them.
        await SeedReactionAsync(db, "moves", "world.component.replaced", MovesEntity, maxPerChain: 8);
        await SeedReactionAsync(db, "marks", "world.containment.moved", SetsStats, maxPerChain: 8);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = "spark", DefinitionId = "stats", Data = """{"n":1}""" }]);

        Assert.False(result.Applied);
        Assert.Equal("EVENT_DEPTH_LIMIT", result.BlockCode);

        await AssertNothingRemainsAsync(db);
    }

    [Fact]
    public async Task A_subscription_that_triggers_itself_stops_at_its_own_limit()
    {
        await using var db = await WorldAsync();

        // Reacts to a component replacement by replacing it again: a chain of one rule.
        await SeedReactionAsync(db, "mark", "world.component.replaced", SetsStats, maxPerChain: 3);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = "spark", DefinitionId = "stats", Data = """{"n":1}""" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIPTION_EXECUTION_LIMIT", result.BlockCode);
        Assert.Contains("subscription.mark", result.BlockReason, StringComparison.Ordinal);

        await AssertNothingRemainsAsync(db);
    }

    /// <summary>
    /// The message a person reads has to name the cause, not just the code. A code with no
    /// explanation is a code somebody greps for and finds nothing.
    /// </summary>
    [Fact]
    public void Every_limit_code_explains_itself()
    {
        foreach (var code in new[]
                 {
                     "EVENT_DEPTH_LIMIT", "EVENT_COUNT_LIMIT",
                     "EXECUTION_COUNT_LIMIT", "SUBSCRIPTION_EXECUTION_LIMIT"
                 })
        {
            var explanation = ChainBudget.Explain(code);

            Assert.False(string.IsNullOrWhiteSpace(explanation));
            Assert.NotEqual("A chain limit was reached.", explanation);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Replaces the component it was told about, which re-triggers a replacement reaction.</summary>
    private const string SetsStats = """
        var id = ctx.event.entityIds[0];
        return {
          effects: [{ type: 'component.set', entityId: id, definitionId: 'stats', data: JSON.stringify({ n: ctx.event.depth + 1 }) }],
          narration: 'set'
        };
        """;

    /// <summary>
    /// The same, but it stops itself past a depth.
    ///
    /// A reaction that returns no effects is still a successful execution — evidence the condition
    /// was evaluated — and creates no child event, which is how a chain ends on purpose rather than
    /// by running out of budget. Built by concatenation because the JavaScript is full of braces
    /// and a raw interpolated string would need every one of them doubled.
    /// </summary>
    private static string SetsStatsUntilDepth(int depth) =>
        "var id = ctx.event.entityIds[0];\n"
        + "if (ctx.event.depth >= " + depth + ") { return { effects: [], narration: 'done' }; }\n"
        + "return {\n"
        + "  effects: [{ type: 'component.set', entityId: id, definitionId: 'stats',"
        + " data: JSON.stringify({ n: ctx.event.depth + 1 }) }],\n"
        + "  narration: 'set'\n"
        + "};";

    /// <summary>Moves the entity it was told about, which re-triggers a containment reaction.</summary>
    private const string MovesEntity = """
        var id = ctx.event.entityIds[0];
        return {
          effects: [{ type: 'containment.move', entityId: id, toEntityId: 'room', slot: 'standing' }],
          narration: 'moved'
        };
        """;

    private static async Task AssertNothingRemainsAsync(DantesRoleplayDbContext db)
    {
        Assert.Empty(db.Events);
        Assert.Empty(db.EventExecutions);

        // The world is exactly as it was: the root effect is rolled back with everything else.
        Assert.False(await db.Components.AnyAsync(c => c.EntityId == "spark"));
    }

    private static EffectApplier Applier(DantesRoleplayDbContext db) =>
        new(db,
            new WorldStore(db),
            new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
            new EventLedger(db),
            new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)));

    private async Task<DantesRoleplayDbContext> WorldAsync()
    {
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Spark", "spark");
        await world.CreateEntityAsync("Room", "room");

        var types = new EventTypeStore(db);

        foreach (var file in DantesRoleplay.DataAccess.Bootstrap.EventTypeSeeder.Load())
        {
            await types.WriteAsync(new WriteEventTypeRequest
            {
                Id = file.Id,
                Category = file.Category,
                Name = file.Name,
                Description = file.Description,
                PayloadSchema = file.Schema,
                Scope = file.Scope,
                Status = EventTypeStatus.Active
            });
        }

        return db;
    }

    private static async Task SeedReactionAsync(
        DantesRoleplayDbContext db,
        string name,
        string eventTypeId,
        string source,
        int maxPerChain)
    {
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = $"mechanic.test.{name}",
            Category = "test",
            Name = name,
            Description = "Reacts, and causes something else to react.",
            Matches = name,
            Requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"" + eventTypeId + "\"]}}",
            Source = source,
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = $"subscription.{name}",
            Category = "test",
            EventTypeId = eventTypeId,
            EventMechanicId = $"mechanic.test.{name}",
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            MaxExecutionsPerChain = maxPerChain,
            Status = SubscriptionStatus.Active
        });
    }
}
