using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using Microsoft.Extensions.DependencyInjection;

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
        var extensions = new InMemoryApplicationExtensionRegistry(sources);
        var extension = extensions.Register(new(app, "fixture-extension", "Fixture extension",
            "Adds fixture catalog records.", ApplicationExtensionClassifications.Homebrew,
            [source.SourceId], ["fixture.extension"], [], [], [], false));
        var activation = new ActiveApplicationManifest(
            app, 1, revision.Revision, revision.Fingerprint, Sha('B'), Sha('C'), Sha('D'), Sha('E'),
            Sha('A'), "fixture-coverage-v1", false,
            [new("catalog", SourceRegistrationFingerprint.Compute(source), 1, 0)],
            [new("file:" + relativePath, "catalog", SourceTrust.Trusted, 0, relativePath,
                "text/markdown", Hash(bytes), bytes.LongLength, true)],
            "fixture-operation", DateTime.UtcNow)
        {
            Extensions = [new(extension.ExtensionId,
                ApplicationExtensionRegistrationFingerprint.Compute(extension), extension.SourceIds,
                extension.NamespaceIds, extension.HigherPriorityThan, extension.OverridesBase)]
        };
        var materializer = new ActivatedApplicationCatalogMaterializer(
            applications, new StaticActivation(activation), sources,
            new StaticRoot("fixture-root", _root), extensions);

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
        var unpublished = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([]), materializer, cursor);
        Assert.False(unpublished.TryGet(app, out _));
        Assert.Equal("APPLICATION_CATALOG_UNPUBLISHED", unpublished.LastFailure(app)?.Code);
        Assert.True(new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy(["fixture"]), materializer, cursor).TryGet(app, out var navigator));
        Assert.Equal(1, Assert.Single(navigator.ListCollections(app)).RecordCount);

        var driftedExtensions = new InMemoryApplicationExtensionRegistry(sources);
        driftedExtensions.Register(extension with { Description = "Changed extension registration." });
        var extensionDrift = Assert.Throws<ApplicationCatalogMaterializationException>(() =>
            new ActivatedApplicationCatalogMaterializer(applications, new StaticActivation(activation), sources,
                new StaticRoot("fixture-root", _root), driftedExtensions).Build(app));
        Assert.Equal("EXTENSION_REGISTRATION_DRIFT", extensionDrift.Code);

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
    public void Prepared_snapshot_cache_is_host_owned_while_catalog_services_remain_scoped()
    {
        var services = new ServiceCollection();
        services.AddCatalogNavigationComponent();

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, value =>
            value.ServiceType == typeof(ActivatedApplicationCatalogSnapshotCache)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, value =>
            value.ServiceType == typeof(ActivatedApplicationCatalogCacheAuthority)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, value =>
            value.ServiceType == typeof(ActivatedApplicationCatalogMaterializer)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, value =>
            value.ServiceType == typeof(ActivatedApplicationCatalogProvider)).Lifetime);
    }

    [Fact]
    public async Task Concurrent_cold_materialization_registers_application_objects_once()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        const string procedurePath = "content/procedures/tools/procedure.fixture.inspect.md";
        const string objectPath = "objects/tools/fixture.object.summary.json";
        var procedure = """
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
        var objectJson = $$"""
            {
              "id": "fixture.object.summary",
              "version": 1,
              "schema": {
                "type": "object",
                "additionalProperties": false,
                "required": ["name"],
                "properties": { "name": { "type": "string" } }
              },
              "roles": { "subject": { "required": true } },
              "sources": [{
                "id": "subject",
                "role": "subject",
                "component": {
                  "qualifiedId": "fixture.component.name",
                  "version": 1,
                  "schemaHash": "{{Sha('A')}}"
                },
                "required": true
              }],
              "relationships": [],
              "references": [],
              "mappings": [{ "inputId": "subject", "sourcePointer": "/name", "targetPointer": "/name" }],
              "collections": [],
              "limits": { "traversalDepth": 1, "itemCount": 8, "outputBytes": 4096, "sqlQueries": 2 },
              "access": { "read": ["dm"], "write": [] }
            }
            """;
        foreach (var (path, content) in new[] { (procedurePath, procedure), (objectPath, objectJson) })
        {
            var fullPath = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        }

        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(app, "Fixture application", "Generic public fixture contracts.", []));
        var sources = new InMemorySourceRegistry();
        var source = sources.Register(new(app, "catalog", "fixture-root", "**/*",
            SourceTrust.Trusted, 0, "fixture-catalog"));
        var winners = new[] { procedurePath, objectPath }.Select(path =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
            return new ActivatedApplicationDocument("file:" + path, "catalog", SourceTrust.Trusted, 0,
                path, path.EndsWith(".md", StringComparison.Ordinal) ? "text/markdown" : "application/json",
                Hash(bytes), bytes.LongLength, true);
        }).ToArray();
        var activation = new ActiveApplicationManifest(app, 1, revision.Revision, revision.Fingerprint,
            Sha('B'), Sha('C'), Sha('D'), Sha('E'), Sha('F'), "fixture-coverage-v1", false,
            [new("catalog", SourceRegistrationFingerprint.Compute(source), winners.Length, 0)], winners,
            "fixture-operation", DateTime.UtcNow);
        var cache = new ActivatedApplicationCatalogSnapshotCache();
        var authority = new ActivatedApplicationCatalogCacheAuthority();
        var projections = new BlockingProjectionRegistry();
        ActivatedApplicationCatalogMaterializer Materializer() => new(
            applications, new StaticActivation(activation), sources,
            new StaticRoot("fixture-root", _root), projections: projections);

        var first = Task.Run(() => Materializer().UsePreparationCache(cache, authority).BuildFeatureSnapshot(app));
        Assert.True(projections.FirstEntered.Wait(TimeSpan.FromSeconds(5)));
        var second = Task.Run(() => Materializer().UsePreparationCache(cache, authority).BuildFeatureSnapshot(app));
        await Task.Delay(150);
        projections.Release.Set();
        var snapshots = await Task.WhenAll(first, second);

        Assert.Equal(1, projections.DefineCalls);
        Assert.Equal(1, projections.MaximumConcurrentDefinitions);
        Assert.Same(snapshots[0], snapshots[1]);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.Hits);

        var failed = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([app.Value]),
            new ActivatedApplicationCatalogMaterializer(applications, new StaticActivation(activation), sources,
                new StaticRoot("fixture-root", _root), projections: new ThrowingProjectionRegistry()),
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("unexpected-catalog-failure-signing-key")));
        Assert.False(failed.TryGet(app, out _));
        var failure = Assert.IsType<PublicApplicationCatalogFailure>(failed.LastFailure(app));
        Assert.Equal("CATALOG_MATERIALIZATION_FAILED", failure.Code);
        Assert.DoesNotContain(nameof(IOException), failure.Message, StringComparison.Ordinal);

        var policyFailed = new ActivatedApplicationCatalogProvider(
            new ThrowingCatalogPolicy(), Materializer(),
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("unexpected-policy-failure-signing-key")));
        Assert.False(policyFailed.TryGet(app, out _));
        Assert.Equal("CATALOG_MATERIALIZATION_FAILED", policyFailed.LastFailure(app)?.Code);
    }

    [Fact]
    public void Retained_permission_preparation_stays_pure_until_published_catalog_access_registers_once()
    {
        var fixture = CreateRetainedObjectFixture();
        var cache = new ActivatedApplicationCatalogSnapshotCache();
        var authority = new ActivatedApplicationCatalogCacheAuthority();
        var projections = new CountingProjectionRegistry();

        var pure = new ActivatedApplicationCatalogMaterializer(fixture.Applications, fixture.Activations,
                fixture.Sources, new StaticRoot("fixture-root", Path.Combine(_root, "must-not-be-read")),
                projections: projections)
            .UsePreparationCache(cache, authority)
            .BuildPermissionSnapshot(fixture.ApplicationId, fixture.Activation);

        Assert.Equal(0, projections.DefineCalls);

        var published = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([fixture.ApplicationId.Value]),
            new ActivatedApplicationCatalogMaterializer(fixture.Applications, fixture.Activations,
                    fixture.Sources, new StaticRoot("fixture-root", Path.Combine(_root, "must-not-be-read")),
                    projections: projections)
                .UsePreparationCache(cache, authority),
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("retained-pure-catalog-signing-key")));
        Assert.True(published.TryGet(fixture.ApplicationId, out _));
        Assert.Equal(1, projections.DefineCalls);

        var normal = new ActivatedApplicationCatalogMaterializer(fixture.Applications, fixture.Activations,
                fixture.Sources, new StaticRoot("fixture-root", Path.Combine(_root, "must-not-be-read")),
                projections: projections)
            .UsePreparationCache(cache, authority)
            .BuildFeatureSnapshot(fixture.ApplicationId);
        Assert.Same(pure, normal);
        Assert.Equal(1, projections.DefineCalls);
    }

    [Fact]
    public void Failed_normal_registration_preserves_pure_retained_snapshot_for_retry()
    {
        var fixture = CreateRetainedObjectFixture();
        var cache = new ActivatedApplicationCatalogSnapshotCache();
        var authority = new ActivatedApplicationCatalogCacheAuthority();
        var projections = new FailOnceProjectionRegistry();
        ActivatedApplicationCatalogMaterializer Materializer() => new(
            fixture.Applications, fixture.Activations, fixture.Sources,
            new StaticRoot("fixture-root", Path.Combine(_root, "must-not-be-read")), projections: projections);

        var pure = Materializer().UsePreparationCache(cache, authority)
            .BuildPermissionSnapshot(fixture.ApplicationId, fixture.Activation);
        Assert.Equal(0, projections.DefineCalls);

        var failed = Assert.Throws<ApplicationCatalogMaterializationException>(() =>
            Materializer().UsePreparationCache(cache, authority).BuildFeatureSnapshot(fixture.ApplicationId));
        Assert.Equal("CATALOG_OBJECT_INVALID", failed.Code);
        Assert.Equal(1, projections.DefineCalls);

        var retried = Materializer().UsePreparationCache(cache, authority)
            .BuildFeatureSnapshot(fixture.ApplicationId);
        Assert.Same(pure, retried);
        Assert.Equal(2, projections.DefineCalls);
    }

    [Fact]
    public async Task Prepared_cache_lookup_never_starts_a_cold_or_pending_lazy_factory()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var cache = new ActivatedApplicationCatalogSnapshotCache();
        var authority = new ActivatedApplicationCatalogCacheAuthority();
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var factoryCalls = 0;

        Assert.False(cache.TryGetPrepared(authority, app, Sha('A'), out _));
        Assert.Equal(0, factoryCalls);

        var preparing = Task.Run(() => Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrCreate(authority, app, "pending-preparation", () =>
            {
                Interlocked.Increment(ref factoryCalls);
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                throw new InvalidOperationException("fixture pending factory");
            })));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        Assert.False(cache.TryGetPrepared(authority, app, Sha('A'), out _));
        Assert.Equal(1, factoryCalls);
        release.Set();
        await preparing;
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
    public void Active_content_entity_is_inspectable_by_original_id_with_exact_json_and_provenance()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        const string relativePath = "content/entities/adventuring-gear/item.fixture.lantern.v1.json";
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        const string json = """
            {
              "id": "item.fixture.lantern.v1",
              "name": "Fixture Lantern",
              "archetype": "fixture.archetype.item",
              "components": {
                "fixture.item-definition": {
                  "kind": "adventuring-gear",
                  "fuel": {
                    "entityId": "fixture.item.oil"
                  }
                }
              }
            }
            """;
        File.WriteAllText(fullPath, json, new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(fullPath);
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(app, "Fixture application", "Generic public fixture content.", []));
        var sources = new InMemorySourceRegistry();
        var source = sources.Register(new(app, "catalog", "fixture-root", "content/**/*",
            SourceTrust.Trusted, 0, "fixture-catalog"));
        var activation = new ActiveApplicationManifest(
            app, 1, revision.Revision, revision.Fingerprint, Sha('B'), Sha('C'), Sha('D'), Sha('E'),
            Sha('A'), "fixture-coverage-v1", false,
            [new("catalog", SourceRegistrationFingerprint.Compute(source), 1, 0)],
            [new("file:" + relativePath, "catalog", SourceTrust.Trusted, 0, relativePath,
                "application/json", Hash(bytes), bytes.LongLength, true)],
            "fixture-operation", DateTime.UtcNow);
        var materializer = new ActivatedApplicationCatalogMaterializer(
            applications, new StaticActivation(activation), sources,
            new StaticRoot("fixture-root", _root));

        var manifest = materializer.Build(app);
        var record = Assert.Single(manifest.Records);
        Assert.Equal("entity", record.Kind);
        Assert.Equal("fixture.item.fixture.lantern.v1", record.QualifiedId);
        Assert.Equal("entities/adventuring-gear", record.Path);
        Assert.Equal(["item.fixture.lantern.v1"], record.Aliases);
        Assert.Equal(["fixture.item-definition"], record.ComponentIds);
        Assert.Equal("fixture.archetype.item", record.ArchetypeId);
        Assert.Equal(["fixture.item.oil"], record.ReferencedEntityIds);
        Assert.Equal(json, record.ContentJson);
        Assert.Equal(relativePath, record.SourceLogicalPath);
        var navigator = new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("entity-catalog-cursor-signing-key")));
        var hit = Assert.Single(navigator.Search(new(app, "item.fixture.lantern.v1", app.Value,
            Kinds: ["entity"])).Records);
        Assert.Equal(record.QualifiedId, hit.Record.QualifiedId);
        Assert.Equal(json, navigator.Inspect(new(app, app.Value, record.QualifiedId)).ContentJson);
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

    [Fact]
    public void Definition_change_advancement_rebuilds_the_scoped_active_catalog_snapshot()
    {
        var applications = new InMemoryApplicationRegistry();
        var sources = new InMemorySourceRegistry();
        var activation = RegisterActivatedFixture(applications, sources, "refresh", 'A');
        var changes = new MutableDefinitionChanges(activation.ApplicationId, activation.ActivationFingerprint);
        var provider = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy(["refresh"]),
            new ActivatedApplicationCatalogMaterializer(applications, new StaticActivation(activation), sources,
                new StaticRoot("fixture-root", _root)),
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("definition-change-catalog-cursor-key")), changes);

        Assert.True(provider.TryGet(activation.ApplicationId, out var before));
        changes.Revision = 2;
        Assert.True(provider.TryGet(activation.ApplicationId, out var after));

        Assert.NotSame(before, after);
    }

    [Fact]
    public void Repeated_equivalent_maximum_catalog_preparation_profile()
    {
        const int recordCount = CatalogNavigationLimits.MaximumRecords;
        var app = ApplicationIdentifier.Parse("profile");
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(app, "Profile application",
            "Maximum-size activated catalog preparation profile.", []));
        var sources = new InMemorySourceRegistry();
        var source = sources.Register(new(app, "catalog", "fixture-root", "profile/**/*",
            SourceTrust.Trusted, 0, "profile-catalog"));
        var winners = new List<ActivatedApplicationDocument>();
        for (var index = 0; index < recordCount; index++)
        {
            var relativePath = $"profile/procedures/group-{index % 10:D2}/procedure.profile-{index:D3}.md";
            var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var markdown = $$"""
                ---
                id: procedure.profile-{{index:D3}}
                category: group-{{index % 10:D2}}
                name: Profile procedure {{index:D3}}
                governs: query(kind: "profile-{{index:D3}}")
                status: active
                ---

                ## Description
                Profile activated catalog preparation for record {{index:D3}}.

                ## Instructions
                1. Inspect the requested profile record.

                ## Constraints
                - Never mutate profile state.
                """;
            File.WriteAllText(fullPath, markdown, new UTF8Encoding(false));
            var bytes = File.ReadAllBytes(fullPath);
            winners.Add(new("file:" + relativePath, "catalog", SourceTrust.Trusted, 0, relativePath,
                "text/markdown", Hash(bytes), bytes.LongLength, true));
        }
        var activation = new ActiveApplicationManifest(app, 1, revision.Revision, revision.Fingerprint,
            Sha('B'), Sha('C'), Sha('D'), Sha('E'), Sha('A'), "profile-coverage-v1", false,
            [new("catalog", SourceRegistrationFingerprint.Compute(source), recordCount, 0)], winners,
            "profile-operation", DateTime.UtcNow);
        var cache = new ActivatedApplicationCatalogSnapshotCache();
        var authority = new ActivatedApplicationCatalogCacheAuthority();
        var allocations = new List<long>();
        var elapsed = new List<long>();
        var snapshots = new List<ActiveCatalogFeatureSnapshot>();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var before = GC.GetTotalAllocatedBytes(false);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            snapshots.Add(new ActivatedApplicationCatalogMaterializer(applications,
                    new StaticActivation(activation), sources, new StaticRoot("fixture-root", _root))
                .UsePreparationCache(cache, authority).BuildFeatureSnapshot(app));
            timer.Stop();
            allocations.Add(GC.GetTotalAllocatedBytes(false) - before);
            elapsed.Add(timer.ElapsedMilliseconds);
        }

        Assert.All(snapshots, snapshot => Assert.Equal(recordCount, snapshot.Manifest.Records.Count));
        Assert.Same(snapshots[0], snapshots[1]);
        Assert.Same(snapshots[0], snapshots[2]);
        Assert.Equal(2, cache.Hits);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.Count);
        Console.WriteLine($"Activated catalog preparation with verified reuse: allocations={string.Join(',', allocations)}; " +
            $"elapsedMs={string.Join(',', elapsed)}");

        var changedActivation = activation with
        {
            ActivationRevision = 2,
            ActivationFingerprint = Sha('9'),
            ResolutionFingerprint = Sha('9')
        };
        var changed = new ActivatedApplicationCatalogMaterializer(applications,
                new StaticActivation(changedActivation), sources, new StaticRoot("fixture-root", _root))
            .UsePreparationCache(cache, authority).BuildFeatureSnapshot(app);
        Assert.NotSame(snapshots[0], changed);
        Assert.NotEqual(snapshots[0].Manifest.Fingerprint, changed.Manifest.Fingerprint);
        Assert.Equal(2, cache.Misses);
        Assert.Equal(1, cache.Count);

        var beforeUnpublished = (cache.Hits, cache.Misses);
        var unpublished = new ActivatedApplicationCatalogProvider(new EmptyPublicApplicationCatalogPolicy(),
            new ActivatedApplicationCatalogMaterializer(applications, new StaticActivation(activation), sources,
                    new StaticRoot("fixture-root", _root)).UsePreparationCache(cache, authority),
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("profile-catalog-cursor-signing-key")));
        Assert.False(unpublished.TryGet(app, out _));
        Assert.Equal(beforeUnpublished, (cache.Hits, cache.Misses));

        var otherAuthority = new ActivatedApplicationCatalogCacheAuthority();
        var otherContext = new ActivatedApplicationCatalogMaterializer(applications,
                new StaticActivation(activation), sources, new StaticRoot("fixture-root", _root))
            .UsePreparationCache(cache, otherAuthority).BuildFeatureSnapshot(app);
        Assert.NotSame(snapshots[0], otherContext);
        Assert.Equal(3, cache.Misses);
        Assert.Equal(2, cache.Count);

        var driftedSources = new InMemorySourceRegistry();
        driftedSources.Register(source with { LogicalIdentity = "changed-registration" });
        var registrationDrift = Assert.Throws<ApplicationCatalogMaterializationException>(() =>
            new ActivatedApplicationCatalogMaterializer(applications, new StaticActivation(activation),
                    driftedSources, new StaticRoot("fixture-root", _root))
                .UsePreparationCache(cache, authority).BuildFeatureSnapshot(app));
        Assert.Equal("SOURCE_REGISTRATION_DRIFT", registrationDrift.Code);

        File.AppendAllText(Path.Combine(_root,
            winners[0].RelativePath.Replace('/', Path.DirectorySeparatorChar)), "\nchanged");
        var fileDrift = Assert.Throws<ApplicationCatalogMaterializationException>(() =>
            new ActivatedApplicationCatalogMaterializer(applications, new StaticActivation(activation), sources,
                    new StaticRoot("fixture-root", _root))
                .UsePreparationCache(cache, authority).BuildFeatureSnapshot(app));
        Assert.Equal("SOURCE_FILE_DRIFT", fileDrift.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Sha(char value) => new(value, 64);

    private static RetainedObjectFixture CreateRetainedObjectFixture()
    {
        var applicationId = ApplicationIdentifier.Parse("fixture");
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(applicationId, "Fixture application",
            "Generic public fixture contracts.", []));
        var sources = new InMemorySourceRegistry();
        var source = sources.Register(new(applicationId, "catalog", "fixture-root", "**/*",
            SourceTrust.Trusted, 0, "fixture-catalog"));
        const string procedurePath = "content/procedures/tools/procedure.fixture.inspect.md";
        const string path = "objects/tools/fixture.object.summary.json";
        var procedureBytes = Encoding.UTF8.GetBytes("""
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
            """);
        var bytes = Encoding.UTF8.GetBytes($$"""
            {
              "id": "fixture.object.summary",
              "version": 1,
              "schema": {
                "type": "object",
                "additionalProperties": false,
                "required": ["name"],
                "properties": { "name": { "type": "string" } }
              },
              "roles": { "subject": { "required": true } },
              "sources": [{
                "id": "subject",
                "role": "subject",
                "component": {
                  "qualifiedId": "fixture.component.name",
                  "version": 1,
                  "schemaHash": "{{Sha('A')}}"
                },
                "required": true
              }],
              "relationships": [],
              "references": [],
              "mappings": [{ "inputId": "subject", "sourcePointer": "/name", "targetPointer": "/name" }],
              "collections": [],
              "limits": { "traversalDepth": 1, "itemCount": 8, "outputBytes": 4096, "sqlQueries": 2 },
              "access": { "read": ["dm"], "write": [] }
            }
            """);
        var procedure = new ActivatedApplicationDocument("retained:" + procedurePath, "catalog", SourceTrust.Trusted, 0,
            procedurePath, "text/markdown", Hash(procedureBytes), procedureBytes.LongLength, true);
        var winner = new ActivatedApplicationDocument("retained:" + path, "catalog", SourceTrust.Trusted, 0,
            path, "application/json", Hash(bytes), bytes.LongLength, true);
        var activation = new ActiveApplicationManifest(applicationId, 1, revision.Revision, revision.Fingerprint,
            Sha('B'), Sha('C'), Sha('D'), Sha('E'), Sha('F'), "fixture-coverage-v1", true,
            [new("catalog", SourceRegistrationFingerprint.Compute(source), 2, 0)], [procedure, winner],
            "fixture-operation", DateTime.UtcNow)
        {
            PreparationVersion = "fixture-preparation-v1"
        };
        return new(applicationId, applications, sources, activation,
            new RetainedStaticActivation(activation,
            [
                new(applicationId, 1, procedure.LogicalIdentity, procedure.ContentFingerprint, procedure.Length,
                    procedureBytes, false),
                new(applicationId, 1, winner.LogicalIdentity, winner.ContentFingerprint, winner.Length, bytes, false)
            ]));
    }

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

    private sealed class MutableDefinitionChanges(ApplicationIdentifier application, string fingerprint)
        : IApplicationDefinitionChangeReader
    {
        public int Revision { get; set; } = 1;

        public ApplicationDefinitionChange? CurrentChange(ApplicationIdentifier applicationId) => applicationId == application
            ? new(application, Revision, fingerprint, "fixture-operation", DateTime.UnixEpoch,
                new([], []), new(new string('A', 64), "fixture", true), new("rebuildable", false, false))
            : null;

        public ApplicationDefinitionChange? RevisionChange(ApplicationIdentifier applicationId, int activationRevision) =>
            applicationId == application && activationRevision == Revision ? CurrentChange(applicationId) : null;

        public IReadOnlyList<ApplicationDefinitionChange> ChangesAfter(ApplicationIdentifier applicationId,
            int afterActivationRevision, int limit) => [];
    }

    private sealed class RetainedStaticActivation(
        ActiveApplicationManifest activation,
        IEnumerable<ActivatedApplicationDocumentEvidence> evidence)
        : IApplicationActivationReader, IActivatedApplicationEvidenceReader
    {
        private readonly IReadOnlyDictionary<string, ActivatedApplicationDocumentEvidence> _evidence = evidence
            .ToDictionary(value => value.LogicalIdentity, StringComparer.Ordinal);

        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == activation.ApplicationId ? activation : null;

        public ActivatedApplicationDocumentEvidence? ReadDocumentEvidence(
            ApplicationIdentifier applicationId, int activationRevision, string logicalIdentity) =>
            applicationId == activation.ApplicationId && activationRevision == activation.ActivationRevision
                ? _evidence.GetValueOrDefault(logicalIdentity)
                : null;
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

    private sealed class BlockingProjectionRegistry : IProjectionDefinitionRegistry
    {
        private int _activeDefinitions;
        private int _defineCalls;
        private int _maximumConcurrentDefinitions;

        public ManualResetEventSlim FirstEntered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public int DefineCalls => Volatile.Read(ref _defineCalls);
        public int MaximumConcurrentDefinitions => Volatile.Read(ref _maximumConcurrentDefinitions);

        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition)
        {
            Interlocked.Increment(ref _defineCalls);
            var active = Interlocked.Increment(ref _activeDefinitions);
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentDefinitions);
                if (active <= maximum || Interlocked.CompareExchange(
                        ref _maximumConcurrentDefinitions, active, maximum) == maximum) break;
            }
            FirstEntered.Set();
            Assert.True(Release.Wait(TimeSpan.FromSeconds(5)));
            Interlocked.Decrement(ref _activeDefinitions);
            return new(definition.Owner, definition.QualifiedId, definition.DeclaredVersion ?? 1,
                "fixture-profile", definition.OutputSchemaJson, Sha('1'), Sha('2'),
                definition.ComponentInputs, definition.DependencyInputs, definition.Mappings, DateTime.UtcNow);
        }

        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) => null;

        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) => new(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
    }

    private sealed class CountingProjectionRegistry : IProjectionDefinitionRegistry
    {
        private int _defineCalls;
        public int DefineCalls => Volatile.Read(ref _defineCalls);

        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition)
        {
            Interlocked.Increment(ref _defineCalls);
            return Registered(definition);
        }

        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) => null;

        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) => EmptyImpactGraph();
    }

    private sealed class FailOnceProjectionRegistry : IProjectionDefinitionRegistry
    {
        private int _defineCalls;
        public int DefineCalls => Volatile.Read(ref _defineCalls);

        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition)
        {
            if (Interlocked.Increment(ref _defineCalls) == 1)
                throw new InvalidOperationException("fixture registration failure");
            return Registered(definition);
        }

        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) => null;

        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) => EmptyImpactGraph();
    }

    private static RegisteredProjectionDefinition Registered(ProjectionDefinitionRequest definition) => new(
        definition.Owner, definition.QualifiedId, definition.DeclaredVersion ?? 1,
        "fixture-profile", definition.OutputSchemaJson, Sha('1'), Sha('2'), definition.ComponentInputs,
        definition.DependencyInputs, definition.Mappings, DateTime.UtcNow);

    private static ProjectionImpactGraph EmptyImpactGraph() => new(
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    private sealed record RetainedObjectFixture(
        ApplicationIdentifier ApplicationId,
        InMemoryApplicationRegistry Applications,
        InMemorySourceRegistry Sources,
        ActiveApplicationManifest Activation,
        RetainedStaticActivation Activations);

    private sealed class ThrowingProjectionRegistry : IProjectionDefinitionRegistry
    {
        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition) =>
            throw new IOException("fixture storage failure");

        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) => null;

        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingCatalogPolicy : IPublicApplicationCatalogPolicy
    {
        public bool IsPublished(ApplicationIdentifier applicationId) =>
            throw new IOException("fixture policy dependency failure");
    }
}
