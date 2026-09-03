using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// One content-addressed human decision about a pair of authored mechanics. The decision is useful
/// only while both exact fingerprints still match; editing either mechanic automatically returns
/// the pair to review without a mutable "acknowledged" flag that can outlive the reviewed content.
/// </summary>
public sealed record CatalogAntiSprawlReview(
    CatalogAntiSprawlReviewEndpoint Left,
    CatalogAntiSprawlReviewEndpoint Right,
    string Disposition,
    string Rationale,
    string SourceName)
{
    public static CatalogAntiSprawlReview Parse(string json, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid(sourceName, "must be an object");
        Exact(root, sourceName, "schemaVersion", "left", "right", "disposition", "rationale");
        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number
            || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw Invalid(sourceName, "schemaVersion must be 1");
        var disposition = Text(root, "disposition", sourceName, 40);
        if (!CatalogAntiSprawlDispositions.All.Contains(disposition))
            throw Invalid(sourceName, "disposition is not supported");
        var left = Endpoint(root.GetProperty("left"), sourceName + ":left");
        var right = Endpoint(root.GetProperty("right"), sourceName + ":right");
        if (left.QualifiedId == right.QualifiedId)
            throw Invalid(sourceName, "left and right must name different mechanics");
        return new(left, right, disposition, Text(root, "rationale", sourceName, 2_000), sourceName);
    }

    private static CatalogAntiSprawlReviewEndpoint Endpoint(JsonElement value, string sourceName)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(sourceName, "must be an object");
        Exact(value, sourceName, "qualifiedId", "contentFingerprint");
        var fingerprint = Text(value, "contentFingerprint", sourceName, 64).ToUpperInvariant();
        if (fingerprint.Length != 64 || fingerprint.Any(character => !char.IsAsciiHexDigit(character)))
            throw Invalid(sourceName, "contentFingerprint must be a SHA-256 value");
        return new(Text(value, "qualifiedId", sourceName, 200), fingerprint);
    }

    private static string Text(JsonElement root, string name, string sourceName, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > maximum
            || value.GetString()!.Trim() != value.GetString())
            throw Invalid(sourceName, $"{name} must be a nonempty trimmed string of at most {maximum} characters");
        return value.GetString()!;
    }

    private static void Exact(JsonElement root, string sourceName, params string[] allowed)
    {
        var names = allowed.ToHashSet(StringComparer.Ordinal);
        if (root.EnumerateObject().Any(property => !names.Contains(property.Name))
            || names.Any(name => !root.TryGetProperty(name, out _)))
            throw Invalid(sourceName, "has missing or unknown properties");
    }

    private static InvalidOperationException Invalid(string sourceName, string detail) =>
        new($"Anti-sprawl review '{sourceName}' {detail}.");
}

public sealed record CatalogAntiSprawlReviewEndpoint(string QualifiedId, string ContentFingerprint);

public static class CatalogAntiSprawlDispositions
{
    public const string Merge = "merge";
    public const string DistinctResponsibility = "distinct-responsibility";
    public const string Replacement = "replacement";
    public const string IntentionalOverride = "intentional-override";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Merge, DistinctResponsibility, Replacement, IntentionalOverride], StringComparer.Ordinal);
}

public static class CatalogAntiSprawlReviewCatalog
{
    public static async Task<IReadOnlyList<CatalogAntiSprawlReview>> ReadAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) return [];
        var reviews = new List<CatalogAntiSprawlReview>();
        foreach (var path in Directory.EnumerateFiles(fullRoot, "*.json", SearchOption.AllDirectories)
                     .Where(path => IsReviewPath(Path.GetRelativePath(fullRoot, path).Replace('\\', '/')))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(fullRoot, path).Replace('\\', '/');
            reviews.Add(CatalogAntiSprawlReview.Parse(
                await File.ReadAllTextAsync(path, cancellationToken), relative));
        }
        return reviews;
    }

    public static bool IsReviewPath(string relativePath)
    {
        var parts = relativePath.Split('/');
        for (var index = 0; index + 2 < parts.Length; index++)
            if (parts[index] == "governance" && parts[index + 1] == "anti-sprawl"
                && parts[index + 2] == "reviews") return true;
        return false;
    }
}

