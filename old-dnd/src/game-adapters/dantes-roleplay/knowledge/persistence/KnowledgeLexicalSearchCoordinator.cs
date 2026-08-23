using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Builds and reads a disposable FTS5 projection, then rechecks canonical knowledge.</summary>
public sealed class KnowledgeLexicalSearchCoordinator(
    IKnowledgeTimelineCoordinator timeline,
    IKnowledgeLexicalIndex index,
    IKnowledgeSearchDocumentSource documents) : IKnowledgeLexicalSearchCoordinator
{
    private readonly IKnowledgeTimelineCoordinator _timeline = timeline;
    private readonly IKnowledgeLexicalIndex _index = index;
    private readonly IKnowledgeSearchDocumentSource _documents = documents;

    public async Task<int> RebuildWorldAsync(
        string worldId,
        CancellationToken cancellationToken = default)
    {
        var history = await _timeline.ReadAsOfAsync(worldId, null, cancellationToken);
        if (!history.Read)
            throw new InvalidOperationException($"{history.Problems[0].Code}: {history.Problems[0].Reason}");
        var documents = await _documents.ReadWorldAsync(worldId, cancellationToken);
        await _index.ReplaceWorldAsync(worldId, documents, cancellationToken);
        return documents.Count;
    }

    public async Task<KnowledgeLexicalSearchResult> SearchAsync(
        KnowledgeLexicalSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.WorldId))
            return KnowledgeLexicalSearchResult.Fail(
                request?.WorldId ?? string.Empty,
                "INVALID_KNOWLEDGE_SEARCH",
                "Search requires a canonical world id.");
        var history = await _timeline.ReadAsOfAsync(request.WorldId, request.AsOfMinute, cancellationToken);
        if (!history.Read)
            return KnowledgeLexicalSearchResult.Fail(
                request.WorldId,
                history.Problems[0].Code,
                history.Problems[0].Reason);

        IReadOnlyList<KnowledgeLexicalCandidate> candidates;
        try
        {
            candidates = await _index.SearchAsync(
                request with { AsOfMinute = history.AsOfMinute }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return KnowledgeLexicalSearchResult.Fail(
                request.WorldId, "INVALID_KNOWLEDGE_SEARCH", exception.Message);
        }

        var applicable = history.Records
            .Where(record => record.TemporalStatus is "atemporal" or "effective")
            .Select(record => record.KnowledgeId)
            .ToHashSet(StringComparer.Ordinal);
        var hits = new List<KnowledgeLexicalSearchHit>();
        foreach (var candidate in candidates)
        {
            if (!applicable.Contains(candidate.KnowledgeId)) continue;
            var document = await _documents.ReadAsync(candidate.KnowledgeId, cancellationToken);
            if (!Allowed(document, request)) continue;
            hits.Add(new(
                document!.KnowledgeId,
                document.Kind,
                document.Status,
                document.SubjectId,
                document.Sensitivity,
                Summary(document.Text),
                candidate.Rank));
        }
        return new(request.WorldId, history.AsOfMinute, hits, "", "");
    }

    private static bool Allowed(KnowledgeLexicalDocument? document, KnowledgeLexicalSearchRequest request) =>
        document is not null &&
        document.WorldId == request.WorldId &&
        (request.AllowedKnowledgeIds is null || request.AllowedKnowledgeIds.Contains(document.KnowledgeId, StringComparer.Ordinal)) &&
        (request.IncludeArchived || document.Status != "archived") &&
        (request.Kinds is not { Count: > 0 } || request.Kinds.Contains(document.Kind, StringComparer.Ordinal)) &&
        (request.SubjectIds is not { Count: > 0 } || request.SubjectIds.Contains(document.SubjectId, StringComparer.Ordinal));

    private static string Summary(string text) =>
        text.Split('\n', StringSplitOptions.None).Skip(1).FirstOrDefault() ?? string.Empty;

    private static bool Id(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id == id.Trim() && id.Length <= 200;
}
