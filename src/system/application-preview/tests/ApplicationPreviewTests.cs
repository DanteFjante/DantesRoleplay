using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.ApplicationPreview.Tests;

public sealed class ApplicationPreviewTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"application-preview-{Guid.NewGuid():N}");

    [Fact]
    public async Task Preview_scans_registered_glob_is_deterministic_redacted_and_read_only()
    {
        Directory.CreateDirectory(Path.Combine(_root, "catalog"));
        var file = Path.Combine(_root, "catalog", "entry.json");
        await File.WriteAllTextAsync(file, "{\"value\":1}");
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var app = ApplicationIdentifier.Parse("fixture-app");
        applications.Register(new(app, "Fixture", "Neutral preview fixture.", []));
        sources.Register(new(app, "catalog", "workspace", "catalog/**/*.json",
            SourceTrust.Trusted, 10, "catalog"));
        var service = Service(applications, sources);
        var operationCount = await db.Operations.CountAsync();
        var scans = new SqliteSourceScanReceiptStore(db);
        var scanCount = scans.For(app, "catalog").Count;

        var first = await service.PreviewAsync(app);
        var replay = await service.PreviewAsync(app);

        Assert.True(first.IsValid);
        Assert.Equal(first.PreviewFingerprint, replay.PreviewFingerprint);
        Assert.Equal(first.Sources, replay.Sources);
        Assert.Equal(first.Winners, replay.Winners);
        Assert.Equal(first.Shadows, replay.Shadows);
        Assert.Equal(first.Problems, replay.Problems);
        Assert.Equal("catalog/entry.json", Assert.Single(first.Winners).RelativePath);
        Assert.DoesNotContain(_root, JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(operationCount, await db.Operations.CountAsync());
        Assert.Equal(scanCount, scans.For(app, "catalog").Count);

        await File.WriteAllTextAsync(file, "{\"value\":2}");
        var changed = await service.PreviewAsync(app);
        Assert.NotEqual(first.PreviewFingerprint, changed.PreviewFingerprint);
        Assert.NotEqual(first.CandidateManifestFingerprint, changed.CandidateManifestFingerprint);
    }

    [Fact]
    public async Task Exact_source_profile_is_canonical_excludes_unselected_extensions_and_rejects_unknowns()
    {
        Directory.CreateDirectory(Path.Combine(_root, "core"));
        Directory.CreateDirectory(Path.Combine(_root, "extension"));
        await File.WriteAllTextAsync(Path.Combine(_root, "core", "core.json"), "{\"core\":true}");
        await File.WriteAllTextAsync(Path.Combine(_root, "extension", "optional.json"), "{\"optional\":true}");
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var app = ApplicationIdentifier.Parse("profile-app");
        applications.Register(new(app, "Profiles", "Neutral source-profile fixture.", []));
        sources.Register(new(app, "dnd2024-core", "workspace", "core/**/*.json",
            SourceTrust.Trusted, 0, "core"));
        sources.Register(new(app, "extension.optional", "workspace", "extension/**/*.json",
            SourceTrust.Trusted, 10, "optional"));
        var service = Service(applications, sources);

        var coreOnly = await service.PreviewAsync(app, ["dnd2024-core"]);
        var extended = await service.PreviewAsync(app, ["dnd2024-core", "extension.optional"]);
        var reordered = await service.PreviewAsync(app, ["extension.optional", "dnd2024-core"]);
        var legacyAll = await service.PreviewAsync(app);

        Assert.True(coreOnly.IsValid);
        Assert.Equal("dnd2024-core", Assert.Single(coreOnly.Sources).SourceId);
        Assert.Equal("core/core.json", Assert.Single(coreOnly.Winners).RelativePath);
        Assert.DoesNotContain(coreOnly.Winners, value => value.SourceId == "extension.optional");
        Assert.True(extended.IsValid);
        Assert.Equal(2, extended.Sources.Count);
        Assert.Equal(2, extended.Winners.Count);
        Assert.NotEqual(coreOnly.PreviewFingerprint, extended.PreviewFingerprint);
        Assert.Equal(extended.PreviewFingerprint, reordered.PreviewFingerprint);
        Assert.Equal(extended.PreviewFingerprint, legacyAll.PreviewFingerprint);
        Assert.Equal(extended.Sources, reordered.Sources);

        var unknown = await Assert.ThrowsAsync<ApplicationPreviewException>(() =>
            service.PreviewAsync(app, ["dnd2024-core", "extension.unknown"]));
        var duplicate = await Assert.ThrowsAsync<ApplicationPreviewException>(() =>
            service.PreviewAsync(app, ["dnd2024-core", "dnd2024-core"]));
        Assert.Equal("SOURCE_SELECTION_UNKNOWN", unknown.Code);
        Assert.Equal("SOURCE_SELECTION_INVALID", duplicate.Code);
    }

    [Fact]
    public async Task Unknown_root_returns_closed_invalid_problem_and_unknown_application_is_typed()
    {
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var app = ApplicationIdentifier.Parse("fixture-app");
        applications.Register(new(app, "Fixture", "", []));
        sources.Register(new(app, "catalog", "missing", "catalog/**/*.json",
            SourceTrust.Trusted, 1, "catalog"));
        var service = new ApplicationPreviewService(
            applications,
            sources,
            new RegisteredSourceScanner(sources, new EmptyAllowedSourceRootResolver(), new LocalDocumentScanner()),
            new SourceOverlayResolver());

        var preview = await service.PreviewAsync(app);
        var problem = Assert.Single(preview.Problems);
        Assert.False(preview.IsValid);
        Assert.Equal("SOURCE_ROOT_UNKNOWN", problem.Code);
        Assert.Equal(string.Empty, problem.LogicalPath);
        Assert.DoesNotContain("missing", problem.Message, StringComparison.OrdinalIgnoreCase);

        var exception = await Assert.ThrowsAsync<ApplicationPreviewException>(() =>
            service.PreviewAsync(ApplicationIdentifier.Parse("unknown-app")));
        Assert.Equal("APPLICATION_UNKNOWN", exception.Code);
    }

    [Fact]
    public async Task Preview_fingerprint_binds_documents_hidden_by_an_overlay_conflict()
    {
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var app = ApplicationIdentifier.Parse("fixture-app");
        applications.Register(new(app, "Fixture", "", []));
        sources.Register(new(app, "one", "workspace", "one/**/*.json", SourceTrust.Trusted, 5, "one"));
        sources.Register(new(app, "two", "workspace", "two/**/*.json", SourceTrust.Trusted, 5, "two"));
        var scanner = new MutableScanner([
            Document(app, "one", 'A'),
            Document(app, "two", 'B')]);
        var service = new ApplicationPreviewService(applications, sources, scanner, new SourceOverlayResolver());

        var first = await service.PreviewAsync(app);
        scanner.Documents = [Document(app, "one", 'A'), Document(app, "two", 'C')];
        var changed = await service.PreviewAsync(app);

        Assert.False(first.IsValid);
        Assert.Contains(first.Problems, problem => problem.Code == "SOURCE_OVERLAY_CONFLICT");
        Assert.NotEqual(first.ScannedDocumentsFingerprint, changed.ScannedDocumentsFingerprint);
        Assert.NotEqual(first.PreviewFingerprint, changed.PreviewFingerprint);
    }

    [Fact]
    public async Task Deterministic_mechanic_conflict_blocks_activation_until_an_exact_trusted_review_exists()
    {
        var catalog = Path.Combine(_root, "catalog");
        var mechanics = Path.Combine(catalog, "mechanics");
        var reviewDirectory = Path.Combine(catalog, "governance", "anti-sprawl", "reviews");
        Directory.CreateDirectory(mechanics);
        Directory.CreateDirectory(reviewDirectory);
        var left = new MechanicFile("fixture-app.mechanic.location.create", "game.core.world.location",
            "Create location", "Creates one location shell.", "register a location", "{}",
            "return { narration: 'created', effects: [] };", "", MechanicStatus.Active);
        var right = new MechanicFile("fixture-app.mechanic.location.register", "game.core.world.location",
            "Register location", "Registers an existing location.", "register a location", "{}",
            "return { narration: 'registered', effects: [] };", "", MechanicStatus.Active);
        await WriteMechanicAsync(mechanics, "left", left);
        await WriteMechanicAsync(mechanics, "right", right);

        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var app = ApplicationIdentifier.Parse("fixture-app");
        applications.Register(new(app, "Fixture", "Neutral anti-sprawl fixture.", []));
        sources.Register(new(app, "catalog", "workspace", "catalog/**/*.*",
            SourceTrust.Trusted, 10, "catalog"));
        var roots = new Roots(_root);
        var service = new ApplicationPreviewService(applications, sources,
            new InMemoryApplicationExtensionRegistry(sources),
            new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
            new SourceOverlayResolver(), new ApplicationAntiSprawlGate(roots));

        var blocked = await service.PreviewAsync(app);
        Assert.False(blocked.IsValid);
        Assert.Contains(blocked.Problems, value => value.Code == "ANTI_SPRAWL_CONFLICT");
        var conflict = Assert.Single(blocked.AntiSprawlFindings, value => value.Blocking);
        Assert.Equal("unreviewed", conflict.ReviewState);

        var review = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            left = new { qualifiedId = left.Id, contentFingerprint = left.ContentHash },
            right = new { qualifiedId = right.Id, contentFingerprint = right.ContentHash },
            disposition = CatalogAntiSprawlDispositions.DistinctResponsibility,
            rationale = "One mechanic creates the shell; the other registers an existing shell."
        });
        await File.WriteAllTextAsync(Path.Combine(reviewDirectory, "location-responsibilities.json"), review);
        var reviewed = await service.PreviewAsync(app);
        Assert.True(reviewed.IsValid, string.Join("; ", reviewed.Problems.Select(value => value.Message)));
        Assert.Equal("reviewed", Assert.Single(reviewed.AntiSprawlFindings).ReviewState);

        right = right with { Source = "return { narration: 'revised', effects: [] };" };
        await WriteMechanicAsync(mechanics, "right", right);
        var expired = await service.PreviewAsync(app);
        Assert.False(expired.IsValid);
        Assert.Equal("stale", Assert.Single(expired.AntiSprawlFindings, value => value.Blocking).ReviewState);
    }

    [Fact]
    public async Task Duplicate_selected_extension_namespace_blocks_preview_without_requiring_a_mechanic_pair()
    {
        var app = ApplicationIdentifier.Parse("fixture-app");
        var extension = new Func<string, ApplicationExtensionRegistration>(id => new(
            app, id, id, "Namespace ownership fixture.", ApplicationExtensionClassifications.Homebrew,
            [id], ["fixture-app.mechanic.shared"], [], [], [], true));
        var extensions = new CompiledApplicationExtensionSet(app, new string('A', 64),
            [extension("alpha"), extension("beta")], ["alpha", "beta", ApplicationExtensionIdentity.Base]);

        var result = await new ApplicationAntiSprawlGate(new Roots(_root))
            .EvaluateAsync(app, [], [], extensions);

        Assert.Contains(result.Problems, value => value.Code == "ANTI_SPRAWL_NAMESPACE_CONFLICT");
    }

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ApplicationPreviewService Service(IApplicationRegistry applications, ISourceRegistry sources) => new(
        applications,
        sources,
        new RegisteredSourceScanner(sources, new Roots(_root), new LocalDocumentScanner()),
        new SourceOverlayResolver());

    private static async Task WriteMechanicAsync(string directory, string name, MechanicFile mechanic)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".md"), mechanic.ToMarkdown());
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".js"), mechanic.Source + Environment.NewLine);
    }

    private sealed class Roots(string root) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = root;
            return allowedRootId == "workspace";
        }
    }

    private static GenericSourceDocument Document(
        ApplicationIdentifier applicationId,
        string sourceId,
        char fingerprint) => GenericSourceDocument.Create(
            applicationId, sourceId, SourceTrust.Trusted, 5, "shared/item.json",
            "application/json", new string(fingerprint, 64), 1, true);

    private sealed class MutableScanner(IReadOnlyList<GenericSourceDocument> documents) : IRegisteredSourceScanner
    {
        public IReadOnlyList<GenericSourceDocument> Documents { get; set; } = documents;

        public Task<RegisteredSourceScanResult> ScanAsync(
            ApplicationIdentifier applicationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RegisteredSourceScanResult(Documents, []));
    }
}
