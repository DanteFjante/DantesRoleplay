using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Interactions;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class InteractionFeatureRetrievalTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "interaction-retrieval-" + Guid.NewGuid().ToString("N"));
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("sample-app");
    private static readonly ApplicationIdentifier OtherApplication = ApplicationIdentifier.Parse("other-app");

    [Fact]
    public async Task Trusted_and_untrusted_lanes_are_host_bound_and_never_mix()
    {
        var provider = new MutableSnapshots(Snapshot());
        var retriever = new InteractionFeatureRetriever(provider);

        // "find", not the authored phrase "find feature": an exact phrase is a key and short-circuits
        // to Exact, which would say nothing about lane binding.
        var trusted = await retriever.SearchAsync(new(Application, InteractionRetrievalLane.TrustedFeature),
            new("find", 10));
        var untrusted = await retriever.SearchAsync(new(Application, InteractionRetrievalLane.UntrustedReference),
            new("sample-app.untrusted", 10));

        Assert.Equal(InteractionRetrievalMode.LexicalFallback, trusted.Mode);
        Assert.Equal("VECTOR_INDEX_DISABLED", trusted.AvailabilityCode);
        Assert.Equal(["sample-app.trusted"], trusted.Hits.Select(hit => hit.Reference.QualifiedId));
        Assert.All(trusted.Hits, hit => Assert.Equal(InteractionRetrievalLane.TrustedFeature, hit.Reference.Lane));
        Assert.Equal(InteractionRetrievalMode.Exact, untrusted.Mode);
        Assert.Equal("sample-app.untrusted", Assert.Single(untrusted.Hits).Reference.QualifiedId);
        Assert.Equal(InteractionRetrievalLane.UntrustedReference, Assert.Single(untrusted.Hits).Reference.Lane);
        Assert.DoesNotContain("function", Assert.Single(trusted.Hits).ContractJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_or_cross_application_snapshot_is_a_typed_empty_result()
    {
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(Snapshot()));

        var result = await retriever.SearchAsync(new(OtherApplication, InteractionRetrievalLane.TrustedFeature), new("find", 10));

        Assert.Equal(InteractionRetrievalMode.Unavailable, result.Mode);
        Assert.Equal("CATALOG_UNAVAILABLE", result.AvailabilityCode);
        Assert.Empty(result.Hits);
    }

    [Fact]
    public async Task Namespace_filters_and_disabled_namespaces_are_applied_before_exact_or_lexical_search()
    {
        using var database = new SqliteFixture();
        using var db = database.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        registry.Register(new CatalogNamespaceRegistration("sample-app", "sample-app", "Sample application features.",
            [CatalogNamespaceKinds.Mechanic, CatalogNamespaceKinds.Procedure]));
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(Snapshot()), namespaces: registry);
        var scope = new InteractionFeatureRetrievalScope(Application, InteractionRetrievalLane.TrustedFeature);

        var visible = await retriever.SearchAsync(scope, new("find feature", namespaceId: "sample-app"));
        Assert.NotEmpty(visible.Hits);

        registry.SetEnabled("sample-app", enabled: false);
        var hidden = await retriever.SearchAsync(scope, new("sample-app.trusted"));
        Assert.Empty(hidden.Hits);
    }

    [Fact]
    public async Task An_application_neutral_record_is_visible_through_its_unprefixed_namespace()
    {
        // Most catalog records carry an application-neutral id and reach retrieval qualified with
        // the application prefix. Their namespace is registered under the unprefixed id, so a gate
        // that only tested the qualified form hid nearly the whole catalog from retrieval while
        // catalog browsing still returned it.
        using var database = new SqliteFixture();
        using var db = database.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        foreach (var id in new[] { "procedure", "procedure.campaign" })
            registry.Register(new CatalogNamespaceRegistration(id, "sample-app", $"{id} contracts.", [CatalogNamespaceKinds.Procedure],
                ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed retrieval fixture."));
        var snapshot = Snapshot([Record("sample-app.procedure.campaign.create", "Create a campaign",
            "Creates a campaign.", ["start a new campaign"])]);
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(snapshot), namespaces: registry);
        var scope = new InteractionFeatureRetrievalScope(Application, InteractionRetrievalLane.TrustedFeature);

        var result = await retriever.SearchAsync(scope, new("start a new campaign"));
        var unprefixed = await retriever.SearchAsync(scope, new("campaign", namespaceId: "procedure.campaign"));
        var qualified = await retriever.SearchAsync(scope, new("campaign", namespaceId: "sample-app.procedure.campaign"));

        Assert.Equal(InteractionRetrievalMode.Exact, result.Mode);
        Assert.Equal("sample-app.procedure.campaign.create", Assert.Single(result.Hits).Reference.QualifiedId);
        // Either spelling of the namespace filter selects it: the registry lists the unprefixed
        // form, while a result reference shows the qualified one.
        Assert.NotEmpty(unprefixed.Hits);
        Assert.NotEmpty(qualified.Hits);
    }

    [Fact]
    public async Task Feature_search_automatically_returns_the_active_extension_winner()
    {
        using var database = new SqliteFixture();
        using var db = database.CreateContext();
        var registry = new SqliteCatalogNamespaceRegistry(db);
        new SqliteApplicationRegistry(db).Register(new(Application, "Sample", "Overlay retrieval fixture.", []));
        foreach (var id in new[] { "sample-app", "sample-app.rules", "sample-app.homebrew", "sample-app.homebrew.rules" })
            registry.Register(new CatalogNamespaceRegistration(id, "sample-app", $"{id} features.", [CatalogNamespaceKinds.Procedure],
                ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed retrieval fixture."));
        registry.RegisterProfile(new("sample-app", "homebrew", "Homebrew features override base features."));
        registry.RegisterResolutionKey(new("sample-app", "homebrew", "rules.fireball",
            CatalogNamespaceKinds.Procedure, "The logical Fireball procedure."));
        registry.Register(new CatalogNamespaceOverlayRule("sample-app", "homebrew", "sample-app.homebrew.rules", "sample-app.rules",
            CatalogNamespaceKinds.Procedure));
        var records = new[]
        {
            Record("sample-app.rules.fireball", "Fireball", "Base fireball feature.", ["fireball"]),
            Record("sample-app.homebrew.rules.fireball", "Fireball", "Homebrew fireball feature.", ["fireball"])
        };
        var snapshot = WithResolution(Snapshot(records),
            CatalogExtensionResolutionContext.Create(Application, new string('B', 64),
                [new("homebrew", "Homebrew", "Fixture homebrew.", "homebrew", ["fixture-homebrew"],
                    ["sample-app.homebrew"], [], true)]));
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(snapshot), namespaces: registry);

        var result = await retriever.SearchAsync(
            new(Application, InteractionRetrievalLane.TrustedFeature), new("fireball"));

        Assert.Equal("sample-app.homebrew.rules.fireball", Assert.Single(result.Hits).Reference.QualifiedId);
    }

    [Fact]
    public void Active_snapshot_rejects_a_document_not_in_the_current_catalog_manifest()
    {
        var snapshot = Snapshot();
        var forged = snapshot.Documents[0] with { Record = snapshot.Documents[0].Record with { Version = 2 } };

        Assert.Throws<ArgumentException>(() => new ActiveCatalogFeatureSnapshot(snapshot.Manifest, [forged]));
    }

    [Fact]
    public async Task Equivalent_query_normalization_and_current_lexical_order_are_deterministic()
    {
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(Snapshot()));

        var left = await retriever.SearchAsync(new(Application, InteractionRetrievalLane.TrustedFeature), new("  Find\u00a0Feature  ", 10));
        var right = await retriever.SearchAsync(new(Application, InteractionRetrievalLane.TrustedFeature), new("Find Feature", 10));

        Assert.Equal(left.Hits.Select(hit => hit.Reference.QualifiedId), right.Hits.Select(hit => hit.Reference.QualifiedId));
        Assert.Equal(left.Hits.Select(hit => hit.LexicalRank), right.Hits.Select(hit => hit.LexicalRank));
        Assert.Throws<InteractionContractException>(() => new InteractionFeatureSearchInput("x", 51));
        Assert.Throws<InteractionContractException>(() => new InteractionFeatureSearchInput("\u0001"));
    }

    [Fact]
    public async Task Active_query_contracts_are_discoverable_as_trusted_capabilities()
    {
        var content = """
            {"id":"sample-app.query.resume","category":"campaign.resume","name":"Resume","description":"Read the current campaign summary.","matches":["resume campaign"],"roles":{},"executor":"mechanic-projection","projection":{"qualifiedId":"sample-app.mechanic.resume","version":1,"contentHash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","outputSchemaHash":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"},"outputSchema":{"type":"object"},"exposure":"model-visible","status":"active"}
            """;
        var record = new CatalogRecordDefinition("sample", ApplicationQueryContract.CatalogKind,
            "sample-app.query.resume", "Resume", "Read the current campaign summary.", [],
            ["resume campaign"], "", "active", 1, content, Hash(content), "source", "queries/resume.json");
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(Snapshot([record])));

        var result = await retriever.SearchAsync(
            new(Application, InteractionRetrievalLane.TrustedFeature),
            new("resume campaign", kinds: [ApplicationQueryContract.CatalogKind], statuses: ["active"]));

        var hit = Assert.Single(result.Hits);
        Assert.Equal(InteractionRetrievalMode.Exact, result.Mode);
        Assert.Equal(ApplicationQueryContract.CatalogKind, hit.Reference.Kind);
        Assert.Equal(record.ContentFingerprint, hit.Reference.ContentFingerprint);
    }

    [Fact]
    public async Task Rebuild_and_hybrid_search_use_only_current_same_lane_documents()
    {
        var provider = new MutableSnapshots(Snapshot());
        var vectors = new MemoryVectors();
        var retriever = new InteractionFeatureRetriever(provider, new DeterministicEmbeddings(), vectors);
        var scope = new InteractionFeatureRetrievalScope(Application, InteractionRetrievalLane.TrustedFeature);

        var rebuild = await retriever.RebuildAsync(scope);
        var result = await retriever.SearchAsync(scope, new("alpha", 10));

        Assert.True(rebuild.Rebuilt);
        Assert.Equal(3, rebuild.DocumentCount);
        Assert.Equal(InteractionRetrievalMode.Hybrid, result.Mode);
        Assert.Equal("sample-app.alpha", result.Hits[0].Reference.QualifiedId);
        Assert.All(result.Hits, hit => Assert.Equal(InteractionRetrievalLane.TrustedFeature, hit.Reference.Lane));
        Assert.DoesNotContain(vectors.Ids, id => id == "sample-app.untrusted");
    }

    [Fact]
    public async Task Changed_snapshot_cannot_reuse_or_return_a_stale_vector_document()
    {
        var provider = new MutableSnapshots(Snapshot());
        var vectors = new MemoryVectors();
        var retriever = new InteractionFeatureRetriever(provider, new DeterministicEmbeddings(), vectors);
        var scope = new InteractionFeatureRetrievalScope(Application, InteractionRetrievalLane.TrustedFeature);
        await retriever.RebuildAsync(scope);
        provider.Snapshot = Snapshot(fingerprintMarker: 'B', alphaDescription: "Current replacement feature.");

        var result = await retriever.SearchAsync(scope, new("alpha feature", 10));

        var alpha = Assert.Single(result.Hits, hit => hit.Reference.QualifiedId == "sample-app.alpha");
        Assert.Equal(Hash("{\"id\":\"sample-app.alpha\",\"description\":\"Current replacement feature.\"}"), alpha.Reference.ContentFingerprint);
        Assert.Contains("Current replacement", alpha.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Derived_sqlite_index_is_disposable_scoped_and_deterministic()
    {
        var location = InteractionDerivedIndexLocation.Create(_temporaryRoot);
        var index = new SqliteInteractionDerivedVectorIndex(location);
        var identity = new EmbeddingProviderIdentity("fixture", "embedding", "1", 2);
        var generation = Generation(identity);
        await index.ReplaceAsync(generation,
        [
            InteractionVectorDocument.Create(Reference("sample-app.alpha"), "alpha", [1f, 0f]),
            InteractionVectorDocument.Create(Reference("sample-app.beta"), "beta", [0f, 1f])
        ]);

        var candidates = await index.SearchAsync(generation, [1f, 0f], 10);

        Assert.True(File.Exists(location.DatabasePath));
        Assert.Equal(["sample-app.alpha", "sample-app.beta"], candidates.Select(candidate => candidate.QualifiedId));
        Assert.Throws<InteractionContractException>(() => InteractionDerivedIndexLocation.Create(Path.GetPathRoot(_temporaryRoot)!));
        Assert.Throws<InteractionContractException>(() => InteractionDerivedIndexLocation.Create("relative"));
    }

    [Fact]
    public async Task Deleted_or_stale_derived_index_preserves_lexical_results()
    {
        var location = InteractionDerivedIndexLocation.Create(_temporaryRoot);
        var index = new SqliteInteractionDerivedVectorIndex(location);
        var provider = new MutableSnapshots(Snapshot());
        var retriever = new InteractionFeatureRetriever(provider, new DeterministicEmbeddings(), index);
        var scope = new InteractionFeatureRetrievalScope(Application, InteractionRetrievalLane.TrustedFeature);
        await retriever.RebuildAsync(scope);
        File.Delete(location.DatabasePath);

        var deleted = await retriever.SearchAsync(scope, new("find", 10));
        await retriever.RebuildAsync(scope);
        provider.Snapshot = Snapshot(fingerprintMarker: 'B');
        var stale = await retriever.SearchAsync(scope, new("find", 10));

        Assert.Equal(InteractionRetrievalMode.LexicalFallback, deleted.Mode);
        Assert.Equal("VECTOR_INDEX_UNAVAILABLE", deleted.AvailabilityCode);
        Assert.Equal("sample-app.trusted", Assert.Single(deleted.Hits).Reference.QualifiedId);
        Assert.Equal(InteractionRetrievalMode.LexicalFallback, stale.Mode);
        Assert.Equal("VECTOR_INDEX_STALE", stale.AvailabilityCode);
    }

    [Fact]
    public async Task Broken_vector_index_falls_back_without_changing_catalog_results()
    {
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(Snapshot()), new DeterministicEmbeddings(), new ThrowingVectors());

        var result = await retriever.SearchAsync(new(Application, InteractionRetrievalLane.TrustedFeature), new("find", 10));

        Assert.Equal(InteractionRetrievalMode.LexicalFallback, result.Mode);
        Assert.Equal("VECTOR_INDEX_UNAVAILABLE", result.AvailabilityCode);
        Assert.Equal("sample-app.trusted", Assert.Single(result.Hits).Reference.QualifiedId);
    }

    [Fact]
    public async Task Complete_current_contract_below_document_ceiling_is_returned_without_truncation()
    {
        var description = "Character creation feature.";
        var payload = new string('x', 49_000);
        var content = $$"""{"id":"sample-app.character-create","description":"{{description}}","source":"{{payload}}"}""";
        var record = Record("sample-app.character-create", "Character create", description, ["create character"], content);
        var snapshot = Snapshot([record]);
        var retriever = new InteractionFeatureRetriever(new MutableSnapshots(snapshot));

        var result = await retriever.SearchAsync(
            new(Application, InteractionRetrievalLane.TrustedFeature),
            new("create character", 10));

        var hit = Assert.Single(result.Hits);
        Assert.Equal(content, hit.ContractJson);
        Assert.Equal(content.Length, hit.ContractJson.Length);
    }

    [Fact]
    public void Contract_above_document_ceiling_still_fails_closed()
    {
        var content = $$"""{"id":"sample-app.too-large","source":"{{new string('x', InteractionRetrievalLimits.MaximumDocumentText)}}"}""";
        var record = Record("sample-app.too-large", "Too large", "Oversized feature.", ["too large"], content);

        var exception = Assert.Throws<InteractionContractException>(() => InteractionRetrievalFingerprint.SearchText(record));

        Assert.Equal("RETRIEVAL_DOCUMENT_TOO_LARGE", exception.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private static ActiveCatalogFeatureSnapshot Snapshot(char fingerprintMarker = 'A', string alphaDescription = "Alpha feature.")
    {
        var records = new[]
        {
            Record("sample-app.alpha", "Alpha", alphaDescription, ["alpha feature"]),
            Record("sample-app.beta", "Beta", "Beta feature.", ["beta feature"]),
            Record("sample-app.trusted", "Trusted", "Find feature.", ["find feature"]),
            Record("sample-app.untrusted", "Untrusted", "Find feature.", ["find feature"])
        };
        var manifest = CatalogNavigationManifest.Create(Application, new string(fingerprintMarker, 64), "catalog-lexical-v1",
            [new("sample", "Sample", "Generic sample contracts.")],
            [new("sample", "", "Sample", "Generic sample contracts.", CatalogDescriptionStatus.Authored)], records);
        return new(manifest,
        [
            new(records[0], SourceTrust.Trusted), new(records[1], SourceTrust.Trusted),
            new(records[2], SourceTrust.Trusted), new(records[3], SourceTrust.Untrusted)
        ]);
    }

    private static ActiveCatalogFeatureSnapshot Snapshot(IReadOnlyList<CatalogRecordDefinition> records)
    {
        var manifest = CatalogNavigationManifest.Create(Application, new string('A', 64), "catalog-lexical-v1",
            [new("sample", "Sample", "Generic sample contracts.")],
            [new("sample", "", "Sample", "Generic sample contracts.", CatalogDescriptionStatus.Authored)], records);
        return new(manifest, records.Select(record => new ActiveCatalogFeatureDocument(record, SourceTrust.Trusted)).ToArray());
    }

    private static ActiveCatalogFeatureSnapshot WithResolution(
        ActiveCatalogFeatureSnapshot snapshot,
        CatalogExtensionResolutionContext resolution) => new(snapshot.Manifest, snapshot.Documents)
        {
            Resolution = resolution
        };

    private static CatalogRecordDefinition Record(string id, string name, string description, IReadOnlyList<string> phrases)
    {
        var content = $$"""{"id":"{{id}}","description":"{{description}}"}""";
        return Record(id, name, description, phrases, content);
    }

    private static CatalogRecordDefinition Record(string id, string name, string description, IReadOnlyList<string> phrases, string content)
    {
        return new("sample", "procedure", id, name, description, [], phrases, "", "active", 1, content,
            Hash(content), "source", "procedures/" + id + ".md");
    }

    private static InteractionFeatureReference Reference(string qualifiedId) =>
        new(Application, InteractionRetrievalLane.TrustedFeature, new string('A', 64), "procedure", qualifiedId, 1, new string('B', 64));

    private static InteractionRetrievalGeneration Generation(EmbeddingProviderIdentity identity) =>
        new(InteractionRetrievalFingerprint.GenerationKey(Application, InteractionRetrievalLane.TrustedFeature, new string('A', 64), identity),
            Application, InteractionRetrievalLane.TrustedFeature, new string('A', 64), InteractionRetrievalFingerprint.FormatVersion, identity);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class MutableSnapshots(ActiveCatalogFeatureSnapshot snapshot) : IActiveCatalogFeatureSnapshotProvider
    {
        public ActiveCatalogFeatureSnapshot Snapshot { get; set; } = snapshot;
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot snapshot)
        {
            snapshot = Snapshot;
            return applicationId == Snapshot.Manifest.ApplicationId;
        }
    }

    private sealed class DeterministicEmbeddings : ITextEmbeddingProvider
    {
        private static readonly EmbeddingProviderIdentity Identity = new("fixture", "embedding", "1", 2);
        public Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddingProviderStatus(true, Identity));
        public Task<EmbeddingBatchResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddingBatchResult(Identity, inputs.Select(Vector).ToArray()));
        private static float[] Vector(string text) => text.Contains("beta", StringComparison.OrdinalIgnoreCase) ? [0f, 1f] : [1f, 0f];
    }

    private sealed class MemoryVectors : IInteractionDerivedVectorIndex
    {
        private readonly Dictionary<string, IReadOnlyList<InteractionVectorDocument>> _documents = new(StringComparer.Ordinal);
        public IReadOnlyList<string> Ids => _documents.Values.SelectMany(value => value).Select(value => value.Reference.QualifiedId).ToArray();
        public Task ReplaceAsync(InteractionRetrievalGeneration generation, IReadOnlyList<InteractionVectorDocument> documents, CancellationToken cancellationToken = default)
        {
            _documents[generation.GenerationKey] = documents.ToArray();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<InteractionVectorCandidate>> SearchAsync(InteractionRetrievalGeneration generation, float[] query, int limit, CancellationToken cancellationToken = default)
        {
            if (!_documents.TryGetValue(generation.GenerationKey, out var documents)) return Task.FromResult<IReadOnlyList<InteractionVectorCandidate>>([]);
            return Task.FromResult<IReadOnlyList<InteractionVectorCandidate>>(documents.Select(value => new InteractionVectorCandidate(
                value.Reference.QualifiedId, value.Vector[0] == query[0] ? 0 : 1)).OrderBy(value => value.Distance).ThenBy(value => value.QualifiedId, StringComparer.Ordinal).Take(limit).ToArray());
        }
    }

    private sealed class ThrowingVectors : IInteractionDerivedVectorIndex
    {
        public Task ReplaceAsync(InteractionRetrievalGeneration generation, IReadOnlyList<InteractionVectorDocument> documents, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<InteractionVectorCandidate>> SearchAsync(InteractionRetrievalGeneration generation, float[] query, int limit, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
