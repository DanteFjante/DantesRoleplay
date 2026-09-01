using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests.Ecs;

public sealed class EcsRoleConstraintTests : IDisposable
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PageSchema = """
    {"type":"object","additionalProperties":false,"required":["slug","title"],"properties":{"slug":{"type":"string"},"title":{"type":"string"}},
     "x-dantes-entity-roles":["page"],"x-dantes-role-constraints":[{
       "id":"fixture.page.slug.unique","scope":"application-publication","selector":{"semanticRole":"page"},
       "minimumEnabled":0,"maximumEnabled":null,"requires":[],"uniqueKeys":[{"name":"slug","source":{"semanticRole":"page"},"jsonPointer":"/slug"}]}]}
    """;
    private const string IndexSchema = """
    {"type":"object","additionalProperties":false,"x-dantes-entity-roles":["index-page"],"x-dantes-role-constraints":[{
      "id":"fixture.index-page.cardinality","scope":"application-publication","selector":{"semanticRole":"index-page"},
      "minimumEnabled":0,"maximumEnabled":1,"requires":[{"semanticRole":"page"}],"uniqueKeys":[]}]}
    """;

    private readonly string _database = Path.Combine(Path.GetTempPath(), $"dantes-ecs-role-{Guid.NewGuid():N}.db");

    [Fact]
    public void Policy_parser_supports_roles_component_selectors_cardinality_requirements_and_keys()
    {
        var policy = EcsComponentRolePolicyParser.Parse(PageSchema);

        Assert.Equal(["page"], policy.SemanticRoles);
        var constraint = Assert.Single(policy.Constraints);
        Assert.Equal(EcsStateSpaceScope.ApplicationPublication, constraint.Scope);
        Assert.Equal(EcsEntitySelectorKind.SemanticRole, constraint.Selector.Kind);
        Assert.Null(constraint.MaximumEnabled);
        Assert.Equal("/slug", Assert.Single(constraint.UniqueKeys).JsonPointer);

        var componentPolicy = EcsComponentRolePolicyParser.Parse("""
        {"type":"object","x-dantes-role-constraints":[{"id":"fixture.component.max","scope":"runtime-state-space",
        "selector":{"componentTypeId":"fixture.marker"},"minimumEnabled":1,"maximumEnabled":2,
        "requires":[{"componentTypeId":"fixture.base"}],"uniqueKeys":[]}]}
        """);
        var component = Assert.Single(componentPolicy.Constraints);
        Assert.Equal(EcsEntitySelectorKind.Component, component.Selector.Kind);
        Assert.Equal(1, component.MinimumEnabled);
        Assert.Equal(2, component.MaximumEnabled);
    }

    [Fact]
    public async Task Publication_policy_enforces_index_cardinality_slug_uniqueness_and_page_requirement()
    {
        await using var setup = await OpenAsync();
        var first = await setup.Entities.CreateEntityAsync("publication", "first", "First");
        var second = await setup.Entities.CreateEntityAsync("publication", "second", "Second");
        var incomplete = await setup.Entities.CreateEntityAsync("publication", "incomplete", "Incomplete");
        var requirement = await Assert.ThrowsAsync<EcsRoleConstraintException>(() =>
            setup.Entities.AddComponentAsync(Write("incomplete", setup.Index, "{}", 0, raw: true)));
        Assert.Equal("ROLE_REQUIREMENT_VIOLATION", requirement.Code);

        await setup.Entities.AddComponentAsync(Write("first", setup.Page, "home", 0));
        await setup.Entities.AddComponentAsync(Write("first", setup.Index, "{}", 0, raw: true));
        await setup.Entities.AddComponentAsync(Write("second", setup.Page, "rules", 0));

        var maximum = await Assert.ThrowsAsync<EcsRoleConstraintException>(() =>
            setup.Entities.AddComponentAsync(Write("second", setup.Index, "{}", 0, raw: true)));
        Assert.Equal("ROLE_CARDINALITY_VIOLATION", maximum.Code);

        var duplicate = await Assert.ThrowsAsync<EcsRoleConstraintException>(async () =>
        {
            var duplicateEntity = await setup.Entities.CreateEntityAsync("publication", "duplicate", "Duplicate");
            _ = duplicateEntity;
            await setup.Entities.AddComponentAsync(Write("duplicate", setup.Page, "home", 0));
        });
        Assert.Equal("ROLE_UNIQUENESS_VIOLATION", duplicate.Code);

        Assert.True(await setup.Entities.DeleteEntityAsync("publication", "first", first.Revision));
        await setup.Entities.AddComponentAsync(Write("second", setup.Index, "{}", 0, raw: true));
        var enable = await Assert.ThrowsAsync<EcsRoleConstraintException>(() =>
            setup.Lifecycle.SetEntityEnabledAsync("publication", "first", true, first.Revision + 1));
        Assert.Equal("ROLE_CARDINALITY_VIOLATION", enable.Code);
        Assert.False((await setup.Lifecycle.GetEntityAsync("publication", "first"))!.IsEnabled);
        Assert.True((await setup.Lifecycle.GetEntityAsync("publication", "second"))!.IsEnabled);
        Assert.True((await setup.Lifecycle.GetEntityAsync("publication", "incomplete"))!.IsEnabled);
        _ = second;
        _ = incomplete;
    }

    [Fact]
    public async Task Effect_batch_validates_once_after_all_component_changes()
    {
        await using var setup = await OpenAsync();
        var applier = new ApplicationEcsEffectApplier(setup.Db, setup.Entities, setup.Spaces,
            new OperationLog(setup.Db), roleConstraints: setup.Constraints);
        var result = await applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = "publication",
            Effects =
            [
                new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = "home", Name = "Home" },
                new() { Type = ApplicationEcsEffectType.ComponentAdd, EntityId = "home", ComponentType = Reference(setup.Index), DataJson = "{}" },
                new() { Type = ApplicationEcsEffectType.ComponentAdd, EntityId = "home", ComponentType = Reference(setup.Page), DataJson = "{\"slug\":\"home\",\"title\":\"Home\"}" }
            ]
        });

        Assert.True(result.Applied);
        Assert.NotNull(await setup.Entities.GetComponentAsync("publication", "home", setup.Index.QualifiedId));
    }

    [Fact]
    public async Task Concurrent_index_creation_serializes_before_constraint_validation()
    {
        await using (var setup = await OpenAsync())
        {
            await setup.Entities.CreateEntityAsync("publication", "one", "One");
            await setup.Entities.CreateEntityAsync("publication", "two", "Two");
            await setup.Entities.AddComponentAsync(Write("one", setup.Page, "one", 0));
            await setup.Entities.AddComponentAsync(Write("two", setup.Page, "two", 0));
        }

        await using var first = await OpenExistingAsync();
        await using var second = await OpenExistingAsync();
        var attempts = await Task.WhenAll(
            AttemptAsync(() => first.Entities.AddComponentAsync(Write("one", first.Index, "{}", 0, raw: true))),
            AttemptAsync(() => second.Entities.AddComponentAsync(Write("two", second.Index, "{}", 0, raw: true))));

        Assert.Single(attempts, value => value is null);
        Assert.Single(attempts, value => value == "ROLE_CARDINALITY_VIOLATION");
        await using var verify = await OpenExistingAsync();
        var count = (await verify.Entities.ListComponentsAsync("publication", "one", null, 10)).Components
            .Count(value => value.Type.QualifiedTypeId == verify.Index.QualifiedId)
            + (await verify.Entities.ListComponentsAsync("publication", "two", null, 10)).Components
            .Count(value => value.Type.QualifiedTypeId == verify.Index.QualifiedId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Only_one_publication_space_is_allowed_but_runtime_spaces_ignore_publication_policy()
    {
        await using var setup = await OpenAsync();
        Assert.Throws<InvalidOperationException>(() => setup.Spaces.Create(new(
            "other-publication", setup.Revision, Hash, Hash, EcsStateSpaceScope.ApplicationPublication)));
        setup.Spaces.Create(new("runtime", setup.Revision, Hash, Hash, EcsStateSpaceScope.Runtime));
        var first = await setup.Entities.CreateEntityAsync("runtime", "one", "One");
        var second = await setup.Entities.CreateEntityAsync("runtime", "two", "Two");
        await setup.Entities.AddComponentAsync(new("runtime", "one", Reference(setup.Page),
            "{\"slug\":\"same\",\"title\":\"One\"}", 0));
        await setup.Entities.AddComponentAsync(new("runtime", "two", Reference(setup.Page),
            "{\"slug\":\"same\",\"title\":\"Two\"}", 0));
        await setup.Entities.AddComponentAsync(new("runtime", "one", Reference(setup.Index), "{}", 0));
        await setup.Entities.AddComponentAsync(new("runtime", "two", Reference(setup.Index), "{}", 0));
        _ = first;
        _ = second;
    }

    private async Task<Fixture> OpenAsync()
    {
        var fixture = await Fixture.OpenAsync(_database, migrate: true);
        try
        {
            fixture.Register();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private Task<Fixture> OpenExistingAsync() => Fixture.OpenAsync(_database, migrate: false);

    private static EcsComponentWrite Write(
        string entityId,
        RegisteredComponentTypeVersion type,
        string slugOrJson,
        int expectedRevision,
        bool raw = false) => new("publication", entityId, Reference(type), raw
            ? slugOrJson : $"{{\"slug\":\"{slugOrJson}\",\"title\":\"{slugOrJson}\"}}", expectedRevision);

    private static EcsComponentReference Reference(RegisteredComponentTypeVersion type) =>
        new(type.QualifiedId, type.Version, type.SchemaHash);

    private static async Task<string?> AttemptAsync(Func<Task<EcsComponentView>> action)
    {
        try { await action(); return null; }
        catch (EcsRoleConstraintException exception) { return exception.Code; }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = _database + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(DantesRoleplayDbContext db)
        {
            Db = db;
            Schemas = new BoundedJsonSchemaValidator();
            Applications = new SqliteApplicationRegistry(db);
            Types = new SqliteComponentTypeRegistry(db, Schemas);
            Spaces = new SqliteStateSpaceRegistry(db, Applications);
            Constraints = new SqliteEcsRoleConstraintValidator(db);
            Entities = new SqliteEntityComponentStore(db, Types, Schemas, Constraints);
            Lifecycle = new SqliteEcsLifecycleStore(db, Constraints);
        }

        public DantesRoleplayDbContext Db { get; }
        public BoundedJsonSchemaValidator Schemas { get; }
        public SqliteApplicationRegistry Applications { get; }
        public SqliteComponentTypeRegistry Types { get; }
        public SqliteStateSpaceRegistry Spaces { get; }
        public SqliteEcsRoleConstraintValidator Constraints { get; }
        public SqliteEntityComponentStore Entities { get; }
        public SqliteEcsLifecycleStore Lifecycle { get; }
        public ApplicationRevision Revision { get; private set; } = null!;
        public RegisteredComponentTypeVersion Page { get; private set; } = null!;
        public RegisteredComponentTypeVersion Index { get; private set; } = null!;

        public static async Task<Fixture> OpenAsync(string path, bool migrate)
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={path};Cache=Shared;Default Timeout=30").Options;
            var db = new DantesRoleplayDbContext(options);
            if (migrate) await db.Database.MigrateAsync();
            var fixture = new Fixture(db);
            if (!migrate) fixture.Load();
            return fixture;
        }

        public void Register()
        {
            var app = ApplicationIdentifier.Parse("fixture");
            Revision = Applications.Register(new(app, "Fixture", "ECS constraint fixture.", []));
            Page = Types.Define(new(app, "fixture.page", PageSchema));
            Index = Types.Define(new(app, "fixture.index-page", IndexSchema));
            Spaces.Create(new("publication", Revision, Hash, Hash, EcsStateSpaceScope.ApplicationPublication));
        }

        private void Load()
        {
            var app = ApplicationIdentifier.Parse("fixture");
            Revision = Applications.Get(app)!;
            Page = Types.GetLatest("fixture.page")!;
            Index = Types.GetLatest("fixture.index-page")!;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
