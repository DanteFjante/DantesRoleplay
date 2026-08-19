using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.Events;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>Catalog representation of a versioned event type; schema bytes live in a sidecar.</summary>
public sealed record EventTypeFile(string Id, string Category, string Name, string Description, string Scope, EventTypeStatus Status, string Schema)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string ContentHash => DantesRoleplay.Content.ContentHash.Of(Category, Name, Description, Schema, Scope, Status.ToString());
    public string ToJson() => JsonSerializer.Serialize(new Payload(Id, Category, Name, Description, Scope, Status.ToString()), Json) + "\n";
    public static EventTypeFile Parse(string json, string source, string? schema)
    {
        Payload? p; try { p = JsonSerializer.Deserialize<Payload>(json, Json); } catch (JsonException ex) { throw new InvalidOperationException($"{source} is not valid JSON: {ex.Message}", ex); }
        if (p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Category) || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(schema)) throw new InvalidOperationException($"{source} requires id, category, name and a .schema.json sidecar.");
        if (!Enum.TryParse<EventTypeStatus>(p.Status, true, out var status)) throw new InvalidOperationException($"{source} has an invalid status '{p.Status}'.");
        return new(p.Id.Trim(), p.Category.Trim(), p.Name.Trim(), p.Description ?? string.Empty, p.Scope ?? string.Empty, status, schema.Trim());
    }
    private sealed record Payload(string? Id, string? Category, string? Name, string? Description, string? Scope, string? Status);
}
