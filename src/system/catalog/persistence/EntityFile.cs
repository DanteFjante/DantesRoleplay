using System.Text.Json;
using System.Text.Json.Nodes;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// One entity as a catalog file: its name, where it sits, and every component attached to it.
///
/// Containment is folded in rather than kept in a separate edge file. A thing is inside at most one
/// other thing — the database enforces that with a unique constraint on the contained id — so
/// "where is this?" is a property of the entity, and splitting it out would mean reading two files
/// to answer one question.
///
/// Relationships are NOT folded in: they are genuinely many-to-many and belong to neither end.
///
/// <b>Component data is canonicalised, not byte-preserved.</b> This is the one place the catalog
/// reformats what it carries, and the reason is that component data is machine-written state — the
/// output of JSON.stringify inside a mechanic — rather than authored text. Rule source and JSON
/// Schemas keep their exact bytes because a person wrote them and a person will read the diff;
/// nobody is going to be upset that an ability-score blob came back with different spacing. What
/// matters is that both sides canonicalise the same way, so a round trip is stable.
/// </summary>
public sealed record EntityFile(
    string Id,
    string Name,
    string? ContainerId,
    string ContainerSlot,
    IReadOnlyList<EntityComponent> Components)
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

    public string ContentHash => DantesRoleplay.Content.ContentHash.ForEntity(
        Name,
        ContainerId,
        ContainerSlot,
        Ordered().Select(c => (c.DefinitionId, c.Data)));

    /// <summary>Components in a stable order. Dictionary order is not one, and the fingerprint depends on it.</summary>
    public IEnumerable<EntityComponent> Ordered() =>
        Components.OrderBy(c => c.DefinitionId, StringComparer.Ordinal);

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["id"] = Id,
            ["name"] = Name
        };

        if (!string.IsNullOrEmpty(ContainerId))
        {
            root["container"] = new JsonObject
            {
                ["id"] = ContainerId,
                ["slot"] = ContainerSlot
            };
        }

        var components = new JsonObject();

        foreach (var component in Ordered())
        {
            components[component.DefinitionId] = Node(component.Data, Id, component.DefinitionId);
        }

        root["components"] = components;

        return root.ToJsonString(Indented) + "\n";
    }

    public static EntityFile Parse(string json, string sourceName)
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

        var id = (string?)root["id"];
        var name = (string?)root["name"];

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"{sourceName} is missing 'id'.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{sourceName} is missing 'name'.");
        }

        string? containerId = null;
        var slot = string.Empty;

        if (root["container"] is JsonObject container)
        {
            containerId = (string?)container["id"];
            slot = (string?)container["slot"] ?? string.Empty;
        }

        var components = new List<EntityComponent>();

        if (root["components"] is JsonObject declared)
        {
            foreach (var (definitionId, value) in declared)
            {
                components.Add(new EntityComponent(
                    definitionId,
                    value?.ToJsonString(Compact) ?? "{}"));
            }
        }

        return new EntityFile(id.Trim(), name.Trim(), containerId, slot, components);
    }

    /// <summary>
    /// The one definition of what a component's data looks like once normalised.
    ///
    /// Both the file side and the database side run through this before fingerprinting, so a blob
    /// stored minified and the same blob written out indented are recognised as the same data
    /// rather than reported as an edit nobody made.
    /// </summary>
    public static string CanonicalData(string? data, string entityId, string definitionId) =>
        Node(data, entityId, definitionId).ToJsonString(Compact);

    private static JsonNode Node(string? data, string entityId, string definitionId)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(data) ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The '{definitionId}' component of entity '{entityId}' does not hold valid JSON, "
                + $"so it cannot be written to or read from a catalog: {ex.Message}",
                ex);
        }
    }
}

/// <param name="Data">Canonical compact JSON. See <see cref="EntityFile.CanonicalData"/>.</param>
public sealed record EntityComponent(string DefinitionId, string Data);
