using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 5a: a reaction subscription runs against an accepted event, and what it proposes goes
/// through the same door as any other change.
///
/// The property under test throughout is atomicity. A reaction is not a follow-up that happens
/// after a change — it is part of the change, so anything that stops it from completing has to
/// leave the world exactly as it was. "The change committed but its consequence did not" is the
/// state this whole design exists to make unreachable.
/// </summary>
public sealed class EventRouterTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ---- it runs ---------------------------------------------------------------------------

    [Fact]
    public async Task A_reaction_runs_and_its_effects_are_applied()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Applied);

        var marked = await new WorldStore(db).GetEntityAsync("spark");
        Assert.NotNull(marked);
        Assert.Contains("marked", marked.Components.Single(c => c.DefinitionId == "stats").Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reaction_can_resolve_an_ordinary_role_from_a_declared_event_payload_field()
    {
        await using var db = await WorldAsync();
        await DeclareEntityPayloadFieldAsync(db);
        var world = new WorldStore(db);
        await world.CreateEntityAsync("Watched", "watched");
        await world.SetComponentAsync("watched", "stats", """{"vigour":1}""");

        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.payload-role",
            Category = "test",
            Name = "Payload role",
            Description = "Reads one ordinary role from the accepted event payload.",
            Matches = "payload role",
            Requirements = """{"roles":{"subject":{"components":["stats"]}},"event":{"mode":"reaction","types":["world.component.replaced"]}}""",
            Source = "return { narration: ctx.roles.subject.id };",
            Status = MechanicStatus.Active
        });
        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.reaction.payload-role",
            Category = "test",
            EventTypeId = "world.component.replaced",
            EventMechanicId = "mechanic.test.payload-role",
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            RoleFromEventPayloadJson = "{\"subject\":\"entityId\"}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = "watched", DefinitionId = "stats", Data = """{"vigour":2}""" }]);

        Assert.True(result.Applied);
        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
        Assert.Equal("watched", execution.Narration);
    }

    [Fact]
    public async Task A_fanout_selector_runs_receivers_in_ordinal_endpoint_order()
    {
        await using var db = await WorldAsync();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("active.marker", "Active", "Presence selects a receiver.");
        await world.CreateEntityAsync("Scope", "scope.one");
        await world.CreateEntityAsync("Zed", "zed");
        await world.CreateEntityAsync("Aye", "aye");
        await world.SetComponentAsync("zed", "active.marker", "{}");
        await world.SetComponentAsync("aye", "active.marker", "{}");
        await world.RelateAsync("scope.one", "zed", "scope.member");
        await world.RelateAsync("scope.one", "aye", "scope.member");

        await new EventTypeStore(db).WriteAsync(new WriteEventTypeRequest
        {
            Id = "test.fanout.changed", Category = "test", Name = "Fanout changed",
            Description = "A test scoped event.", PayloadSchema = "{\"type\":\"object\"}", Status = EventTypeStatus.Active
        });
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.fanout", Category = "test", Name = "Fanout", Description = "Reads a selected role.", Matches = "fanout",
            Requirements = """{"roles":{"receiver":{"components":[]}},"event":{"mode":"reaction","types":["test.fanout.changed"]}}""",
            Source = "return { narration: ctx.roles.receiver.id };", Status = MechanicStatus.Active
        });
        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.reaction.fanout", Category = "test", EventTypeId = "test.fanout.changed",
            EventMechanicId = "mechanic.test.fanout", Mode = SubscriptionMode.Reaction, Scope = "scope.one",
            FanoutSelectorJson = """{"role":"receiver","relationshipKind":"scope.member","direction":"scope-to-candidate","componentId":"active.marker"}""",
            MaxExecutionsPerChain = 8,
            Status = SubscriptionStatus.Active
        });
        var accepted = await new EventLedger(db).WriteAcceptedAsync(
            [new ProposedEvent("test.fanout.changed", "{}", [], "scope.one", 0)], "fanout-root");

        var result = await new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), world)
            .RouteAsync(accepted, 123, new ChainBudget());

        Assert.True(result.Ok, result.Reason);
        Assert.Equal(["aye", "zed"], result.Outcomes.Select(x => x.Execution.Narration));
    }

    [Fact]
    public async Task A_fanout_selector_rejects_more_than_eight_before_any_receiver_runs()
    {
        await using var db = await WorldAsync();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("active.marker", "Active", "Presence selects a receiver.");
        await world.CreateEntityAsync("Scope", "scope.limit");
        for (var index = 0; index < 9; index++)
        {
            var id = $"candidate-{index}";
            await world.CreateEntityAsync(id, id);
            await world.SetComponentAsync(id, "active.marker", "{}");
            await world.RelateAsync("scope.limit", id, "scope.member");
        }
        await new EventTypeStore(db).WriteAsync(new WriteEventTypeRequest { Id = "test.fanout.limit", Category = "test", Name = "Fanout limit", Description = "A test scoped event.", PayloadSchema = "{\"type\":\"object\"}", Status = EventTypeStatus.Active });
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest { Id = "mechanic.test.fanout.limit", Category = "test", Name = "Fanout limit", Description = "Runs no receiver on limit failure.", Matches = "fanout", Requirements = """{"roles":{"receiver":{"components":[]}},"event":{"mode":"reaction","types":["test.fanout.limit"]}}""", Source = "return { narration: ctx.roles.receiver.id };", Status = MechanicStatus.Active });
        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest { Id = "subscription.reaction.fanout.limit", Category = "test", EventTypeId = "test.fanout.limit", EventMechanicId = "mechanic.test.fanout.limit", Mode = SubscriptionMode.Reaction, Scope = "scope.limit", FanoutSelectorJson = """{"role":"receiver","relationshipKind":"scope.member","direction":"scope-to-candidate","componentId":"active.marker"}""", Status = SubscriptionStatus.Active });
        var accepted = await new EventLedger(db).WriteAcceptedAsync([new ProposedEvent("test.fanout.limit", "{}", [], "scope.limit", 0)], "fanout-limit-root");

        var result = await new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), world)
            .RouteAsync(accepted, 123, new ChainBudget());

        Assert.False(result.Ok);
        Assert.Equal("SUBSCRIBER_FANOUT_LIMIT", result.Code);
    }

    [Fact]
    public async Task A_corrupt_payload_role_mapping_rolls_the_root_change_back()
    {
        await using var db = await WorldAsync();
        await DeclareEntityPayloadFieldAsync(db);
        var world = new WorldStore(db);
        await world.CreateEntityAsync("Watched", "watched");
        await world.SetComponentAsync("watched", "stats", """{"vigour":1}""");

        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.payload-role",
            Category = "test",
            Name = "Payload role",
            Description = "Reads one ordinary role from the accepted event payload.",
            Matches = "payload role",
            Requirements = """{"roles":{"subject":{"components":["stats"]}},"event":{"mode":"reaction","types":["world.component.replaced"]}}""",
            Source = "return { narration: ctx.roles.subject.id };",
            Status = MechanicStatus.Active
        });
        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.reaction.payload-role",
            Category = "test",
            EventTypeId = "world.component.replaced",
            EventMechanicId = "mechanic.test.payload-role",
            Mode = SubscriptionMode.Reaction,
            RoleFromEventPayloadJson = "{\"subject\":\"entityId\"}",
            Status = SubscriptionStatus.Active
        });
        var registration = Assert.Single(await db.SubscriptionVersions.ToListAsync());
        registration.RoleFromEventPayloadJson = "{\"subject\":\"definitionId\"}";
        await db.SaveChangesAsync();
        var eventCount = await db.Events.CountAsync();

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = "watched", DefinitionId = "stats", Data = """{"vigour":2}""" }]);

        Assert.True(result.Blocked);
        Assert.Equal("SUBSCRIBER_INVALID_ROLE_BINDING", result.BlockCode);
        Assert.Equal(eventCount, await db.Events.CountAsync());
        Assert.Contains("1", (await world.GetEntityAsync("watched"))!.Components.Single(component => component.DefinitionId == "stats").Data, StringComparison.Ordinal);
    }

    /// <summary>
    /// The execution record is the answer to "why did the world change like that?", so it has to
    /// carry the whole derivation rather than the fact that something ran.
    /// </summary>
    [Fact]
    public async Task An_execution_records_the_whole_derivation()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks);

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());

        Assert.Equal("subscription.reaction.mark", execution.SubscriptionId);
        Assert.Equal(1, execution.SubscriptionVersion);
        Assert.Equal("mechanic.test.mark", execution.MechanicId);
        Assert.Equal(1, execution.MechanicVersion);
        Assert.Equal(0, execution.Ordinal);
        Assert.Equal(1, execution.EffectCount);
        Assert.Equal(0, execution.EventCount);
        Assert.NotEqual(0, execution.Seed);
        Assert.Equal("marked", execution.Narration);
        Assert.NotEqual("{}", execution.ProjectionJson);
        Assert.NotEqual("{}", execution.OutputJson);
    }

    /// <summary>
    /// A reaction's own effects record events one deeper, naming the event they answer, and the
    /// sequence keeps climbing — two batches both numbering from zero would make "the third thing
    /// that happened" ambiguous.
    /// </summary>
    [Fact]
    public async Task A_reactions_effects_are_recorded_one_deeper_and_name_their_cause()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks);

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        var events = await new EventLedger(db).FindAsync(correlationId: result.CorrelationId);

        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { 0, 1 }, events.Select(e => e.Sequence));

        var root = events[0];
        var child = events[1];

        Assert.Equal("world.entity.created", root.TypeId);
        Assert.Equal(0, root.Depth);
        Assert.Equal(string.Empty, root.CausationId);

        Assert.Equal("world.component.replaced", child.TypeId);
        Assert.Equal(1, child.Depth);
        Assert.Equal(root.Id, child.CausationId);

        // And it stops there, because nothing is registered against the event the reaction caused.
        // The chain itself is unbounded by design; what bounds it are the limits, not this test.
        Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
        Assert.Single(await new EventLedger(db).FindAsync(causationId: root.Id));
    }

    // ---- it does not run ---------------------------------------------------------------------

    [Fact]
    public async Task A_registration_watching_another_type_does_not_run()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks, eventTypeId: "world.entity.deleted");

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.Empty(db.EventExecutions);
    }

    [Fact]
    public async Task A_registration_tracking_a_different_entity_does_not_run()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks, trackedEntityIdsJson: """["bystander"]""");

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.Empty(db.EventExecutions);
    }

    [Fact]
    public async Task A_payload_filter_that_does_not_match_excludes_the_registration()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks, payloadEqualsJson: """{"entityId":"bystander"}""");

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.Empty(db.EventExecutions);
    }

    // ---- it fails ------------------------------------------------------------------------------

    /// <summary>
    /// The whole point. A reaction that cannot complete leaves nothing behind — no entity, no
    /// event, no execution — because the change and its consequence are one fact.
    /// </summary>
    [Fact]
    public async Task A_throwing_reaction_takes_the_entire_change_down_with_it()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, "throw new Error('the ward objects');");

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.False(result.Applied);
        Assert.True(result.Blocked);
        Assert.Equal("SUBSCRIBER_FAILED", result.BlockCode);

        Assert.False(await db.Entities.AnyAsync(e => e.Id == "spark"));
        Assert.Empty(db.Events);
        Assert.Empty(db.EventExecutions);
    }

    [Fact]
    public async Task A_registration_whose_mechanic_is_inactive_aborts_the_change()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks);

        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.mark",
            Category = "test",
            Name = "Mark",
            Description = "Retired.",
            Matches = "mark",
            Requirements = ReactionRequirements("world.entity.created"),
            Source = Marks,
            Status = MechanicStatus.Deprecated
        });

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Blocked);
        Assert.Equal("SUBSCRIBER_UNAVAILABLE", result.BlockCode);
        Assert.False(await db.Entities.AnyAsync(e => e.Id == "spark"));
        Assert.Empty(db.Events);
    }

    [Fact]
    public async Task A_reaction_that_returns_a_guard_decision_is_rejected()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, "return { decision: 'allow' };");

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Blocked);
        Assert.Equal("SUBSCRIBER_FORBIDDEN_OUTPUT", result.BlockCode);
        Assert.Empty(db.Events);
    }

    /// <summary>
    /// A reaction's effects are guarded like anything else. A reaction is not a way around the
    /// rules that apply to the change that triggered it.
    /// </summary>
    [Fact]
    public async Task A_guard_vetoing_a_reactions_effect_rolls_the_whole_change_back()
    {
        await using var db = await WorldAsync();
        await SeedReactionAsync(db, Marks);
        await SeedGuardAsync(db, "world.component.replaced");

        var result = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        Assert.True(result.Blocked);
        Assert.Equal("CHILD_BLOCKED", result.BlockCode);
        Assert.False(await db.Entities.AnyAsync(e => e.Id == "spark"));
        Assert.Empty(db.Events);
        Assert.Empty(db.EventExecutions);
    }

    // ---- determinism ----------------------------------------------------------------------------

    /// <summary>
    /// A chain replays from the root seed alone, so the same correlation derives the same seed.
    /// Drawn seeds would make a recorded ruling unreproducible, which defeats recording it.
    /// </summary>
    [Fact]
    public void A_reaction_seed_is_derived_from_its_exact_position()
    {
        var root = EventRouter.RootSeedFrom("correlation-one");

        Assert.Equal(root, EventRouter.RootSeedFrom("correlation-one"));
        Assert.NotEqual(root, EventRouter.RootSeedFrom("correlation-two"));

        var seed = EventRouter.DeriveSeed(root, 0, "subscription.a", "reaction", 0);

        Assert.Equal(seed, EventRouter.DeriveSeed(root, 0, "subscription.a", "reaction", 0));
        Assert.NotEqual(seed, EventRouter.DeriveSeed(root, 1, "subscription.a", "reaction", 0));
        Assert.NotEqual(seed, EventRouter.DeriveSeed(root, 0, "subscription.b", "reaction", 0));
        Assert.NotEqual(seed, EventRouter.DeriveSeed(root, 0, "subscription.a", "reaction", 1));
        Assert.NotEqual(seed, EventRouter.DeriveSeed(root, 0, "subscription.a", "guard", 0));
        Assert.True(seed >= 0);
    }

    /// <summary>The envelope a reaction sees, which the contract says it must branch on rather than sniff.</summary>
    [Fact]
    public async Task A_reaction_sees_the_full_event_envelope()
    {
        await using var db = await WorldAsync();

        // Records the whole envelope into narration so the test can read what the sandbox saw.
        await SeedReactionAsync(db, "return { narration: JSON.stringify(ctx.event) };");

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "spark", Name = "Spark" }]);

        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
        using var envelope = JsonDocument.Parse(execution.Narration);
        var root = envelope.RootElement;

        Assert.Equal("reaction", root.GetProperty("mode").GetString());
        Assert.Equal("world.entity.created", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("typeVersion").GetInt32());
        Assert.Equal(0, root.GetProperty("depth").GetInt32());
        Assert.Equal(0, root.GetProperty("sequence").GetInt32());
        Assert.Equal(string.Empty, root.GetProperty("causationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("id").GetString()));

        // The payload is embedded JSON, not a string containing JSON.
        Assert.Equal(JsonValueKind.Object, root.GetProperty("payload").ValueKind);
        Assert.Equal("spark", root.GetProperty("payload").GetProperty("entityId").GetString());
        Assert.Equal("spark", root.GetProperty("entityIds")[0].GetString());
    }

    /// <summary>
    /// A reaction sees the entities the event affected, keyed by id, carrying the components it
    /// declared — and nothing it did not declare.
    ///
    /// The undeclared half matters as much as the declared half. A middleware that silently
    /// received every component of every affected entity could depend on data it never asked for,
    /// and the day someone removed that component nothing would explain why the rule broke.
    /// </summary>
    [Fact]
    public async Task A_reaction_sees_the_components_it_declared_and_no_others()
    {
        await using var db = await WorldAsync();
        var world = new WorldStore(db);

        await world.DefineComponentAsync("secrets", "Secrets", "Deliberately not declared.");
        await world.CreateEntityAsync("Watched", "watched");
        await world.SetComponentAsync("watched", "stats", """{"vigour":7}""");
        await world.SetComponentAsync("watched", "secrets", """{"hidden":true}""");

        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.mark",
            Category = "test",
            Name = "Mark",
            Description = "Reports what it can see.",
            Matches = "mark",

            // Declares stats, not secrets.
            Requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"world.component.replaced\"],"
                           + "\"components\":[\"stats\"]}}",
            Source = "return { narration: JSON.stringify(ctx.eventEntities) };",
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.reaction.mark",
            Category = "test",
            EventTypeId = "world.component.replaced",
            EventMechanicId = "mechanic.test.mark",
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = "watched", DefinitionId = "stats", Data = """{"vigour":9}""" }]);

        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
        using var seen = JsonDocument.Parse(execution.Narration);

        // Keyed by entity id, exactly like ctx.roles.
        var watched = seen.RootElement.GetProperty("watched");

        Assert.Equal("Watched", watched.GetProperty("name").GetString());

        var components = watched.GetProperty("components");

        Assert.True(components.TryGetProperty("stats", out _));
        Assert.False(components.TryGetProperty("secrets", out _), "an undeclared component reached the sandbox");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Marks whatever entity the event concerns. One effect, so counts are unambiguous.</summary>
    private const string Marks = """
        var id = ctx.event.entityIds[0];
        return {
          effects: [{ type: 'component.set', entityId: id, definitionId: 'stats', data: JSON.stringify({ marked: true }) }],
          narration: 'marked'
        };
        """;

    /// <summary>
    /// Built by concatenation, not a raw interpolated string: with <c>$$</c> the JSON's own trailing
    /// <c>}}</c> is read as an interpolation close, and counting braces to satisfy the compiler is
    /// how a test grows a bug that has nothing to do with what it tests.
    /// </summary>
    private static string EventRequirements(string mode, string eventTypeId) =>
        "{\"event\":{\"mode\":\"" + mode + "\",\"types\":[\"" + eventTypeId + "\"]}}";

    private static string ReactionRequirements(string eventTypeId) =>
        EventRequirements("reaction", eventTypeId);

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

        // Real, and deliberately uninvolved. A subscription may only track entities that exist, so
        // "watching something else" has to be a genuine something else rather than a made-up id.
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

    private static async Task DeclareEntityPayloadFieldAsync(DantesRoleplayDbContext db)
    {
        var store = new EventTypeStore(db);
        var existing = (await store.GetAsync("world.component.replaced"))!;
        await store.WriteAsync(new WriteEventTypeRequest
        {
            Id = existing.Id,
            Category = existing.Category,
            Name = existing.Name,
            Description = existing.Description,
            PayloadSchema = """{"type":"object","additionalProperties":false,"required":["effectIndex","entityId","definitionId","before","after"],"properties":{"effectIndex":{"type":"integer","minimum":0},"entityId":{"type":"string"},"definitionId":{"type":"string"},"before":{},"after":{}},"x-dantes-entity-payload-fields":["entityId"]}""",
            Scope = existing.Scope,
            Status = existing.Status,
            ChangeNote = "Declare the structural event entity id for generic role binding tests."
        });
    }

    private static async Task SeedReactionAsync(
        DantesRoleplayDbContext db,
        string source,
        string eventTypeId = "world.entity.created",
        string trackedEntityIdsJson = "[]",
        string payloadEqualsJson = "{}")
    {
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.mark",
            Category = "test",
            Name = "Mark",
            Description = "Reacts to a structural event.",
            Matches = "mark",
            Requirements = ReactionRequirements(eventTypeId),
            Source = source,
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.reaction.mark",
            Category = "test",
            EventTypeId = eventTypeId,
            EventMechanicId = "mechanic.test.mark",
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = trackedEntityIdsJson,
            PayloadEqualsJson = payloadEqualsJson,
            Status = SubscriptionStatus.Active
        });
    }

    private static async Task SeedGuardAsync(DantesRoleplayDbContext db, string eventTypeId)
    {
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.veto-child",
            Category = "test",
            Name = "Veto child",
            Description = "Refuses the change a reaction proposes.",
            Matches = "veto child",
            Requirements = EventRequirements("guard", eventTypeId),
            Source = "return { decision: 'deny', code: 'CHILD_BLOCKED', reason: 'The ward refuses the consequence.' };",
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.guard.veto-child",
            Category = "test",
            EventTypeId = eventTypeId,
            EventMechanicId = "mechanic.test.veto-child",
            Mode = SubscriptionMode.Guard,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });
    }
}
