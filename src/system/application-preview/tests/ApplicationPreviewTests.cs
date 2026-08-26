using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.LocalAI;
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
