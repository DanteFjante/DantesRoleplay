using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 5's exit gate: a chain is not merely correct once, it is the SAME chain every time.
///
/// An audit ledger that cannot be replayed records what happened without being able to show it.
/// These are the properties that make a past ruling reviewable rather than merely stored — the
/// order rules run in, the numbers they drew, and the fact that a rule which decided to do nothing
/// still left evidence that it was asked.
/// </summary>
public sealed class ChainDeterminismTests
{
    /// <summary>
    /// Ascending declared order, then id. The tiebreak is not decoration: two subscriptions at the
    /// same order would otherwise run in whatever sequence the database returned them, and a chain
    /// whose order depends on that is not reproducible and therefore not auditable.
    /// </summary>
    [Fact]
    public async Task Reactions_run_in_declared_order_then_by_id()
    {
        using var fixture = new SqliteFixture();
        await using var db = await WorldAsync(fixture);

        // Registered in an order that is neither the declared order nor alphabetical, so passing
        // by accident is not available.
        await SeedReactionAsync(db, "subscription.reaction.middle", order: 0, note: "middle");
        await SeedReactionAsync(db, "subscription.reaction.last", order: 10, note: "last");
        await SeedReactionAsync(db, "subscription.reaction.aaa-tied", order: 0, note: "aaa-tied");
        await SeedReactionAsync(db, "subscription.reaction.first", order: -10, note: "first");

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Applied);

        var ran = await db.EventExecutions.AsNoTracking()
            .OrderBy(e => e.Ordinal)
            .Select(e => e.Narration)
            .ToListAsync();

        // -10 first; then the two at 0 settled by id, where "aaa-tied" precedes "middle"; then 10.
        Assert.Equal(new[] { "first", "aaa-tied", "middle", "last" }, ran.ToArray());
    }

    /// <summary>
    /// The same root produces the same chain in a database that has never seen it. Two fixtures,
    /// not two contexts — one connection is one database, and replaying into the rows the first
    /// run left would prove nothing.
    /// </summary>
    [Fact]
    public async Task A_chain_replays_identically_in_a_fresh_database()
    {
        var first = await RollAsync("operation-fixed-for-replay");
        var second = await RollAsync("operation-fixed-for-replay");

        Assert.Equal(first.Seed, second.Seed);
        Assert.Equal(first.Narration, second.Narration);
        Assert.Equal(first.Types, second.Types);
        Assert.Equal(first.Component, second.Component);

        // And it is derived from the root rather than fixed: a different root draws differently.
        // Asserted on the seed rather than the roll, because two rolls may legitimately coincide
        // and a test that fails one run in a million is worse than no test.
        var other = await RollAsync("operation-a-different-root");

        Assert.NotEqual(first.Seed, other.Seed);
    }

    /// <summary>
    /// A rule that looked and decided to do nothing is not the same as a rule that never ran, and
    /// the ledger has to be able to tell them apart. The execution row IS the evidence that the
    /// condition was evaluated.
    /// </summary>
    [Fact]
    public async Task A_reaction_that_decides_to_do_nothing_still_records_that_it_ran()
    {
        using var fixture = new SqliteFixture();
        await using var db = await WorldAsync(fixture);

        // A real condition, evaluated in the sandbox, that happens to be false here. Not a
        // declarative filter — those exclude before anything runs and correctly leave no trace.
        await SeedReactionAsync(db, "subscription.reaction.picky", order: 0, note: "considered",
            body: "if (ctx.event.payload.name !== 'Something Else') { return { narration: 'considered' }; }");

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Applied);

        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());

        Assert.Equal("considered", execution.Narration);
        Assert.Equal(0, execution.EffectCount);
        Assert.Equal(0, execution.EventCount);

        // One event, the caller's own. Deciding to do nothing creates no child.
        Assert.Single(await db.Events.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// A reaction that never finishes is not left to run. The limit fires inside the transaction,
    /// so the change that triggered it goes back too.
    /// </summary>
    [Fact]
    public async Task A_reaction_that_never_finishes_takes_the_change_down_with_it()
    {
        using var fixture = new SqliteFixture();
        await using var db = await WorldAsync(fixture);

        await SeedReactionAsync(db, "subscription.reaction.spin", order: 0, note: "spin",
            body: "var n = 0; while (true) { n = n + 1; }");

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIBER_LIMIT", result.BlockCode);

        Assert.False(await db.Entities.AsNoTracking().AnyAsync(e => e.Id == "spark"));
        Assert.Empty(await db.Events.AsNoTracking().ToListAsync());
        Assert.Empty(await db.EventExecutions.AsNoTracking().ToListAsync());
    }

    // ---- helpers -----------------------------------------------------------------------------

    private async Task<(long Seed, string Narration, string[] Types, string Component)> RollAsync(string rootOperationId)
    {
        using var fixture = new SqliteFixture();
        await using var db = await WorldAsync(fixture);

        await SeedReactionAsync(db, "subscription.reaction.roll", order: 0, note: "roll",
            body: """
                var roll = ctx.randomInt(1, 1000000);
                return {
                  narration: String(roll),
                  effects: [{ type: 'component.set', entityId: 'bystander', definitionId: 'stats',
                              data: JSON.stringify({ roll: roll }) }]
                };
                """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }],
            rootOperationId: rootOperationId);

        Assert.True(result.Applied);

        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());

        var types = await db.Events.AsNoTracking()
            .OrderBy(e => e.Sequence)
            .Select(e => e.TypeId)
            .ToArrayAsync();

        var component = await db.Components.AsNoTracking().Select(c => c.Data).SingleAsync();

        return (execution.Seed, execution.Narration, types, component);
    }

    private static EffectApplier Applier(DantesRoleplayDbContext db) =>
        new(db,
            new WorldStore(db),
            new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
            new EventLedger(db),
            new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)));

    private static async Task<DantesRoleplayDbContext> WorldAsync(SqliteFixture fixture)
    {
        var db = fixture.CreateContext();
        var world = new WorldStore(db);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Bystander", "bystander");

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

    /// <summary>
    /// One reaction on <c>world.entity.created</c>. The default body just narrates its note, which
    /// is what makes the order it ran in readable straight off the execution rows.
    /// </summary>
    private static async Task SeedReactionAsync(
        DantesRoleplayDbContext db,
        string subscriptionId,
        int order,
        string note,
        string? body = null)
    {
        var mechanicId = $"mechanic.test.{note}";

        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = mechanicId,
            Category = "test",
            Name = mechanicId,
            Description = "Reacts to a creation.",
            Matches = "react " + note,
            Requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"world.entity.created\"]}}",
            Source = body ?? $"return {{ narration: '{note}' }};",
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = subscriptionId,
            Category = "test",
            EventTypeId = "world.entity.created",
            EventMechanicId = mechanicId,
            Mode = SubscriptionMode.Reaction,
            Order = order,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });
    }
}
