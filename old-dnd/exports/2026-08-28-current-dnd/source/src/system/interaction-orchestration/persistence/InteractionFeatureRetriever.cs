using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.CatalogNavigation;
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
    IInteractionDerivedVectorIndex? vectors = null) : IInteractionFeatureRetriever
{
    private static readonly byte[] CursorKey = SHA256.HashData(Encoding.UTF8.GetBytes(
        "dantes-roleplay/interaction-feature-retrieval-cursors/v1"));
    private readonly IActiveCatalogFeatureSnapshotProvider _snapshots = snapshots;
    private readonly ITextEmbeddingProvider? _embeddings = embeddings;
    private readonly IInteractionDerivedVectorIndex? _vectors = vectors;

    public async Task<InteractionFeatureSearchResult> SearchAsync(
        InteractionFeatureRetrievalScope scope,
        InteractionFeatureSearchInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);
        if (!_snapshots.TryGetSnapshot(scope.ApplicationId, out var snapshot))
            return Unavailable("CATALOG_UNAVAILABLE", "The host-bound application catalog is unavailable.");

        var current = Current(snapshot, scope);
        if (current.ErrorCode.Length != 0)
            return Unavailable(current.ErrorCode, current.ErrorMessage);
        if (current.Documents.Count == 0)
            return InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Lexical, []);

        var exact = current.Documents.SingleOrDefault(document => document.Record.QualifiedId == input.Query);
        if (exact is not null)
            return InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Exact,
                [Hit(snapshot, scope, exact, null, null, true)]);

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
            return LexicalFallback(snapshot, scope, lexical, input.Limit, "VECTOR_INDEX_DISABLED",
                "Vector retrieval is not configured; lexical retrieval remains available.");

        EmbeddingProviderStatus status;
        try { status = await _embeddings.CheckAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return LexicalFallback(snapshot, scope, lexical, input.Limit, "EMBEDDING_UNAVAILABLE", "Embedding retrieval is unavailable; lexical retrieval remains available."); }
        if (!status.Ready || status.Identity is null)
            return LexicalFallback(snapshot, scope, lexical, input.Limit,
                SafeCode(status.ErrorCode, "EMBEDDING_UNAVAILABLE"), SafeMessage(status.ErrorMessage, "Embedding retrieval is unavailable; lexical retrieval remains available."));

        EmbeddingBatchResult embedded;
        try { embedded = await _embeddings.EmbedAsync([input.Query], cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return LexicalFallback(snapshot, scope, lexical, input.Limit, "EMBEDDING_UNAVAILABLE", "Embedding retrieval is unavailable; lexical retrieval remains available."); }
        if (!embedded.Ok || embedded.Identity is null || embedded.Identity != status.Identity || embedded.Vectors.Count != 1)
            return LexicalFallback(snapshot, scope, lexical, input.Limit,
                SafeCode(embedded.ErrorCode, "EMBEDDING_UNAVAILABLE"), SafeMessage(embedded.ErrorMessage, "Embedding retrieval is unavailable; lexical retrieval remains available."));

        var generation = Generation(scope, snapshot.Manifest.Fingerprint, status.Identity);
        IReadOnlyList<InteractionVectorCandidate> vector;
        try { vector = await _vectors.SearchAsync(generation, embedded.Vectors[0], CandidateLimit(input.Limit), cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InteractionContractException exception) { return LexicalFallback(snapshot, scope, lexical, input.Limit, SafeCode(exception.Code, "VECTOR_INDEX_UNAVAILABLE"), "Vector retrieval is unavailable; lexical retrieval remains available."); }
        catch { return LexicalFallback(snapshot, scope, lexical, input.Limit, "VECTOR_INDEX_UNAVAILABLE", "Vector retrieval is unavailable; lexical retrieval remains available."); }

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

        var hits = fused.OrderByDescending(pair => pair.Value.Score).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(input.Limit)
            .Select(pair => Hit(snapshot, scope, documents[pair.Key], pair.Value.LexicalRank, pair.Value.VectorRank, false))
            .ToArray();
        return InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Hybrid, hits);
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
            var texts = batch.Select(value => InteractionRetrievalFingerprint.SearchText(value.Record)).ToArray();
            EmbeddingBatchResult embedded;
            try { embedded = await _embeddings.EmbedAsync(texts, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return new(false, current.Documents.Count, AvailabilityCode: "EMBEDDING_UNAVAILABLE", AvailabilityMessage: "Embedding retrieval is unavailable."); }
            if (!embedded.Ok || embedded.Identity != status.Identity || embedded.Vectors.Count != batch.Length)
                return new(false, current.Documents.Count, AvailabilityCode: SafeCode(embedded.ErrorCode, "EMBEDDING_RESPONSE_INVALID"), AvailabilityMessage: SafeMessage(embedded.ErrorMessage, "Embedding retrieval returned an invalid response."));
            for (var index = 0; index < batch.Length; index++)
                built.Add(InteractionVectorDocument.Create(Reference(snapshot, scope, batch[index].Record), texts[index], embedded.Vectors[index]));
        }

        var generation = Generation(scope, snapshot.Manifest.Fingerprint, status.Identity);
        try { await _vectors.ReplaceAsync(generation, built, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(false, current.Documents.Count, AvailabilityCode: "VECTOR_INDEX_UNAVAILABLE", AvailabilityMessage: "The disposable vector index could not be rebuilt."); }
        return new(true, built.Count, generation.GenerationKey);
    }

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
        var result = navigator.Search(new(scope.ApplicationId, input.Query, null, "", input.Kinds, input.Statuses,
            CandidateLimit(input.Limit)));
        var map = documents.ToDictionary(value => value.Record.QualifiedId, StringComparer.Ordinal);
        return result.Records.Select(hit => (map[hit.Record.QualifiedId], hit.Rank)).ToArray();
    }

    private static InteractionFeatureSearchResult LexicalFallback(
        ActiveCatalogFeatureSnapshot snapshot,
        InteractionFeatureRetrievalScope scope,
        IReadOnlyList<(ActiveCatalogFeatureDocument Document, int Rank)> lexical,
        int limit,
        string code,
        string message) =>
        InteractionFeatureSearchResult.Create(InteractionRetrievalMode.LexicalFallback,
            lexical.Take(limit).Select(value => Hit(snapshot, scope, value.Document, value.Rank, null, false)), code, message);

    private static InteractionFeatureSearchResult Unavailable(string code, string message) =>
        InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Unavailable, [], code, message);

    private static InteractionFeatureHit Hit(
        ActiveCatalogFeatureSnapshot snapshot,
        InteractionFeatureRetrievalScope scope,
        ActiveCatalogFeatureDocument document,
        int? lexicalRank,
        int? vectorRank,
        bool exact) => InteractionFeatureHit.Create(Reference(snapshot, scope, document.Record), document.Record, lexicalRank, vectorRank, exact);

    private static InteractionFeatureReference Reference(ActiveCatalogFeatureSnapshot snapshot, InteractionFeatureRetrievalScope scope, CatalogRecordDefinition record) =>
        InteractionFeatureReference.Create(scope.ApplicationId, scope.Lane, snapshot.Manifest.Fingerprint, record);

    private static InteractionRetrievalGeneration Generation(InteractionFeatureRetrievalScope scope, string catalogFingerprint, EmbeddingProviderIdentity identity) =>
        new(InteractionRetrievalFingerprint.GenerationKey(scope.ApplicationId, scope.Lane, catalogFingerprint, identity),
            scope.ApplicationId, scope.Lane, catalogFingerprint, InteractionRetrievalFingerprint.FormatVersion, identity);

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
