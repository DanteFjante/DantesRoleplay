using System.Text.RegularExpressions;

namespace DantesRoleplay.Knowledge;

/// <summary>
/// Small deterministic first-generation lexical projection. Authorization filters run before
/// scoring and limiting; there is no persistent index or canonical write.
/// </summary>
public sealed partial class DeterministicKnowledgeLexicalRetriever : IKnowledgeLexicalRetriever
{
    public IReadOnlyList<KnowledgeLexicalHit> Search(
        IReadOnlyList<CanonicalKnowledgeDocument> documents,
        KnowledgeLexicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(request);
        if (documents.Count > 10_000 || string.IsNullOrWhiteSpace(request.Query) ||
            request.Query != request.Query.Trim() || request.Query.Length > 500 ||
            request.AsOfMinute is < 0 or > 1_000_000_000 || request.Limit is < 1 or > 100 ||
            request.Kinds is { Count: > 16 } || request.SubjectIds is { Count: > 100 })
            return [];
        var tokens = Token().Matches(request.Query).Select(value => value.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tokens.Length == 0) return [];

        // Keep the order explicit: host allowlist and structural filters precede any text scoring.
        var authorized = documents.Where(document =>
            (request.AllowedKnowledgeIds is null ||
             request.AllowedKnowledgeIds.Contains(document.KnowledgeId)) &&
            !document.Archived &&
            (document.ValidFromMinute is null || document.ValidFromMinute <= request.AsOfMinute) &&
            (document.ValidUntilMinute is null || document.ValidUntilMinute > request.AsOfMinute) &&
            (request.Kinds is not { Count: > 0 } || request.Kinds.Contains(document.Kind, StringComparer.Ordinal)) &&
            (request.SubjectIds is not { Count: > 0 } || request.SubjectIds.Contains(document.SubjectId, StringComparer.Ordinal)));

        return authorized.Select(document => new { Document = document, Rank = Score(document.SearchText, tokens) })
            .Where(value => value.Rank > 0)
            .OrderByDescending(value => value.Rank)
            .ThenBy(value => value.Document.KnowledgeId, StringComparer.Ordinal)
            .Take(request.Limit)
            .Select(value => new KnowledgeLexicalHit(value.Document, value.Rank))
            .ToArray();
    }

    private static double Score(string text, IReadOnlyList<string> tokens)
    {
        var total = 0;
        foreach (var token in tokens)
        {
            var count = 0;
            var start = 0;
            while ((start = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                start += token.Length;
            }
            if (count == 0) return 0;
            total += count;
        }
        return total;
    }

    [GeneratedRegex("[\\p{L}\\p{N}_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
