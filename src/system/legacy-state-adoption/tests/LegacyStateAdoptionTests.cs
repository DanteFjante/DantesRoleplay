using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.LegacyStateAdoption.Tests;

public sealed class LegacyStateAdoptionTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Exact_preview_adopts_complete_graph_preserves_source_and_replays()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db);
        var context = Context("0123456789abcdef0123456789abcdef");

        var required = await Assert.ThrowsAsync<LegacyStateAdoptionException>(() =>
            setup.Service.AdoptAsync(setup.Request, context));
        Assert.Equal("DRY_RUN_REQUIRED", required.Code);

        var preview = await setup.Service.PreviewAsync(setup.Request, context);
        Assert.Equal("would-adopt", preview.Outcome);
        Assert.Equal(2, preview.Inventory.EntityCount);
        Assert.Equal(1, preview.Inventory.ComponentCount);
        Assert.Equal(1, preview.Inventory.ContainmentCount);
        Assert.Equal(1, preview.Inventory.RelationshipCount);
        Assert.Equal(0, await CountAsync(db, "system_ecs_entity"));

        var receipt = await setup.Service.AdoptAsync(setup.Request, context);
        var replay = await setup.Service.AdoptAsync(setup.Request, context);

        Assert.Equal(receipt, replay);
        Assert.Equal("adopted", receipt.Outcome);
        Assert.Equal(2, await CountAsync(db, "entity"));
        Assert.Equal(1, await CountAsync(db, "component"));
        Assert.Equal(1, await CountAsync(db, "containment"));
        Assert.Equal(1, await CountAsync(db, "relationship"));
        Assert.Equal(2, await CountAsync(db, "system_ecs_entity"));
        Assert.Equal(1, await CountAsync(db, "system_ecs_component"));
        Assert.Equal(1, await CountAsync(db, "system_ecs_containment"));
        Assert.Equal(1, await CountAsync(db, "system_ecs_relationship"));
        Assert.Equal(1, await CountAsync(db, "system_legacy_state_adoption"));

        var migrated = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking().SingleAsync();
        var edge = await db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking().SingleAsync();
        Assert.Equal("fixture.stats", migrated.QualifiedTypeId);
        Assert.Equal("{\"value\":7}", migrated.Data);
        Assert.Equal("fixture.knows", edge.QualifiedKind);
        Assert.Equal("{\"strength\":2}", edge.Data);

        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var entityStore = new SqliteEntityComponentStore(db, setup.Registry, setup.Schema);
        var edgeStore = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        var entities = await entityStore.ListEntitiesAsync("adopted-space", null, 10);
        var component = await entityStore.GetComponentAsync("adopted-space", "actor", "fixture.stats");
        var containments = await edgeStore.ListContainmentsAsync("adopted-space");
        var relationships = await edgeStore.ListRelationshipsAsync("adopted-space");
        Assert.Equal(["actor", "target"], entities.Entities.Select(value => value.EntityId));
        Assert.Equal("{\"value\":7}", component!.ValueJson);
        Assert.Equal("actor", Assert.Single(containments).ContainedEntityId);
        Assert.Equal("{\"strength\":2}", Assert.Single(relationships).DataJson);
    }

    [Fact]
    public async Task Legacy_change_after_preview_requires_a_new_preview_and_writes_nothing()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db);
        var context = Context("1123456789abcdef0123456789abcdef");
        await setup.Service.PreviewAsync(setup.Request, context);
        var legacy = await db.Components.SingleAsync();
        legacy.Data = "{\"value\":8}";
        legacy.Revision++;
        legacy.UpdatedAt = legacy.UpdatedAt.AddSeconds(1);
        await db.SaveChangesAsync();

        var stale = await Assert.ThrowsAsync<LegacyStateAdoptionException>(() =>
            setup.Service.AdoptAsync(setup.Request, context));

        Assert.Equal("DRY_RUN_STALE", stale.Code);
        Assert.Equal(0, await CountAsync(db, "system_state_space"));
        Assert.Null(await new OperationLog(db).GetAsync(context.RequestToken));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Mapping_must_be_exact_owned_and_schema_valid()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db);
        var missing = setup.Request with { ComponentMappings = [] };
        var incomplete = await Assert.ThrowsAsync<LegacyStateAdoptionException>(() =>
            setup.Service.PreviewAsync(missing, Context("2123456789abcdef0123456789abcdef")));
        Assert.Equal("COMPONENT_MAPPING_INCOMPLETE", incomplete.Code);

        var legacy = await db.Components.SingleAsync();
        legacy.Data = "{\"value\":\"wrong\"}";
        await db.SaveChangesAsync();
        var invalid = await Assert.ThrowsAsync<LegacyStateAdoptionException>(() =>
            setup.Service.PreviewAsync(setup.Request, Context("3123456789abcdef0123456789abcdef")));
        Assert.Equal("COMPONENT_VALUE_INVALID", invalid.Code);
        Assert.Equal(0, await CountAsync(db, "system_state_space"));
    }

    [Fact]
    public async Task Soft_deleted_entities_and_their_retained_graph_rows_are_not_adopted()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db);
        var now = DateTime.UtcNow;
        db.Entities.Add(new Entity
        {
            Id = "deleted-fixture", Name = "Deleted fixture", CreatedAt = now.AddMinutes(-2),
            DeletedAt = now.AddMinutes(-1)
        });
        db.Components.Add(new Component
        {
            EntityId = "deleted-fixture", DefinitionId = "stats", Data = "{\"value\":\"invalid\"}",
            Revision = 1, CreatedAt = now.AddMinutes(-2), UpdatedAt = now.AddMinutes(-1)
        });
        db.Containments.Add(new Containment
        {
            ContainerId = "target", ContainedId = "deleted-fixture", Slot = "retained",
            CreatedAt = now.AddMinutes(-2)
        });
        db.Relationships.Add(new Relationship
        {
            FromEntityId = "deleted-fixture", ToEntityId = "target", Kind = "knows",
            Data = "{\"strength\":99}", CreatedAt = now.AddMinutes(-2)
        });
        await db.SaveChangesAsync();
        var context = Context("5123456789abcdef0123456789abcdef");

        var preview = await setup.Service.PreviewAsync(setup.Request, context);
        var receipt = await setup.Service.AdoptAsync(setup.Request, context);

        Assert.Equal(new LegacyStateInventory(2, 1, 1, 1,
            preview.Inventory.SourceFingerprint, preview.Inventory.EvidenceFingerprint), preview.Inventory);
        Assert.Equal(preview.Inventory, receipt.Inventory);
        Assert.Equal(3, await CountAsync(db, "entity"));
        Assert.Equal(2, await CountAsync(db, "component"));
        Assert.Equal(2, await CountAsync(db, "containment"));
        Assert.Equal(2, await CountAsync(db, "relationship"));
        Assert.Equal(2, await CountAsync(db, "system_ecs_entity"));
        Assert.Equal(1, await CountAsync(db, "system_ecs_component"));
        Assert.Equal(1, await CountAsync(db, "system_ecs_containment"));
        Assert.Equal(1, await CountAsync(db, "system_ecs_relationship"));
        Assert.DoesNotContain(await db.Set<ApplicationEcsEntityRecord>().AsNoTracking().ToListAsync(),
            value => value.Id == "deleted-fixture");
    }

    [Fact]
    public async Task Explicit_closed_scope_adopts_only_the_selected_legacy_graph()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db);
        var now = DateTime.UtcNow;
        db.Entities.Add(new Entity { Id = "unselected", Name = "Unselected", CreatedAt = now });
        await db.SaveChangesAsync();

        var scoped = setup.Request with { EntityIds = ["actor", "target"] };
        var context = Context("6123456789abcdef0123456789abcdef");
        var preview = await setup.Service.PreviewAsync(scoped, context);
        var receipt = await setup.Service.AdoptAsync(scoped, context);

        Assert.Equal(2, preview.Inventory.EntityCount);
        Assert.Equal(preview.Inventory, receipt.Inventory);
        var migrated = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == "adopted-space").Select(value => value.Id).ToArrayAsync();
        Assert.Equal(["actor", "target"], migrated.OrderBy(value => value));
    }

    [Fact]
    public async Task Explicit_scope_must_include_both_ends_of_an_active_edge()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db);
        var scoped = setup.Request with { EntityIds = ["actor"] };

        var failure = await Assert.ThrowsAsync<LegacyStateAdoptionException>(() =>
            setup.Service.PreviewAsync(scoped, Context("7123456789abcdef0123456789abcdef")));

        Assert.Equal("ENTITY_SCOPE_NOT_CLOSED", failure.Code);
        Assert.Equal(0, await CountAsync(db, "system_ecs_entity"));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_binding_graph_and_adoption_evidence()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db);
        var context = Context("4123456789abcdef0123456789abcdef");
        await setup.Service.PreviewAsync(setup.Request, context);
        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var failing = new LegacyStateAdoptionService(db, setup.Applications, setup.Activations,
            setup.Registry, setup.Schema, stateSpaces, new FailingOperationLog());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.AdoptAsync(setup.Request, context));

        Assert.Null(stateSpaces.Get("adopted-space"));
        Assert.Equal(0, await CountAsync(db, "system_ecs_entity"));
        Assert.Equal(0, await CountAsync(db, "system_ecs_component"));
        Assert.Equal(0, await CountAsync(db, "system_ecs_containment"));
        Assert.Equal(0, await CountAsync(db, "system_ecs_relationship"));
        Assert.Equal(0, await CountAsync(db, "system_legacy_state_adoption"));
        Assert.Null(await new OperationLog(db).GetAsync(context.RequestToken));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    public void Dispose() => _fixture.Dispose();

    private static async Task<Setup> SetupAsync(DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        var app = ApplicationIdentifier.Parse("fixture");
        var application = applications.Register(new(app, "Fixture", "Neutral adoption fixture.", []));
        var active = new ActiveApplicationManifest(app, 1, application.Revision, application.Fingerprint,
            new string('B', 64), new string('C', 64), new string('D', 64), new string('E', 64),
            new string('F', 64), "coverage-v1", true, [], [], "activation-operation", DateTime.UtcNow);
        var schema = new BoundedJsonSchemaValidator();
        var registry = new SqliteComponentTypeRegistry(db, schema);
        var type = registry.Define(new(app, "fixture.stats",
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}},\"required\":[\"value\"],\"additionalProperties\":false}"));
        var now = DateTime.UtcNow.AddMinutes(-1);
        db.Entities.AddRange(
            new Entity { Id = "actor", Name = "Actor", CreatedAt = now },
            new Entity { Id = "target", Name = "Target", CreatedAt = now });
        db.ComponentDefinitions.Add(new ComponentDefinition
        {
            Id = "stats", Name = "Stats", Description = "Fixture stats.", Schema = "{}",
            CreatedAt = now, UpdatedAt = now
        });
        db.Components.Add(new Component
        {
            EntityId = "actor", DefinitionId = "stats", Data = "{\"value\":7}", Revision = 3,
            CreatedAt = now, UpdatedAt = now
        });
        db.Containments.Add(new Containment
        {
            ContainerId = "target", ContainedId = "actor", Slot = "near", CreatedAt = now
        });
        db.Relationships.Add(new Relationship
        {
            FromEntityId = "actor", ToEntityId = "target", Kind = "knows",
            Data = "{\"strength\":2}", CreatedAt = now
        });
        await db.SaveChangesAsync();

        var activations = new StaticActivation(active);
        var service = new LegacyStateAdoptionService(db, applications, activations,
            registry, schema, new SqliteStateSpaceRegistry(db, applications), new OperationLog(db));
        var request = new LegacyStateAdoptionRequest("adopted-space", app, active.ActivationFingerprint,
            [new("stats", new(type.QualifiedId, type.Version, type.SchemaHash))],
            [new("knows", "fixture.knows")]);
        return new(service, request, applications, activations, registry, schema);
    }

    private static LegacyStateAdoptionContext Context(string token) => new(token,
        "Adopt a neutral legacy graph.", ["procedure.system.use"], new(
            "principal." + new string('a', 64), "test", "modify", "system.private-host",
            "legacy-adoption-test", true, "PRIVATE_OPERATOR_ALLOWED"));

    private static async Task<long> CountAsync(DantesRoleplayDbContext db, string table)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed record Setup(
        LegacyStateAdoptionService Service,
        LegacyStateAdoptionRequest Request,
        SqliteApplicationRegistry Applications,
        StaticActivation Activations,
        SqliteComponentTypeRegistry Registry,
        BoundedJsonSchemaValidator Schema);
    private sealed class StaticActivation(ActiveApplicationManifest active) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == active.ApplicationId ? active : null;
    }

    private sealed class FailingOperationLog : IOperationLog
    {
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Operation?>(null);
        public Task<Operation> RecordAsync(
            string tool, string summary, bool success, string intent = "", string subject = "",
            IEnumerable<string>? proceduresCited = null, string error = "",
            bool consumesReadEvidence = false, CancellationToken cancellationToken = default,
            string mechanicId = "", int? mechanicVersion = null, long? seed = null,
            string projectionJson = "", string guardEvidenceJson = "", string id = "") =>
            throw new InvalidOperationException("Injected audit failure.");
        public Task<IReadOnlyList<Operation>> RecentAsync(
            int limit = 20, bool failuresOnly = false, string? tool = null, string? subject = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Operation>>([]);
        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
