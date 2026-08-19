using System.Text.Json;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// One component definition as a catalog file, and the parser for reading it back.
///
/// JSON rather than markdown, because a component definition is data: an id, a name, a sentence of
/// description, and a JSON Schema. Wrapping that in front matter and '## ' sections would add a
/// parser and gain nothing — the rule throughout the catalog is prose to markdown, data to JSON,
/// code to its own language's file.
///
/// The schema lives in a sibling <c>&lt;id&gt;.schema.json</c> rather than nested inside this file.
/// Nested, it would be reserialised on every round trip and a schema nobody edited would come back
/// looking changed. As a sibling its bytes are preserved exactly, and the file is something a JSON
/// Schema validator can be pointed at directly.
///
/// The sibling's presence is what says a schema exists. There is no pointer to it in here: a
/// reference that can disagree with the filesystem is a reference that eventually does.
/// </summary>
public sealed record ComponentDefinitionFile(
    string Id,
    string Name,
    string Description,
    string Schema)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Fingerprint of the authored content. The schema is part of it; the id is not.</summary>
    public string ContentHash => DantesRoleplay.Content.ContentHash.ForComponentDefinition(
        Name, Description, Schema);

    /// <summary>The definition file. The schema goes to its sibling and is not written here.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(new Payload(Id, Name, Description), Json) + "\n";

    /// <param name="schemaSidecar">Contents of the sibling .schema.json, or null when there is none.</param>
    public static ComponentDefinitionFile Parse(string json, string sourceName, string? schemaSidecar = null)
    {
        Payload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<Payload>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{sourceName} is not valid JSON: {ex.Message}", ex);
        }

        if (payload is null)
        {
            throw new InvalidOperationException($"{sourceName} is empty.");
        }

        if (string.IsNullOrWhiteSpace(payload.Id))
        {
            throw new InvalidOperationException($"{sourceName} is missing 'id'.");
        }

        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            throw new InvalidOperationException($"{sourceName} is missing 'name'.");
        }

        return new ComponentDefinitionFile(
            payload.Id.Trim(),
            payload.Name.Trim(),
            payload.Description ?? string.Empty,
            NormaliseSchema(schemaSidecar));
    }

    public static ComponentDefinitionFile FromDefinition(ComponentDefinition definition) =>
        new(definition.Id, definition.Name, definition.Description, definition.Schema);

    /// <summary>
    /// A schema file that is present but blank is the same as no schema at all. Left unnormalised,
    /// an empty sidecar would fingerprint differently from a missing one and that definition would
    /// read as drifted on every import.
    /// </summary>
    private static string NormaliseSchema(string? schemaSidecar) =>
        string.IsNullOrWhiteSpace(schemaSidecar) ? string.Empty : schemaSidecar.Trim();

    /// <summary>
    /// The serialisation shape, separate from the record so the schema — which is deliberately not
    /// in this file — cannot be written into it by accident when a property is added.
    /// </summary>
    private sealed record Payload(string Id, string Name, string? Description);
}
