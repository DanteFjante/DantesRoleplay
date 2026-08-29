using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.Projections.Tests;

public sealed class ProjectionMaterializationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Theory]
    [InlineData("{\"$defs\":{\"value\":{\"type\":\"object\",\"properties\":{\"score\":{\"type\":\"integer\"}}}},\"$ref\":\"#/$defs/value\"}", "/score", true)]
    [InlineData("{\"allOf\":[{\"type\":\"object\",\"properties\":{\"score\":{\"type\":\"integer\"}}}]}", "/score", true)]
    [InlineData("{\"anyOf\":[{\"properties\":{\"score\":{\"type\":\"integer\"}}},{\"properties\":{\"score\":{\"type\":\"string\"}}}]}", "/score", true)]
    [InlineData("{\"anyOf\":[{\"properties\":{\"score\":{\"type\":\"integer\"}}},{\"properties\":{\"other\":{\"type\":\"string\"}}}]}", "/score", false)]
    [InlineData("{\"type\":\"array\",\"prefixItems\":[{\"properties\":{\"score\":{\"type\":\"integer\"}}}]}", "/0/score", true)]
    public void Schema_path_discovery_follows_the_bounded_profile(string schema, string pointer, bool expected) =>
        Assert.Equal(expected, ProjectionSchemaPath.Exists(schema, pointer));

    [Fact]
    public void Impact_service_traverses_exact_fields_projections_and_whole_component_conservatively()
    {
        var setup = Setup("impact-projection", "impact-space");
        var type = setup.Types.Define(new(setup.Application, "impact-projection.stats",
            "{\"type\":\"object\",\"properties\":{\"strength\":{\"type\":\"integer\"},\"dexterity\":{\"type\":\"integer\"}}}"));
        var component = Ref(type);
        var strength = setup.Registry.Define(new(setup.Application, "impact-projection.strength-view",
            "{\"type\":\"integer\"}", [new("stats", "subject", component)], [],
            [new("stats", "/strength", "")]));
        var attack = setup.Registry.Define(new(setup.Application, "impact-projection.attack-view",
            "{\"type\":\"integer\"}", [],
            [new("strength", strength.Reference, new Dictionary<string, string> { ["subject"] = "subject" })],
            [new("strength", "", "")]));
        var dexterity = setup.Registry.Define(new(setup.Application, "impact-projection.dexterity-view",
            "{\"type\":\"integer\"}", [new("stats", "subject", component)], [],
            [new("stats", "/dexterity", "")]));
        var reader = new SqliteProjectionImpactSnapshotReader(setup.Db);
        var service = new ProjectionImpactService(new SqliteApplicationRegistry(setup.Db), reader);

        var inventory = service.Analyze(setup.Application);
        var repeated = service.Analyze(setup.Application);
        var field = service.Analyze(setup.Application,
            $"component:{type.QualifiedId}@{type.Version}#/strength");
        var direct = service.Analyze(setup.Application,
            $"component:{type.QualifiedId}@{type.Version}#/strength", transitive: false);
        var whole = service.Analyze(setup.Application,
            $"component:{type.QualifiedId}@{type.Version}");

        Assert.Equal(inventory.GraphFingerprint, repeated.GraphFingerprint);
        Assert.Equal(5, inventory.Nodes.Count);
        Assert.Equal(3, inventory.Edges.Count);
        Assert.Equal(
            [(strength.QualifiedId, 1), (attack.QualifiedId, 2)],
            field.Dependents.Select(value => (value.Node.QualifiedId, value.Depth)).ToArray());
        Assert.Equal(strength.QualifiedId, Assert.Single(direct.Dependents).Node.QualifiedId);
        Assert.Equal("component", whole.Root!.Kind);
        Assert.Equal(
            [dexterity.QualifiedId, strength.QualifiedId],
            whole.Dependents.Where(value => value.Depth == 1).Select(value => value.Node.QualifiedId)
                .Order(StringComparer.Ordinal).ToArray());
        Assert.Contains(whole.Dependents, value => value.Node.QualifiedId == attack.QualifiedId && value.Depth == 2);
        Assert.All(field.Dependents, value => Assert.NotEmpty(value.Reasons));
        Assert.Throws<ProjectionImpactException>(() => service.Analyze(
            setup.Application, "component:impact-projection.stats@999#/strength"));
    }

    [Fact]
    public void Impact_service_returns_a_valid_empty_inventory_for_an_application_without_definitions()
    {
        var setup = Setup("empty-impact", "empty-impact-space");
        var service = new ProjectionImpactService(new SqliteApplicationRegistry(setup.Db),
            new SqliteProjectionImpactSnapshotReader(setup.Db));

        var report = service.Analyze(setup.Application);

        Assert.Empty(report.Nodes);
        Assert.Empty(report.Edges);
        Assert.Empty(report.Dependents);
        Assert.Equal(64, report.GraphFingerprint.Length);
    }

    [Fact]
    public async Task Versioned_structural_projection_materializes_dependencies_and_returns_source_evidence()
    {
        var db = _fixture.CreateContext(); var app = ApplicationIdentifier.Parse("projection-app");
        var apps = new SqliteApplicationRegistry(db); var revision = apps.Register(new(app, "Projection", "", []));
        new SqliteStateSpaceRegistry(db, apps).Create(new("projection-space", revision, new string('A', 64)));
        var schemas = new BoundedJsonSchemaValidator(); var types = new SqliteComponentTypeRegistry(db, schemas);
        var store = new SqliteEntityComponentStore(db, types, schemas); await store.CreateEntityAsync("projection-space", "orban", "Orban");
        var type = types.Define(new(app, "projection-app.stats", "{\"type\":\"object\",\"required\":[\"strength\"],\"properties\":{\"strength\":{\"type\":\"integer\"}}}"));
        var component = new EcsComponentReference(type.QualifiedId, type.Version, type.SchemaHash);
        var written = await store.AddComponentAsync(new("projection-space", "orban", component, "{\"strength\":16}", 0));
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas);
        var child = registry.Define(new(app, "projection-app.attack-input", "{\"type\":\"object\",\"required\":[\"score\"],\"properties\":{\"score\":{\"type\":\"integer\"}}}",
            [new("stats", "actor", component)], [], [new("stats", "/strength", "/score")]));
        var parent = registry.Define(new(app, "projection-app.attack-view", "{\"type\":\"object\",\"required\":[\"attackScore\"],\"properties\":{\"attackScore\":{\"type\":\"integer\"}}}",
            [], [new("input", child.Reference, new Dictionary<string, string> { ["actor"] = "actor" })], [new("input", "/score", "/attackScore")]));

        var materializer = new ProjectionMaterializer(registry, store, new SqliteStateSpaceRegistry(db, apps), schemas);
        var result = await materializer.MaterializeAsync(new("projection-space", parent.Reference, new Dictionary<string, string> { ["actor"] = "orban" }));
        Assert.Equal("{\"attackScore\":16}", result.OutputJson);
        Assert.Equal(parent.Reference, result.Projection);
        Assert.Equal(new ProjectionSourceRevision("orban", component, written.Revision), Assert.Single(result.SourceRevisions));
        Assert.Equal("projection-app.attack-view@1", Assert.Single(registry.GetImpactGraph(app).Reverse["projection-app.attack-input@1"]));
    }

    [Fact]
    public void Definition_replay_is_immutable_and_cross_application_type_is_rejected()
    {
        var db = _fixture.CreateContext(); var first = ApplicationIdentifier.Parse("first-projection"); var other = ApplicationIdentifier.Parse("other-projection");
        var apps = new SqliteApplicationRegistry(db); apps.Register(new(first, "First", "", [])); apps.Register(new(other, "Other", "", []));
        var schemas = new BoundedJsonSchemaValidator(); var types = new SqliteComponentTypeRegistry(db, schemas);
        var firstType = types.Define(new(first, "first-projection.value", "{\"type\":\"integer\"}")); var otherType = types.Define(new(other, "other-projection.value", "{\"type\":\"integer\"}"));
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas);
        var request = new ProjectionDefinitionRequest(first, "first-projection.view", "{\"type\":\"object\",\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}", [new("value", "entity", new(firstType.QualifiedId, firstType.Version, firstType.SchemaHash))], [], [new("value", "", "/value")]);
        var initial = registry.Define(request); Assert.Equal(initial.Reference, registry.Define(request).Reference);
        Assert.Throws<ArgumentException>(() => registry.Define(request with { QualifiedId = "first-projection.invalid", ComponentInputs = [new("value", "entity", new(otherType.QualifiedId, otherType.Version, otherType.SchemaHash))] }));
        Assert.Throws<ArgumentException>(() => registry.Define(request with { QualifiedId = "first-projection.missing-path", Mappings = [new("value", "/missing", "/value")] }));
    }

    [Fact]
    public void Local_reference_definitions_replay_append_and_invalid_requests_leave_no_rows()
    {
        var setup = Setup("registry-projection", "registry-space");
        var type = setup.Types.Define(new(setup.Application, "registry-projection.stats",
            "{\"$defs\":{\"stats\":{\"type\":\"object\",\"properties\":{\"score\":{\"type\":\"integer\"}}}},\"allOf\":[{\"$ref\":\"#/$defs/stats\"}]}"));
        var reference = Ref(type);
        var firstRequest = new ProjectionDefinitionRequest(setup.Application, "registry-projection.view",
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}",
            [new("stats", "subject", reference)], [], [new("stats", "/score", "/value")]);
        var first = setup.Registry.Define(firstRequest);
        Assert.Equal(first.Reference, setup.Registry.Define(firstRequest).Reference);
        var second = setup.Registry.Define(firstRequest with
        {
            OutputSchemaJson = "{\"type\":\"object\",\"properties\":{\"renamed\":{\"type\":\"integer\"}}}",
            Mappings = [new("stats", "/score", "/renamed")]
        });
        Assert.Equal(2, second.Version);

        Assert.Throws<ArgumentException>(() => setup.Registry.Define(firstRequest with
        {
            QualifiedId = "registry-projection.duplicate",
            Mappings = [new("stats", "/score", "/value"), new("stats", "/score", "/value")]
        }));
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(firstRequest with
        {
            QualifiedId = "registry-projection.unknown-input",
            Mappings = [new("absent", "/score", "/value")]
        }));
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(firstRequest with
        {
            QualifiedId = "registry-projection.excess-inputs",
            ComponentInputs = Enumerable.Range(0, 33).Select(index => new ProjectionComponentInput("input" + index, "role" + index, reference)).ToArray()
        }));
        Assert.Null(setup.Registry.Get("registry-projection.duplicate", 1));
        Assert.Null(setup.Registry.Get("registry-projection.unknown-input", 1));
        Assert.Null(setup.Registry.Get("registry-projection.excess-inputs", 1));
    }

    [Fact]
    public void Transitive_dependency_depth_is_rejected_during_registration()
    {
        var setup = Setup("depth-projection", "depth-space");
        var type = setup.Types.Define(new(setup.Application, "depth-projection.value", "{\"type\":\"integer\"}"));
        var current = setup.Registry.Define(new(setup.Application, "depth-projection.p0", "{\"type\":\"integer\"}",
            [new("value", "subject", Ref(type))], [], [new("value", "", "")]));
        for (var depth = 1; depth <= 16; depth++)
            current = setup.Registry.Define(new(setup.Application, $"depth-projection.p{depth}", "{\"type\":\"integer\"}", [],
                [new("prior", current.Reference, new Dictionary<string, string> { ["subject"] = "subject" })], [new("prior", "", "")]));

        var rejectedId = "depth-projection.p17";
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(new(setup.Application, rejectedId, "{\"type\":\"integer\"}", [],
            [new("prior", current.Reference, new Dictionary<string, string> { ["subject"] = "subject" })], [new("prior", "", "")])));
        Assert.Null(setup.Registry.Get(rejectedId, 1));
    }

    [Fact]
    public async Task Multi_component_materialization_uses_one_batch_and_fails_closed_for_missing_output_and_scope()
    {
        var setup = Setup("batch-projection", "batch-space");
        await setup.Store.CreateEntityAsync("batch-space", "complete", "Complete");
        await setup.Store.CreateEntityAsync("batch-space", "missing", "Missing");
        var first = setup.Types.Define(new(setup.Application, "batch-projection.first", "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"integer\"}}}"));
        var second = setup.Types.Define(new(setup.Application, "batch-projection.second", "{\"type\":\"object\",\"properties\":{\"b\":{\"type\":\"integer\"}}}"));
        await setup.Store.AddComponentAsync(new("batch-space", "complete", Ref(first), "{\"a\":1}", 0));
        await setup.Store.AddComponentAsync(new("batch-space", "complete", Ref(second), "{\"b\":2}", 0));
        var definition = setup.Registry.Define(new(setup.Application, "batch-projection.view",
            "{\"type\":\"object\",\"required\":[\"a\",\"b\"],\"additionalProperties\":false,\"properties\":{\"a\":{\"type\":\"integer\"},\"b\":{\"type\":\"integer\"}}}",
            [new("first", "subject", Ref(first)), new("second", "subject", Ref(second))], [],
            [new("first", "/a", "/a"), new("second", "/b", "/b")]));
        var counting = new CountingStore(setup.Store);
        var materializer = new ProjectionMaterializer(setup.Registry, counting, setup.StateSpaces, setup.Schemas);
        var result = await materializer.MaterializeAsync(new("batch-space", definition.Reference,
            new Dictionary<string, string> { ["subject"] = "complete" }));
        Assert.Equal("{\"a\":1,\"b\":2}", result.OutputJson);
        Assert.Equal(1, counting.BatchReads);
        Assert.Equal(2, result.SourceRevisions.Count);

        await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new("batch-space", definition.Reference,
            new Dictionary<string, string> { ["subject"] = "missing" })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new("batch-space", definition.Reference,
            new Dictionary<string, string>())));

        var otherApp = ApplicationIdentifier.Parse("other-batch-projection");
        var apps = new SqliteApplicationRegistry(setup.Db); var otherRevision = apps.Register(new(otherApp, "Other", "", []));
        setup.StateSpaces.Create(new("other-batch-space", otherRevision, new string('B', 64)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new("other-batch-space", definition.Reference,
            new Dictionary<string, string> { ["subject"] = "complete" })));

        var invalidOutput = setup.Registry.Define(new(setup.Application, "batch-projection.invalid-output", "{\"type\":\"string\"}",
            [new("first", "subject", Ref(first))], [], [new("first", "/a", "")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(new("batch-space", invalidOutput.Reference,
            new Dictionary<string, string> { ["subject"] = "complete" })));
    }

    private ProjectionSetup Setup(string applicationId, string stateSpaceId)
    {
        var db = _fixture.CreateContext(); var application = ApplicationIdentifier.Parse(applicationId);
        var applications = new SqliteApplicationRegistry(db); var revision = applications.Register(new(application, applicationId, "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications); spaces.Create(new(stateSpaceId, revision, new string('A', 64)));
        var schemas = new BoundedJsonSchemaValidator(); var types = new SqliteComponentTypeRegistry(db, schemas);
        var store = new SqliteEntityComponentStore(db, types, schemas);
        return new(db, application, spaces, schemas, types, store, new SqliteProjectionDefinitionRegistry(db, types, schemas));
    }

    private static EcsComponentReference Ref(RegisteredComponentTypeVersion type) => new(type.QualifiedId, type.Version, type.SchemaHash);

    private sealed record ProjectionSetup(DantesRoleplayDbContext Db, ApplicationIdentifier Application, SqliteStateSpaceRegistry StateSpaces,
        BoundedJsonSchemaValidator Schemas, SqliteComponentTypeRegistry Types, SqliteEntityComponentStore Store, SqliteProjectionDefinitionRegistry Registry);

    private sealed class CountingStore(IEntityComponentStore inner) : IEntityComponentStore
    {
        public int BatchReads { get; private set; }
        public Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name, CancellationToken cancellationToken = default) => inner.CreateEntityAsync(stateSpaceId, entityId, name, cancellationToken);
        public Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId, CancellationToken cancellationToken = default) => inner.GetEntityAsync(stateSpaceId, entityId, cancellationToken);
        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId, string? afterEntityId, int limit, CancellationToken cancellationToken = default) => inner.ListEntitiesAsync(stateSpaceId, afterEntityId, limit, cancellationToken);
        public Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision, CancellationToken cancellationToken = default) => inner.DeleteEntityAsync(stateSpaceId, entityId, expectedRevision, cancellationToken);
        public Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId, string qualifiedTypeId, CancellationToken cancellationToken = default) => inner.GetComponentAsync(stateSpaceId, entityId, qualifiedTypeId, cancellationToken);
        public async Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId, IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default) { BatchReads++; return await inner.GetComponentsAsync(stateSpaceId, locators, cancellationToken); }
        public Task<EcsComponentDiscoveryPage> ListComponentsAsync(string stateSpaceId, string entityId, string? afterQualifiedTypeId, int limit, CancellationToken cancellationToken = default) => inner.ListComponentsAsync(stateSpaceId, entityId, afterQualifiedTypeId, limit, cancellationToken);
        public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => inner.AddComponentAsync(write, cancellationToken);
        public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => inner.SetComponentAsync(write, cancellationToken);
        public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write, CancellationToken cancellationToken = default) => inner.MergeComponentAsync(write, cancellationToken);
        public Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId, EcsComponentReference type, int expectedRevision, CancellationToken cancellationToken = default) => inner.RemoveComponentAsync(stateSpaceId, entityId, type, expectedRevision, cancellationToken);
    }

    public void Dispose() => _fixture.Dispose();
}
