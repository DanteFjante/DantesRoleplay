using System.Text.Json;

namespace DantesRoleplay.DataAccess;

/// <summary>Canonical JSON forms used by both persisted and file-first subscription identity.</summary>
internal static class SubscriptionCanonicalJson
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static string Object(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return Object(document.RootElement);
    }

    public static string Object(JsonElement value) =>
        "{" + string.Join(",", value.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => JsonSerializer.Serialize(property.Name, Compact) + ":" + property.Value.GetRawText())) + "}";

    public static string Ids(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        return Ids(document.RootElement);
    }

    public static string Ids(JsonElement value) =>
        JsonSerializer.Serialize(value.EnumerateArray()
            .Select(item => item.GetString()!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal), Compact);
}
