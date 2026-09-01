using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Security.Cryptography;
using System.Text;

namespace DantesRoleplay.Tests;

public sealed class CatalogNamespaceTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"catalog-namespaces-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Qualified_ids_use_namespace_segments_as_directories()
    {
        Assert.Equal("mechanics/dnd2024/character/ability-score.md",
            CatalogLayout.MechanicMarkdown("ignored", "dnd2024.character.ability-score"));
        Assert.Equal("components/shared.json", CatalogLayout.Component("shared"));
        Assert.Equal("namespaces/dnd2024/character/_namespace.json",
            CatalogLayout.Namespace("dnd2024.character"));
    }

    [Theory]
    [InlineData("game.con.rule")]
    [InlineData("game.rules.com1")]
    public void Reserved_device_names_are_rejected_in_every_namespace_segment(string id)
    {
        Assert.Throws<InvalidOperationException>(() => CatalogLayout.Component(id));
        Assert.Throws<InvalidOperationException>(() => CatalogLayout.Namespace(id));
    }

    [Fact]
    public void Every_database_write_is_blocked_after_registry_adoption_when_the_kind_is_wrong()
    {
        using var db = _fixture.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        registry.Register(new CatalogNamespaceRegistration("dnd2024", "dnd2024", "D&D 2024 authored content.",
            [CatalogNamespaceKinds.Mechanic], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed test namespace."));
        registry.Register(new CatalogNamespaceRegistration("dnd2024.characters", "dnd2024", "Character state only.",
            [CatalogNamespaceKinds.Entity], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed test namespace."));

        db.Mechanics.Add(new Mechanic
        {
            Id = "dnd2024.characters.attack",
            Category = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var exception = Assert.Throws<CatalogNamespaceException>(() => db.SaveChanges());
        Assert.Equal("NAMESPACE_KIND_FORBIDDEN", exception.Code);
    }

    [Fact]
    public void Normal_writes_require_reviewed_namespaces_and_review_is_parent_first()
    {
        using var db = _fixture.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        registry.Register(new CatalogNamespaceRegistration("pending", "pending", "Pending records.",
            [CatalogNamespaceKinds.Mechanic]));

        var unreviewed = Assert.Throws<CatalogNamespaceException>(() =>
            registry.RequireRecordNamespace("pending.attack", CatalogNamespaceKinds.Mechanic));
        Assert.Equal("NAMESPACE_UNREVIEWED", unreviewed.Code);
        db.Mechanics.Add(new Mechanic
        {
            Id = "pending.attack",
            Category = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        var save = Assert.Throws<CatalogNamespaceException>(() => db.SaveChanges());
        Assert.Equal("NAMESPACE_UNREVIEWED", save.Code);
        db.ChangeTracker.Clear();

        registry.Register(new CatalogNamespaceRegistration("pending.child", "pending", "Child records.",
            [CatalogNamespaceKinds.Mechanic]));
        var parentFirst = Assert.Throws<CatalogNamespaceException>(() => registry.SetReview(
            "pending.child", CatalogNamespaceReviewStatuses.Reviewed, "Reviewed child."));
        Assert.Equal("NAMESPACE_PARENT_UNREVIEWED", parentFirst.Code);

        registry.SetReview("pending", CatalogNamespaceReviewStatuses.Reviewed, "Reviewed parent.");
        registry.SetReview("pending.child", CatalogNamespaceReviewStatuses.Reviewed, "Reviewed child.");
        Assert.Equal("pending.child", registry.RequireRecordNamespace(
            "pending.child.attack", CatalogNamespaceKinds.Mechanic).Id);
    }

    [Fact]
    public void Search_uses_descriptions_and_disabled_namespaces_are_hidden_by_default()
    {
        using var db = _fixture.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        registry.Register(new CatalogNamespaceRegistration("thalorien", "thalorien", "Thalorien world-building records.",
            [CatalogNamespaceKinds.Document], ["world lore"]));
        registry.Register(new CatalogNamespaceRegistration("thalorien.people", "thalorien", "People in Thalorien.",
            [CatalogNamespaceKinds.Document]));

        Assert.Equal("thalorien", Assert.Single(registry.Search("world-building")).Namespace.Id);
        registry.SetEnabled("thalorien", enabled: false);
        Assert.Empty(registry.Search("world-building"));
        Assert.Single(registry.Search("world-building", includeDisabled: true));
        Assert.Null(registry.Get("thalorien.people"));
    }

    [Fact]
    public void Explicit_overlay_profiles_require_registered_resolution_keys_and_real_dominance()
    {
        using var db = _fixture.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        new SqliteApplicationRegistry(db).Register(new(
            ApplicationIdentifier.Parse("game"), "Game", "Neutral overlay fixture.", []));
        foreach (var id in new[] { "core", "campaign", "unrelated" })
            registry.Register(new CatalogNamespaceRegistration(id, id, $"{id} records.", [CatalogNamespaceKinds.Document]));
        var profile = registry.RegisterProfile(new(
            "game", "campaign-default", "Campaign records override shared defaults."));
        var key = registry.RegisterResolutionKey(new(
            "game", profile.ProfileId, "rule", CatalogNamespaceKinds.Document,
            "The logical rule identity shared by overlay candidates."));
        registry.Register(new CatalogNamespaceOverlayRule(
            "game", profile.ProfileId, "campaign", "core", null));

        var core = new CatalogResolutionCandidate("core.rule", "core", CatalogNamespaceKinds.Document, "rule");
        var campaign = new CatalogResolutionCandidate("campaign.rule", "campaign", CatalogNamespaceKinds.Document, "rule");
        var resolved = registry.Resolve("game", profile.ProfileId, [core, campaign]);
        Assert.Equal(profile.ProfileId, resolved.ProfileId);
        Assert.Equal(key, resolved.ResolutionKey);
        Assert.Equal(campaign, resolved.Winner);
        Assert.Equal([core], resolved.Shadowed);
        Assert.Equal(profile, Assert.Single(registry.ProfilesForApplication("game")));
        Assert.Equal(key, Assert.Single(registry.ResolutionKeysForProfile("game", profile.ProfileId)));

        var unrelated = new CatalogResolutionCandidate("unrelated.rule", "unrelated", CatalogNamespaceKinds.Document, "rule");
        var exception = Assert.Throws<CatalogNamespaceException>(() =>
            registry.Resolve("game", profile.ProfileId, [core, unrelated]));
        Assert.Equal("NAMESPACE_OVERLAY_AMBIGUOUS", exception.Code);

        var unknownKey = core with { ResolutionKey = "unregistered" };
        var missing = Assert.Throws<CatalogNamespaceException>(() =>
            registry.Resolve("game", profile.ProfileId, [unknownKey]));
        Assert.Equal("NAMESPACE_RESOLUTION_KEY_UNKNOWN", missing.Code);

        var isolated = registry.RegisterProfile(new(
            "game", "no-overrides", "A profile with no namespace precedence."));
        registry.RegisterResolutionKey(new(
            "game", isolated.ProfileId, "rule", CatalogNamespaceKinds.Document, "The same logical rule identity."));
        var isolatedAmbiguity = Assert.Throws<CatalogNamespaceException>(() =>
            registry.Resolve("game", isolated.ProfileId, [core, campaign]));
        Assert.Equal("NAMESPACE_OVERLAY_AMBIGUOUS", isolatedAmbiguity.Code);

        registry.Register(new CatalogNamespaceOverlayRule(
            "game", isolated.ProfileId, "campaign", "core", CatalogNamespaceKinds.Document));
        var cycle = Assert.Throws<CatalogNamespaceException>(() => registry.Register(
            new CatalogNamespaceOverlayRule(
                "game", isolated.ProfileId, "core", "campaign", CatalogNamespaceKinds.Document)));
        Assert.Equal("NAMESPACE_OVERLAY_CYCLE", cycle.Code);
        Assert.Single(registry.RulesForProfile("game", isolated.ProfileId));
    }

    [Fact]
    public void Catalog_search_automatically_resolves_active_extensions_without_changing_exact_inspection()
    {
        using var db = _fixture.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        var application = ApplicationIdentifier.Parse("game");
        new SqliteApplicationRegistry(db).Register(new(application, "Game", "Overlay search fixture.", []));
        foreach (var id in new[] { "game", "game.rules", "game.homebrew", "game.homebrew.rules" })
            registry.Register(new CatalogNamespaceRegistration(id, "game", $"{id} records.", [CatalogNamespaceKinds.Document],
                ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed search fixture."));
        registry.RegisterProfile(new("game", "homebrew", "Homebrew rules override the base rules."));
        registry.RegisterResolutionKey(new("game", "homebrew", "rules.fireball",
            CatalogNamespaceKinds.Document, "The logical Fireball rule."));
        registry.Register(new CatalogNamespaceOverlayRule("game", "homebrew", "game.homebrew.rules", "game.rules",
            CatalogNamespaceKinds.Document));

        static CatalogRecordDefinition Record(string id, string source)
        {
            var content = $$"""{"id":"{{id}}"}""";
            return new("game", "document", id, "Fireball", $"{source} Fireball rule.", [], [], "rules",
                "active", 1, content, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
                source, $"catalog/{source}/fireball.json");
        }
        var manifest = CatalogNavigationManifest.Create(application, new string('A', 64), "catalog-lexical-v1",
            [new("game", "Game", "Overlay search fixture.")],
            [new("game", "", "Game", "Overlay search fixture.", CatalogDescriptionStatus.Authored),
                new("game", "rules", "Rules", "Rule records.", CatalogDescriptionStatus.Authored)],
            [Record("game.rules.fireball", "base"), Record("game.homebrew.rules.fireball", "homebrew")]);
        var resolution = CatalogExtensionResolutionContext.Create(application, new string('B', 64),
            [new("homebrew", "Homebrew", "Fixture homebrew.", "homebrew", ["fixture-homebrew"],
                ["game.homebrew"], [], true)]);
        var navigator = new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("catalog-overlay-search-test-key-32-bytes")), resolution);

        Assert.Equal("game.homebrew.rules.fireball",
            Assert.Single(navigator.Search(new(application, "fireball")).Records).Record.QualifiedId);
        var resolved = navigator.Search(new(application, "fireball", IncludeShadowed: true));
        Assert.Equal(2, resolved.Records.Count);
        var evidence = Assert.Single(resolved.ResolutionDiagnostics!);
        Assert.Equal("rules.fireball", evidence.ResolutionKey);
        Assert.Equal(["game.rules.fireball"], evidence.ShadowedQualifiedIds);
        Assert.Equal("game.rules.fireball",
            navigator.Inspect(new(application, "game", "game.rules.fireball")).Summary.QualifiedId);
    }

    [Fact]
    public async Task Overlay_profile_migration_preserves_implicit_rules_in_a_named_legacy_profile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"catalog-overlay-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={path}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260831134852_CatalogNamespaceReview");
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO system_catalog_namespace
                    (Id, ParentId, Owner, Description, AllowedKindsJson, AliasesJson,
                     ReviewStatus, ReviewNote, ReviewedAtUtc, CreatedAtUtc, UpdatedAtUtc, DisabledAtUtc)
                VALUES
                    ('core', NULL, 'fixture', 'Core records.', '["document"]', '[]',
                     'reviewed', 'Reviewed.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL),
                    ('campaign', NULL, 'fixture', 'Campaign records.', '["document"]', '[]',
                     'reviewed', 'Reviewed.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL);
                INSERT INTO system_catalog_namespace_overlay
                    (ApplicationId, HigherNamespaceId, LowerNamespaceId, RecordKind, CreatedAtUtc)
                VALUES ('game', 'campaign', 'core', '', CURRENT_TIMESTAMP);
                """);

            await migrator.MigrateAsync();
            var registry = new SqliteCatalogNamespaceRegistry(db);

            var profile = registry.GetProfile("game", "legacy-default");
            Assert.NotNull(profile);
            Assert.Equal("Migrated implicit namespace overlay profile.", profile.Description);
            Assert.Equal("campaign", Assert.Single(
                registry.RulesForProfile("game", "legacy-default")).HigherNamespaceId);
            Assert.Empty(registry.ResolutionKeysForProfile("game", "legacy-default"));
            await db.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task A_new_export_is_strict_and_self_describes_all_used_namespaces()
    {
        await using var db = _fixture.CreateContext();
        await new ContentHashBackfill(db).RunAsync();
        await new MechanicSeeder(new MechanicStore(db)).SeedAsync();

        await new CatalogExporter(db).ExportAsync(_root, new CatalogExportOptions(RulesOnly: true));
        var contents = await CatalogReader.ReadAsync(_root);

        Assert.Equal(CatalogManifest.CurrentSchemaVersion, contents.Manifest!.SchemaVersion);
        Assert.NotEmpty(contents.Namespaces);
        Assert.All(contents.Namespaces, value =>
            Assert.Equal(CatalogNamespaceReviewStatuses.NeedsReview, value.ReviewStatus));
        Assert.All(contents.Mechanics, mechanic => Assert.True(File.Exists(CatalogLayout.ToFileSystemPath(
            _root, CatalogLayout.MechanicMarkdown(mechanic.Category, mechanic.Id)))));
    }

    [Fact]
    public async Task Namespace_only_import_never_imports_catalog_records()
    {
        await using var db = _fixture.CreateContext();
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var importer = new CatalogImporter(
            db,
            new MechanicStore(db),
            new ProcedureStore(db),
            new WorldStore(db));

        var preview = await importer.ApplyNamespacesOnlyAsync(RepositoryCatalog(), dryRun: true);
        Assert.Equal(contents.Namespaces.Count, preview.Created);
        Assert.Empty(new SqliteCatalogNamespaceRegistry(db).List(includeDisabled: true));

        var applied = await importer.ApplyNamespacesOnlyAsync(RepositoryCatalog());
        Assert.Equal(contents.Namespaces.Count, applied.Created);
        Assert.Equal(contents.Namespaces.Count,
            new SqliteCatalogNamespaceRegistry(db).List(includeDisabled: true).Count);
        Assert.Empty(db.Mechanics);
        Assert.Empty(db.Entities);

        var replay = await importer.ApplyNamespacesOnlyAsync(RepositoryCatalog());
        Assert.Equal(contents.Namespaces.Count, replay.Unchanged);
        Assert.Equal(0, replay.Created);
        Assert.Equal(0, replay.Updated);
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
