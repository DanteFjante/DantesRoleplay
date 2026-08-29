using System.Text.Json;
using System.Text.RegularExpressions;

namespace DantesRoleplay.Events;

/// <summary>
/// The small, generic bridge between an event payload schema and one reaction role. Event types
/// still own their schemas; subscriptions merely opt into a field explicitly declared here.
/// </summary>
public static class EventPayloadRoleMetadata
{
    public const string EntityPayloadFieldsExtension = "x-dantes-entity-payload-fields";

    /// <summary>Removes host-only metadata before passing the schema to a strict JSON-Schema dialect.</summary>
    public static string WithoutExtension(string payloadSchema)
    {
        using var document = JsonDocument.Parse(payloadSchema);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(EntityPayloadFieldsExtension, out _)) return payloadSchema;
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (property.Name != EntityPayloadFieldsExtension) properties[property.Name] = property.Value.Clone();
        return JsonSerializer.Serialize(properties);
    }

    public static bool TryRead(string payloadSchema, out IReadOnlyList<string> fields, out string problem)
    {
        fields = [];
        problem = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payloadSchema);
            return TryRead(document.RootElement, out fields, out problem);
        }
        catch (JsonException)
        {
            problem = "Payload schema must be valid JSON.";
            return false;
        }
    }

    private static bool TryRead(JsonElement root, out IReadOnlyList<string> fields, out string problem)
    {
        fields = [];
        problem = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            problem = "Payload schema must have an object root.";
            return false;
        }
        if (!root.TryGetProperty(EntityPayloadFieldsExtension, out var extension)) return true;
        if (extension.ValueKind != JsonValueKind.Array)
        {
            problem = $"{EntityPayloadFieldsExtension} must be an array of direct string payload fields.";
            return false;
        }
        var names = extension.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToList();
        if (names.Count is < 1 or > 12 || names.Any(string.IsNullOrWhiteSpace) || names.Any(name => name != name!.Trim()) || names.Distinct(StringComparer.Ordinal).Count() != names.Count || !names.SequenceEqual(names.OrderBy(name => name, StringComparer.Ordinal)))
        {
            problem = $"{EntityPayloadFieldsExtension} must contain 1–12 distinct, ordinal-sorted nonempty field names.";
            return false;
        }
        if (!root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            problem = $"{EntityPayloadFieldsExtension} requires an object root properties declaration.";
            return false;
        }
        foreach (var name in names.Cast<string>())
        {
            if (!properties.TryGetProperty(name, out var property)
                || property.ValueKind != JsonValueKind.Object
                || !property.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "string", StringComparison.Ordinal))
            {
                problem = $"Declared entity payload field '{name}' must be a direct property with type 'string'.";
                return false;
            }
        }
        fields = names.Cast<string>().ToList();
        return true;
    }
}

/// <summary>Parses the deliberately bounded subscription-side mapping without interpreting it.</summary>
public static class SubscriptionRoleFromEventPayload
{
    public static bool TryRead(string json, out KeyValuePair<string, string>? mapping, out string problem)
    {
        mapping = null;
        problem = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problem = "roleFromEventPayload must be a JSON object.";
                return false;
            }
            var entries = document.RootElement.EnumerateObject().ToList();
            if (entries.Count > 1)
            {
                problem = "roleFromEventPayload may bind at most one role.";
                return false;
            }
            if (entries.Count == 0) return true;
            var entry = entries[0];
            var field = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name != entry.Name.Trim() || string.IsNullOrWhiteSpace(field) || field != field.Trim())
            {
                problem = "roleFromEventPayload must map one nonempty role to one nonempty payload field.";
                return false;
            }
            mapping = new(entry.Name, field);
            return true;
        }
        catch (JsonException)
        {
            problem = "roleFromEventPayload must be valid JSON.";
            return false;
        }
    }
}

/// <summary>Closed, generic relationship/component selector for one reaction role.</summary>
public sealed record SubscriptionFanoutSelector(
    string Role,
    string RelationshipKind,
    string Direction,
    string ComponentId)
{
    public bool ScopeToCandidate => Direction == "scope-to-candidate";
}

public static partial class SubscriptionFanoutSelectorMetadata
{
    private static readonly string[] Names = ["role", "relationshipKind", "direction", "componentId"];

    public static bool TryRead(string json, out SubscriptionFanoutSelector? selector, out string problem)
    {
        selector = null;
        problem = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problem = "fanoutSelector must be a JSON object.";
                return false;
            }
            var properties = document.RootElement.EnumerateObject().ToList();
            if (properties.Count == 0) return true;
            if (properties.Count != Names.Length || properties.Any(x => !Names.Contains(x.Name, StringComparer.Ordinal)))
            {
                problem = "fanoutSelector must have exactly role, relationshipKind, direction, and componentId.";
                return false;
            }
            string? Read(string name) => document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var role = Read("role");
            var kind = Read("relationshipKind");
            var direction = Read("direction");
            var component = Read("componentId");
            if (string.IsNullOrWhiteSpace(role) || role != role.Trim()
                || string.IsNullOrWhiteSpace(component) || component != component.Trim()
                || string.IsNullOrWhiteSpace(kind) || kind != kind.Trim() || kind.Length > 100 || !DottedKind().IsMatch(kind)
                || direction is not ("scope-to-candidate" or "candidate-to-scope"))
            {
                problem = "fanoutSelector requires nonempty role and componentId, a dotted relationshipKind of at most 100 characters, and a supported direction.";
                return false;
            }
            selector = new(role, kind, direction, component);
            return true;
        }
        catch (JsonException)
        {
            problem = "fanoutSelector must be valid JSON.";
            return false;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedKind();
}
