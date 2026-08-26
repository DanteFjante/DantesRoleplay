using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.CatalogNavigation.Tests;

public sealed class ActivatedApplicationCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"activated-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void Explicit_policy_materializes_exact_active_metadata_and_file_drift_fails_closed()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        const string relativePath = "content/procedures/tools/procedure.fixture.inspect.md";
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var markdown = """
            ---
            id: procedure.fixture.inspect
            category: tools.inspect
            name: Inspect fixture
            governs: query(kind: "fixture.inspect")
            status: active
            ---

            ## Description
            Inspect one generic fixture.

            ## Instructions
            1. Supply the fixture identity.

            ## Constraints
            - Never change fixture state.
            """;
        File.WriteAllText(fullPath, markdown, new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(fullPath);

        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(app, "Fixture application", "Generic public fixture contracts.", []));
        var sources = new InMemorySourceRegistry();
        var source = sources.Register(new(app, "catalog", "fixture-root", "content/**/*",
            SourceTrust.Trusted, 0, "fixture-catalog"));
        var activation = new ActiveApplicationManifest(
            app, 1, revision.Revision, revision.Fingerprint, Sha('B'), Sha('C'), Sha('D'), Sha('E'),
            Sha('A'), "fixture-coverage-v1", false,
            [new("catalog", SourceRegistrationFingerprint.Compute(source), 1, 0)],
            [new("file:" + relativePath, "catalog", SourceTrust.Trusted, 0, relativePath,
                "text/markdown", Hash(bytes), bytes.LongLength, true)],
            "fixture-operation", DateTime.UtcNow);
        var materializer = new ActivatedApplicationCatalogMaterializer(
            applications, new StaticActivation(activation), sources,
            new StaticRoot("fixture-root", _root));

        var manifest = materializer.Build(app);
        var snapshot = materializer.BuildFeatureSnapshot(app);
        var record = Assert.Single(manifest.Records);
        Assert.Equal("fixture", Assert.Single(manifest.Collections).Id);
        Assert.Equal("fixture.procedure.fixture.inspect", record.QualifiedId);
        Assert.Equal("procedures/tools/inspect", record.Path);
        Assert.Equal("content/procedures/tools/procedure.fixture.inspect.md", record.SourceLogicalPath);
        using var content = JsonDocument.Parse(record.ContentJson);
        Assert.Equal("query(kind: \"fixture.inspect\")", content.RootElement.GetProperty("governs").GetString());
        Assert.Equal(CatalogDescriptionStatus.Authored, manifest.Nodes.Single(node => node.Path == "").DescriptionStatus);
        Assert.All(manifest.Nodes.Where(node => node.Path != ""),
            node => Assert.Equal(CatalogDescriptionStatus.Missing, node.DescriptionStatus));
        Assert.Equal(SourceTrust.Trusted, Assert.Single(snapshot.Documents).Trust);
        Assert.Equal(record.ContentFingerprint, Assert.Single(snapshot.Documents).Record.ContentFingerprint);

        var cursor = new CatalogCursorCodec(Encoding.UTF8.GetBytes("activated-catalog-test-cursor-signing-key"));
        Assert.False(new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([]), materializer, cursor).TryGet(app, out _));
        Assert.True(new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy(["fixture"]), materializer, cursor).TryGet(app, out var navigator));
        Assert.Equal(1, Assert.Single(navigator.ListCollections(app)).RecordCount);

        File.AppendAllText(fullPath, "\nchanged");
        var drift = Assert.Throws<ApplicationCatalogMaterializationException>(() => materializer.Build(app));
        Assert.Equal("SOURCE_FILE_DRIFT", drift.Code);
    }

    [Fact]
    public void Publication_policy_rejects_reserved_duplicate_and_unbounded_ids()
    {
        Assert.Throws<ArgumentException>(() => new ConfiguredPublicApplicationCatalogPolicy(["system"]));
        Assert.Throws<ArgumentException>(() => new ConfiguredPublicApplicationCatalogPolicy(["fixture", "fixture"]));
        Assert.Throws<ArgumentException>(() => new ConfiguredPublicApplicationCatalogPolicy(
            Enumerable.Range(0, 101).Select(index => $"app{index}")));
    }

    [Fact]
    public void Active_query_json_is_searchable_with_exact_source_provenance()
    {
        var app = ApplicationIdentifier.Parse("query-fixture");
        const string relativePath = "content/queries/tools/query.inspect.json";
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(new
        {
            id = "query-fixture.query.inspect",
            category = "tools.inspect",
            name = "Inspect fixture state",
            description = "Returns one bounded safe fixture view.",
            matches = new[] { "inspect fixture" },
            roles = new Dictionary<string, string> { ["subject"] = "The fixture entity." },
            executor = "projection",
            projection = new
            {
                qualifiedId = "query-fixture.projection.inspect",
                version = 1,
                contentHash = Sha('A'),
                outputSchemaHash = Sha('B')
            },
            outputSchema = new { type = "object", properties = new { value = new { type = "integer" } } },
            exposure = "model-visible",
            status = "active"
        });
        File.WriteAllText(fullPath, json, new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(fullPath);
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(app, "Query fixture", "Generic query catalog.", []));
        var sources = new InMemorySourceRegistry();
        var source = sources.Register(new(app, "catalog", "fixture-root", "content/**/*",
            SourceTrust.Trusted, 0, "query-catalog"));
        var activation = new ActiveApplicationManifest(app, 1, revision.Revision, revision.Fingerprint,
            Sha('C'), Sha('D'), Sha('E'), Sha('F'), Sha('1'), "coverage-v1", false,
            [new("catalog", SourceRegistrationFingerprint.Compute(source), 1, 0)],
            [new("file:" + relativePath, "catalog", SourceTrust.Trusted, 0, relativePath,
                "application/json", Hash(bytes), bytes.LongLength, true)],
            "operation.query", DateTime.UtcNow);
        var materializer = new ActivatedApplicationCatalogMaterializer(applications,
            new StaticActivation(activation), sources, new StaticRoot("fixture-root", _root));

        var manifest = materializer.Build(app);
        var record = Assert.Single(manifest.Records);
        Assert.Equal("query", record.Kind);
        Assert.Equal("query-fixture.query.inspect", record.QualifiedId);
        Assert.Equal("queries/tools/inspect", record.Path);
        Assert.Equal(relativePath, record.SourceLogicalPath);
        var navigator = new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("query-catalog-cursor-signing-key")));
        Assert.Equal(record.QualifiedId, Assert.Single(navigator.Search(new(app,
            "inspect fixture", app.Value, Kinds: ["query"])).Records).Record.QualifiedId);
    }

    [Fact]
    public void Zero_and_two_application_hosts_share_one_vector_free_boundary_without_cross_application_leakage()
    {
        var applications = new InMemoryApplicationRegistry();
        var sources = new InMemorySourceRegistry();
        var alphaActivation = RegisterActivatedFixture(applications, sources, "alpha", 'A');
        var betaActivation = RegisterActivatedFixture(applications, sources, "beta", 'B');
        var materializer = new ActivatedApplicationCatalogMaterializer(
            applications, new StaticActivations([alphaActivation, betaActivation]), sources,
            new StaticRoot("fixture-root", _root));
        var cursors = new CatalogCursorCodec(
            Encoding.UTF8.GetBytes("multi-application-catalog-cursor-signing-key"));

        var empty = new ActivatedApplicationCatalogProvider(
            new EmptyPublicApplicationCatalogPolicy(), materializer, cursors);
        Assert.False(empty.TryGet(ApplicationIdentifier.Parse("alpha"), out _));
        Assert.False(empty.TryGet(ApplicationIdentifier.Parse("beta"), out _));

        var published = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy(["alpha", "beta"]), materializer, cursors);
        var alpha = ApplicationIdentifier.Parse("alpha");
        var beta = ApplicationIdentifier.Parse("beta");
        Assert.True(published.TryGet(alpha, out var alphaCatalog));
        Assert.True(published.TryGet(beta, out var betaCatalog));
        Assert.Equal(2, Assert.Single(alphaCatalog.ListCollections(alpha)).RecordCount);
        Assert.Equal(2, Assert.Single(betaCatalog.ListCollections(beta)).RecordCount);

        var alphaPage = alphaCatalog.Search(new(alpha, "inspect", "alpha", PageSize: 1));
        var betaResults = betaCatalog.Search(new(beta, "inspect", "beta", PageSize: 100));
        Assert.NotNull(alphaPage.NextCursor);
        Assert.All(alphaPage.Records, hit => Assert.StartsWith("alpha.", hit.Record.QualifiedId));
        Assert.Equal(2, betaResults.Records.Count);
        Assert.All(betaResults.Records, hit => Assert.StartsWith("beta.", hit.Record.QualifiedId));
        Assert.Empty(betaCatalog.Search(new(beta, "alpha", "beta")).Records);
        Assert.Throws<ArgumentException>(() => betaCatalog.Inspect(new(
            beta, "beta", alphaPage.Records[0].Record.QualifiedId)));
        Assert.Throws<InvalidOperationException>(() => betaCatalog.Search(new(
            beta, "inspect", "beta", PageSize: 1, Cursor: alphaPage.NextCursor)));

        var alphaRecord = alphaCatalog.Inspect(new(
            alpha, "alpha", alphaPage.Records[0].Record.QualifiedId));
        Assert.Contains("alpha", alphaRecord.ContentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("beta", alphaRecord.ContentJson, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Sha(char value) => new(value, 64);

    private ActiveApplicationManifest RegisterActivatedFixture(
        InMemoryApplicationRegistry applications,
        InMemorySourceRegistry sources,
        string applicationId,
        char fingerprintMarker)
    {
        var app = ApplicationIdentifier.Parse(applicationId);
        var revision = applications.Register(new(
            app, $"{applicationId} application", $"Generic {applicationId} public contracts.", []));
        var sourceId = $"{applicationId}-catalog";
        var source = sources.Register(new(
            app, sourceId, "fixture-root", $"{applicationId}/**/*",
            SourceTrust.Trusted, 0, $"{applicationId}-fixture-catalog"));
        var winners = new List<ActivatedApplicationDocument>();
        foreach (var suffix in new[] { "primary", "secondary" })
        {
            var relativePath = $"{applicationId}/procedures/tools/procedure.inspect-{suffix}.md";
            var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var markdown = $$"""
                ---
                id: procedure.inspect-{{suffix}}
                category: tools.inspect
                name: Inspect {{applicationId}} {{suffix}}
                governs: query(kind: "{{applicationId}}.inspect-{{suffix}}")
                status: active
                ---

                ## Description
                Inspect the {{applicationId}} {{suffix}} fixture.

                ## Instructions
                1. Supply the {{applicationId}} fixture identity.

                ## Constraints
                - Never change fixture state.
                """;
            File.WriteAllText(fullPath, markdown, new UTF8Encoding(false));
            var bytes = File.ReadAllBytes(fullPath);
            winners.Add(new(
                "file:" + relativePath, sourceId, SourceTrust.Trusted, 0, relativePath,
                "text/markdown", Hash(bytes), bytes.LongLength, true));
        }
        return new(
            app, 1, revision.Revision, revision.Fingerprint, Sha('C'), Sha('D'), Sha('E'), Sha('F'),
            Sha(fingerprintMarker), $"{applicationId}-coverage-v1", false,
            [new(sourceId, SourceRegistrationFingerprint.Compute(source), 1, 0)], winners,
            $"{applicationId}-operation", DateTime.UtcNow);
    }

    private sealed class StaticActivation(ActiveApplicationManifest activation) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == activation.ApplicationId ? activation : null;
    }

    private sealed class StaticActivations(IEnumerable<ActiveApplicationManifest> activations)
        : IApplicationActivationReader
    {
        private readonly IReadOnlyDictionary<ApplicationIdentifier, ActiveApplicationManifest> _activations =
            activations.ToDictionary(value => value.ApplicationId);

        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            _activations.GetValueOrDefault(applicationId);
    }

    private sealed class StaticRoot(string id, string root) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = allowedRootId == id ? root : "";
            return canonicalPath.Length > 0;
        }
    }
}
