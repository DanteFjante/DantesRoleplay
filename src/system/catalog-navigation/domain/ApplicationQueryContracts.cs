using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text;
using DantesRoleplay.Applications;

namespace DantesRoleplay.CatalogNavigation;

public enum ApplicationQueryExposure
{
    ModelVisible,
    BindingOnly
}

/// <summary>
/// Strict application-authored metadata for one host-executed read-only query. The executable
/// implementation remains an exact registered projection; this record only makes that projection
/// discoverable and declares whether its complete output is safe to return to a model.
/// </summary>
public sealed record ApplicationQueryContract(
    string Id,
    string Category,
    string Name,
    string Description,
    IReadOnlyList<string> Matches,
    IReadOnlyDictionary<string, string> Roles,
    string Executor,
    string ProjectionQualifiedId,
    int ProjectionVersion,
    string ProjectionContentHash,
    string OutputSchemaHash,
    string OutputSchemaJson,
    ApplicationQueryExposure Exposure,
    string Status)
{
    public const string CatalogKind = "query";
    public const string ProjectionExecutor = "projection";

    public static ApplicationQueryContract Parse(string json, ApplicationIdentifier owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length is 0 or > CatalogNavigationLimits.MaximumContentLength)
            throw new ArgumentException("A query contract must contain bounded JSON.", nameof(json));
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw Invalid("A query contract must be an object.");
        Exact(root, "id", "category", "name", "description", "matches", "roles", "executor",
            "projection", "outputSchema", "exposure", "status");

        var id = String(root, "id", 400);
        if (!id.StartsWith(owner.Value + ".", StringComparison.Ordinal)
            || !Segments(id[(owner.Value.Length + 1)..], '.'))
            throw Invalid("A query id must be qualified by its registered application.");
        var category = String(root, "category", 200);
        if (!Segments(category, '.')) throw Invalid("A query category must contain bounded identifier segments.");
        var name = Text(root, "name", 400);
        var description = Text(root, "description", CatalogNavigationLimits.MaximumTextLength);
        var matches = Strings(root, "matches", CatalogNavigationLimits.MaximumAliasesPerRecord, 200);
        var roles = StringMap(root, "roles", 32, 1_000);
        var executor = String(root, "executor", 63);
        if (executor != ProjectionExecutor) throw Invalid("The query executor kind is not supported.");

        if (!root.TryGetProperty("projection", out var projection) || projection.ValueKind != JsonValueKind.Object)
            throw Invalid("A query requires an exact projection reference.");
        Exact(projection, "qualifiedId", "version", "contentHash", "outputSchemaHash");
        var projectionId = String(projection, "qualifiedId", 200);
        if (!projectionId.StartsWith(owner.Value + ".", StringComparison.Ordinal)
            || !Segments(projectionId[(owner.Value.Length + 1)..], '.'))
            throw Invalid("A query projection must be qualified by the same application.");
        if (!projection.TryGetProperty("version", out var versionElement)
            || !versionElement.TryGetInt32(out var version) || version < 1)
            throw Invalid("A query projection version must be positive.");
        var contentHash = Hash(String(projection, "contentHash", 64));
        var schemaHash = Hash(String(projection, "outputSchemaHash", 64));
        if (!root.TryGetProperty("outputSchema", out var schema) || schema.ValueKind != JsonValueKind.Object)
            throw Invalid("A query output schema must be a JSON object.");
        if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > 65_536)
            throw Invalid("A query output schema exceeds the closed interaction bound.");

        var exposure = String(root, "exposure", 32) switch
        {
            "model-visible" => ApplicationQueryExposure.ModelVisible,
            "binding-only" => ApplicationQueryExposure.BindingOnly,
            _ => throw Invalid("A query exposure must be model-visible or binding-only.")
        };
        var status = String(root, "status", 32);
        if (status is not ("active" or "draft" or "retired"))
            throw Invalid("A query status is not supported.");

        return new(id, category, name, description, matches, roles, executor, projectionId, version,
            contentHash, schemaHash, schema.GetRawText(), exposure, status);
    }

    private static void Exact(JsonElement value, params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var properties = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Distinct(StringComparer.Ordinal).Count() != properties.Length
            || properties.Any(property => !allowed.Contains(property))
            || names.Any(name => !properties.Contains(name, StringComparer.Ordinal)))
            throw Invalid("A query contract contains missing, duplicate, or unknown properties.");
    }

    private static string String(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw Invalid($"Query property '{name}' must be a string.");
        var result = value.GetString()!;
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximum || result != result.Trim()
            || result.Any(char.IsControl))
            throw Invalid($"Query property '{name}' is invalid or unbounded.");
        return result;
    }

    private static string Text(JsonElement root, string name, int maximum) => String(root, name, maximum);

    private static IReadOnlyList<string> Strings(JsonElement root, string name, int maximumCount, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw Invalid($"Query property '{name}' must be an array.");
        var result = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
            ? item.GetString()! : throw Invalid($"Query property '{name}' must contain strings.")).ToArray();
        if (result.Length > maximumCount || result.Distinct(StringComparer.Ordinal).Count() != result.Length
            || result.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > maximumLength
                || item != item.Trim() || item.Any(char.IsControl)))
            throw Invalid($"Query property '{name}' is invalid or unbounded.");
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyDictionary<string, string> StringMap(
        JsonElement root, string name, int maximumCount, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw Invalid($"Query property '{name}' must be an object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!Segments(property.Name, '.') || property.Value.ValueKind != JsonValueKind.String
                || !result.TryAdd(property.Name, property.Value.GetString()!))
                throw Invalid($"Query property '{name}' contains an invalid or duplicate role.");
        }
        if (result.Count > maximumCount || result.Values.Any(item => string.IsNullOrWhiteSpace(item)
                || item.Length > maximumLength || item != item.Trim() || item.Any(char.IsControl)))
            throw Invalid($"Query property '{name}' is invalid or unbounded.");
        return new ReadOnlyDictionary<string, string>(result);
    }

    private static bool Segments(string value, char separator) => value.Length > 0
        && value.Split(separator).All(segment => segment is { Length: > 0 and <= 63 }
            && char.IsAsciiLetterLower(segment[0])
            && segment.All(character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character) || character == '-'));

    private static string Hash(string value) => value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F')
            ? value
            : throw Invalid("A query projection requires uppercase SHA-256 hashes.");

    private static ArgumentException Invalid(string message) => new(message, "json");
}
