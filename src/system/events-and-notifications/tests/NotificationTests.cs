using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 6: a rule tells a person something.
///
/// The split this class exists to hold in place is between CONTENT and DELIVERY STATE. Content
/// arrives once, from a reaction that committed with its entire chain, and is never editable
/// afterwards — it is evidence that a rule at a version decided this was worth saying. State is
/// the only mutable thing, and the call that moves it cannot touch anything else.
/// </summary>
public sealed class NotificationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_reaction_raises_a_notice_that_commits_with_its_chain()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, """
            return {
              narration: 'the bell is rung',
              notifications: [{ topic: 'watch.arrival', subject: 'Someone new is here',
                                body: 'Spark arrived at the gate.', entityIds: ['bystander'] }]
            };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Applied);

        var notice = Assert.Single(await new NotificationStore(db).FindAsync());

        Assert.Equal("watch.arrival", notice.Topic);
        Assert.Equal("Someone new is here", notice.Subject);
        Assert.Equal(NotificationState.Unread, notice.State);
        Assert.Null(notice.ReadAt);
        Assert.Equal(new[] { "bystander" }, notice.EntityIds);

        // Linked to the chain that produced it, both ways: which change, and which rule.
        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
        var root = await db.Events.AsNoTracking().OrderBy(e => e.Sequence).FirstAsync();

        Assert.Equal(execution.Id, notice.ExecutionId);
        Assert.Equal(root.Id, notice.EventId);
        Assert.Equal(root.CorrelationId, notice.CorrelationId);
    }

    /// <summary>
    /// A notice belongs to the change that produced it. If the change goes back, so does the
    /// notice — otherwise somebody is told about something that never happened.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_change_leaves_no_notice_behind()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, """
            return {
              notifications: [{ topic: 'watch.arrival', subject: 'Someone new is here' }],
              effects: [{ type: 'component.set', entityId: 'nowhere', definitionId: 'stats', data: '{}' }]
            };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync());
        Assert.False(await db.Entities.AsNoTracking().AnyAsync(e => e.Id == "spark"));
    }

    [Fact]
    public async Task A_notice_with_no_subject_fails_the_whole_change()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, """
            return { notifications: [{ topic: 'watch.arrival', subject: '   ' }] };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIBER_INVALID_NOTIFICATION", result.BlockCode);
        Assert.Contains("readable in a list", result.BlockReason, StringComparison.Ordinal);
        Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_notice_cannot_be_about_an_entity_that_is_not_there()
    {
        await using var db = await WorldAsync();

        await SeedReactionAsync(db, """
            return { notifications: [{ topic: 'watch.arrival', subject: 'Someone', entityIds: ['nobody'] }] };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.Equal("SUBSCRIBER_INVALID_NOTIFICATION", result.BlockCode);
        Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync());
    }

    // ---- delivery state -----------------------------------------------------------------------

    [Fact]
    public async Task Reading_is_recorded_once_and_marking_unread_clears_it()
    {
        await using var db = await WorldAsync();
        var store = new NotificationStore(db);
        var id = await RaiseAsync(db);

        var read = await store.SetStateAsync(id, NotificationState.Read);

        Assert.True(read.Ok);
        Assert.NotNull(read.Notification!.ReadAt);

        var firstReadAt = read.Notification.ReadAt;

        // Idempotent, and silently so: a client retrying a call it is unsure about should not have
        // to find out which way the first attempt went.
        var again = await store.SetStateAsync(id, NotificationState.Read);

        Assert.True(again.Ok);
        Assert.Equal(firstReadAt, again.Notification!.ReadAt);

        // Unread means "I mean to come back to this", so a read timestamp would say the opposite.
        var unread = await store.SetStateAsync(id, NotificationState.Unread);

        Assert.True(unread.Ok);
        Assert.Null(unread.Notification!.ReadAt);
        Assert.Equal(NotificationState.Unread, unread.Notification.State);
    }

    [Fact]
    public async Task Archiving_keeps_the_read_time_and_is_one_way()
    {
        await using var db = await WorldAsync();
        var store = new NotificationStore(db);
        var id = await RaiseAsync(db);

        await store.SetStateAsync(id, NotificationState.Read);
        var archived = await store.SetStateAsync(id, NotificationState.Archived);

        Assert.True(archived.Ok);
        Assert.NotNull(archived.Notification!.ArchivedAt);
        Assert.NotNull(archived.Notification.ReadAt);

        // "I have dealt with this" must not be something a later mistake quietly undoes.
        var reopened = await store.SetStateAsync(id, NotificationState.Unread);

        Assert.False(reopened.Ok);
        Assert.Contains("one-way", reopened.Problem, StringComparison.Ordinal);

        // Nothing is lost — it is still readable, which is what makes the refusal reasonable.
        var stillThere = Assert.Single(await store.FindAsync(state: NotificationState.Archived));

        Assert.Equal(id, stillThere.Id);
    }

    [Fact]
    public async Task An_unknown_notice_is_refused_rather_than_invented()
    {
        await using var db = await WorldAsync();

        var result = await new NotificationStore(db).SetStateAsync("no-such-notice", NotificationState.Read);

        Assert.False(result.Ok);
        Assert.Null(result.Notification);
    }

    // ---- reading ------------------------------------------------------------------------------

    [Fact]
    public async Task Filters_narrow_what_they_say_they_narrow()
    {
        await using var db = await WorldAsync();
        var store = new NotificationStore(db);

        await SeedReactionAsync(db, """
            return {
              notifications: [
                { topic: 'watch.arrival', subject: 'About the bystander', entityIds: ['bystander'] },
                { topic: 'watch.rumour', subject: 'About nobody in particular' }
              ]
            };
            """);

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        var all = await store.FindAsync();

        Assert.Equal(2, all.Count);

        // Ordinal runs across the whole chain, so notices read back in the order the rule made them.
        Assert.Equal(new[] { 1, 0 }, all.Select(n => n.Ordinal).ToArray());

        Assert.Equal("About the bystander", Assert.Single(await store.FindAsync(topic: "watch.arrival")).Subject);

        // Through the join index, not by matching text. The second notice names nobody, and a body
        // search would have found the wrong things and missed the right ones.
        Assert.Equal("About the bystander", Assert.Single(await store.FindAsync(entityId: "bystander")).Subject);

        Assert.Equal(2, (await store.FindAsync(correlationId: all[0].CorrelationId)).Count);
        Assert.Empty(await store.FindAsync(topic: "watch.nothing"));
        Assert.Empty(await store.FindAsync(state: NotificationState.Archived));

        // Exclusive upper bound, so two adjacent windows neither overlap nor skip.
        Assert.Empty(await store.FindAsync(to: all[0].CreatedAt));
        Assert.Equal(2, (await store.FindAsync(from: all[0].CreatedAt)).Count);
    }

    [Fact]
    public async Task A_limit_is_clamped_rather_than_trusted()
    {
        await using var db = await WorldAsync();
        await RaiseAsync(db);

        Assert.Single(await new NotificationStore(db).FindAsync(limit: 100_000));
        Assert.Single(await new NotificationStore(db).FindAsync(limit: 0));
    }

    /// <summary>
    /// Telling somebody something is not a change to the world, and reading a notice is not a
    /// change to anything. Neither may put a row in the ledger.
    /// </summary>
    [Fact]
    public async Task Moving_a_notice_emits_no_event()
    {
        await using var db = await WorldAsync();
        var id = await RaiseAsync(db);

        var before = await db.Events.AsNoTracking().CountAsync();

        await new NotificationStore(db).SetStateAsync(id, NotificationState.Read);

        Assert.Equal(before, await db.Events.AsNoTracking().CountAsync());
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Commits one change whose reaction raises exactly one notice, and returns its id.</summary>
    private static async Task<string> RaiseAsync(DantesRoleplayDbContext db)
    {
        await SeedReactionAsync(db, """
            return { notifications: [{ topic: 'watch.arrival', subject: 'Someone new is here',
                                       body: 'Spark arrived.', entityIds: ['bystander'] }] };
            """);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Applied);

        return (await new NotificationStore(db).FindAsync())[0].Id;
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

    private static async Task SeedReactionAsync(DantesRoleplayDbContext db, string source)
    {
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.tell",
            Category = "test",
            Name = "Tell",
            Description = "Tells somebody something.",
            Matches = "tell",
            Requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"world.entity.created\"]}}",
            Source = source,
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.reaction.tell",
            Category = "test",
            EventTypeId = "world.entity.created",
            EventMechanicId = "mechanic.test.tell",
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });
    }
}
