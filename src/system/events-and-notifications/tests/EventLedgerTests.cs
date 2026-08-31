using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// The structural event ledger: what a committed world change records, and what it must not.
///
/// The first test here is the one that matters most. Event types are catalog contracts with
/// `additionalProperties: false`, and the producer originally built payloads by serialising the
/// effect object — PascalCase keys plus five extra properties, so every row ever written violated
/// its own registered schema three ways over. Nothing validated payloads at write time, so nothing
/// said so. This asserts conformance against the schemas actually shipped in `catalog/event-types`,
/// which is the only version of that claim worth making.
/// </summary>
public sealed class EventLedgerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ---- the payloads conform ---------------------------------------------------------

    [Fact]
    public async Task Every_structural_payload_conforms_to_its_registered_schema()
    {
        await using var db = await WorldAsync();
        var schemas = RegisteredSchemas();

        foreach (var effect in EveryStructuralEffect())
        {
            var written = await ApplyOneAsync(db, effect);

            Assert.True(
                schemas.TryGetValue(written.TypeId, out var schema),
                $"'{written.TypeId}' is produced by the applier but not registered in the catalog.");

            AssertConforms(written.PayloadJson, schema!, $"{effect.Type} -> {written.TypeId}");
        }
    }

    /// <summary>
    /// Each effect maps to the one event type the plan's mapping table names. A silently wrong
    /// mapping would be invisible: the row is well-formed, the payload validates, and only a reader
    /// who knew what to expect would notice it says the wrong thing happened.
    /// </summary>
    [Fact]
    public async Task Each_effect_maps_to_its_declared_event_type()
    {
        await using var db = await WorldAsync();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EffectType.EntityCreate] = "world.entity.created",
            [EffectType.EntityDelete] = "world.entity.deleted",
            [EffectType.ComponentAdd] = "world.component.added",
            [EffectType.ComponentSet] = "world.component.replaced",
            [EffectType.ComponentMerge] = "world.component.merged",
            [EffectType.ComponentRemove] = "world.component.removed",
            [EffectType.ContainmentMove] = "world.containment.moved",
            [EffectType.RelationshipCreate] = "world.relationship.created",
            [EffectType.RelationshipRemove] = "world.relationship.removed"
        };

        foreach (var effect in EveryStructuralEffect())
        {
            var written = await ApplyOneAsync(db, effect);
            Assert.Equal(expected[effect.Type!], written.TypeId);
        }
    }

    /// <summary>
    /// An event always names a real entity, because an effect list can never create one without
    /// saying which id it is creating.
    ///
    /// This was written the other way round first — asserting that a generated id gets recorded —
    /// and the validator refused the effect outright. That refusal is deliberate and load-bearing:
    /// the whole list is validated before any of it is applied, so an id the applier would have
    /// minted does not exist yet and a later effect in the same list could not name it. The applier
    /// still reports the id it actually touched, which costs nothing and is the first step of the
    /// receipt pipeline the plan describes, but nothing downstream may rely on it filling a gap
    /// that validation does not allow to exist.
    /// </summary>
    [Fact]
    public async Task An_entity_cannot_be_created_without_an_id_so_no_event_can_name_a_blank_one()
    {
        await using var db = await WorldAsync();

        var rejected = await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, Name = "Nameless" }]);

        Assert.False(rejected.Applied);
        Assert.Contains("entityId", Assert.Single(rejected.Problems).Problem, StringComparison.Ordinal);
        Assert.Empty(db.Events);

        // And with an id, the event names it.
        var written = await ApplyOneAsync(db, new Effect { Type = EffectType.EntityCreate, EntityId = "named", Name = "Named" });

        using var payload = JsonDocument.Parse(written.PayloadJson);

        Assert.Equal("named", payload.RootElement.GetProperty("entityId").GetString());
        Assert.Equal(new[] { "named" }, written.EntityIds);
        Assert.True(await db.Entities.AnyAsync(e => e.Id == "named"));
    }

    // ---- what a change replaced ------------------------------------------------------------

    /// <summary>
    /// The question an audit ledger exists to answer is "what did this rule actually do?", and a
    /// row recording only the new value cannot answer it — 4 vigour is a scratch or nearly fatal
    /// depending entirely on what it replaced. So every payload carries `before` and `after`, and
    /// null there means there was nothing to record, never that nobody looked.
    /// </summary>
    [Fact]
    public async Task A_change_records_what_it_replaced()
    {
        await using var db = await WorldAsync();

        // Orban was seeded at vigour 10.
        var replaced = await ApplyOneAsync(db, new Effect
        {
            Type = EffectType.ComponentSet,
            EntityId = "orban",
            DefinitionId = "stats",
            Data = """{"vigour":4}"""
        });

        using var replacement = JsonDocument.Parse(replaced.PayloadJson);

        Assert.Equal(0, replacement.RootElement.GetProperty("effectIndex").GetInt32());
        Assert.Equal(10, replacement.RootElement.GetProperty("before").GetProperty("vigour").GetInt32());
        Assert.Equal(4, replacement.RootElement.GetProperty("after").GetProperty("vigour").GetInt32());

        // A creation replaced nothing, and says so with an explicit null rather than by omitting
        // the field — an absent key would read as "not captured".
        var created = await ApplyOneAsync(db, new Effect { Type = EffectType.EntityCreate, EntityId = "sol", Name = "Sol" });

        using var creation = JsonDocument.Parse(created.PayloadJson);

        Assert.Equal(JsonValueKind.Null, creation.RootElement.GetProperty("before").ValueKind);
        Assert.Equal("Sol", creation.RootElement.GetProperty("after").GetProperty("name").GetString());

        // Deletion is the one change where the ledger holds the only remaining copy of what was
        // there, so the whole entity goes in — components and all.
        var deleted = await ApplyOneAsync(db, new Effect { Type = EffectType.EntityDelete, EntityId = "orban" });

        using var deletion = JsonDocument.Parse(deleted.PayloadJson);
        var gone = deletion.RootElement.GetProperty("before");

        Assert.Equal("Orban", gone.GetProperty("name").GetString());
        Assert.Equal(4, gone.GetProperty("components").GetProperty("stats").GetProperty("vigour").GetInt32());
        Assert.Equal(JsonValueKind.Null, deletion.RootElement.GetProperty("after").ValueKind);
    }

    /// <summary>
    /// A merge is the one effect where what was asked for and what happened genuinely differ, so
    /// the payload states both. Recording only the patch would make a shallow merge look like a
    /// replacement that lost data; recording only the result would hide what the rule intended.
    /// </summary>
    [Fact]
    public async Task A_merge_records_both_the_patch_and_the_result()
    {
        await using var db = await WorldAsync();

        var merged = await ApplyOneAsync(db, new Effect
        {
            Type = EffectType.ComponentMerge,
            EntityId = "orban",
            DefinitionId = "stats",
            Data = """{"resolve":2}"""
        });

        using var payload = JsonDocument.Parse(merged.PayloadJson);
        var root = payload.RootElement;

        Assert.False(root.GetProperty("patch").TryGetProperty("vigour", out _));
        Assert.Equal(2, root.GetProperty("patch").GetProperty("resolve").GetInt32());
        Assert.Equal(10, root.GetProperty("before").GetProperty("vigour").GetInt32());
        Assert.Equal(10, root.GetProperty("after").GetProperty("vigour").GetInt32());
        Assert.Equal(2, root.GetProperty("after").GetProperty("resolve").GetInt32());
    }

    /// <summary>
    /// A move states where something went, which is half the fact. "Taken out of Orban's hand and
    /// hung on the wall" needs the end it left, and the effect never said it.
    /// </summary>
    [Fact]
    public async Task A_move_records_both_ends()
    {
        await using var db = await WorldAsync();

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.ContainmentMove, EntityId = "lantern", ToEntityId = "orban", Slot = "left-hand" }]);

        var moved = await ApplyOneAsync(db, new Effect
        {
            Type = EffectType.ContainmentMove,
            EntityId = "lantern",
            ToEntityId = "room",
            Slot = "hook"
        });

        using var payload = JsonDocument.Parse(moved.PayloadJson);
        var root = payload.RootElement;

        Assert.Equal("orban", root.GetProperty("before").GetProperty("containerId").GetString());
        Assert.Equal("left-hand", root.GetProperty("before").GetProperty("slot").GetString());
        Assert.Equal("room", root.GetProperty("after").GetProperty("containerId").GetString());
        Assert.Equal("hook", root.GetProperty("after").GetProperty("slot").GetString());
    }

    // ---- linkage --------------------------------------------------------------------------

    /// <summary>
    /// The correlation id IS the root operation id. Allocating it before the transaction is what
    /// lets a row be linked to its audit row the moment it is written, instead of by a second
    /// update afterwards that could fail on its own and leave orphans.
    /// </summary>
    [Fact]
    public async Task Events_carry_the_operation_id_the_caller_allocated()
    {
        await using var db = await WorldAsync();
        var operationId = Operation.NewId();

        var result = await Applier(db).ApplyAsync(
            [
                new Effect { Type = EffectType.EntityCreate, EntityId = "linked", Name = "Linked" },
                new Effect { Type = EffectType.ComponentAdd, EntityId = "linked", DefinitionId = "stats", Data = """{"vigour":3}""" }
            ],
            rootOperationId: operationId);

        Assert.True(result.Applied);
        Assert.Equal(operationId, result.CorrelationId);

        var events = await new EventLedger(db).FindAsync(rootOperationId: operationId);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(operationId, e.CorrelationId));
        Assert.All(events, e => Assert.Equal(operationId, e.RootOperationId));

        // Effect order, not clock order: a batch shares one timestamp, so sequence is the only
        // thing that can order it.
        Assert.Equal(new[] { 0, 1 }, events.Select(e => e.Sequence));
        Assert.Equal(new[] { "world.entity.created", "world.component.added" }, events.Select(e => e.TypeId));
    }

    [Fact]
    public async Task A_batch_with_no_supplied_operation_id_still_shares_one_correlation()
    {
        await using var db = await WorldAsync();

        var result = await Applier(db).ApplyAsync(
            [
                new Effect { Type = EffectType.EntityCreate, EntityId = "a", Name = "A" },
                new Effect { Type = EffectType.EntityCreate, EntityId = "b", Name = "B" }
            ]);

        var events = await new EventLedger(db).FindAsync(correlationId: result.CorrelationId);

        Assert.Equal(2, events.Count);
        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));
    }

    // ---- what must NOT be recorded ----------------------------------------------------------

    [Fact]
    public async Task A_dry_run_records_nothing()
    {
        await using var db = await WorldAsync();

        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "never", Name = "Never" }],
            dryRun: true);

        Assert.Empty(db.Events);
    }

    /// <summary>
    /// A denied change leaves no trace in the ledger at all. The refusal belongs in the operation
    /// audit; an event would assert that something happened to the world, and nothing did.
    /// </summary>
    [Fact]
    public async Task A_guard_denial_records_no_event()
    {
        await using var db = await WorldAsync();
        await SeedDenyingGuardAsync(db);

        var router = new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db));
        var applier = new EffectApplier(db, new WorldStore(db), router, new EventLedger(db));

        var result = await applier.ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "blocked", Name = "Blocked" }]);

        Assert.True(result.Blocked);
        Assert.Empty(db.Events);
        Assert.False(await db.Entities.AnyAsync(e => e.Id == "blocked"));
    }

    // ---- reading it back ----------------------------------------------------------------------

    [Fact]
    public async Task Every_filter_narrows_what_it_says_it_narrows()
    {
        await using var db = await WorldAsync();
        var ledger = new EventLedger(db);

        var first = Operation.NewId();
        await Applier(db).ApplyAsync(
            [new Effect { Type = EffectType.EntityCreate, EntityId = "one", Name = "One" }],
            rootOperationId: first);

        var second = Operation.NewId();
        await Applier(db).ApplyAsync(
            [
                new Effect { Type = EffectType.EntityCreate, EntityId = "two", Name = "Two" },
                new Effect { Type = EffectType.ComponentAdd, EntityId = "two", DefinitionId = "stats", Data = "{}" }
            ],
            rootOperationId: second);

        Assert.Equal(3, (await ledger.FindAsync()).Count);
        Assert.Equal(2, (await ledger.FindAsync(correlationId: second)).Count);
        Assert.Equal(2, (await ledger.FindAsync(rootOperationId: second)).Count);
        Assert.Equal(2, (await ledger.FindAsync(type: "world.entity.created")).Count);
        Assert.Equal(2, (await ledger.FindAsync(entityId: "two")).Count);
        Assert.Single(await ledger.FindAsync(entityId: "one"));

        // afterSequence is exclusive, so paging from the last sequence you saw never repeats a row.
        Assert.Single(await ledger.FindAsync(correlationId: second, afterSequence: 0));
        Assert.Empty(await ledger.FindAsync(correlationId: second, afterSequence: 1));

        Assert.Equal(3, (await ledger.FindAsync(from: DateTime.UtcNow.AddMinutes(-5))).Count);
        Assert.Empty(await ledger.FindAsync(from: DateTime.UtcNow.AddMinutes(5)));
        Assert.Empty(await ledger.FindAsync(to: DateTime.UtcNow.AddMinutes(-5)));

        // Nothing has caused anything yet — reactions are a later slice.
        Assert.Empty(await ledger.FindAsync(causationId: "anything"));
    }

    [Fact]
    public async Task One_event_can_be_read_in_full_and_a_missing_one_is_null()
    {
        await using var db = await WorldAsync();
        var ledger = new EventLedger(db);

        await Applier(db).ApplyAsync([new Effect { Type = EffectType.EntityCreate, EntityId = "full", Name = "Full" }]);

        var summary = Assert.Single(await ledger.FindAsync());
        var detail = await ledger.GetAsync(summary.Id);

        Assert.NotNull(detail);
        Assert.Equal(summary.Id, detail.Id);
        Assert.Contains("\"entityId\":\"full\"", detail.PayloadJson, StringComparison.Ordinal);
        Assert.Null(await ledger.GetAsync("no-such-event"));
    }

    [Fact]
    public async Task A_limit_is_clamped_rather_than_trusted()
    {
        await using var db = await WorldAsync();
        var ledger = new EventLedger(db);

        await Applier(db).ApplyAsync([new Effect { Type = EffectType.EntityCreate, EntityId = "clamped", Name = "Clamped" }]);

        Assert.Single(await ledger.FindAsync(limit: 0));
        Assert.Single(await ledger.FindAsync(limit: -5));
        Assert.Single(await ledger.FindAsync(limit: 100_000));
    }

    [Fact]
    public async Task Recent_history_has_a_total_newest_first_order_and_a_non_repeating_cursor()
    {
        await using var db = await WorldAsync();
        var timestamp = DateTime.UtcNow;
        db.Events.AddRange(
            Event("a", timestamp, 0, "one"),
            Event("b", timestamp, 1, "two"),
            Event("c", timestamp.AddSeconds(1), 0, "three"));
        await db.SaveChangesAsync();
        var ledger = new EventLedger(db);

        var first = await ledger.ListRecentAsync(new EventHistoryQuery(Limit: 2));
        var second = await ledger.ListRecentAsync(new EventHistoryQuery(Before: first.NextCursor, Limit: 2));

        Assert.Equal(["c", "b"], first.Events.Select(@event => @event.Id));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["a"], second.Events.Select(@event => @event.Id));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Recent_history_filters_on_indexed_effect_facts()
    {
        await using var db = await WorldAsync();
        var timestamp = DateTime.UtcNow;
        db.Events.AddRange(
            Event("create-one", timestamp, 0, "one", "world.entity.created", "operation-one"),
            Event("create-two", timestamp.AddSeconds(1), 0, "two", "world.entity.created", "operation-two"),
            Event("delete-two", timestamp.AddSeconds(2), 0, "two", "world.entity.deleted", "operation-two"));
        await db.SaveChangesAsync();
        var ledger = new EventLedger(db);

        Assert.Single((await ledger.ListRecentAsync(new EventHistoryQuery(EntityId: "one"))).Events);
        Assert.Equal(["delete-two", "create-two"], (await ledger.ListRecentAsync(
            new EventHistoryQuery(RootOperationId: "operation-two"))).Events.Select(@event => @event.Id));
        Assert.Single((await ledger.ListRecentAsync(new EventHistoryQuery(TypeId: "world.entity.deleted"))).Events);
    }

    /// <summary>
    /// The nine structural types ship with the kernel, not only with the catalog.
    ///
    /// They were catalog-only, which meant a fresh install could not run `commit(kind: "effects")`
    /// at all — the ledger has nothing to record the change against. Nothing declared that
    /// dependency and no test covered it; the protocol walk found it only once the ledger started
    /// reporting the missing type instead of failing obscurely later.
    /// </summary>
    [Fact]
    public void Every_structural_event_type_is_embedded_in_the_kernel()
    {
        var embedded = EventTypeSeeder.Load().Select(f => f.Id).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(RegisteredSchemas().Keys.Order(StringComparer.Ordinal), embedded);
        Assert.All(EventTypeSeeder.Load(), file => Assert.False(string.IsNullOrWhiteSpace(file.Schema)));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static EffectApplier Applier(DantesRoleplayDbContext db) =>
        new(db, new WorldStore(db), null, new EventLedger(db));

    /// <summary>A world with the pieces every structural effect needs, and the nine types registered.</summary>
    private async Task<DantesRoleplayDbContext> WorldAsync()
    {
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.CreateEntityAsync("Lantern", "lantern");
        await world.CreateEntityAsync("Room", "room");
        await world.SetComponentAsync("orban", "stats", """{"vigour":10}""");

        var types = new EventTypeStore(db);

        foreach (var (id, schema) in RegisteredSchemas())
        {
            await types.WriteAsync(new WriteEventTypeRequest
            {
                Id = id,
                Category = "world",
                Name = id,
                Description = "Structural event, registered from the catalog.",
                PayloadSchema = schema,
                Status = EventTypeStatus.Active
            });
        }

        return db;
    }

    /// <summary>One of every structural effect, ordered so each is valid when it runs.</summary>
    private static IEnumerable<Effect> EveryStructuralEffect() =>
    [
        new() { Type = EffectType.EntityCreate, EntityId = "made", Name = "Made" },
        new() { Type = EffectType.ComponentAdd, EntityId = "made", DefinitionId = "stats", Data = """{"vigour":1}""" },
        new() { Type = EffectType.ComponentSet, EntityId = "made", DefinitionId = "stats", Data = """{"vigour":2}""" },
        new() { Type = EffectType.ComponentMerge, EntityId = "made", DefinitionId = "stats", Data = """{"resolve":3}""" },
        new() { Type = EffectType.ComponentRemove, EntityId = "made", DefinitionId = "stats" },
        new() { Type = EffectType.ContainmentMove, EntityId = "made", ToEntityId = "room", Slot = "standing" },
        new() { Type = EffectType.RelationshipCreate, EntityId = "made", ToEntityId = "lantern", Kind = "carries", Data = """{"hand":"left"}""" },
        new() { Type = EffectType.RelationshipRemove, EntityId = "made", ToEntityId = "lantern", Kind = "carries" },
        new() { Type = EffectType.EntityDelete, EntityId = "made" }
    ];

    /// <summary>Applies one effect on its own and returns the single event it recorded.</summary>
    private static async Task<EventDetail> ApplyOneAsync(DantesRoleplayDbContext db, Effect effect)
    {
        var operationId = Operation.NewId();
        var result = await Applier(db).ApplyAsync([effect], rootOperationId: operationId);

        Assert.True(result.Applied, $"{effect.Type} was not applied: {string.Join("; ", result.Problems.Select(p => p.Problem))}");

        var summary = Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: operationId));
        var detail = await new EventLedger(db).GetAsync(summary.Id);

        Assert.NotNull(detail);
        return detail;
    }

    private static EventRecord Event(
        string id,
        DateTime timestamp,
        int sequence,
        string entityId,
        string type = "world.entity.created",
        string rootOperationId = "operation") => new()
    {
        Id = id,
        TypeId = type,
        TypeVersion = 1,
        Scope = "test",
        PayloadJson = "{}",
        Timestamp = timestamp,
        CorrelationId = rootOperationId,
        RootOperationId = rootOperationId,
        Sequence = sequence,
        Entities = [new EventEntity { EventId = id, EntityId = entityId, Ordinal = 0 }]
    };

    private static async Task SeedDenyingGuardAsync(DantesRoleplayDbContext db)
    {
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.deny-everything",
            Category = "test",
            Name = "Deny everything",
            Description = "Refuses every entity creation.",
            Matches = "deny everything",
            Requirements = """{"event":{"mode":"guard","types":["world.entity.created"]}}""",
            Source = "return { decision: 'deny', code: 'TEST_BLOCKED', reason: 'Blocked for this test.' };",
            Status = MechanicStatus.Active
        });

        await new SubscriptionStore(db).WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.guard.deny-everything",
            Category = "test",
            EventTypeId = "world.entity.created",
            EventMechanicId = "mechanic.test.deny-everything",
            Mode = SubscriptionMode.Guard,
            FixedRoleEntityIdsJson = "{}",
            TrackedEntityIdsJson = "[]",
            PayloadEqualsJson = "{}",
            Status = SubscriptionStatus.Active
        });
    }

    /// <summary>The schemas as shipped, read from the catalog rather than restated here.</summary>
    private static Dictionary<string, string> RegisteredSchemas()
    {
        var directory = Path.Combine(RepositoryRoot(), "catalog", "event-types");
        var schemas = new Dictionary<string, string>(StringComparer.Ordinal);

        var structuralIds = EventTypeSeeder.Load().Select(file => file.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*.schema.json"))
        {
            var name = Path.GetFileName(path);
            var id = name[..^".schema.json".Length];
            if (structuralIds.Contains(id)) schemas[id] = File.ReadAllText(path);
        }

        Assert.Equal(structuralIds.Count, schemas.Count);
        return schemas;
    }

    /// <summary>
    /// Enough of JSON Schema to check what these ten schemas actually say: required properties,
    /// `additionalProperties: false`, and declared scalar types. Deliberately not a general
    /// validator — a dependency for ten object schemas would be a poor trade, and the failure
    /// messages a hand-rolled check can give are better than a generic one's.
    /// </summary>
    private static void AssertConforms(string payloadJson, string schemaJson, string what)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        using var schema = JsonDocument.Parse(schemaJson);

        var properties = schema.RootElement.GetProperty("properties");
        var declared = properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var present = payload.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        if (schema.RootElement.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(x => x.GetString()!))
            {
                Assert.True(
                    present.Contains(name, StringComparer.Ordinal),
                    $"{what}: payload is missing required '{name}'. It has: {string.Join(", ", present)}");
            }
        }

        if (schema.RootElement.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False)
        {
            var extra = present.Where(name => !declared.Contains(name)).ToList();

            Assert.True(
                extra.Count == 0,
                $"{what}: the schema forbids extra properties but the payload has {string.Join(", ", extra)}.");
        }

        foreach (var property in payload.RootElement.EnumerateObject())
        {
            if (!properties.TryGetProperty(property.Name, out var spec)
                || !spec.TryGetProperty("type", out var allowedTypes))
            {
                // An untyped slot, e.g. "data": {} — anything goes, on purpose.
                continue;
            }

            string[] allowed = allowedTypes.ValueKind == JsonValueKind.String
                ? [allowedTypes.GetString()!]
                : allowedTypes.EnumerateArray().Select(x => x.GetString()!).ToArray();

            var actual = property.Value.ValueKind switch
            {
                JsonValueKind.String => "string",
                JsonValueKind.Null => "null",

                // JSON has one number type; JSON Schema distinguishes integer from it. A whole
                // number satisfies both, so report the narrower one and accept the wider below.
                JsonValueKind.Number => property.Value.TryGetInt64(out _) ? "integer" : "number",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Object => "object",
                JsonValueKind.Array => "array",
                _ => "undefined"
            };

            Assert.True(
                allowed.Contains(actual, StringComparer.Ordinal)
                || (actual == "integer" && allowed.Contains("number", StringComparer.Ordinal)),
                $"{what}: '{property.Name}' is {actual}, but the schema allows {string.Join(" or ", allowed)}.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.slnx").Any() || directory.EnumerateFiles("*.sln").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root from the test output directory.");
    }
}
