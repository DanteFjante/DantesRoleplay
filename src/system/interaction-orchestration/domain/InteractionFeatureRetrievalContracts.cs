using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

public enum InteractionRetrievalLane
{
    TrustedFeature,
    UntrustedReference,
    TrustedRecipe
}

public enum InteractionRetrievalMode
{
    Exact,
    Lexical,
    Hybrid,
    LexicalFallback,
    Unavailable
}

public static class InteractionRetrievalLimits
{
    public const int MaximumQueryLength = 256;
    public const int MaximumResults = 50;
    public const int MaximumFilters = 16;
    public const int MaximumDocumentText = 64_000;
    public const int MaximumVectorDimensions = 8_192;
    public const int HybridCandidateMultiplier = 4;
    public const int ReciprocalRankConstant = 60;
}

/// <summary>Host-bound retrieval context. The caller may not select an application or trust lane.</summary>
public sealed record InteractionFeatureRetrievalScope
{
    public InteractionFeatureRetrievalScope(ApplicationIdentifier applicationId, InteractionRetrievalLane lane)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        if (!Enum.IsDefined(lane)) throw new InteractionContractException("INVALID_RETRIEVAL_LANE", "The retrieval lane is not supported.");
        Lane = lane;
    }

    public ApplicationIdentifier ApplicationId { get; }
    public InteractionRetrievalLane Lane { get; }
}

public sealed record InteractionFeatureSearchInput
{
    public InteractionFeatureSearchInput(
        string query,
        int limit = InteractionRetrievalLimits.MaximumResults,
        IReadOnlyList<string>? kinds = null,
        IReadOnlyList<string>? statuses = null,
        string? namespaceId = null,
        bool includeShadowed = false)
    {
        Query = NormalizeQuery(query);
        if (limit is < 1 or > InteractionRetrievalLimits.MaximumResults)
            throw new InteractionContractException("INVALID_RETRIEVAL_LIMIT", "The retrieval result limit is outside the closed range.");
        Limit = limit;
        Kinds = CopyFilters(kinds, nameof(kinds));
        Statuses = CopyFilters(statuses, nameof(statuses));
        if (namespaceId is not null && !CatalogNamespaceIdentity.IsNamespaceId(namespaceId))
            throw new InteractionContractException("INVALID_RETRIEVAL_NAMESPACE", "The retrieval namespace is invalid.", nameof(namespaceId));
        NamespaceId = namespaceId;
        IncludeShadowed = includeShadowed;
    }

    public string Query { get; }
    public int Limit { get; }
    public IReadOnlyList<string> Kinds { get; }
    public IReadOnlyList<string> Statuses { get; }
    public string? NamespaceId { get; }
    public bool IncludeShadowed { get; }

    private static string NormalizeQuery(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (normalized.Length is 0 or > InteractionRetrievalLimits.MaximumQueryLength || normalized.Any(char.IsControl))
            throw new InteractionContractException("INVALID_RETRIEVAL_QUERY", "The retrieval query must be bounded normalized text.", nameof(value));
        return normalized;
    }

    private static IReadOnlyList<string> CopyFilters(IReadOnlyList<string>? values, string parameter)
    {
        values ??= [];
        if (values.Count > InteractionRetrievalLimits.MaximumFilters)
            throw new InteractionContractException("INVALID_RETRIEVAL_FILTER", "The retrieval filter collection is too large.", parameter);
        var copied = values.Select(value =>
        {
            if (!CatalogNavigationManifest.IsIdentifier(value))
                throw new InteractionContractException("INVALID_RETRIEVAL_FILTER", "A retrieval filter is invalid.", parameter);
            return value;
        }).Order(StringComparer.Ordinal).ToArray();
        if (copied.Distinct(StringComparer.Ordinal).Count() != copied.Length)
            throw new InteractionContractException("INVALID_RETRIEVAL_FILTER", "A retrieval filter is duplicated.", parameter);
        return Array.AsReadOnly(copied);
    }
}

