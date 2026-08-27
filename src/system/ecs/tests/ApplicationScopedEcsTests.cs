using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.Ecs.Tests;

public sealed class ApplicationScopedEcsTests : IDisposable
{
    private static readonly string Manifest = new('A', 64);
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Every_json_kind_round_trips_against_its_exact_registered_type()
    {
        var setup = Setup("fixture-app", "space-one");
        await setup.Store.CreateEntityAsync("space-one", "entity", "Fixture");
        var values = new[]
        {
            ("object", "{\"type\":\"object\"}", "{\"value\":1}"),
            ("array", "{\"type\":\"array\"}", "[1,2]"),
            ("string", "{\"type\":\"string\"}", "\"text\""),
            ("integer", "{\"type\":\"integer\"}", "7"),
            ("number", "{\"type\":\"number\"}", "1.50"),
            ("boolean", "{\"type\":\"boolean\"}", "true"),
            ("null", "{\"type\":\"null\"}", "null")
        };

        foreach (var (name, schema, value) in values)
        {
            var type = setup.Types.Define(new(setup.Application, $"fixture-app.{name}", schema));
            var reference = new EcsComponentReference(type.QualifiedId, type.Version, type.SchemaHash);
            var written = await setup.Store.AddComponentAsync(new("space-one", "entity", reference, value, 0));
            Assert.Equal(value, written.ValueJson);
            Assert.Equal(value, (await setup.Store.GetComponentAsync("space-one", "entity", reference.QualifiedTypeId))!.ValueJson);
        }
    }

