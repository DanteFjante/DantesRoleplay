using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

/// <summary>
/// Retrieves only current active catalog records. Vector rows are candidates only; every returned
/// result is rebuilt from this snapshot and therefore cannot become catalog authority.
/// </summary>
public sealed class InteractionFeatureRetriever(
    IActiveCatalogFeatureSnapshotProvider snapshots,
    ITextEmbeddingProvider? embeddings = null,
    IInteractionDerivedVectorIndex? vectors = null,
    ICatalogNamespaceRegistry? namespaces = null) : IInteractionFeatureRetriever
{
    private static readonly byte[] CursorKey = SHA256.HashData(Encoding.UTF8.GetBytes(
        "dantes-roleplay/interaction-feature-retrieval-cursors/v1"));
    private readonly IActiveCatalogFeatureSnapshotProvider _snapshots = snapshots;
    private readonly ITextEmbeddingProvider? _embeddings = embeddings;
    private readonly IInteractionDerivedVectorIndex? _vectors = vectors;
    private readonly ICatalogNamespaceRegistry? _namespaces = namespaces;

    public async Task<InteractionFeatureSearchResult> SearchAsync(
        InteractionFeatureRetrievalScope scope,
        InteractionFeatureSearchInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);
        if (!_snapshots.TryGetSnapshot(scope.ApplicationId, out var snapshot))
        {
            // Name the materialization failure when the provider recorded one; "unavailable" on its
            // own gives a caller nothing to act on.
            var failure = (_snapshots as IPublicApplicationCatalogDiagnostics)?.LastFailure(scope.ApplicationId);
            return failure is null
                ? Unavailable("CATALOG_UNAVAILABLE", "The host-bound application catalog is unavailable.")
                : Unavailable("CATALOG_UNAVAILABLE",
                    $"The host-bound application catalog could not be materialized: {failure.Code} — {failure.Message}");
        }

        var current = Current(snapshot, scope);
        if (current.ErrorCode.Length != 0)
            return Unavailable(current.ErrorCode, current.ErrorMessage);
        current = (FilterNamespaces(current.Documents, scope.ApplicationId, input.NamespaceId), "", "");
        var resolved = Resolve(snapshot, current.Documents, input.IncludeShadowed);
        current = (resolved.Records, "", "");
        if (current.Documents.Count == 0)
            return InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Lexical, []);

        // An exact qualified id, or an exact authored phrase. Phrases are the alternative keys a
        // record declares for itself -- "what does a location need to be playable" naming the
        // contract that answers it -- and they exist precisely because similarity alone drifts:
        // without this, that question retrieves whichever document merely shares vocabulary.
        // Ambiguity is not resolved here; two records claiming one phrase fall through to ranking.
        var exact = current.Documents.SingleOrDefault(document => document.Record.QualifiedId == input.Query)
            ?? SinglePhraseMatch(current.Documents, input.Query);
        if (exact is not null)
            return InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Exact,
                [Hit(snapshot, scope, exact, null, null, true)],
                resolutionDiagnostics: DiagnosticsForHits(resolved.Diagnostics,
                    [Hit(snapshot, scope, exact, null, null, true)]));

        IReadOnlyList<(ActiveCatalogFeatureDocument Document, int Rank)> lexical;
        try
        {
            lexical = Lexical(snapshot, scope, current.Documents, input);
        }
        catch (ArgumentException)
        {
            return Unavailable("CATALOG_SEARCH_INVALID", "The host-bound catalog could not search the bounded request.");
        }

        if (_embeddings is null || _vectors is null)
            return LexicalFallback(snapshot, scope, lexical, input, "VECTOR_INDEX_DISABLED",
                "Vector retrieval is not configured; lexical retrieval remains available.");

        EmbeddingProviderStatus status;
        try { status = await _embeddings.CheckAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return LexicalFallback(snapshot, scope, lexical, input, "EMBEDDING_UNAVAILABLE", "Embedding retrieval is unavailable; lexical retrieval remains available."); }
        if (!status.Ready || status.Identity is null)
            return LexicalFallback(snapshot, scope, lexical, input,
                SafeCode(status.ErrorCode, "EMBEDDING_UNAVAILABLE"), SafeMessage(status.ErrorMessage, "Embedding retrieval is unavailable; lexical retrieval remains available."));

        EmbeddingBatchResult embedded;
        try { embedded = await _embeddings.EmbedAsync([input.Query], cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return LexicalFallback(snapshot, scope, lexical, input, "EMBEDDING_UNAVAILABLE", "Embedding retrieval is unavailable; lexical retrieval remains available."); }
        if (!embedded.Ok || embedded.Identity is null || embedded.Identity != status.Identity || embedded.Vectors.Count != 1)
            return LexicalFallback(snapshot, scope, lexical, input,
                SafeCode(embedded.ErrorCode, "EMBEDDING_UNAVAILABLE"), SafeMessage(embedded.ErrorMessage, "Embedding retrieval is unavailable; lexical retrieval remains available."));

        var generation = Generation(scope, snapshot, status.Identity);
        IReadOnlyList<InteractionVectorCandidate> vector;
        try { vector = await _vectors.SearchAsync(generation, embedded.Vectors[0], CandidateLimit(input.Limit), cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InteractionContractException exception) { return LexicalFallback(snapshot, scope, lexical, input, SafeCode(exception.Code, "VECTOR_INDEX_UNAVAILABLE"), "Vector retrieval is unavailable; lexical retrieval remains available."); }
        catch { return LexicalFallback(snapshot, scope, lexical, input, "VECTOR_INDEX_UNAVAILABLE", "Vector retrieval is unavailable; lexical retrieval remains available."); }

        var documents = current.Documents.ToDictionary(value => value.Record.QualifiedId, StringComparer.Ordinal);
        var fused = new Dictionary<string, Fusion>(StringComparer.Ordinal);
        for (var index = 0; index < lexical.Count; index++)
        {
            ref var item = ref CollectionsMarshalHelper.GetOrAdd(fused, lexical[index].Document.Record.QualifiedId);
            item.Score += Reciprocal(index);
            item.LexicalRank = lexical[index].Rank;
        }
        for (var index = 0; index < vector.Count; index++)
        {
            if (!documents.ContainsKey(vector[index].QualifiedId)) continue;
            ref var item = ref CollectionsMarshalHelper.GetOrAdd(fused, vector[index].QualifiedId);
            item.Score += Reciprocal(index);
            item.VectorRank = index + 1;
        }

        var ranked = fused.OrderByDescending(pair => pair.Value.Score).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (Document: documents[pair.Key], Fusion: pair.Value)).ToArray();
        var hits = ranked.Take(input.Limit)
            .Select(value => Hit(snapshot, scope, value.Document, value.Fusion.LexicalRank, value.Fusion.VectorRank, false))
            .ToArray();
        return InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Hybrid, hits,
            resolutionDiagnostics: DiagnosticsForHits(resolved.Diagnostics, hits));
    }

    public async Task<InteractionFeatureRebuildResult> RebuildAsync(
        InteractionFeatureRetrievalScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!_snapshots.TryGetSnapshot(scope.ApplicationId, out var snapshot))
            return new(false, 0, AvailabilityCode: "CATALOG_UNAVAILABLE", AvailabilityMessage: "The host-bound application catalog is unavailable.");
        var current = Current(snapshot, scope);
        if (current.ErrorCode.Length != 0)
            return new(false, 0, AvailabilityCode: current.ErrorCode, AvailabilityMessage: current.ErrorMessage);
        var resolved = Resolve(snapshot, current.Documents, includeShadowed: false);
        current = (resolved.Records, "", "");
        if (_embeddings is null || _vectors is null)
            return new(false, current.Documents.Count, AvailabilityCode: "VECTOR_INDEX_DISABLED", AvailabilityMessage: "Vector retrieval is not configured.");

        EmbeddingProviderStatus status;
        try { status = await _embeddings.CheckAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(false, current.Documents.Count, AvailabilityCode: "EMBEDDING_UNAVAILABLE", AvailabilityMessage: "Embedding retrieval is unavailable."); }
        if (!status.Ready || status.Identity is null)
            return new(false, current.Documents.Count, AvailabilityCode: SafeCode(status.ErrorCode, "EMBEDDING_UNAVAILABLE"), AvailabilityMessage: SafeMessage(status.ErrorMessage, "Embedding retrieval is unavailable."));

        var built = new List<InteractionVectorDocument>(current.Documents.Count);
        foreach (var batch in current.Documents.Chunk(32))
        {
            var texts = batch.Select(value => InteractionRetrievalFingerprint.EmbeddingText(value.Record)).ToArray();
            EmbeddingBatchResult embedded;
            try { embedded = await _embeddings.EmbedAsync(texts, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return new(false, current.Documents.Count, AvailabilityCode: "EMBEDDING_UNAVAILABLE", AvailabilityMessage: "Embedding retrieval is unavailable."); }
            if (!embedded.Ok || embedded.Identity != status.Identity || embedded.Vectors.Count != batch.Length)
                return new(false, current.Documents.Count, AvailabilityCode: SafeCode(embedded.ErrorCode, "EMBEDDING_RESPONSE_INVALID"), AvailabilityMessage: SafeMessage(embedded.ErrorMessage, "Embedding retrieval returned an invalid response."));
            for (var index = 0; index < batch.Length; index++)
                built.Add(InteractionVectorDocument.Create(Reference(snapshot, scope, batch[index].Record), texts[index], embedded.Vectors[index]));
        }

        var generation = Generation(scope, snapshot, status.Identity);
        try { await _vectors.ReplaceAsync(generation, built, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(false, current.Documents.Count, AvailabilityCode: "VECTOR_INDEX_UNAVAILABLE", AvailabilityMessage: "The disposable vector index could not be rebuilt."); }
        return new(true, built.Count, generation.GenerationKey);
    }

    /// <summary>
    /// The one record claiming this phrase as an alias or match phrase, or null when none or
    /// several do. Comparison is trimmed, case-insensitive and whitespace-collapsed, so a phrase
    /// keeps working when a caller types it with different spacing or capitals.
    /// </summary>
    private static ActiveCatalogFeatureDocument? SinglePhraseMatch(
        IReadOnlyList<ActiveCatalogFeatureDocument> documents,
        string query)
    {
        var wanted = NormalizePhrase(query);
        if (wanted.Length == 0) return null;
        ActiveCatalogFeatureDocument? found = null;
        foreach (var document in documents)
        {
            var claims = document.Record.Aliases.Concat(document.Record.MatchPhrases)
                .Any(value => NormalizePhrase(value) == wanted);
            if (!claims) continue;
            if (found is not null) return null;
            found = document;
        }
        return found;
    }

    private static string NormalizePhrase(string value) =>
        string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static (IReadOnlyList<ActiveCatalogFeatureDocument> Documents, string ErrorCode, string ErrorMessage) Current(
        ActiveCatalogFeatureSnapshot snapshot,
        InteractionFeatureRetrievalScope scope)
    {
        if (snapshot.Manifest.ApplicationId != scope.ApplicationId)
            return ([], "CATALOG_APPLICATION_MISMATCH", "The catalog snapshot does not match the host-bound application.");
        var trust = scope.Lane == InteractionRetrievalLane.TrustedFeature ? SourceTrust.Trusted : SourceTrust.Untrusted;
        try
        {
            var values = snapshot.Documents.Where(document => document.Trust == trust)
                .Where(document => document.Record.Kind is "procedure" or "mechanic")
                .Select(document =>
                {
                    _ = InteractionRetrievalFingerprint.SearchText(document.Record);
                    return document;
                }).ToArray();
            return (Array.AsReadOnly(values), "", "");
        }
        catch (InteractionContractException)
        {
            return ([], "CATALOG_RECORD_UNAVAILABLE", "An active catalog feature could not form a bounded retrieval document.");
        }
    }

    private static IReadOnlyList<(ActiveCatalogFeatureDocument Document, int Rank)> Lexical(
        ActiveCatalogFeatureSnapshot snapshot,
        InteractionFeatureRetrievalScope scope,
        IReadOnlyList<ActiveCatalogFeatureDocument> documents,
        InteractionFeatureSearchInput input)
    {
        var laneFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            snapshot.Manifest.Fingerprint + "\0" + scope.Lane.ToString())));
        var manifest = CatalogNavigationManifest.Create(snapshot.Manifest.ApplicationId, laneFingerprint,
            snapshot.Manifest.SortVersion, snapshot.Manifest.Collections, snapshot.Manifest.Nodes,
            documents.Select(value => value.Record).ToArray());
        var navigator = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(CursorKey));
        // The namespace filter is not passed on: these documents have already been narrowed by it,
        // and the navigator recognises only the application-qualified spelling of a namespace.
        var result = navigator.Search(new(scope.ApplicationId, input.Query, null, "", input.Kinds, input.Statuses,
            CandidateLimit(input.Limit)));
        var map = documents.ToDictionary(value => value.Record.QualifiedId, StringComparer.Ordinal);
        return result.Records.Select(hit => (map[hit.Record.QualifiedId], hit.Rank)).ToArray();
    }

    private InteractionFeatureSearchResult LexicalFallback(
        ActiveCatalogFeatureSnapshot snapshot,
        InteractionFeatureRetrievalScope scope,
        IReadOnlyList<(ActiveCatalogFeatureDocument Document, int Rank)> lexical,
        InteractionFeatureSearchInput input,
        string code,
        string message)
    {
        var hits = lexical.Take(input.Limit)
            .Select(value => Hit(snapshot, scope, value.Document, value.Rank, null, false)).ToArray();
        return InteractionFeatureSearchResult.Create(InteractionRetrievalMode.LexicalFallback,
            hits, code, message);
    }

    private static InteractionFeatureSearchResult Unavailable(string code, string message) =>
        InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Unavailable, [], code, message);

    private static CatalogExtensionSearchSelection<ActiveCatalogFeatureDocument> Resolve(
        ActiveCatalogFeatureSnapshot snapshot,
        IReadOnlyList<ActiveCatalogFeatureDocument> documents,
        bool includeShadowed) => CatalogExtensionSearch.Apply(snapshot.Resolution, documents,
            value => value.Record.QualifiedId, value => value.Record.Kind, includeShadowed);

    private static IReadOnlyList<CatalogResolutionDiagnosticView> DiagnosticsForHits(
        IReadOnlyList<CatalogResolutionDiagnosticView> resolutions,
        IReadOnlyList<InteractionFeatureHit> hits)
    {
        var ids = hits.Select(value => value.Reference.QualifiedId).ToHashSet(StringComparer.Ordinal);
        return Array.AsReadOnly(resolutions.Where(value => ids.Contains(value.WinnerQualifiedId)).ToArray());
    }

    private IReadOnlyList<ActiveCatalogFeatureDocument> FilterNamespaces(
        IReadOnlyList<ActiveCatalogFeatureDocument> documents,
        ApplicationIdentifier application,
        string? requestedNamespace)
    {
        var registryActive = _namespaces is not null && _namespaces.List(includeDisabled: true).Count != 0;
        var prefix = application.Value + ".";
        return documents.Where(document =>
        {
            var namespaceId = CatalogNamespaceIdentity.NamespaceOf(document.Record.QualifiedId);
            // A caller may name the namespace either way round: qualified, as it appears in a
            // result reference, or unprefixed, as the namespace registry lists it.
            var unprefixed = namespaceId.Length > prefix.Length && namespaceId.StartsWith(prefix, StringComparison.Ordinal)
                ? namespaceId[prefix.Length..]
                : null;
            if (requestedNamespace is not null
                && !Within(namespaceId, requestedNamespace)
                && !(unprefixed is not null && Within(unprefixed, requestedNamespace))) return false;
            return !registryActive || IsRegistered(namespaceId, unprefixed);
        }).ToArray();
    }

    private static bool Within(string namespaceId, string requested) =>
        namespaceId == requested || namespaceId.StartsWith(requested + ".", StringComparison.Ordinal);

    /// <summary>
    /// Whether a document's namespace is registered. The namespace registry is keyed by a record's
    /// OWN id, but a record reaches this layer under its application-qualified id: for the many
    /// records whose id is application-neutral, that id is "&lt;application&gt;.&lt;record id&gt;",
    /// so the registered namespace is the qualified one with the application prefix removed.
    /// Testing only the qualified form hid every application-neutral record -- procedure.campaign.*,
    /// procedure.quest.*, procedure.world.* and most of the catalog -- from retrieval, while catalog
    /// browsing, which does not apply this gate, still returned them. Both forms are tested because
    /// records whose id already carries the application prefix register under the qualified form.
    /// </summary>
    private bool IsRegistered(string namespaceId, string? unprefixed) =>
        _namespaces!.Get(namespaceId) is not null
        || (unprefixed is not null && _namespaces.Get(unprefixed) is not null);

    private static InteractionFeatureHit Hit(
        ActiveCatalogFeatureSnapshot snapshot,
        InteractionFeatureRetrievalScope scope,
        ActiveCatalogFeatureDocument document,
        int? lexicalRank,
        int? vectorRank,
        bool exact) => InteractionFeatureHit.Create(Reference(snapshot, scope, document.Record), document.Record, lexicalRank, vectorRank, exact);

    private static InteractionFeatureReference Reference(ActiveCatalogFeatureSnapshot snapshot, InteractionFeatureRetrievalScope scope, CatalogRecordDefinition record) =>
        InteractionFeatureReference.Create(scope.ApplicationId, scope.Lane, snapshot.Manifest.Fingerprint, record);

    private static InteractionRetrievalGeneration Generation(
        InteractionFeatureRetrievalScope scope,
        ActiveCatalogFeatureSnapshot snapshot,
        EmbeddingProviderIdentity identity) =>
        new(InteractionRetrievalFingerprint.GenerationKey(scope.ApplicationId, scope.Lane,
                snapshot.Manifest.Fingerprint, identity, snapshot.Resolution?.Fingerprint),
            scope.ApplicationId, scope.Lane, snapshot.Manifest.Fingerprint,
            InteractionRetrievalFingerprint.FormatVersion, identity)
        {
            ResolutionFingerprint = snapshot.Resolution?.Fingerprint ?? snapshot.Manifest.Fingerprint
        };

    private static int CandidateLimit(int limit) => Math.Min(CatalogNavigationLimits.MaximumPageSize,
        Math.Max(limit, limit * InteractionRetrievalLimits.HybridCandidateMultiplier));
    private static double Reciprocal(int zeroBasedRank) => 1d / (InteractionRetrievalLimits.ReciprocalRankConstant + zeroBasedRank + 1d);
    private static string SafeCode(string value, string fallback) => string.IsNullOrWhiteSpace(value) || value.Length > 100 || value.Any(char.IsControl) ? fallback : value;
    private static string SafeMessage(string value, string fallback) => string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(char.IsControl) ? fallback : value;

    private struct Fusion
    {
        public double Score;
        public int? LexicalRank;
        public int? VectorRank;
    }

    private static class CollectionsMarshalHelper
    {
        public static ref Fusion GetOrAdd(Dictionary<string, Fusion> values, string key) =>
            ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(values, key, out _);
    }
}