public sealed record InteractionFeatureReference(
    ApplicationIdentifier ApplicationId,
    InteractionRetrievalLane Lane,
    string CatalogFingerprint,
    string Kind,
    string QualifiedId,
    int Version,
    string ContentFingerprint)
{
    public static InteractionFeatureReference Create(
        ApplicationIdentifier applicationId,
        InteractionRetrievalLane lane,
        string catalogFingerprint,
        CatalogRecordDefinition record)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(record);
        if (!Enum.IsDefined(lane) || record.Version < 1 || !UpperSha(catalogFingerprint) || !UpperSha(record.ContentFingerprint)
            || !record.QualifiedId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal))
            throw new InteractionContractException("INVALID_FEATURE_REFERENCE", "The feature reference has invalid current provenance.");
        return new(applicationId, lane, catalogFingerprint, record.Kind, record.QualifiedId, record.Version, record.ContentFingerprint);
    }

    private static bool UpperSha(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
}

public sealed record InteractionFeatureHit(
    InteractionFeatureReference Reference,
    string Name,
    string Description,
    string ContractJson,
    int? LexicalRank,
    int? VectorRank,
    bool Exact)
{
    public static InteractionFeatureHit Create(
        InteractionFeatureReference reference,
        CatalogRecordDefinition record,
        int? lexicalRank,
        int? vectorRank,
        bool exact)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(record);
        if (reference.QualifiedId != record.QualifiedId || reference.ContentFingerprint != record.ContentFingerprint
            || record.ContentJson.Length > InteractionRetrievalLimits.MaximumDocumentText)
            throw new InteractionContractException("INVALID_RETRIEVAL_HIT", "The returned feature is not the exact bounded current record.");
        return new(reference, record.Name, record.Description, record.ContentJson, lexicalRank, vectorRank, exact);
    }
}

public sealed record InteractionFeatureSearchResult(
    InteractionRetrievalMode Mode,
    IReadOnlyList<InteractionFeatureHit> Hits,
    string AvailabilityCode = "",
    string AvailabilityMessage = "",
    IReadOnlyList<CatalogResolutionDiagnosticView>? ResolutionDiagnostics = null)
{
    public static InteractionFeatureSearchResult Create(
        InteractionRetrievalMode mode,
        IEnumerable<InteractionFeatureHit> hits,
        string availabilityCode = "",
        string availabilityMessage = "",
        IReadOnlyList<CatalogResolutionDiagnosticView>? resolutionDiagnostics = null)
    {
        if (!Enum.IsDefined(mode)) throw new InteractionContractException("INVALID_RETRIEVAL_MODE", "The retrieval mode is not supported.");
        ArgumentNullException.ThrowIfNull(hits);
        var copied = hits.ToArray();
        if (copied.Length > InteractionRetrievalLimits.MaximumResults || copied.Any(value => value is null)
            || copied.Select(value => value.Reference.QualifiedId).Distinct(StringComparer.Ordinal).Count() != copied.Length)
            throw new InteractionContractException("INVALID_RETRIEVAL_RESULT", "The retrieval result is invalid or unbounded.");
        if ((mode is InteractionRetrievalMode.LexicalFallback or InteractionRetrievalMode.Unavailable)
            && (string.IsNullOrWhiteSpace(availabilityCode) || string.IsNullOrWhiteSpace(availabilityMessage)))
            throw new InteractionContractException("INVALID_RETRIEVAL_AVAILABILITY", "Fallback and unavailable results require safe availability evidence.");
        if (availabilityCode.Length > 100 || availabilityMessage.Length > 500 || availabilityCode.Any(char.IsControl) || availabilityMessage.Any(char.IsControl))
            throw new InteractionContractException("INVALID_RETRIEVAL_AVAILABILITY", "Availability evidence is invalid or unbounded.");
        var resolutions = resolutionDiagnostics?.ToArray() ?? [];
        if (resolutions.Select(value => value.WinnerQualifiedId).Distinct(StringComparer.Ordinal).Count() != resolutions.Length)
            throw new InteractionContractException("INVALID_RETRIEVAL_RESULT", "Overlay resolution evidence is duplicated.");
        return new(mode, Array.AsReadOnly(copied), availabilityCode, availabilityMessage,
            Array.AsReadOnly(resolutions));
    }
}

