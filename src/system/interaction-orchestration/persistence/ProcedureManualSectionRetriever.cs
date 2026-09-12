using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.Interactions;

/// <summary>One permitted, current manual section prepared by the manual owner.</summary>
internal sealed record ProcedureManualSectionVectorSource(
    string ProcedureId,
    int Version,
    string SourceFingerprint,
    string SectionReference,
    string SectionContentFingerprint,
    string SearchText);

internal sealed record ProcedureManualSectionVectorHit(string SectionReference, int Rank);

internal sealed record ProcedureManualSectionSearchResult(
    InteractionRetrievalMode Mode,
    IReadOnlyList<ProcedureManualSectionVectorHit> Hits,
    string AvailabilityCode = "");

/// <summary>
/// Rebuildable semantic index over the exact system-manual sections selected by the host. It owns no
/// manual content or authorization. The caller rechecks authoritative procedure revisions before use.
/// </summary>
public sealed class ProcedureManualSectionRetriever(
    ITextEmbeddingProvider? embeddings,
    IInteractionDerivedVectorIndex? vectors,
    InteractionRetrievalRefreshCoordinator refresh)
{
    internal const int MaximumSections = 4_096;
    private const int ResultLimit = 8;
    private const string SectionFormat = "procedure-manual-section-v1";

    internal async Task<ProcedureManualSectionSearchResult> SearchAsync(
        ApplicationIdentifier applicationId,
        string query,
        IReadOnlyList<ProcedureManualSectionVectorSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(sources);
        _ = new InteractionFeatureSearchInput(query, ResultLimit);
        if (sources.Count == 0) return new(InteractionRetrievalMode.Lexical, []);
        if (!TryPrepare(applicationId, sources, out var prepared, out var sourceFingerprint))
            return Fallback("MANUAL_SECTION_INDEX_LIMIT");
        if (embeddings is null || vectors is null) return Fallback("VECTOR_INDEX_DISABLED");

        EmbeddingProviderStatus status;
        try { status = await embeddings.CheckAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Fallback("EMBEDDING_UNAVAILABLE"); }
        if (!status.Ready || status.Identity is null) return Fallback(Safe(status.ErrorCode, "EMBEDDING_UNAVAILABLE"));

        EmbeddingBatchResult queryEmbedding;
        try { queryEmbedding = await embeddings.EmbedAsync([query], cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Fallback("EMBEDDING_UNAVAILABLE"); }
        if (!queryEmbedding.Ok || queryEmbedding.Identity != status.Identity || queryEmbedding.Vectors.Count != 1)
            return Fallback(Safe(queryEmbedding.ErrorCode, "EMBEDDING_RESPONSE_INVALID"));

        var generation = Generation(applicationId, sourceFingerprint, status.Identity);
        IReadOnlyList<InteractionVectorCandidate> candidates;
        try
        {
            candidates = await vectors.SearchAsync(generation, queryEmbedding.Vectors[0], ResultLimit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InteractionContractException exception) when (exception.Code is "VECTOR_INDEX_STALE" or "VECTOR_INDEX_UNAVAILABLE")
        {
            var rebuilt = await refresh.RunAsync(generation.GenerationKey,
                () => RebuildAsync(generation, prepared, cancellationToken), cancellationToken);
            if (!rebuilt.Rebuilt) return Fallback(Safe(rebuilt.AvailabilityCode, "VECTOR_REFRESH_FAILED"));
            try { candidates = await vectors.SearchAsync(generation, queryEmbedding.Vectors[0], ResultLimit, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return Fallback("VECTOR_INDEX_UNAVAILABLE"); }
        }
        catch { return Fallback("VECTOR_INDEX_UNAVAILABLE"); }

        var references = prepared.ToDictionary(value => value.Reference.QualifiedId,
            value => value.Source.SectionReference, StringComparer.Ordinal);
        var hits = candidates.Where(value => references.ContainsKey(value.QualifiedId)).Take(ResultLimit)
            .Select((value, index) => new ProcedureManualSectionVectorHit(references[value.QualifiedId], index + 1)).ToArray();
        return new(InteractionRetrievalMode.Hybrid, Array.AsReadOnly(hits));
    }

    private async Task<InteractionFeatureRebuildResult> RebuildAsync(
        InteractionRetrievalGeneration generation,
        IReadOnlyList<PreparedSection> sections,
        CancellationToken cancellationToken)
    {
        var documents = new List<InteractionVectorDocument>(sections.Count);
        foreach (var batch in sections.Chunk(32))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmbeddingBatchResult embedded;
            try { embedded = await embeddings!.EmbedAsync(batch.Select(value => value.Source.SearchText).ToArray(), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return new(false, sections.Count, AvailabilityCode: "EMBEDDING_UNAVAILABLE"); }
            if (!embedded.Ok || embedded.Identity != generation.Embedding || embedded.Vectors.Count != batch.Length)
                return new(false, sections.Count, AvailabilityCode: Safe(embedded.ErrorCode, "EMBEDDING_RESPONSE_INVALID"));
            for (var index = 0; index < batch.Length; index++)
                documents.Add(InteractionVectorDocument.Create(batch[index].Reference,
                    batch[index].Source.SearchText, embedded.Vectors[index]));
        }
        try { await vectors!.ReplaceAsync(generation, documents, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(false, sections.Count, AvailabilityCode: "VECTOR_INDEX_UNAVAILABLE"); }
        return new(true, sections.Count, generation.GenerationKey);
    }

    private static bool TryPrepare(ApplicationIdentifier applicationId,
        IReadOnlyList<ProcedureManualSectionVectorSource> sources,
        out IReadOnlyList<PreparedSection> prepared,
        out string sourceFingerprint)
    {
        prepared = [];
        sourceFingerprint = "";
        if (sources.Count > MaximumSections || sources.Any(value => value is null
            || value.Version < 1 || !UpperHash(value.SourceFingerprint) || !UpperHash(value.SectionContentFingerprint)
            || string.IsNullOrWhiteSpace(value.ProcedureId) || value.ProcedureId.Length > 200
            || string.IsNullOrWhiteSpace(value.SectionReference) || value.SectionReference.Length > 500
            || string.IsNullOrWhiteSpace(value.SearchText)
            || value.SearchText.Length > InteractionRetrievalLimits.MaximumEmbeddingInputText)
            || sources.Select(value => value.SectionReference).Distinct(StringComparer.Ordinal).Count() != sources.Count)
            return false;
        var pins = sources.OrderBy(value => value.SectionReference, StringComparer.Ordinal).Select(value => new
        {
            value.ProcedureId, value.Version, value.SourceFingerprint,
            value.SectionReference, value.SectionContentFingerprint,
            searchFingerprint = Hash(value.SearchText)
        }).ToArray();
        var exactSourceFingerprint = Hash(InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        { format = SectionFormat, applicationId = applicationId.Value, sections = pins })));
        sourceFingerprint = exactSourceFingerprint;
        prepared = Array.AsReadOnly(sources.OrderBy(value => value.SectionReference, StringComparer.Ordinal).Select(value =>
        {
            var identity = Hash(JsonSerializer.Serialize(new
            { value.ProcedureId, value.Version, value.SourceFingerprint, value.SectionReference, value.SectionContentFingerprint }));
            var id = applicationId.Value + ".derived.manual." + identity[..32].ToLowerInvariant();
            var content = Hash(InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                format = SectionFormat, value.ProcedureId, value.Version, value.SourceFingerprint,
                value.SectionReference, value.SectionContentFingerprint, searchFingerprint = Hash(value.SearchText)
            })));
            return new PreparedSection(value, new(applicationId, InteractionRetrievalLane.SystemManual,
                exactSourceFingerprint, "manual-section", id, value.Version, content));
        }).ToArray());
        return true;
    }

    private static InteractionRetrievalGeneration Generation(ApplicationIdentifier applicationId,
        string sourceFingerprint, EmbeddingProviderIdentity identity) =>
        new(InteractionRetrievalFingerprint.GenerationKey(applicationId, InteractionRetrievalLane.SystemManual,
                sourceFingerprint, identity), applicationId, InteractionRetrievalLane.SystemManual,
            sourceFingerprint, InteractionRetrievalFingerprint.FormatVersion, identity);

    private static ProcedureManualSectionSearchResult Fallback(string code) =>
        new(InteractionRetrievalMode.LexicalFallback, [], code);
    private static string Safe(string? code, string fallback) => string.IsNullOrWhiteSpace(code)
        || code.Length > 100 || code.Any(char.IsControl) ? fallback : code;
    private static bool UpperHash(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record PreparedSection(ProcedureManualSectionVectorSource Source, InteractionFeatureReference Reference);
}
