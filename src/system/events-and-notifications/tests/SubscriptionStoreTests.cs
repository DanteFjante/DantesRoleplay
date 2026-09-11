using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>
/// Covers subscription registration and validation independently from EventRouterTests, which
/// own dispatch and event-ledger behavior.
/// </summary>
public sealed class SubscriptionStoreTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Scoped_subscription_catalog_round_trip_preserves_source_and_fingerprint()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Reaction);
        var catalogs = SourceCatalog();
        var original = await new SubscriptionStore(db, catalogs).WriteAsync(Request() with
        {
            Id = "subscription.reaction.catalog-source", Mode = SubscriptionMode.Reaction,
            EventMechanicId = "fixture.mechanic.test-event", Source = new("fixture", "space.1")
        });
        var directory = Path.Combine(Path.GetTempPath(), $"subscription-source-roundtrip-{Guid.NewGuid():n}");
        try
        {
            await new CatalogExporter(db).ExportAsync(directory);
            var path = CatalogLayout.ToFileSystemPath(directory, CatalogLayout.Subscription(original.Subscription.Id));
            var exported = SubscriptionFile.Parse(await File.ReadAllTextAsync(path), path);
            Assert.Equal(original.Subscription.Source, exported.Source);
            Assert.Equal(original.Subscription.SourceHash, exported.ContentHash);
            Assert.NotEqual(exported.ContentHash, (exported with { Source = null }).ContentHash);
            Assert.NotEqual(exported.ContentHash, (exported with { Source = new("fixture", "space.2") }).ContentHash);
            using var target = new SqliteFixture();
            await using var destination = target.CreateContext();
            var imported = await new CatalogImporter(destination, new MechanicStore(destination),
                new ProcedureStore(destination), new WorldStore(destination), new EventTypeStore(destination),
                new SubscriptionStore(destination, catalogs)).ApplyAsync(directory, new CatalogImportOptions());
            Assert.False(imported.Aborted);
            var actual = await new SubscriptionStore(destination, catalogs).GetAsync(original.Subscription.Id);
            Assert.NotNull(actual);
            Assert.Equal(original.Subscription.Source, actual.Source);
            Assert.Equal(original.Subscription.SourceHash, actual.SourceHash);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

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
    public async Task An_application_subscription_round_trips_its_exact_source_scope()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Reaction);
        var source = new EventSourceContext("fixture", "space.1");
        var store = new SubscriptionStore(db, SourceCatalog());

        var sourceRequest = Request() with { Id = "subscription.reaction.source", EventMechanicId = "fixture.mechanic.test-event", Mode = SubscriptionMode.Reaction, Source = source };
        var written = await store.WriteAsync(sourceRequest);
        var detail = await store.GetAsync(sourceRequest.Id);

        Assert.Equal(source, written.Subscription.Source);
        Assert.Equal(source, detail!.Source);
        Assert.True(written.Subscription.DependenciesHealthy);
        Assert.True(detail.DependenciesHealthy);
        Assert.True(Assert.Single(await store.FindAsync(), item => item.Id == sourceRequest.Id).DependenciesHealthy);
        Assert.False((await new SubscriptionStore(db).GetAsync(sourceRequest.Id))!.DependenciesHealthy);
        Assert.NotEqual((await store.WriteAsync(Request() with { Mode = SubscriptionMode.Reaction })).Subscription.Source,
            detail.Source);
    }

    [Fact]
    public async Task A_subscription_source_requires_both_bounded_ids()
    {
        await using var db = _fixture.CreateContext();
        await SeedEventMechanicAsync(db, EventMechanicMode.Reaction);
        var checks = await new SubscriptionStore(db).CheckAsync(Request() with
        {
            Mode = SubscriptionMode.Reaction,
            Source = new EventSourceContext("fixture", "")
        });

        var source = Assert.Single(checks, check => check.Name == "source-context");
        Assert.False(source.Passed);
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

    private static IPublicApplicationCatalogProvider SourceCatalog()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var content = JsonSerializer.Serialize(new { requirements = "{\"event\":{\"mode\":\"reaction\",\"types\":[\"test.changed\"]}}", source = "return { narration: 'test' };" });
        var record = new CatalogRecordDefinition(app.Value, "mechanic", "fixture.mechanic.test-event", "Test event", "A test catalog event mechanic.", [], [], "mechanics", "active", 1, content,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))), "test", "mechanics/test-event.md");
        var manifest = CatalogNavigationManifest.Create(app, new string('A', 64), "catalog-lexical-v1",
            [new(app.Value, "Fixture", "Fixture catalog.")],
            [new(app.Value, "", "Fixture", "Fixture catalog.", CatalogDescriptionStatus.Authored), new(app.Value, "mechanics", "Mechanics", "Fixture mechanics.", CatalogDescriptionStatus.Authored)],
            [record]);
        return new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(Encoding.UTF8.GetBytes("fixture-subscription-catalog-key-32")))
        });
    }
}
