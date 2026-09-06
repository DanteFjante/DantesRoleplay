using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Projections;

public sealed record ApplicationObjectRole(string RoleId, bool Required);
public sealed record ApplicationObjectSource(string InputId, bool Required);
public sealed record ApplicationObjectReference(string InputId, bool Required);
public sealed record ApplicationObjectEndpointComponent(string Endpoint, EcsComponentReference Type);
public sealed record ApplicationObjectRelationship(
    string RelationshipId,
    string QualifiedKind,
    string FromRole,
    string ToRole,
    string Cardinality,
    string TargetPointer,
    IReadOnlyList<ApplicationObjectEndpointComponent> RequiredEndpointComponents,
    IReadOnlyList<ApplicationObjectEndpointComponent> OptionalEndpointComponents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Direction = null);
public sealed record ApplicationObjectOrder(string Pointer, string Direction);
public sealed record ApplicationObjectCollection(
    string CollectionId,
    string SourceId,
    int PageSize,
    int MaximumPageSize,
    IReadOnlyList<ApplicationObjectOrder> Order,
    string Cursor);
public sealed record ApplicationObjectLimits(
    int TraversalDepth,
    int ItemCount,
    int OutputBytes,
    int SqlQueries);
public sealed record ApplicationObjectAccess(
    IReadOnlyList<string> ReadPerspectives,
    IReadOnlyList<string> WritePerspectives);
public sealed record ApplicationObjectWritePath(string Pointer, IReadOnlyList<string> Operations);
public sealed record ApplicationObjectWriteContractRequest(
    string EditSchemaJson,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<ApplicationObjectWritePath> Paths);
public sealed record RegisteredApplicationObjectWriteContract(
    string EditSchemaJson,
    string EditSchemaProfileId,
    string EditSchemaHash,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<ApplicationObjectWritePath> Paths);
public sealed record GeneratedApplicationObjectWriteMapping(
    string ObjectPointer,
    string Operation,
    string? InputId,
    string? SourcePointer,
    string? RelationshipId);

public sealed record ApplicationObjectContractRequest(
    IReadOnlyList<ApplicationObjectRole> Roles,
    IReadOnlyList<ApplicationObjectSource> Sources,
    IReadOnlyList<ApplicationObjectRelationship> Relationships,
    IReadOnlyList<ApplicationObjectReference> References,
    IReadOnlyList<ApplicationObjectCollection> Collections,
    ApplicationObjectLimits Limits,
    ApplicationObjectAccess Access,
    ApplicationObjectWriteContractRequest? Writes);

public sealed record RegisteredApplicationObjectContract(
    string ProfileId,
    IReadOnlyList<ApplicationObjectRole> Roles,
    IReadOnlyList<ApplicationObjectSource> Sources,
    IReadOnlyList<ApplicationObjectRelationship> Relationships,
    IReadOnlyList<ApplicationObjectReference> References,
    IReadOnlyList<ApplicationObjectCollection> Collections,
    ApplicationObjectLimits Limits,
    ApplicationObjectAccess Access,
    RegisteredApplicationObjectWriteContract? Writes,
    IReadOnlyList<GeneratedApplicationObjectWriteMapping> GeneratedWriteMappings)
{
    public const string ContractProfileId = "application-object/v1";
}

