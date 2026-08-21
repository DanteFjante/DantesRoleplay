using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 2 proves registration and validation only. These tests intentionally do not try to
/// execute middleware: no dispatch or event ledger exists in this slice.
/// </summary>
public sealed class SubscriptionStoreTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_subscription_is_versioned_and_canonicalises_its_filters()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Guard);
        var store = new SubscriptionStore(db);

        var first = await store.WriteAsync(Request() with
        {
            PayloadEqualsJson = "{\"z\":true,\"a\":1}",
            Status = SubscriptionStatus.Active
        });
        var second = await store.WriteAsync(Request() with
        {
            Order = 7,
            PayloadEqualsJson = "{\"z\":true,\"a\":1}",
            ChangeNote = "Run this guard after the basic validity check."
        });

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(2, second.Subscription.Version);
        Assert.Equal("{\"a\":1,\"z\":true}", (await store.GetAsync("subscription.guard.test"))!.PayloadEqualsJson);
        Assert.Equal(0, (await store.GetAsync("subscription.guard.test", 1))!.Order);
        Assert.Equal(7, (await store.GetAsync("subscription.guard.test", 2))!.Order);
    }

    [Fact]
    public async Task A_subscription_cannot_change_between_guard_and_reaction()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Guard);
        var store = new SubscriptionStore(db);
        await store.WriteAsync(Request());

        var checks = await store.CheckAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            ChangeNote = "Attempting to change a stable middleware identity."
        });

        var mode = Assert.Single(checks, check => check.Name == "mode-immutable");
        Assert.False(mode.Passed);
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            ChangeNote = "Attempting to change a stable middleware identity."
        }));
    }

    [Fact]
    public async Task A_subscription_requires_an_active_mechanic_with_the_exact_declared_type_and_mode()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Reaction);
        var store = new SubscriptionStore(db);

        var checks = await store.CheckAsync(Request());

        var mechanic = Assert.Single(checks, check => check.Name == "event-mechanic");
        Assert.False(mechanic.Passed);
        Assert.Contains("requested Guard mode", mechanic.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_reaction_can_bind_one_declared_payload_field_to_one_ordinary_role()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(
            db,
            EventMechanicMode.Reaction,
            """{"type":"object","properties":{"subjectId":{"type":"string"}},"x-dantes-entity-payload-fields":["subjectId"]}""",
            """{"subject":{"components":[]}}""");
        var store = new SubscriptionStore(db);

        var created = await store.WriteAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            RoleFromEventPayloadJson = "{\"subject\":\"subjectId\"}"
        });

        Assert.Equal("{\"subject\":\"subjectId\"}", created.Subscription.RoleFromEventPayloadJson);
        Assert.True((await store.GetAsync("subscription.guard.test"))!.SourceHash.Length == 64);
    }

    [Fact]
    public async Task A_payload_role_binding_is_rejected_when_the_event_type_did_not_declare_its_field()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Reaction, rolesJson: """{"subject":{"components":[]}}""");
        var checks = await new SubscriptionStore(db).CheckAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            RoleFromEventPayloadJson = "{\"subject\":\"subjectId\"}"
        });

        Assert.False(Assert.Single(checks, check => check.Name == "role-from-event-payload").Passed);
    }

    [Fact]
    public async Task A_scoped_reaction_can_select_one_required_role_from_a_closed_fanout_selector()
    {
        await using var db = _fixture.CreateContext();
        await new WorldStore(db).DefineComponentAsync("active.marker", "Active", "Presence selects a receiver.");
        await SeedEventMechanicAsync(db, EventMechanicMode.Reaction, rolesJson: """{"receiver":{"components":[]}}""");

        var created = await new SubscriptionStore(db).WriteAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            FixedRoleEntityIdsJson = "{}",
            Scope = "scope.test",
            FanoutSelectorJson = """{"componentId":"active.marker","direction":"scope-to-candidate","relationshipKind":"scope.member","role":"receiver"}"""
        });

        Assert.Equal("{\"componentId\":\"active.marker\",\"direction\":\"scope-to-candidate\",\"relationshipKind\":\"scope.member\",\"role\":\"receiver\"}", created.Subscription.FanoutSelectorJson);
        Assert.True(created.Subscription.SourceHash.Length == 64);
    }

    [Fact]
    public async Task A_fanout_selector_rejects_payload_binding_and_an_extra_property()
    {
        await using var db = _fixture.CreateContext();
        await new WorldStore(db).DefineComponentAsync("active.marker", "Active", "Presence selects a receiver.");
        await SeedEventMechanicAsync(db, EventMechanicMode.Reaction, rolesJson: """{"receiver":{"components":[]}}""");
        var store = new SubscriptionStore(db);

        var mixed = await store.CheckAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            Scope = "scope.test",
            RoleFromEventPayloadJson = "{\"receiver\":\"subjectId\"}",
            FanoutSelectorJson = """{"componentId":"active.marker","direction":"scope-to-candidate","relationshipKind":"scope.member","role":"receiver"}"""
        });
        var malformed = await store.CheckAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            Scope = "scope.test",
            FanoutSelectorJson = """{"componentId":"active.marker","direction":"scope-to-candidate","extra":true,"relationshipKind":"scope.member","role":"receiver"}"""
        });

        Assert.False(Assert.Single(mixed, check => check.Name == "fanout-selector").Passed);
        Assert.False(Assert.Single(malformed, check => check.Name == "fanoutSelector").Passed);
    }

    [Fact]
    public async Task The_public_commit_verb_dry_runs_a_subscription_without_registering_it()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Guard);
        var payload = JsonSerializer.Serialize(new
        {
            id = "subscription.guard.test",
            category = "test",
            eventTypeId = "test.changed",
            eventMechanicId = "mechanic.test.event",
            mode = "guard",
            fixedRoleEntityIdsJson = "{}",
            trackedEntityIdsJson = "[]",
            payloadEqualsJson = "{}",
            status = "draft"
        });

        var result = await new CommitTool().CommitAsync(
            procedures: new ProcedureStore(db),
            world: new WorldStore(db),
            effects: null!,
            mechanics: new MechanicStore(db),
            eventTypes: new EventTypeStore(db),
            subscriptions: new SubscriptionStore(db),
            actions: null!,
            itineraries: null!,
            campaigns: null!,
            campaignBootstrapper: null!,
            campaignContinuity: null!,
            campaignSessions: null!,
            campaignSessionStarter: null!,
            quests: null!,
            questLifecycle: null!,
            log: new OperationLog(db),
            notifications: new NotificationStore(db),
            kind: "subscription",
            payload: payload,
            dryRun: true);

        Assert.True(result.Ok, JsonSerializer.Serialize(result));
        Assert.False(await new SubscriptionStore(db).ExistsAsync("subscription.guard.test"));
    }

    private static WriteSubscriptionRequest Request() => new()
    {
        Id = "subscription.guard.test",
        Category = "test",
        EventTypeId = "test.changed",
        EventMechanicId = "mechanic.test.event",
        Mode = SubscriptionMode.Guard,
        Order = 0,
        FixedRoleEntityIdsJson = "{}",
        TrackedEntityIdsJson = "[]",
        PayloadEqualsJson = "{}",
        MaxExecutionsPerChain = 1,
        Status = SubscriptionStatus.Draft
    };

    private static async Task SeedEventMechanicAsync(DantesRoleplayDbContext db, EventMechanicMode mode, string? payloadSchema = null, string rolesJson = "{}")
    {
        await new EventTypeStore(db).WriteAsync(new WriteEventTypeRequest
        {
            Id = "test.changed",
            Category = "test",
            Name = "Test changed",
            Description = "A test-only declared event.",
            PayloadSchema = payloadSchema ?? "{\"type\":\"object\"}",
            Status = EventTypeStatus.Active
        });
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.event",
            Category = "test",
            Name = "Test event mechanic",
            Description = "A test middleware target.",
            Matches = "test event",
            Requirements = $"{{\"roles\":{rolesJson},\"event\":{{\"mode\":\"{mode.ToString().ToLowerInvariant()}\",\"types\":[\"test.changed\"]}}}}",
            Source = "return { narration: 'test', effects: [] };",
            Status = MechanicStatus.Active
        });
    }
}
