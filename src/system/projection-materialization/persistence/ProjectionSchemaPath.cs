using System.Text.Json.Nodes;

namespace DantesRoleplay.Projections;

/// <summary>Conservative path discovery over the closed Slice 5 JSON Schema profile.</summary>
public static class ProjectionSchemaPath
{
    private const int MaximumTraversalDepth = 64;

    public static bool Exists(string schemaJson, string pointer)
    {
        if (pointer == "") return true;
        var root = JsonNode.Parse(schemaJson);
        if (root is null) return false;
        var tokens = pointer.Split('/').Skip(1)
            .Select(x => x.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))
            .ToArray();
        return Supports(root, root, tokens, 0, 0, new HashSet<string>(StringComparer.Ordinal));
    }

    private static bool Supports(
        JsonNode root,
        JsonNode? schema,
        IReadOnlyList<string> tokens,
        int tokenIndex,
        int depth,
        HashSet<string> references)
    {
        if (tokenIndex == tokens.Count) return true;
        if (schema is not JsonObject current || depth > MaximumTraversalDepth) return false;

        if (current["properties"] is JsonObject properties
            && properties.TryGetPropertyValue(tokens[tokenIndex], out var property)
            && Supports(root, property, tokens, tokenIndex + 1, depth + 1, references))
            return true;

        if (int.TryParse(tokens[tokenIndex], out var arrayIndex) && arrayIndex >= 0)
        {
            if (current["prefixItems"] is JsonArray prefix && arrayIndex < prefix.Count)
                return Supports(root, prefix[arrayIndex], tokens, tokenIndex + 1, depth + 1, references);
            if (current["items"] is JsonNode items
                && Supports(root, items, tokens, tokenIndex + 1, depth + 1, references))
                return true;
        }

        if (current["$ref"] is JsonValue referenceValue
            && referenceValue.TryGetValue<string>(out var reference)
            && references.Add(reference))
        {
            try
            {
                if (Resolve(root, reference) is { } resolved
                    && Supports(root, resolved, tokens, tokenIndex, depth + 1, references))
                    return true;
            }
            finally { references.Remove(reference); }
        }

        if (current["allOf"] is JsonArray allOf
            && allOf.Any(branch => Supports(root, branch, tokens, tokenIndex, depth + 1, new(references))))
            return true;

        foreach (var keyword in new[] { "anyOf", "oneOf" })
            if (current[keyword] is JsonArray alternatives && alternatives.Count > 0
                && alternatives.All(branch => Supports(root, branch, tokens, tokenIndex, depth + 1, new(references))))
                return true;

        return false;
    }

    private static JsonNode? Resolve(JsonNode root, string reference)
    {
        if (reference == "#") return root;
        if (!reference.StartsWith("#/", StringComparison.Ordinal)) return null;
        string decoded;
        try { decoded = Uri.UnescapeDataString(reference[2..]); }
        catch (UriFormatException) { return null; }
        JsonNode? current = root;
        foreach (var raw in decoded.Split('/'))
        {
            var token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current is JsonObject value && value.TryGetPropertyValue(token, out var child)) current = child;
            else if (current is JsonArray array && int.TryParse(token, out var index) && index >= 0 && index < array.Count) current = array[index];
            else return null;
        }
        return current;
    }
}