/// <summary>Strict parser for one catalog-authored application object document.</summary>
public static class ApplicationObjectDocument
{
    public static ProjectionDefinitionRequest Parse(string json, ApplicationIdentifier owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(owner);
        if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024)
            throw Invalid("An application object document exceeds its source bound.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        var root = document.RootElement;
        Exact(root, "id", "version", "schema", "roles", "sources", "relationships", "references",
            "mappings", "collections", "limits", "access", root.TryGetProperty("writes", out _) ? "writes" : null);
        var id = Identifier(root, "id", 200);
        if (!id.StartsWith(owner.Value + ".", StringComparison.Ordinal))
            throw Invalid("An application object ID must be qualified by its owner.");
        var version = Positive(root, "version", 1_000_000);
        var schema = Object(root, "schema").GetRawText();

        var roles = Properties(Object(root, "roles"), 32).Select(property =>
        {
            Exact(property.Value, "required");
            return new ApplicationObjectRole(Token(property.Name), Boolean(property.Value, "required"));
        }).ToArray();
        if (roles.Length == 0) throw Invalid("An application object requires at least one role.");

        var sources = Array(root, "sources", 32).Select(value =>
        {
            Exact(value, "id", "role", "component", "required");
            return new ProjectionComponentInput(Identifier(value, "id", 200), Identifier(value, "role", 200),
                Component(Object(value, "component")));
        }).ToArray();
        var sourceDeclarations = Array(root, "sources", 32).Select(value =>
            new ApplicationObjectSource(Identifier(value, "id", 200), Boolean(value, "required"))).ToArray();

        var references = Array(root, "references", 32).Select(value =>
        {
            Exact(value, "id", "object", "roles", "required");
            var reference = Object(value, "object");
            Exact(reference, "qualifiedId", "version", "contentFingerprint");
            return new ProjectionDependencyInput(Identifier(value, "id", 200),
                new ProjectionReference(Identifier(reference, "qualifiedId", 200),
                    Positive(reference, "version", 1_000_000), Hash(reference, "contentFingerprint")),
                StringMap(Object(value, "roles"), 64));
        }).ToArray();
        var referenceDeclarations = Array(root, "references", 32).Select(value =>
            new ApplicationObjectReference(Identifier(value, "id", 200), Boolean(value, "required"))).ToArray();

        var relationships = Array(root, "relationships", 32).Select(value =>
        {
            Exact(value, "id", "qualifiedKind", "fromRole", "toRole", "cardinality", "targetPointer",
                "requiredEndpointComponents", "optionalEndpointComponents",
                value.TryGetProperty("direction", out _) ? "direction" : null);
            return new ApplicationObjectRelationship(
                Identifier(value, "id", 200), Identifier(value, "qualifiedKind", 200),
                Identifier(value, "fromRole", 200), Identifier(value, "toRole", 200),
                Identifier(value, "cardinality", 32), Pointer(value, "targetPointer"),
                EndpointComponents(Array(value, "requiredEndpointComponents", 32)),
                EndpointComponents(Array(value, "optionalEndpointComponents", 32)),
                value.TryGetProperty("direction", out _) ? Identifier(value, "direction", 16) : null);
        }).ToArray();

        var mappings = Array(root, "mappings", 128).Select(value =>
        {
            Exact(value, "inputId", "sourcePointer", "targetPointer");
            return new StructuralProjectionMapping(Identifier(value, "inputId", 200),
                Pointer(value, "sourcePointer"), Pointer(value, "targetPointer"));
        }).ToArray();

        var collections = Array(root, "collections", 8).Select(value =>
        {
            Exact(value, "id", "sourceId", "pageSize", "maximumPageSize", "order", "cursor");
            var order = Array(value, "order", 4).Select(item =>
            {
                Exact(item, "path", "direction");
                return new ApplicationObjectOrder(Pointer(item, "path"), Identifier(item, "direction", 8));
            }).ToArray();
            return new ApplicationObjectCollection(Identifier(value, "id", 200), Identifier(value, "sourceId", 200),
                Positive(value, "pageSize", 500), Positive(value, "maximumPageSize", 500), order,
                Identifier(value, "cursor", 64));
        }).ToArray();

        var limitsValue = Object(root, "limits");
        Exact(limitsValue, "traversalDepth", "itemCount", "outputBytes", "sqlQueries");
        var limits = new ApplicationObjectLimits(Positive(limitsValue, "traversalDepth", 16),
            Positive(limitsValue, "itemCount", 10_000), Positive(limitsValue, "outputBytes", 1_048_576),
            Positive(limitsValue, "sqlQueries", 64));
        var accessValue = Object(root, "access");
        Exact(accessValue, "read", "write");
        var access = new ApplicationObjectAccess(StringArray(accessValue, "read", 2, 16),
            StringArray(accessValue, "write", 2, 16));

        ApplicationObjectWriteContractRequest? writes = null;
        if (root.TryGetProperty("writes", out var writesValue))
        {
            Exact(writesValue, "schema", "capabilities", "paths");
            writes = new(Object(writesValue, "schema").GetRawText(),
                StringArray(writesValue, "capabilities", 4, 32),
                Array(writesValue, "paths", 128).Select(value =>
                {
                    Exact(value, "path", "operations");
                    return new ApplicationObjectWritePath(Pointer(value, "path"),
                        StringArray(value, "operations", 4, 32));
                }).ToArray());
        }
        return new(owner, id, schema, sources, references, mappings,
            new(roles, sourceDeclarations, relationships, referenceDeclarations, collections, limits, access, writes),
            version);
    }

