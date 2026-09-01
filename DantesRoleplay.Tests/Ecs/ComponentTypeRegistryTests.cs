using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.Ecs.Tests;

public sealed class ComponentTypeRegistryTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public void Component_type_versions_persist_reload_append_and_replay_normalized_schema()
    {
        var application = ApplicationIdentifier.Parse("fixture-app");
        using (var db = _fixture.CreateContext())
        {
            new SqliteApplicationRegistry(db).Register(new(application, "Fixture", "", []));
            var registry = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
            var first = registry.Define(new(application, "fixture-app.stats", "{ \"type\" : \"object\" }"));
            var replay = registry.Define(new(application, "fixture-app.stats", "{\"type\":\"object\"}"));
            var second = registry.Define(new(application, "fixture-app.stats", "{\"type\":\"array\"}"));

            Assert.Equal(1, first.Version);
            Assert.Equal(first, replay);
            Assert.Equal(2, second.Version);
            Assert.NotEqual(first.SchemaHash, second.SchemaHash);
            Assert.Equal(SystemJsonSchemaProfile.Version1Id, first.ProfileId);
        }

        using (var db = _fixture.CreateContext())
        {
            var registry = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
            var reloaded = Assert.IsType<RegisteredComponentTypeVersion>(registry.Get("fixture-app.stats", 2));
            Assert.Equal("fixture-app", reloaded.Owner.Value);
            Assert.Equal("{\"type\":\"array\"}", reloaded.SchemaJson);
        }
    }

    [Fact]
    public void Invalid_schema_unknown_application_and_cross_namespace_leave_no_type()
    {
        var application = ApplicationIdentifier.Parse("fixture-app");
        using var db = _fixture.CreateContext();
        new SqliteApplicationRegistry(db).Register(new(application, "Fixture", "", []));
        var registry = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());

        Assert.Throws<ArgumentException>(() => registry.Define(new(application, "other.stats", "true")));
        Assert.Throws<ArgumentException>(() => registry.Define(new(application, "fixture-app.stats", "{\"format\":\"date\"}")));
        Assert.Throws<ArgumentException>(() => registry.Define(new(ApplicationIdentifier.Parse("missing-app"), "missing-app.stats", "true")));
        Assert.Null(registry.Get("fixture-app.stats", 1));
        Assert.Null(registry.Get("missing-app.stats", 1));
    }

    [Fact]
    public void Latest_component_type_discovery_is_application_scoped_and_version_exact()
    {
        using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var fixture = ApplicationIdentifier.Parse("fixture-app");
        var other = ApplicationIdentifier.Parse("other-app");
        applications.Register(new(fixture, "Fixture", "", []));
        applications.Register(new(other, "Other", "", []));
        var registry = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        registry.Define(new(fixture, "fixture-app.zulu", "true"));
        registry.Define(new(fixture, "fixture-app.alpha", "true"));
        var alphaTwo = registry.Define(new(fixture, "fixture-app.alpha", "false"));
        registry.Define(new(other, "other-app.alpha", "true"));

        var first = registry.ListLatestPage(fixture, null, 1);
        Assert.Equal(["fixture-app.alpha"], first.ComponentTypes.Select(value => value.QualifiedId));
        Assert.Equal(2, Assert.Single(first.ComponentTypes).Version);
        Assert.Equal(alphaTwo.SchemaHash, Assert.Single(first.ComponentTypes).SchemaHash);
        Assert.Equal("fixture-app.alpha", first.NextQualifiedId);
        Assert.Equal(["fixture-app.zulu"], registry.ListLatestPage(fixture, first.NextQualifiedId, 1)
            .ComponentTypes.Select(value => value.QualifiedId));
        Assert.Throws<InvalidOperationException>(() => registry.ListLatestPage(fixture, "fixture-app.missing", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.ListLatestPage(fixture, null, 0));
    }

    [Fact]
    public async Task Forward_migration_preserves_preexisting_data_and_refuses_destructive_downgrade()
    {
        var file = Path.Combine(Path.GetTempPath(), $"component-types-{Guid.NewGuid():n}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite($"Data Source={file}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            var migrations = db.Database.GetMigrations().ToList();
            var target = Assert.Single(migrations, x => x.EndsWith("_ComponentTypeSchemaRegistry", StringComparison.Ordinal));
            var previous = migrations[migrations.IndexOf(target) - 1];
            await db.GetService<IMigrator>().MigrateAsync(previous);

            await db.Database.ExecuteSqlRawAsync("CREATE TABLE pre_component_type_evidence (Id TEXT PRIMARY KEY, Value TEXT NOT NULL);");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO pre_component_type_evidence VALUES ('1', 'retained');");
            await db.GetService<IMigrator>().MigrateAsync();
            Assert.Equal("retained", await ScalarAsync(db, "SELECT Value FROM pre_component_type_evidence WHERE Id = '1';"));
            Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='system_component_type_version';"));

            var application = ApplicationIdentifier.Parse("fixture-app");
            new SqliteApplicationRegistry(db).Register(new(application, "Fixture", "", []));
            new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator())
                .Define(new(application, "fixture-app.stats", "true"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM system_component_type_version;"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Profile_v2_migration_preserves_v1_rows_accepts_v2_and_refuses_lossy_downgrade()
    {
        var file = Path.Combine(Path.GetTempPath(), $"component-profile-v2-{Guid.NewGuid():n}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite($"Data Source={file}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            var migrations = db.Database.GetMigrations().ToList();
            var target = Assert.Single(migrations, value =>
                value.EndsWith("_ApplicationSchemaProfileV2", StringComparison.Ordinal));
            var previous = migrations[migrations.IndexOf(target) - 1];
            await db.GetService<IMigrator>().MigrateAsync(previous);

            var application = ApplicationIdentifier.Parse("fixture-app");
            new SqliteApplicationRegistry(db).Register(new(application, "Fixture", "", []));
            var compiled = new BoundedJsonSchemaValidator().Compile("{\"type\":\"string\"}");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO system_component_type (QualifiedId, ApplicationId, CreatedAtUtc)
                VALUES ({"fixture-app.note"}, {application.Value}, {DateTime.UtcNow});
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO system_component_type_version
                    (QualifiedId, Version, ProfileId, SchemaJson, SchemaHash, CreatedAtUtc)
                VALUES ({"fixture-app.note"}, {1}, {compiled.ProfileId}, {compiled.NormalizedSchema},
                    {compiled.SchemaHash}, {DateTime.UtcNow});
                """);

            await db.GetService<IMigrator>().MigrateAsync(target);
            await db.GetService<IMigrator>().MigrateAsync();
            var registry = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
            var reloaded = Assert.IsType<RegisteredComponentTypeVersion>(registry.Get("fixture-app.note", 1));
            Assert.Equal(SystemJsonSchemaProfile.Version1Id, reloaded.ProfileId);
            Assert.Equal(compiled.SchemaHash, reloaded.SchemaHash);
            var version2 = registry.Define(new(application, "fixture-app.digest",
                "{\"type\":\"string\",\"pattern\":\"^[0-9a-f]{64}$\"}"));
            Assert.Equal(SystemJsonSchemaProfile.Version2Id, version2.ProfileId);

            await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(previous));
            db.ChangeTracker.Clear();
            Assert.Equal(2L, await ScalarAsync(db, "SELECT count(*) FROM system_component_type_version;"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
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
}
