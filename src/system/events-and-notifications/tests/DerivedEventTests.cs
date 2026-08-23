using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 5d: a reaction says something happened that no structural change describes.
///
/// This is the difference between a ledger of edits and a ledger of events. "The ward was spent"
/// and "the alarm was raised" may leave the world byte-identical, and are still the facts the next
/// rule in the chain needs to hear. Everything here is about making that assertion trustworthy:
/// it is validated at emission, guarded like any other proposal, and it fails the whole root
/// change when it is wrong, because a false statement in an audit trail is worse than a missing
/// one.
/// </summary>
public sealed class DerivedEventTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ---- it works --------------------------------------------------------------------------

    [Fact]
    public async Task A_reaction_can_declare_an_event_the_world_does_not_show()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, "mechanic.test.announce", "subscription.reaction.announce",
            "world.entity.created",
            """
            return {
              narration: 'the bell is rung',
              events: [{ type: 'campaign.alarm.raised', payload: { severity: 2 }, entityIds: ['bystander'] }]
            };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Applied);

        var events = await db.Events.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync();
        var root = events[0];
        var announced = Assert.Single(events, e => e.TypeId == "campaign.alarm.raised");

        // Caused by the event the rule was handling, one deeper, in the same chain. Nothing about
        // being declared rather than derived changes its place.
        Assert.Equal(root.Id, announced.CausationId);
        Assert.Equal(root.CorrelationId, announced.CorrelationId);
        Assert.Equal(1, announced.Depth);
        Assert.Equal(1, announced.Sequence);

        using var payload = JsonDocument.Parse(announced.PayloadJson);
        Assert.Equal(2, payload.RootElement.GetProperty("severity").GetInt32());

        // The execution counts events separately from effects, so "changed nothing, announced
        // something" is visible at a glance.
        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
        Assert.Equal(0, execution.EffectCount);
        Assert.Equal(1, execution.EventCount);

        // Who asserted it. Causation alone cannot say — two subscriptions answering the same event
        // would both name it, and nothing would tell a reader which made the claim.
        Assert.Equal(execution.Id, announced.ProducerExecutionId);
        Assert.Equal(string.Empty, root.ProducerExecutionId);

        // And the world really is unchanged apart from the entity the caller created.
        Assert.False(await db.Components.AsNoTracking().AnyAsync());
    }

    /// <summary>
    /// The point of a declared event is that other rules can answer it. Without this, it is a log
    /// line with extra ceremony.
    /// </summary>
    [Fact]
    public async Task A_declared_event_can_itself_be_reacted_to()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, "mechanic.test.announce", "subscription.reaction.announce",
            "world.entity.created",
            """
            return { events: [{ type: 'campaign.alarm.raised', payload: { severity: 5 }, entityIds: ['bystander'] }] };
            """);

        await SeedReactionAsync(db, "mechanic.test.answer", "subscription.reaction.answer",
            "campaign.alarm.raised",
            """
            return {
              narration: 'the guard turns',
              effects: [{ type: 'component.set', entityId: 'bystander', definitionId: 'stats',
                          data: JSON.stringify({ alerted: ctx.event.payload.severity }) }]
            };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Applied);

        var component = Assert.Single(await db.Components.AsNoTracking().ToListAsync());

        Assert.Contains("\"alerted\":5", component.Data, StringComparison.Ordinal);

        // Three links: the creation, the alarm it raised, and the change the alarm caused.
        var events = await db.Events.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync();

        Assert.Equal(
            new[] { "world.entity.created", "campaign.alarm.raised", "world.component.replaced" },
            events.Select(e => e.TypeId).ToArray());

        Assert.Equal(new[] { 0, 1, 2 }, events.Select(e => e.Depth).ToArray());
        Assert.Equal(events[0].Id, events[1].CausationId);
        Assert.Equal(events[1].Id, events[2].CausationId);
    }

    // ---- it is not a back door ---------------------------------------------------------------

    /// <summary>
    /// A structural type is the kernel's own record of what it did. A rule able to declare one
    /// could claim a component was replaced that never was — in the one place whose entire value
    /// is that it can be believed.
    /// </summary>
    [Fact]
    public async Task A_rule_cannot_declare_a_structural_event()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, "mechanic.test.forge", "subscription.reaction.forge",
            "world.entity.created",
            """
            return { events: [{ type: 'world.component.replaced',
                                payload: { effectIndex: 0, entityId: 'bystander', definitionId: 'stats',
                                           before: null, after: { vigour: 99 } } }] };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIBER_INVALID_EVENT", result.BlockCode);
        Assert.Contains("cannot be declared by a rule", result.BlockReason, StringComparison.Ordinal);

        await AssertNothingHappenedAsync(db);
    }

    [Fact]
    public async Task A_declared_event_must_match_its_registered_schema()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, "mechanic.test.malformed", "subscription.reaction.malformed",
            "world.entity.created",
            """
            return { events: [{ type: 'campaign.alarm.raised', payload: { severity: 'loud' } }] };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIBER_INVALID_EVENT", result.BlockCode);
        Assert.Contains("does not match its registered schema", result.BlockReason, StringComparison.Ordinal);

        await AssertNothingHappenedAsync(db);
    }

    [Fact]
    public async Task A_declared_event_cannot_name_an_entity_that_is_not_there()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, "mechanic.test.ghost", "subscription.reaction.ghost",
            "world.entity.created",
            """
            return { events: [{ type: 'campaign.alarm.raised', payload: { severity: 1 }, entityIds: ['nobody'] }] };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIBER_INVALID_EVENT", result.BlockCode);
        Assert.Contains("'nobody'", result.BlockReason, StringComparison.Ordinal);

        await AssertNothingHappenedAsync(db);
    }

    [Fact]
    public async Task An_unregistered_type_cannot_be_declared()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, "mechanic.test.unknown", "subscription.reaction.unknown",
            "world.entity.created",
            """
            return { events: [{ type: 'campaign.nothing.declared', payload: {} }] };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIBER_INVALID_EVENT", result.BlockCode);
        Assert.Contains("not registered", result.BlockReason, StringComparison.Ordinal);

        await AssertNothingHappenedAsync(db);
    }

    /// <summary>
    /// A guard vetoing a declared event rolls back the complete root — including the parent
    /// reaction's own work and the change that triggered it. The veto is the same veto; nothing
    /// about a declared event makes it a softer one.
    /// </summary>
    [Fact]
    public async Task A_guard_vetoing_a_declared_event_rolls_the_whole_root_back()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, "mechanic.test.announce", "subscription.reaction.announce",
            "world.entity.created",
            """
            return {
              effects: [{ type: 'component.set', entityId: 'bystander', definitionId: 'stats',
                          data: JSON.stringify({ marked: true }) }],
              events: [{ type: 'campaign.alarm.raised', payload: { severity: 9 }, entityIds: ['bystander'] }]
            };
            """);

        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.silence",
            Category = "test",
            Name = "Silence",
            Description = "Refuses to let an alarm be raised.",
            Matches = "silence",
            Requirements = "{\"event\":{\"mode\":\"guard\",\"types\":[\"campaign.alarm.raised\"]}}",
            Source = "return { decision: 'deny', code: 'ALARM_SILENCED', reason: 'No alarm can be raised here.' };",
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.guard.silence",
            Category = "test",
            EventTypeId = "campaign.alarm.raised",
            EventMechanicId = "mechanic.test.silence",
            Mode = SubscriptionMode.Guard,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("ALARM_SILENCED", result.BlockCode);

        // The parent reaction's effect went in before the veto and must be gone with everything
        // else. This is the assertion that would catch a chain rolled back only as far as the
        // event that failed.
        Assert.False(await db.Components.AsNoTracking().AnyAsync());
        await AssertNothingHappenedAsync(db);
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>No world change, no event, no execution: the root either happened whole or not.</summary>
    private static async Task AssertNothingHappenedAsync(DantesRoleplayDbContext db)
    {
        Assert.False(await db.Entities.AsNoTracking().AnyAsync(e => e.Id == "spark"));
        Assert.Empty(await db.Events.AsNoTracking().ToListAsync());
        Assert.Empty(await db.EventExecutions.AsNoTracking().ToListAsync());
    }

    private static EffectApplier Applier(DantesRoleplayDbContext db) =>
        new(db,
            new WorldStore(db),
            new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
            new EventLedger(db),
            new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)));

    /// <summary>The nine structural types, plus one campaign type a rule is allowed to declare.</summary>
    private async Task<DantesRoleplayDbContext> WorldAsync()
    {
        var db = _fixture.CreateContext();
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

        await types.WriteAsync(new WriteEventTypeRequest
        {
            Id = "campaign.alarm.raised",
            Category = "campaign",
            Name = "Alarm raised",
            Description = "Something raised an alarm. The world need not show it.",

            // severity is required and numeric, so the schema has something to actually reject.
            PayloadSchema = "{\"type\":\"object\",\"additionalProperties\":false,"
                            + "\"required\":[\"severity\"],\"properties\":{\"severity\":{\"type\":\"integer\"}}}",
            Status = EventTypeStatus.Active
        });

        return db;
    }

    private static async Task SeedReactionAsync(
        DantesRoleplayDbContext db,
        string mechanicId,
        string subscriptionId,
        string eventTypeId,
        string source)
    {
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = mechanicId,
            Category = "test",
            Name = mechanicId,
            Description = "Declares an event.",
            Matches = "declare",
            Requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"" + eventTypeId + "\"]}}",
            Source = source,
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = subscriptionId,
            Category = "test",
            EventTypeId = eventTypeId,
            EventMechanicId = mechanicId,
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });
    }
}