    private static IReadOnlyList<ApplicationObjectEndpointComponent> EndpointComponents(IEnumerable<JsonElement> values) =>
        values.Select(value =>
        {
            Exact(value, "endpoint", "component");
            return new ApplicationObjectEndpointComponent(Identifier(value, "endpoint", 8),
                Component(Object(value, "component")));
        }).ToArray();

    private static EcsComponentReference Component(JsonElement value)
    {
        Exact(value, "qualifiedId", "version", "schemaHash");
        return new(Identifier(value, "qualifiedId", 200), Positive(value, "version", 1_000_000),
            Hash(value, "schemaHash"));
    }

    private static IReadOnlyDictionary<string, string> StringMap(JsonElement value, int maximum)
    {
        var result = Properties(value, maximum).ToDictionary(property => Token(property.Name), property =>
            property.Value.ValueKind == JsonValueKind.String ? Token(property.Value.GetString()!)
                : throw Invalid("Object role bindings must contain strings."), StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, string>(result);
    }

    private static IReadOnlyList<string> StringArray(JsonElement root, string name, int maximum, int length)
    {
        var result = Array(root, name, maximum).Select(value => value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw Invalid($"Object property '{name}' must contain strings.")).ToArray();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length || result.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Length > length || value != value.Trim() || value.Any(char.IsControl)))
            throw Invalid($"Object property '{name}' contains invalid values.");
        return System.Array.AsReadOnly(result);
    }

    private static JsonElement Object(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw Invalid($"Object property '{name}' must be an object.");
        return value;
    }

    private static IEnumerable<JsonElement> Array(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > maximum)
            throw Invalid($"Object property '{name}' must be a bounded array.");
        return value.EnumerateArray();
    }

    private static IEnumerable<JsonProperty> Properties(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("An object map is required.");
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length > maximum || properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
            throw Invalid("An object map is duplicate or unbounded.");
        return properties;
    }

    private static string Identifier(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw Invalid($"Object property '{name}' must be a string.");
        var result = value.GetString()!;
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximum || result != result.Trim()
            || result.Any(char.IsControl)) throw Invalid($"Object property '{name}' is invalid.");
        return result;
    }

    private static string Pointer(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            throw Invalid($"Object property '{name}' must be a string.");
        var value = element.GetString()!;
        if (value.Length > 1_000 || value != value.Trim() || value.Any(char.IsControl))
            throw Invalid($"Object property '{name}' is invalid.");
        if (value != "" && !value.StartsWith("/", StringComparison.Ordinal))
            throw Invalid($"Object property '{name}' must be a JSON pointer.");
        return value;
    }

    private static int Positive(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result)
            || result < 1 || result > maximum) throw Invalid($"Object property '{name}' is outside its bound.");
        return result;
    }

    private static bool Boolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid($"Object property '{name}' must be boolean.");
        return value.GetBoolean();
    }

    private static string Hash(JsonElement root, string name)
    {
        var value = Identifier(root, name, 64);
        return ProjectionReference.Hash(value) ? value : throw Invalid($"Object property '{name}' must be an uppercase SHA-256 hash.");
    }

    private static string Token(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value != value.Trim()
            || !char.IsAsciiLetterLower(value[0])
            || value.Any(character => !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character)
                || character is '-' or '.')))
            throw Invalid("Object identifiers must contain bounded lowercase identifier segments.");
        return value;
    }

    private static void Exact(JsonElement value, params string?[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("An object value must be an object.");
        var expected = names.Where(name => name is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw Invalid("An application object contains missing, duplicate, or unknown properties.");
    }

    private static ArgumentException Invalid(string message) => new(message, "json");
}
