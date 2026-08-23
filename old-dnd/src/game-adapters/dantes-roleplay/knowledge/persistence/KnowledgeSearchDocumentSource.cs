using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Creates one canonical atomic search document from current world state.</summary>
public sealed class KnowledgeSearchDocumentSource(IWorldStore world) : IKnowledgeSearchDocumentSource
{
    private const string Fact = "game.core.world.fact";
    private const string Rumour = "game.core.world.rumour";
    private const string Secret = "game.core.world.secret";
    private const string Clue = "game.core.world.clue";
    private const string Classification = "game.core.world.knowledge.classification";
    private const string Validity = "game.core.world.knowledge.validity";
    private const string KnowledgeWorld = "game.core.world.knowledge.in-world";
    private const string About = "game.core.world.knowledge.about";
    private readonly IWorldStore _world = world;

    public async Task<IReadOnlyList<KnowledgeLexicalDocument>> ReadWorldAsync(
        string worldId,
        CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in new[] { Fact, Rumour, Secret, Clue })
            foreach (var entity in await _world.FindEntitiesAsync(
                         withDefinitionId: kind,
                         limit: 10_000,
                         cancellationToken: cancellationToken))
                ids.Add(entity.Id);

        var documents = new List<KnowledgeLexicalDocument>();
        foreach (var id in ids.OrderBy(value => value, StringComparer.Ordinal))
        {
            var document = await ReadAsync(id, cancellationToken);
            if (document is not null && document.WorldId == worldId) documents.Add(document);
        }
        return documents;
    }

    public async Task<KnowledgeLexicalDocument?> ReadAsync(
        string knowledgeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _world.GetEntityAsync(knowledgeId, cancellationToken);
        if (entity is null) return null;
        var primary = entity.Components
            .Where(component => component.DefinitionId is Fact or Rumour or Secret or Clue)
            .ToArray();
        var classification = Component(entity, Classification);
        if (primary.Length != 1 || !ClassificationData(classification, out var sensitivity)) return null;
        if (!PrimaryData(primary[0].Data, out var status, out var summary)) return null;

        var links = await _world.GetRelationshipsAsync(entity.Id, includeIncoming: false, cancellationToken);
        var worlds = links.Where(link => link.Kind == KnowledgeWorld && Empty(link.Data))
            .Select(link => link.ToEntityId).ToArray();
        var subjects = links.Where(link => link.Kind == About && Empty(link.Data))
            .Select(link => link.ToEntityId).ToArray();
        if (worlds.Length != 1 || subjects.Length != 1) return null;

        var subject = await _world.GetEntityAsync(subjects[0], cancellationToken);
        if (subject is null || !ValidityData(Component(entity, Validity), out var from, out var until))
            return null;

        var kind = primary[0].DefinitionId switch
        {
            Fact => "fact",
            Rumour => "rumour",
            Secret => "secret",
            Clue => "clue",
            _ => string.Empty
        };
        var text = $"{entity.Name}\n{summary}\n{subject.Name}\n{entity.Id}\n{subject.Id}";
        var hash = ContentHash.Of(
            entity.Id,
            worlds[0],
            kind,
            entity.Name,
            primary[0].Data,
            classification,
            subject.Name,
            subject.Id,
            Component(entity, Validity));
        return new(entity.Id, worlds[0], kind, status, subject.Id, sensitivity, from, until, hash, text);
    }

    private static string? Component(EntitySnapshot entity, string definition) =>
        entity.Components.SingleOrDefault(component => component.DefinitionId == definition)?.Data;

    private static bool Empty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   !document.RootElement.EnumerateObject().Any();
        }
        catch { return false; }
    }

    private static bool PrimaryData(string json, out string status, out string summary)
    {
        status = summary = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("status", out var statusValue) ||
                statusValue.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("summary", out var summaryValue) ||
                summaryValue.ValueKind != JsonValueKind.String) return false;
            status = statusValue.GetString()!;
            summary = summaryValue.GetString()!;
            return !string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(summary);
        }
        catch { return false; }
    }

    private static bool ClassificationData(string? json, out string sensitivity)
    {
        sensitivity = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("sensitivity", out var value) ||
                value.ValueKind != JsonValueKind.String) return false;
            sensitivity = value.GetString()!;
            return sensitivity is "open" or "discreet" or "confidential" or "secret";
        }
        catch { return false; }
    }

    private static bool ValidityData(string? json, out long? from, out long? until)
    {
        from = until = null;
        if (json is null) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("validFromMinute", out var start) ||
                !start.TryGetInt64(out var startMinute) ||
                startMinute is < 0 or > 1_000_000_000) return false;
            from = startMinute;
            if (!root.TryGetProperty("validUntilMinute", out var end))
                return root.EnumerateObject().Count() == 1;
            if (!end.TryGetInt64(out var endMinute) || endMinute <= startMinute ||
                endMinute > 1_000_000_000 || root.EnumerateObject().Count() != 2) return false;
            until = endMinute;
            return true;
        }
        catch { return false; }
    }
}