/// <summary>Closed mechanic facts used by the deterministic and fuzzy comparison passes.</summary>
public sealed record CatalogAntiSprawlMechanic(
    string QualifiedId,
    string ContentFingerprint,
    string Name,
    string Description,
    IReadOnlyList<string> MatchPhrases,
    MechanicStatus Status,
    IReadOnlyList<string> EffectClaims,
    string ChildGraphFingerprint,
    IReadOnlySet<string> SimilarityTokens,
    string NamespaceOwner,
    string ResolutionKey,
    bool NamespaceAmbiguous)
{
    private static readonly Regex LiteralComponent = new(
        "(?:componentId|definitionId)\\s*:\\s*['\"](?<id>[a-zA-Z0-9._-]{1,200})['\"]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NamedConstant = new(
        "\\b(?<name>[A-Z][A-Z0-9_]*)\\s*=\\s*['\"](?<id>[a-z][a-z0-9._-]{0,199})['\"]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NamedComponent = new(
        "(?:componentId|definitionId)\\s*:\\s*(?<name>[A-Z][A-Z0-9_]*)\\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex LiteralEffect = new(
        "type\\s*:\\s*['\"](?<id>[a-zA-Z0-9._-]{1,100})['\"]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Words = new("[a-z0-9]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(
        ["about", "after", "again", "also", "and", "before", "from", "into", "mechanic", "must",
         "only", "that", "the", "their", "then", "this", "through", "when", "where", "with"],
        StringComparer.Ordinal);

    public static CatalogAntiSprawlMechanic Create(
        MechanicFile file,
        string qualifiedId,
        string namespaceOwner = "base",
        string? resolutionKey = null,
        bool namespaceAmbiguous = false)
    {
        ArgumentNullException.ThrowIfNull(file);
        var requirements = MechanicRequirements.Parse(file.Requirements);
        var constants = NamedConstant.Matches(file.Source)
            .GroupBy(match => match.Groups["name"].Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Groups["id"].Value, StringComparer.Ordinal);
        var components = requirements.EffectComponentIds
            .Concat(LiteralComponent.Matches(file.Source).Select(match => match.Groups["id"].Value))
            .Concat(NamedComponent.Matches(file.Source)
                .Select(match => constants.TryGetValue(match.Groups["name"].Value, out var value) ? value : "")
                .Where(value => value.Length != 0))
            .Select(value => "component:" + value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var effects = LiteralEffect.Matches(file.Source)
            .Select(match => "effect:" + match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var claims = components.Concat(effects).ToArray();
        var phrases = SplitPhrases(file.Matches);
        var childGraph = requirements.Children.Count == 0 ? "" : Hash(JsonSerializer.SerializeToUtf8Bytes(
            requirements.Children.OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => new
            {
                key = value.Key,
                value.Value.MechanicId,
                value.Value.MechanicVersion,
                value.Value.ContentFingerprint,
                roleBindings = value.Value.RoleBindings.OrderBy(binding => binding.Key, StringComparer.Ordinal),
                value.Value.ForEachContentsOf,
                value.Value.ForEachInputProperty,
                value.Value.InputFromEachItemProperty,
                after = value.Value.After.Order(StringComparer.Ordinal),
                value.Value.InheritInput,
                value.Value.Input,
                value.Value.InputFromParentProperty,
                value.Value.InputForEachItem,
                inputFromChild = value.Value.InputFromChildData?.ResultKey
            })));
        var tokenSource = string.Join('\n', new[] { file.Name, file.Description, file.Matches, file.Requirements }
            .Concat(claims));
        var tokens = Words.Matches(tokenSource.ToLowerInvariant()).Select(match => match.Value)
            .Where(value => value.Length >= 3 && !StopWords.Contains(value))
            .ToHashSet(StringComparer.Ordinal);
        return new(qualifiedId, file.ContentHash, file.Name, file.Description, phrases, file.Status,
            claims, childGraph, tokens, namespaceOwner, resolutionKey ?? qualifiedId, namespaceAmbiguous);
    }

    private static IReadOnlyList<string> SplitPhrases(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizePhrase).Where(value => value.Length != 0)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static string NormalizePhrase(string value) =>
        string.Join(' ', Words.Matches(value.ToLowerInvariant()).Select(match => match.Value));

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
}

public sealed record CatalogAntiSprawlAnalysis(IReadOnlyList<MechanicAntiSprawlFinding> Findings)
{
    public IReadOnlyList<MechanicAntiSprawlFinding> Blocking => Findings.Where(value => value.Blocking).ToArray();
    public IReadOnlyList<MechanicAntiSprawlFinding> Candidates => Findings.Where(value => !value.Blocking).ToArray();
}

/// <summary>
/// Deterministic conflicts can gate active mechanics. Token similarity is deliberately advisory:
/// it proposes review candidates but can never establish semantic equivalence or block activation.
/// </summary>
public static class CatalogAntiSprawlAnalyzer
{
    private const double FuzzyThreshold = 0.55;

    public static CatalogAntiSprawlAnalysis Analyze(
        IReadOnlyList<CatalogAntiSprawlMechanic> mechanics,
        IReadOnlyList<CatalogAntiSprawlReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(mechanics);
        ArgumentNullException.ThrowIfNull(reviews);
        var ordered = mechanics.OrderBy(value => value.QualifiedId, StringComparer.Ordinal).ToArray();
        var findings = new List<MechanicAntiSprawlFinding>();
        for (var leftIndex = 0; leftIndex < ordered.Length; leftIndex++)
        for (var rightIndex = leftIndex + 1; rightIndex < ordered.Length; rightIndex++)
        {
            var left = ordered[leftIndex];
            var right = ordered[rightIndex];
            var reasons = DeterministicReasons(left, right);
            var similarity = Similarity(left.SimilarityTokens, right.SimilarityTokens);
            if (reasons.Count == 0 && similarity < FuzzyThreshold) continue;

            var exactReviews = reviews.Where(review => Matches(review, left, right, exactFingerprints: true)).ToArray();
            if (exactReviews.Length > 1)
            {
                findings.Add(new("ANTI_SPRAWL_REVIEW_AMBIGUOUS", "review", true, Endpoint(left), Endpoint(right),
                    reasons, Math.Round(similarity, 3, MidpointRounding.AwayFromZero), "ambiguous", null,
                    $"Mechanics '{left.QualifiedId}' and '{right.QualifiedId}' have more than one current review record."));
                continue;
            }
            var exactReview = exactReviews.SingleOrDefault();
            var staleReview = exactReview is null
                ? reviews.FirstOrDefault(review => Matches(review, left, right, exactFingerprints: false))
                : null;
            var reviewState = exactReview is not null ? "reviewed" : staleReview is not null ? "stale" : "unreviewed";
            var disposition = exactReview?.Disposition ?? staleReview?.Disposition;
            var bothActive = left.Status == MechanicStatus.Active && right.Status == MechanicStatus.Active;
            var allowsCoexistence = exactReview?.Disposition is CatalogAntiSprawlDispositions.DistinctResponsibility
                or CatalogAntiSprawlDispositions.IntentionalOverride;
            // A shared effect family or a shared child graph is common in a composable catalog and is
            // not, by itself, evidence that two mechanics own the same responsibility. Gate activation
            // only on an unambiguous signal, or when both structural signals corroborate one another.
            var structurallyConflicting = IsStructuralConflict(reasons);
            var blocking = structurallyConflicting && bothActive && !allowsCoexistence;
            var classification = reasons.Count == 0 ? "fuzzy" : "deterministic";
            var code = reasons.Count == 0 ? "ANTI_SPRAWL_REVIEW_CANDIDATE" : "ANTI_SPRAWL_CONFLICT";
            var summary = blocking
                ? exactReview?.Disposition is CatalogAntiSprawlDispositions.Merge or CatalogAntiSprawlDispositions.Replacement
                    ? $"Active mechanics '{left.QualifiedId}' and '{right.QualifiedId}' remain in the overlay after a '{exactReview.Disposition}' decision; complete that decision before activation."
                    : $"Active mechanics '{left.QualifiedId}' and '{right.QualifiedId}' conflict and require a current coexistence review before activation."
                : reasons.Count == 0
                    ? $"Mechanics '{left.QualifiedId}' and '{right.QualifiedId}' are similar enough to review; similarity is not an activation decision."
                    : !structurallyConflicting
                        ? $"Mechanics '{left.QualifiedId}' and '{right.QualifiedId}' share a structural signal worth review, but that signal alone is not sufficient to block activation."
                    : $"Mechanics '{left.QualifiedId}' and '{right.QualifiedId}' have a deterministic overlap that is non-blocking because they are drafts or have a current coexistence review.";
            findings.Add(new(code, classification, blocking, Endpoint(left), Endpoint(right), reasons,
                Math.Round(similarity, 3, MidpointRounding.AwayFromZero), reviewState, disposition, summary));
        }

        foreach (var review in reviews.Where(review => !ordered.Any(mechanic =>
                         mechanic.QualifiedId == review.Left.QualifiedId)
                     || !ordered.Any(mechanic => mechanic.QualifiedId == review.Right.QualifiedId))
                     .OrderBy(value => value.SourceName, StringComparer.Ordinal))
        {
            findings.Add(new("ANTI_SPRAWL_REVIEW_ORPHANED", "review", false,
                new(review.Left.QualifiedId, review.Left.ContentFingerprint),
                new(review.Right.QualifiedId, review.Right.ContentFingerprint), [], 0,
                "orphaned", review.Disposition,
                $"Review '{review.SourceName}' does not refer to mechanics in this candidate overlay."));
        }
        return new(findings.OrderByDescending(value => value.Blocking)
            .ThenBy(value => value.Left.QualifiedId, StringComparer.Ordinal)
            .ThenBy(value => value.Right.QualifiedId, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<string> DeterministicReasons(
        CatalogAntiSprawlMechanic left,
        CatalogAntiSprawlMechanic right)
    {
        var reasons = new List<string>();
        var phrases = left.MatchPhrases.Intersect(right.MatchPhrases, StringComparer.Ordinal).ToArray();
        if (phrases.Length != 0) reasons.Add("identical-match-phrase:" + string.Join('|', phrases));
        var claims = left.EffectClaims.Intersect(right.EffectClaims, StringComparer.Ordinal)
            .Where(value => value.StartsWith("component:", StringComparison.Ordinal)).ToArray();
        if (claims.Length != 0) reasons.Add("overlapping-effect-ownership:" + string.Join('|', claims));
        if (left.ChildGraphFingerprint.Length != 0 && left.ChildGraphFingerprint == right.ChildGraphFingerprint)
            reasons.Add("equivalent-child-graph:" + left.ChildGraphFingerprint);
        if (left.NamespaceAmbiguous || right.NamespaceAmbiguous
            || left.NamespaceOwner != right.NamespaceOwner && left.ResolutionKey == right.ResolutionKey)
            reasons.Add("incompatible-namespace-claim:" + left.ResolutionKey);
        return reasons;
    }

    private static bool IsStructuralConflict(IReadOnlyList<string> reasons)
    {
        var identicalPhrase = reasons.Any(value =>
            value.StartsWith("identical-match-phrase:", StringComparison.Ordinal));
        var incompatibleNamespace = reasons.Any(value =>
            value.StartsWith("incompatible-namespace-claim:", StringComparison.Ordinal));
        var overlappingEffects = reasons.Any(value =>
            value.StartsWith("overlapping-effect-ownership:", StringComparison.Ordinal));
        var equivalentGraph = reasons.Any(value =>
            value.StartsWith("equivalent-child-graph:", StringComparison.Ordinal));
        return identicalPhrase || incompatibleNamespace || overlappingEffects && equivalentGraph;
    }

    private static double Similarity(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var intersection = left.Count(right.Contains);
        if (intersection < 4) return 0;
        return 2d * intersection / (left.Count + right.Count);
    }

    private static bool Matches(
        CatalogAntiSprawlReview review,
        CatalogAntiSprawlMechanic left,
        CatalogAntiSprawlMechanic right,
        bool exactFingerprints)
    {
        static bool EndpointMatches(CatalogAntiSprawlReviewEndpoint endpoint, CatalogAntiSprawlMechanic mechanic,
            bool exact) => endpoint.QualifiedId == mechanic.QualifiedId
                && (!exact || endpoint.ContentFingerprint == mechanic.ContentFingerprint);
        return EndpointMatches(review.Left, left, exactFingerprints) && EndpointMatches(review.Right, right, exactFingerprints)
            || EndpointMatches(review.Left, right, exactFingerprints) && EndpointMatches(review.Right, left, exactFingerprints);
    }

    private static MechanicAntiSprawlEndpoint Endpoint(CatalogAntiSprawlMechanic value) =>
        new(value.QualifiedId, value.ContentFingerprint);
}
