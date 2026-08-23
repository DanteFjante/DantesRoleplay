using System.Text.Json;
using System.Text.Json.Nodes;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Every relationship in the world, as one file.
///
/// One file rather than one per edge, because a relationship has no identity of its own: the key is
/// the (from, to, kind) triple, and inventing a filename for each would be inventing an identity
/// the database does not have. That means drift on relationships is all-or-nothing — the set
/// matches or it does not — which is the honest granularity for something with no per-record key.
///
/// Sorted on write, so two exports of one database produce the same bytes and a git diff shows the
/// edge that actually changed rather than a reordering.
/// </summary>
public sealed record RelationshipsFile(IReadOnlyList<RelationshipEntry> Relationships)
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ContentHash
    {
        get
        {
            var fields = new List<string?>();

            foreach (var relationship in Ordered())
            {
                fields.Add(relationship.From);
                fields.Add(relationship.To);
                fields.Add(relationship.Kind);
                fields.Add(relationship.Data);
            }

            return DantesRoleplay.Content.ContentHash.Of(fields.ToArray());
        }
    }

    public IEnumerable<RelationshipEntry> Ordered() => Relationships
        .OrderBy(r => r.From, StringComparer.Ordinal)
        .ThenBy(r => r.To, StringComparer.Ordinal)
        .ThenBy(r => r.Kind, StringComparer.Ordinal);

    public string ToJson()
    {
        var edges = new JsonArray();

        foreach (var relationship in Ordered())
        {
            edges.Add(new JsonObject
            {
                ["from"] = relationship.From,
                ["to"] = relationship.To,
                ["kind"] = relationship.Kind,
                ["data"] = Node(relationship.Data)
            });
        }

        return new JsonObject { ["relationships"] = edges }.ToJsonString(Indented) + "\n";
    }

    public static RelationshipsFile Parse(string json, string sourceName)
    {
        JsonObject root;

        try
        {
            root = JsonNode.Parse(json)?.AsObject()
                   ?? throw new InvalidOperationException($"{sourceName} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{sourceName} is not valid JSON: {ex.Message}", ex);
        }

        var entries = new List<RelationshipEntry>();

        if (root["relationships"] is JsonArray edges)
        {
            foreach (var edge in edges.OfType<JsonObject>())
            {
                var from = (string?)edge["from"];
                var to = (string?)edge["to"];
                var kind = (string?)edge["kind"];

                if (string.IsNullOrWhiteSpace(from)
                    || string.IsNullOrWhiteSpace(to)
                    || string.IsNullOrWhiteSpace(kind))
                {
                    throw new InvalidOperationException(
                        $"{sourceName} has a relationship missing 'from', 'to' or 'kind'. All three "
                        + "are the key — an edge without one of them is not addressable.");
                }

                entries.Add(new RelationshipEntry(
                    from.Trim(),
                    to.Trim(),
                    kind.Trim(),
                    edge["data"]?.ToJsonString(Compact) ?? "{}"));
            }
        }

        return new RelationshipsFile(entries);
    }

    private static JsonNode Node(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(data) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}

public sealed record RelationshipEntry(string From, string To, string Kind, string Data);
