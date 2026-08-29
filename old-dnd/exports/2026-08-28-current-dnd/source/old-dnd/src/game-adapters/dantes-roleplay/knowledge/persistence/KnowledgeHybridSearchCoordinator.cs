using DantesRoleplay.Content;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Builds the shared lexical/vector projection and performs deterministic reciprocal-rank fusion.
/// Every candidate is re-hydrated from canonical world state before it can become a hit.
/// </summary>
public sealed class KnowledgeHybridSearchCoordinator(
    IKnowledgeTimelineCoordinator timeline,
    IKnowledgeSearchDocumentSource documents,
    IKnowledgeLexicalIndex lexical,
    ITextEmbeddingProvider embeddings,
    IKnowledgeVectorIndex vectors,
    KnowledgeRetrievalOptions options) : IKnowledgeHybridSearchCoordinator
{
    private readonly IKnowledgeTimelineCoordinator _timeline = timeline;
    private readonly IKnowledgeSearchDocumentSource _documents = documents;
    private readonly IKnowledgeLexicalIndex _lexical = lexical;
    private readonly ITextEmbeddingProvider _embeddings = embeddings;
    private readonly IKnowledgeVectorIndex _vectors = vectors;
    private readonly KnowledgeRetrievalOptions _options = options;

    public Task<KnowledgeHybridRebuildResult> RebuildWorldAsync(
        string worldId,
        CancellationToken cancellationToken = default) =>
        ProjectWorldAsync(worldId, forceReplace: true, cancellationToken);

    public Task<KnowledgeHybridRebuildResult> SynchronizeWorldAsync(
        string worldId,
        CancellationToken cancellationToken = default) =>
        ProjectWorldAsync(worldId, forceReplace: false, cancellationToken);

    private async Task<KnowledgeHybridRebuildResult> ProjectWorldAsync(
        string worldId,
        bool forceReplace,
        CancellationToken cancellationToken)
    {
        var invalid = _options.Validate();
        if (invalid is not null) throw new InvalidOperationException($"KNOWLEDGE_RETRIEVAL_CONFIG_INVALID: {invalid}");
        var history = await _timeline.ReadAsOfAsync(worldId, null, cancellationToken);
        if (!history.Read)
            throw new InvalidOperationException($"{history.Problems[0].Code}: {history.Problems[0].Reason}");

        var canonical = await _documents.ReadWorldAsync(worldId, cancellationToken);
        await _lexical.ReplaceWorldAsync(worldId, canonical, cancellationToken);

        if (!_options.Vector.Enabled)
            return Fallback(
                worldId,
                canonical.Count,
                "VECTOR_INDEX_DISABLED",
                "The sqlite-vec provider is disabled; lexical retrieval remains available.");

        var status = await _embeddings.CheckAsync(cancellationToken);
        if (!status.Ready || status.Identity is null)
            return Fallback(worldId, canonical.Count, status.ErrorCode, status.ErrorMessage);

        var generation = Generation(status.Identity);
        var replace = forceReplace;
        IReadOnlyDictionary<string, string> hashes = new Dictionary<string, string>();
        if (!replace)
        {
            try
            {
                hashes = await _vectors.ReadContentHashesAsync(generation, worldId, cancellationToken);
                replace = hashes.Count != canonical.Count ||
                          canonical.Any(document => !hashes.ContainsKey(document.KnowledgeId));
            }
            catch (OperationCanceledException) { throw; }
            catch { replace = true; }
        }
        var pending = replace
            ? canonical
            : canonical.Where(document => hashes[document.KnowledgeId] != document.ContentHash).ToArray();
        var indexed = new List<KnowledgeVectorDocument>(pending.Count);
        foreach (var batch in pending.Chunk(_options.BackfillBatchSize))
        {
            var result = await _embeddings.EmbedAsync(batch.Select(document => document.Text).ToArray(), cancellationToken);
            if (!result.Ok || result.Identity is null)
                return Fallback(worldId, canonical.Count, result.ErrorCode, result.ErrorMessage);
            if (result.Identity != status.Identity)
                return Fallback(
                    worldId,
                    canonical.Count,
                    "EMBEDDING_IDENTITY_CHANGED",
                    "The embedding model identity changed during the rebuild; no vectors were replaced.");
            for (var index = 0; index < batch.Length; index++)
                indexed.Add(new(
                    batch[index].KnowledgeId,
                    batch[index].WorldId,
                    batch[index].ContentHash,
                    result.Vectors[index]));
        }

        try
        {
            if (replace)
                await _vectors.ReplaceWorldAsync(generation, worldId, indexed, cancellationToken);
            else if (indexed.Count > 0)
                await _vectors.UpsertAsync(generation, indexed, cancellationToken);
            await _vectors.MarkOtherGenerationsStaleAsync(generation.Id, cancellationToken);
            return new(
                worldId,
                canonical.Count,
                canonical.Count,
                true,
                generation.Id,
                EmbeddedDocuments: indexed.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return Fallback(worldId, canonical.Count, "VECTOR_INDEX_UNAVAILABLE", Safe(exception.Message));
        }
    }

    public async Task<KnowledgeHybridSearchResult> SearchAsync(
        KnowledgeLexicalSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.WorldId))
            return KnowledgeHybridSearchResult.Fail(
                request?.WorldId ?? string.Empty,
                "INVALID_KNOWLEDGE_SEARCH",
                "Search requires a canonical world id.");
        var invalid = _options.Validate();
        if (invalid is not null)
            return KnowledgeHybridSearchResult.Fail(
                request.WorldId, "KNOWLEDGE_RETRIEVAL_CONFIG_INVALID", invalid);

        var history = await _timeline.ReadAsOfAsync(request.WorldId, request.AsOfMinute, cancellationToken);
        if (!history.Read)
            return KnowledgeHybridSearchResult.Fail(
                request.WorldId, history.Problems[0].Code, history.Problems[0].Reason);

        var candidateLimit = Math.Min(
            _options.CandidateLimit,
            Math.Max(request.Limit * 4, request.Limit));
        IReadOnlyList<KnowledgeLexicalCandidate> lexical;
        try
        {
            lexical = await _lexical.SearchAsync(
                request with { AsOfMinute = history.AsOfMinute, Limit = candidateLimit }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return KnowledgeHybridSearchResult.Fail(
                request.WorldId, "INVALID_KNOWLEDGE_SEARCH", Safe(exception.Message));
        }

        IReadOnlyList<KnowledgeVectorCandidate> vector = [];
        var mode = "lexical";
        var generationId = string.Empty;
        var fallbackCode = string.Empty;
        var fallbackMessage = string.Empty;
        if (!_options.Vector.Enabled)
        {
            fallbackCode = "VECTOR_INDEX_DISABLED";
            fallbackMessage = "The sqlite-vec provider is disabled; lexical retrieval remains available.";
        }
        else
        {
            var embedded = await _embeddings.EmbedAsync([request.Query], cancellationToken);
            if (embedded.Ok && embedded.Identity is not null)
            {
                var generation = Generation(embedded.Identity);
                try
                {
                    vector = await _vectors.SearchAsync(
                        generation, request.WorldId, embedded.Vectors[0], candidateLimit, cancellationToken);
                    mode = "hybrid";
                    generationId = generation.Id;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    fallbackCode = "VECTOR_INDEX_UNAVAILABLE";
                    fallbackMessage = Safe(exception.Message);
                }
            }
            else
            {
                fallbackCode = embedded.ErrorCode;
                fallbackMessage = embedded.ErrorMessage;
            }
        }

        var fused = new Dictionary<string, Fusion>(StringComparer.Ordinal);
        for (var index = 0; index < lexical.Count; index++)
        {
            ref var item = ref CollectionsMarshalHelper.GetOrAdd(fused, lexical[index].KnowledgeId);
            item.Score += Reciprocal(index);
            item.LexicalRank = index + 1;
        }
        for (var index = 0; index < vector.Count; index++)
        {
            ref var item = ref CollectionsMarshalHelper.GetOrAdd(fused, vector[index].KnowledgeId);
            item.Score += Reciprocal(index);
            item.VectorRank = index + 1;
            item.VectorDistance = vector[index].Distance;
        }

        var exact = await _documents.ReadAsync(request.Query.Trim(), cancellationToken);
        if (exact is not null && exact.KnowledgeId == request.Query.Trim())
        {
            ref var item = ref CollectionsMarshalHelper.GetOrAdd(fused, exact.KnowledgeId);
            item.Score += 1;
            item.Exact = true;
        }

        var applicable = history.Records
            .Where(record => record.TemporalStatus is "atemporal" or "effective")
            .Select(record => record.KnowledgeId)
            .ToHashSet(StringComparer.Ordinal);
        var hits = new List<KnowledgeHybridSearchHit>();
        foreach (var pair in fused
                     .OrderByDescending(pair => pair.Value.Score)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!applicable.Contains(pair.Key)) continue;
            var document = pair.Key == exact?.KnowledgeId
                ? exact
                : await _documents.ReadAsync(pair.Key, cancellationToken);
            if (!Allowed(document, request)) continue;
            hits.Add(new(
                document!.KnowledgeId,
                document.Kind,
                document.Status,
                document.SubjectId,
                document.Sensitivity,
                Summary(document.Text),
                pair.Value.Score,
                pair.Value.LexicalRank,
                pair.Value.VectorRank,
                pair.Value.VectorDistance,
                pair.Value.Exact));
            if (hits.Count == request.Limit) break;
        }

        return new(
            request.WorldId,
            history.AsOfMinute,
            mode,
            hits,
            generationId,
            fallbackCode,
            fallbackMessage);
    }

    private KnowledgeHybridRebuildResult Fallback(
        string worldId,
        int lexicalDocuments,
        string code,
        string message) =>
        new(worldId, lexicalDocuments, 0, false, FallbackCode: code, FallbackMessage: Safe(message));

    private static KnowledgeVectorGeneration Generation(EmbeddingProviderIdentity identity)
    {
        var fingerprint = ContentHash.Of(
            identity.Provider, identity.Model, identity.Revision, identity.Dimensions.ToString());
        return new($"knowledge.{fingerprint.ToLowerInvariant()}", identity, DateTimeOffset.UtcNow);
    }

    private double Reciprocal(int zeroBasedRank) =>
        1d / (_options.ReciprocalRankConstant + zeroBasedRank + 1d);

    private static bool Allowed(KnowledgeLexicalDocument? document, KnowledgeLexicalSearchRequest request) =>
        document is not null &&
        document.WorldId == request.WorldId &&
        (request.IncludeArchived || document.Status != "archived") &&
        (request.Kinds is not { Count: > 0 } || request.Kinds.Contains(document.Kind, StringComparer.Ordinal)) &&
        (request.SubjectIds is not { Count: > 0 } || request.SubjectIds.Contains(document.SubjectId, StringComparer.Ordinal));

    private static string Summary(string text) =>
        text.Split('\n', StringSplitOptions.None).Skip(1).FirstOrDefault() ?? string.Empty;

    private static bool Id(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id == id.Trim() && id.Length <= 200;

    private static string Safe(string value) => value.Length <= 500 ? value : value[..500];

    private struct Fusion
    {
        public double Score;
        public int? LexicalRank;
        public int? VectorRank;
        public double? VectorDistance;
        public bool Exact;
    }

    private static class CollectionsMarshalHelper
    {
        public static ref Fusion GetOrAdd(Dictionary<string, Fusion> values, string key) =>
            ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                values, key, out _);
    }
}