public sealed record InteractionRetrievalGeneration(
    string GenerationKey,
    ApplicationIdentifier ApplicationId,
    InteractionRetrievalLane Lane,
    string CatalogFingerprint,
    string RetrievalFormatVersion,
    EmbeddingProviderIdentity Embedding)
{
    public string ResolutionFingerprint { get; init; } = CatalogFingerprint;
}

public sealed record InteractionVectorDocument(
    InteractionFeatureReference Reference,
    string SearchText,
    float[] Vector)
{
    public static InteractionVectorDocument Create(InteractionFeatureReference reference, string searchText, float[] vector)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(searchText);
        ArgumentNullException.ThrowIfNull(vector);
        if (searchText.Length is 0 or > InteractionRetrievalLimits.MaximumDocumentText || vector.Length is 0 or > InteractionRetrievalLimits.MaximumVectorDimensions
            || vector.Any(value => !float.IsFinite(value)))
            throw new InteractionContractException("INVALID_RETRIEVAL_VECTOR", "A retrieval vector document is invalid or unbounded.");
        return new(reference, searchText, vector.ToArray());
    }
}

public sealed record InteractionVectorCandidate(string QualifiedId, double Distance);

public interface IInteractionDerivedVectorIndex
{
    Task ReplaceAsync(InteractionRetrievalGeneration generation, IReadOnlyList<InteractionVectorDocument> documents, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InteractionVectorCandidate>> SearchAsync(InteractionRetrievalGeneration generation, float[] query, int limit, CancellationToken cancellationToken = default);
}

public interface IInteractionFeatureRetriever
{
    Task<InteractionFeatureSearchResult> SearchAsync(
        InteractionFeatureRetrievalScope scope,
        InteractionFeatureSearchInput input,
        CancellationToken cancellationToken = default);

    Task<InteractionFeatureRebuildResult> RebuildAsync(
        InteractionFeatureRetrievalScope scope,
        CancellationToken cancellationToken = default);
}

public sealed record InteractionFeatureRebuildResult(
    bool Rebuilt,
    int DocumentCount,
    string GenerationKey = "",
    string AvailabilityCode = "",
    string AvailabilityMessage = "");

public static class InteractionRetrievalFingerprint
{
    public const string FormatVersion = "interaction-feature-retrieval-v1";

    public static string GenerationKey(
        ApplicationIdentifier applicationId,
        InteractionRetrievalLane lane,
        string catalogFingerprint,
        EmbeddingProviderIdentity identity,
        string? resolutionFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(identity);
        var canonical = JsonSerializer.Serialize(new
        {
            applicationId = applicationId.Value,
            lane = lane.ToString().ToLowerInvariant(),
            catalogFingerprint,
            resolutionFingerprint = resolutionFingerprint ?? catalogFingerprint,
            format = FormatVersion,
            embedding = identity
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string SearchText(CatalogRecordDefinition record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var values = new[]
        {
            record.QualifiedId, record.Name, record.Description,
            string.Join('\n', record.Aliases), string.Join('\n', record.MatchPhrases), record.ContentJson
        };
        var text = string.Join('\n', values).Normalize(NormalizationForm.FormKC);
        if (text.Length > InteractionRetrievalLimits.MaximumDocumentText)
            throw new InteractionContractException("RETRIEVAL_DOCUMENT_TOO_LARGE", "The active catalog feature exceeds the retrieval document limit.");
        return text;
    }
}
