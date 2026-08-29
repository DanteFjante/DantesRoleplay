using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.ApplicationRegistry.Tests;

public sealed class ApplicationRegistryPersistenceTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public void Application_registration_persists_is_idempotent_and_rejects_changed_input()
    {
        var baseId = ApplicationIdentifier.Parse("base-app");
        var applicationId = ApplicationIdentifier.Parse("fixture-app");
        var baseRegistration = new ApplicationRegistration(baseId, "Base", "", []);
        var registration = new ApplicationRegistration(applicationId, "Fixture", "Registry fixture", [baseId]);

        using (var db = _fixture.CreateContext())
        {
            var registry = new SqliteApplicationRegistry(db);
            registry.Register(baseRegistration);
            var written = registry.Register(registration);
            var replay = registry.Register(registration);

            Assert.Equal(1, written.Revision);
            Assert.Equal(written.ApplicationId, replay.ApplicationId);
            Assert.Equal(written.Fingerprint, replay.Fingerprint);
            Assert.Equal(written.BaseApplications, replay.BaseApplications);
        }

        using (var db = _fixture.CreateContext())
        {
            var registry = new SqliteApplicationRegistry(db);
            var read = Assert.IsType<ApplicationRevision>(registry.Get(applicationId));
            Assert.Equal("fixture-app", read.ApplicationId.Value);
            Assert.Equal([baseId], read.BaseApplications);
            Assert.Equal("Fixture", registry.Describe(applicationId)!.DisplayName);
            Assert.Equal([baseId, applicationId], registry.List(10).Select(value => value.Id).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => registry.List(101));
            Assert.Throws<InvalidOperationException>(() => registry.Register(registration with { Description = "changed" }));
            Assert.Null(registry.Get(ApplicationIdentifier.Parse("missing-app")));
        }
    }

    [Fact]
    public void Application_discovery_pages_are_stable_bounded_and_reject_stale_keys()
    {
        using var db = _fixture.CreateContext();
        var registry = new SqliteApplicationRegistry(db);
        foreach (var id in new[] { "charlie-app", "alpha-app", "bravo-app" })
            registry.Register(new(ApplicationIdentifier.Parse(id), id, "", []));

        var first = registry.ListPage(null, 2);
        Assert.Equal(["alpha-app", "bravo-app"], first.Applications.Select(value => value.Id.Value));
        Assert.Equal("bravo-app", first.NextApplicationId);

        var second = registry.ListPage(first.NextApplicationId, 2);
        Assert.Equal(["charlie-app"], second.Applications.Select(value => value.Id.Value));
        Assert.Null(second.NextApplicationId);
        Assert.Throws<InvalidOperationException>(() => registry.ListPage("missing-app", 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.ListPage(null, 101));
    }

    [Fact]
    public void Sources_and_scan_receipts_are_isolated_append_only_and_path_safe()
    {
        var applicationId = ApplicationIdentifier.Parse("fixture-app");
        var source = new SourceRegistration(applicationId, "core", "workspace", "catalog/**/*.json", SourceTrust.Trusted, 10, "component:fixture.stats");
        var fingerprint = new string('A', 64);
        var receipt = new SourceScanReceipt(applicationId, "core", 1, SourceScanStatus.Succeeded, fingerprint, DateTime.UtcNow);

        using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(applicationId, "Fixture", "", []));
        var sources = new SqliteSourceRegistry(db);
        var scans = new SqliteSourceScanReceiptStore(db);

        Assert.Equal(source, sources.Register(source));
        Assert.Equal(source, sources.Register(source));
        Assert.Single(sources.For(applicationId));
        Assert.Throws<InvalidOperationException>(() => sources.Register(source with { RelativePathOrGlob = "other/**/*.json" }));
        Assert.Throws<ArgumentException>(() => sources.Register(source with { SourceId = "unsafe", RelativePathOrGlob = "../outside/**/*.json" }));
        Assert.Equal("catalog/**/*.json", Assert.Single(sources.For(applicationId)).RelativePathOrGlob);

        Assert.Equal(receipt, scans.Record(receipt));
        Assert.Equal(receipt, scans.Record(receipt));
        Assert.Throws<InvalidOperationException>(() => scans.Record(receipt with { Generation = 3 }));
        Assert.Throws<InvalidOperationException>(() => scans.Record(receipt with { Status = SourceScanStatus.Failed }));
        Assert.Equal([receipt], scans.For(applicationId, "core"));
        Assert.Equal(receipt, scans.Latest(applicationId, "core"));
        Assert.Equal(source, sources.Get(applicationId, "core"));
        Assert.Single(sources.List(applicationId, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sources.List(applicationId, 0));
        Assert.Empty(scans.For(ApplicationIdentifier.Parse("other-app"), "core"));
        Assert.Null(scans.Latest(ApplicationIdentifier.Parse("other-app"), "core"));
    }

    [Fact]
    public async Task Forward_migration_preserves_preexisting_data_and_refuses_destructive_downgrade()
    {
        var file = Path.Combine(Path.GetTempPath(), $"application-registry-{Guid.NewGuid():n}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={file}")
                .Options;
            await using var db = new DantesRoleplayDbContext(options);
            var migrations = db.Database.GetMigrations().ToList();
            var target = Assert.Single(migrations, x => x.EndsWith("_ApplicationSourceRegistry", StringComparison.Ordinal));
            var previous = migrations[migrations.IndexOf(target) - 1];
            await db.GetService<IMigrator>().MigrateAsync(previous);

            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = "CREATE TABLE pre_slice_evidence (Id TEXT PRIMARY KEY, Value TEXT NOT NULL); INSERT INTO pre_slice_evidence VALUES ('1', 'retained');";
                await seed.ExecuteNonQueryAsync();
            }

            await db.GetService<IMigrator>().MigrateAsync();
            await using (var verify = connection.CreateCommand())
            {
                verify.CommandText = "SELECT Value FROM pre_slice_evidence WHERE Id = '1';";
                Assert.Equal("retained", await verify.ExecuteScalarAsync());
                verify.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'system_application_source_scan';";
                Assert.Equal(1L, await verify.ExecuteScalarAsync());
            }

            var applications = new SqliteApplicationRegistry(db);
            applications.Register(new(ApplicationIdentifier.Parse("fixture-app"), "Fixture", "", []));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
            Assert.NotNull(applications.Get(ApplicationIdentifier.Parse("fixture-app")));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    public void Dispose() => _fixture.Dispose();
}