    [Fact]
    public async Task Writes_validate_contract_revision_and_merge_without_partial_mutation()
    {
        var setup = Setup("fixture-app", "space-one");
        await setup.Store.CreateEntityAsync("space-one", "entity", "Fixture");
        var type = setup.Types.Define(new(setup.Application, "fixture-app.stats", "{\"type\":\"object\",\"required\":[\"a\",\"b\"]}"));
        var reference = new EcsComponentReference(type.QualifiedId, type.Version, type.SchemaHash);
        var first = await setup.Store.AddComponentAsync(new("space-one", "entity", reference, "{\"a\":1,\"b\":2}", 0));
        Assert.Equal(1, first.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.AddComponentAsync(new("space-one", "entity", reference, "{\"a\":1,\"b\":2}", 0)));

        var second = await setup.Store.SetComponentAsync(new("space-one", "entity", reference, "{\"a\":1,\"b\":2}", 1));
        var third = await setup.Store.MergeComponentAsync(new("space-one", "entity", reference, "{\"c\":3}", 2));
        Assert.Equal(3, third.Revision);
        Assert.Equal("{\"a\":1,\"b\":2,\"c\":3}", third.ValueJson);
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Store.MergeComponentAsync(new("space-one", "entity", reference, "[]", 3)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.SetComponentAsync(new("space-one", "entity", reference, "{\"a\":1,\"b\":2}", 2)));
        Assert.Equal(third, await setup.Store.GetComponentAsync("space-one", "entity", reference.QualifiedTypeId));

        Assert.True(await setup.Store.RemoveComponentAsync("space-one", "entity", reference, 3));
        Assert.False(await setup.Store.RemoveComponentAsync("space-one", "entity", reference, 3));
        var recreated = await setup.Store.SetComponentAsync(new("space-one", "entity", reference, "{\"a\":1,\"b\":2}", 0));
        Assert.Equal(1, recreated.Revision);

        var wrongHash = reference with { SchemaHash = new string('B', 64) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.SetComponentAsync(new("space-one", "entity", wrongHash, "{\"a\":1,\"b\":2}", 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Store.SetComponentAsync(new("space-one", "entity", reference, "{\"a\":1}", 1)));
        Assert.Equal(recreated, await setup.Store.GetComponentAsync("space-one", "entity", reference.QualifiedTypeId));
    }

    [Fact]
    public async Task State_spaces_and_application_contracts_are_isolated()
    {
        var first = Setup("first-app", "first-space");
        var second = Setup("second-app", "second-space");
        await first.Store.CreateEntityAsync("first-space", "same-id", "First");
        await second.Store.CreateEntityAsync("second-space", "same-id", "Second");
        var firstType = first.Types.Define(new(first.Application, "first-app.marker", "{\"type\":\"string\"}"));
        var secondType = second.Types.Define(new(second.Application, "second-app.marker", "{\"type\":\"string\"}"));
        var firstReference = new EcsComponentReference(firstType.QualifiedId, firstType.Version, firstType.SchemaHash);
        var secondReference = new EcsComponentReference(secondType.QualifiedId, secondType.Version, secondType.SchemaHash);

        await first.Store.AddComponentAsync(new("first-space", "same-id", firstReference, "\"first\"", 0));
        await second.Store.AddComponentAsync(new("second-space", "same-id", secondReference, "\"second\"", 0));
        Assert.Null(await first.Store.GetComponentAsync("first-space", "same-id", secondReference.QualifiedTypeId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.Store.AddComponentAsync(new("first-space", "same-id", secondReference, "\"wrong\"", 0)));
        Assert.Equal("\"first\"", (await first.Store.GetComponentAsync("first-space", "same-id", firstReference.QualifiedTypeId))!.ValueJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.Store.GetEntityAsync("missing-space", "same-id"));
    }

    [Fact]
    public async Task State_space_component_writes_admit_exact_direct_base_owner_and_reject_unrelated_owner()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var baseApplication = ApplicationIdentifier.Parse("base-app");
        var derivedApplication = ApplicationIdentifier.Parse("derived-app");
        var unrelatedApplication = ApplicationIdentifier.Parse("unrelated-app");
        applications.Register(new(baseApplication, "Base", "", []));
        var derivedRevision = applications.Register(new(
            derivedApplication, "Derived", "", [baseApplication]));
        applications.Register(new(unrelatedApplication, "Unrelated", "", []));
        new SqliteStateSpaceRegistry(db, applications).Create(new(
            "derived-space", derivedRevision, Manifest));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var baseType = types.Define(new(baseApplication, "base-app.marker", "{\"type\":\"string\"}"));
        var unrelatedType = types.Define(new(
            unrelatedApplication, "unrelated-app.marker", "{\"type\":\"string\"}"));
        var store = new SqliteEntityComponentStore(db, types, schemas);
        await store.CreateEntityAsync("derived-space", "fixture", "Fixture");

        var written = await store.AddComponentAsync(new(
            "derived-space", "fixture",
            new(baseType.QualifiedId, baseType.Version, baseType.SchemaHash), "\"base\"", 0));

        Assert.Equal("\"base\"", written.ValueJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddComponentAsync(new(
            "derived-space", "fixture",
            new(unrelatedType.QualifiedId, unrelatedType.Version, unrelatedType.SchemaHash),
            "\"unrelated\"", 0)));
    }

    [Fact]
    public void State_space_bindings_are_registered_immutable_and_pre_activation_only()
    {
        var setup = Setup("fixture-app", "space-one");
        var binding = new StateSpaceBinding("space-one", setup.Revision, Manifest);
        var stateSpaces = new SqliteStateSpaceRegistry(setup.Db, new SqliteApplicationRegistry(setup.Db));
        Assert.Equal("space-one", stateSpaces.Create(binding).StateSpaceId);
        Assert.Equal(binding.ManifestFingerprint, stateSpaces.Create(binding).ManifestFingerprint);
        Assert.Throws<InvalidOperationException>(() => stateSpaces.Create(new StateSpaceBinding("space-one", setup.Revision, new string('B', 64))));

        var unknown = ApplicationIdentifier.Parse("unknown-app");
        var unknownBinding = new StateSpaceBinding("unknown-space", new(unknown, 1, Manifest, []), Manifest);
        Assert.Throws<ArgumentException>(() => stateSpaces.Create(unknownBinding));
    }

    [Fact]
    public async Task Ecs_discovery_pages_are_scoped_stable_and_hide_deleted_entities()
    {
        var setup = Setup("fixture-app", "space-one");
        var stateSpaces = new SqliteStateSpaceRegistry(setup.Db, new SqliteApplicationRegistry(setup.Db));
        stateSpaces.Create(new("space-two", setup.Revision, Manifest));
        var otherApplication = ApplicationIdentifier.Parse("other-app");
        var applications = new SqliteApplicationRegistry(setup.Db);
        var otherRevision = applications.Register(new(otherApplication, "Other", "", []));
        stateSpaces.Create(new("other-space", otherRevision, Manifest));

        var spaces = stateSpaces.ListPage(setup.Application, null, 1);
        Assert.Equal(["space-one"], spaces.StateSpaces.Select(value => value.StateSpaceId));
        Assert.Equal("space-one", spaces.NextStateSpaceId);
        Assert.Equal(["space-two"], stateSpaces.ListPage(setup.Application, spaces.NextStateSpaceId, 1)
            .StateSpaces.Select(value => value.StateSpaceId));
        Assert.Throws<InvalidOperationException>(() => stateSpaces.ListPage(setup.Application, "other-space", 1));

        await setup.Store.CreateEntityAsync("space-one", "charlie", "Charlie");
        await setup.Store.CreateEntityAsync("space-one", "alpha", "Alpha");
        await setup.Store.CreateEntityAsync("space-one", "bravo", "Bravo");
        await setup.Store.DeleteEntityAsync("space-one", "bravo", 1);
        var entityPage = await setup.Store.ListEntitiesAsync("space-one", null, 1);
        Assert.Equal(["alpha"], entityPage.Entities.Select(value => value.EntityId));
        Assert.Equal("alpha", entityPage.NextEntityId);
        Assert.Equal(["charlie"], (await setup.Store.ListEntitiesAsync("space-one", entityPage.NextEntityId, 2))
            .Entities.Select(value => value.EntityId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.ListEntitiesAsync("space-one", "bravo", 1));

        var alpha = setup.Types.Define(new(setup.Application, "fixture-app.alpha", "true"));
        var zulu = setup.Types.Define(new(setup.Application, "fixture-app.zulu", "true"));
        await setup.Store.AddComponentAsync(new("space-one", "alpha", new(alpha.QualifiedId, alpha.Version, alpha.SchemaHash), "1", 0));
        await setup.Store.AddComponentAsync(new("space-one", "alpha", new(zulu.QualifiedId, zulu.Version, zulu.SchemaHash), "2", 0));
        var componentPage = await setup.Store.ListComponentsAsync("space-one", "alpha", null, 1);
        Assert.Equal(["fixture-app.alpha"], componentPage.Components.Select(value => value.Type.QualifiedTypeId));
        Assert.Equal("fixture-app.alpha", componentPage.NextQualifiedTypeId);
        Assert.Equal(["fixture-app.zulu"], (await setup.Store.ListComponentsAsync(
            "space-one", "alpha", componentPage.NextQualifiedTypeId, 1)).Components.Select(value => value.Type.QualifiedTypeId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Store.ListComponentsAsync(
            "space-one", "charlie", "fixture-app.alpha", 1));
    }

    [Fact]
    public async Task Forward_migration_preserves_legacy_world_rows_and_refuses_destructive_downgrade()
    {
        var file = Path.Combine(Path.GetTempPath(), $"application-ecs-{Guid.NewGuid():n}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite($"Data Source={file}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            var migrations = db.Database.GetMigrations().ToList();
            var target = Assert.Single(migrations, x => x.EndsWith("_ApplicationScopedEcs", StringComparison.Ordinal));
            var previous = migrations[migrations.IndexOf(target) - 1];
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO entity (Id, Name, CreatedAt) VALUES ('legacy', 'Legacy', '2026-01-01T00:00:00Z');");

            await db.GetService<IMigrator>().MigrateAsync();
            Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM entity WHERE Id = 'legacy';"));
            Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='system_ecs_component';"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM entity WHERE Id = 'legacy';"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private SetupResult Setup(string applicationId, string stateSpaceId)
    {
        var db = _fixture.CreateContext();
        var application = ApplicationIdentifier.Parse(applicationId);
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(application, applicationId, "", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new(stateSpaceId, revision, Manifest));
        return new(db, application, revision, new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()),
            new SqliteEntityComponentStore(db, new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()), new BoundedJsonSchemaValidator()));
    }

    private static async Task<object?> ScalarAsync(DantesRoleplayDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public void Dispose() => _fixture.Dispose();

    private sealed record SetupResult(
        DantesRoleplayDbContext Db,
        ApplicationIdentifier Application,
        ApplicationRevision Revision,
        SqliteComponentTypeRegistry Types,
        SqliteEntityComponentStore Store);
}
