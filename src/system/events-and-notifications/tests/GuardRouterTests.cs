using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class GuardRouterTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_denied_structural_event_rolls_back_the_entire_direct_effect_batch()
    {
        await using var db = _fixture.CreateContext();
        await new EventTypeStore(db).WriteAsync(new WriteEventTypeRequest { Id = "world.entity.created", Category = "world", Name = "Entity created", Description = "Test event", PayloadSchema = "{\"type\":\"object\"}", Status = EventTypeStatus.Active });
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.deny-create", Category = "test", Name = "Deny create", Description = "Blocks test entity creation.", Matches = "deny create",
            Requirements = "{\"event\":{\"mode\":\"guard\",\"types\":[\"world.entity.created\"]}}",
            Source = "return { decision: 'deny', code: 'TEST_BLOCKED', reason: 'Creation is blocked for this test.' };", Status = MechanicStatus.Active
        });
        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.guard.test-create", Category = "test", EventTypeId = "world.entity.created", EventMechanicId = "mechanic.test.deny-create", Mode = SubscriptionMode.Guard,
            FixedRoleEntityIdsJson = "{}", TrackedEntityIdsJson = "[]", PayloadEqualsJson = "{}", Status = SubscriptionStatus.Active
        });

        var world = new WorldStore(db);
        var router = new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db));
        var result = await new EffectApplier(db, world, router).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "guarded", Name = "Guarded" }],
            rootOperationId: "guard-seed-test");

        Assert.True(result.Blocked);
        Assert.Equal("TEST_BLOCKED", result.BlockCode);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == "guarded"));
        var evaluation = Assert.Single(result.GuardEvaluations);
        Assert.Equal(
            EventRouter.DeriveSeed(
                EventRouter.RootSeedFrom("guard-seed-test"),
                sequence: 0,
                subscriptionId: "subscription.guard.test-create",
                mode: "guard",
                ordinal: 0),
            evaluation.Seed);
    }

    [Fact]
    public async Task A_guarded_dry_run_reports_its_denial_and_leaves_no_world_state()
    {
        await using var db = _fixture.CreateContext();
        await SeedDenyCreateAsync(db);
        var world = new WorldStore(db);
        var router = new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db));

        var result = await new EffectApplier(db, world, router).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "dry-guarded", Name = "Dry guarded" }], dryRun: true);

        Assert.True(result.Blocked);
        Assert.Single(result.ProposedEvents);
        Assert.Single(result.GuardEvaluations);
        Assert.False(await db.Entities.AnyAsync(entity => entity.Id == "dry-guarded"));
    }

    [Fact]
    public async Task A_guard_seed_uses_the_sequence_the_ledger_will_assign()
    {
        await using var db = _fixture.CreateContext();
        await new EventTypeStore(db).WriteAsync(new WriteEventTypeRequest
        {
            Id = "world.entity.created",
            Category = "world",
            Name = "Entity created",
            Description = "Test event",
            PayloadSchema = "{\"type\":\"object\"}",
            Status = EventTypeStatus.Active
        });
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.allow-create",
            Category = "test",
            Name = "Allow create",
            Description = "Allows test entity creation.",
            Matches = "allow create",
            Requirements = "{\"event\":{\"mode\":\"guard\",\"types\":[\"world.entity.created\"]}}",
            Source = "return { decision: 'allow', narration: 'Allowed for this test.', effects: [] };",
            Status = MechanicStatus.Active
        });
        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.guard.allow-create",
            Category = "test",
            EventTypeId = "world.entity.created",
            EventMechanicId = "mechanic.test.allow-create",
            Mode = SubscriptionMode.Guard,
            Status = SubscriptionStatus.Active
        });

        const string correlation = "guard-sequence-test";
        var world = new WorldStore(db);
        var router = new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), world);
        var applier = new EffectApplier(db, world, router, new EventLedger(db));

        var first = await applier.ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "first", Name = "First" }],
            rootOperationId: correlation);
        var second = await applier.ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "second", Name = "Second" }],
            rootOperationId: correlation);

        Assert.True(first.Applied);
        Assert.True(second.Applied);
        Assert.Equal(
            EventRouter.DeriveSeed(
                EventRouter.RootSeedFrom(correlation),
                sequence: 1,
                subscriptionId: "subscription.guard.allow-create",
                mode: "guard",
                ordinal: 0),
            Assert.Single(second.GuardEvaluations).Seed);
    }

    [Fact]
    public async Task A_denial_records_structured_proposal_and_guard_evidence_in_the_root_audit()
    {
        await using var db = _fixture.CreateContext();
        await SeedDenyCreateAsync(db);
        var world = new WorldStore(db);
        var router = new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db));

        var envelope = await new WorldHandler().ApplyEffectsAsync(
            new EffectApplier(db, world, router), new OperationLog(db),
            [new Effect { Type = EffectType.EntityCreate, EntityId = "audited", Name = "Audited" }]);

        Assert.False(envelope.Ok);
        Assert.Equal("EVENT_BLOCKED", envelope.Error?.Code);
        var operation = await db.Operations.SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(operation.GuardEvidenceJson));
        Assert.Contains("TEST_BLOCKED", operation.GuardEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("world.entity.created", operation.GuardEvidenceJson, StringComparison.Ordinal);
    }

    private static async Task SeedDenyCreateAsync(DantesRoleplayDbContext db)
    {
        await new EventTypeStore(db).WriteAsync(new WriteEventTypeRequest { Id = "world.entity.created", Category = "world", Name = "Entity created", Description = "Test event", PayloadSchema = "{\"type\":\"object\"}", Status = EventTypeStatus.Active });
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.deny-create", Category = "test", Name = "Deny create", Description = "Blocks test entity creation.", Matches = "deny create",
            Requirements = "{\"event\":{\"mode\":\"guard\",\"types\":[\"world.entity.created\"]}}",
            Source = "return { decision: 'deny', code: 'TEST_BLOCKED', reason: 'Creation is blocked for this test.' };", Status = MechanicStatus.Active
        });
        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.guard.test-create", Category = "test", EventTypeId = "world.entity.created", EventMechanicId = "mechanic.test.deny-create", Mode = SubscriptionMode.Guard,
            FixedRoleEntityIdsJson = "{}", TrackedEntityIdsJson = "[]", PayloadEqualsJson = "{}", Status = SubscriptionStatus.Active
        });
    }
}
