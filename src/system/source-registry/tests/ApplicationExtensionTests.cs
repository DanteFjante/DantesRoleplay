using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Sources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SourceRegistry.Tests;

public sealed class ApplicationExtensionTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("sample");

    [Fact]
    public void Registration_is_immutable_and_compilation_produces_one_deterministic_order()
    {
        var sources = Sources("base", "alpha", "beta");
        var registry = new InMemoryApplicationExtensionRegistry(sources);
        var alpha = registry.Register(new(Application, "alpha", "Alpha", "First extension.", "homebrew", ["alpha"],
            ["sample.alpha"], [], [], [], true));
        var beta = registry.Register(new(Application, "beta", "Beta", "Higher extension.", "third-party", ["beta"],
            ["sample.beta"], ["alpha"], [], ["alpha"], true));

        Assert.Equal(alpha, registry.Register(alpha));
        var compiled = ApplicationExtensionSetCompiler.Compile(Application, registry.For(Application));

        Assert.Equal(["beta", "alpha", "base"], compiled.PriorityOrder);
        Assert.Equal(compiled.Fingerprint,
            ApplicationExtensionSetCompiler.Compile(Application, [beta, alpha]).Fingerprint);
        Assert.Throws<InvalidOperationException>(() => registry.Register(alpha with
        {
            Description = "Changed metadata."
        }));
    }

    [Fact]
    public void Compilation_rejects_missing_dependencies_conflicts_and_ambiguous_precedence()
    {
        var sources = Sources("alpha", "beta");
        var registry = new InMemoryApplicationExtensionRegistry(sources);
        registry.Register(new(Application, "alpha", "Alpha", "Alpha extension.", "homebrew", ["alpha"],
            ["sample.alpha"], ["beta"], [], [], true));
        registry.Register(new(Application, "beta", "Beta", "Beta extension.", "compatibility", ["beta"],
            ["sample.beta"], [], ["alpha"], [], true));

        Assert.Equal("EXTENSION_DEPENDENCY_MISSING", Assert.Throws<ApplicationExtensionSetException>(() =>
            ApplicationExtensionSetCompiler.Compile(Application, registry.For(Application), ["alpha"])).Code);
        Assert.Equal("EXTENSION_CONFLICT", Assert.Throws<ApplicationExtensionSetException>(() =>
            ApplicationExtensionSetCompiler.Compile(Application, registry.For(Application))).Code);

        var ambiguous = new[]
        {
            new ApplicationExtensionRegistration(Application, "alpha", "Alpha", "Alpha extension.", "homebrew", ["alpha"],
                ["sample.alpha"], [], [], [], true),
            new ApplicationExtensionRegistration(Application, "beta", "Beta", "Beta extension.", "third-party", ["beta"],
                ["sample.beta"], [], [], [], true)
        };
        Assert.Equal("EXTENSION_PRIORITY_AMBIGUOUS", Assert.Throws<ApplicationExtensionSetException>(() =>
            ApplicationExtensionSetCompiler.Compile(Application, ambiguous)).Code);
    }

    [Fact]
    public async Task Existing_registration_upgrades_with_safe_legacy_presentation_metadata()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"extension-upgrade-{Guid.NewGuid():n}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.MigrateAsync("20260831171544_EcsRoleConstraintsAndSystemWeb");
            var application = ApplicationIdentifier.Parse("legacy-app");
            new SqliteApplicationRegistry(db).Register(new(application, "Legacy", "Upgrade fixture.", []));
            new SqliteSourceRegistry(db).Register(new(application, "legacy-source", "workspace",
                "legacy/**/*", SourceTrust.Trusted, 0, "legacy-source"));
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO system_application_extension
                    (ApplicationId, ExtensionId, Description, SourceIdsJson, NamespaceIdsJson,
                     DependenciesJson, ConflictsWithJson, HigherPriorityThanJson, OverridesBase,
                     RegistrationFingerprint, CreatedAtUtc)
                VALUES
                    ('legacy-app', 'legacy-extension', 'Legacy extension.', '["legacy-source"]',
                     '["legacy-app.extension.legacy"]', '[]', '[]', '[]', 0,
                     'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', CURRENT_TIMESTAMP);
                """);

            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            var upgraded = new SqliteApplicationExtensionRegistry(db, new SqliteSourceRegistry(db))
                .Get(application, "legacy-extension");

            Assert.NotNull(upgraded);
            Assert.Equal("legacy-extension", upgraded.DisplayName);
            Assert.Equal(ApplicationExtensionClassifications.ThirdParty, upgraded.Classification);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static InMemorySourceRegistry Sources(params string[] ids)
    {
        var result = new InMemorySourceRegistry();
        foreach (var (id, index) in ids.Select((value, index) => (value, index)))
            result.Register(new(Application, id, "workspace", $"{id}/**/*", SourceTrust.Trusted,
                index, id));
        return result;
    }
}
