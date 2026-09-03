using System.Text.Json;
using System.Text.RegularExpressions;

namespace DantesRoleplay.Mechanics;

/// <summary>
/// Reads a bounded, declaration-owned path of entity references from already validated component
/// JSON. Segments are property names separated by dots; a segment ending in <c>[]</c> expands one
/// array. This is deliberately much smaller than JSONPath: mechanics cannot filter, recurse, or
/// select arbitrary state that they did not declare up front.
/// </summary>
public static partial class ComponentReferencePath
{
    public const int MaxPathLength = 200;

    public static bool IsValid(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.Length <= MaxPathLength &&
        path.Split('.').All(segment => Segment().IsMatch(segment));

    public static bool TryRead(
        JsonElement root,
        string path,
        out IReadOnlyList<string> entityIds,
        out string problem)
    {
        entityIds = [];
        problem = string.Empty;
        if (root.ValueKind != JsonValueKind.Object || !IsValid(path))
        {
            problem = "The declared component-reference path is invalid.";
            return false;
        }

        IReadOnlyList<JsonElement> current = [root];
        foreach (var rawSegment in path.Split('.'))
        {
            var expands = rawSegment.EndsWith("[]", StringComparison.Ordinal);
            var propertyName = expands ? rawSegment[..^2] : rawSegment;
            var next = new List<JsonElement>();
            foreach (var value in current)
            {
                if (value.ValueKind != JsonValueKind.Object ||
                    !value.TryGetProperty(propertyName, out var property))
                {
                    problem = $"The component-reference path lacks '{propertyName}'.";
                    return false;
                }

                if (!expands)
                {
                    next.Add(property);
                    continue;
                }

                if (property.ValueKind != JsonValueKind.Array)
                {
                    problem = $"The component-reference path expected '{propertyName}' to be an array.";
                    return false;
                }
                next.AddRange(property.EnumerateArray());
                if (next.Count > ProjectionLimits.MaxReferencedEntities)
                {
                    problem = "The component-reference path exceeds the referenced-entity limit.";
                    return false;
                }
            }
            current = next;
        }

        var result = new List<string>(current.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in current)
        {
            var entityId = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ValueKind == JsonValueKind.Object &&
                  value.TryGetProperty("entityId", out var reference) &&
                  reference.ValueKind == JsonValueKind.String
                    ? reference.GetString()
                    : null;
            if (string.IsNullOrWhiteSpace(entityId) || entityId != entityId.Trim() || entityId.Length > 400)
            {
                problem = "The component-reference path does not end in bounded entity references.";
                return false;
            }
            if (seen.Add(entityId)) result.Add(entityId);
        }

        entityIds = result;
        return true;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*(?:\\[\\])?$")]
    private static partial Regex Segment();
}
