using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Projections;

namespace DantesRoleplay.CatalogNavigation;

public enum ApplicationQueryExposure
{
    ModelVisible,
    BindingOnly
}

/// <summary>A catalog-owned selection from the currently authorized campaign, not a caller-supplied role.</summary>
public sealed record ApplicationQueryCampaignSelection(string QueryId, string EntityIdField);

/// <summary>
/// A catalog-declared, application-neutral selection proof. Selector roles are mapped only from
/// roles already resolved and authorized for the parent query.
/// </summary>
public sealed record ApplicationQuerySelection(
    string QueryId,
    string TargetRole,
    string ResultPointer,
    IReadOnlyDictionary<string, string> RoleBindings);

/// <summary>
/// One catalog-declared role source. Role and context keys are application-owned opaque names;
/// this generic contract never assigns meaning to them.
/// </summary>
public sealed record ApplicationQueryRoleBinding(string Source, string? Pointer = null, string? Key = null);

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
    string Status,
    string? InputSchemaJson = null,
    ApplicationQueryCampaignSelection? CampaignSelection = null)
{
    public const string CatalogKind = "query";
    /// <summary>
    /// The maximum number of catalog-declared selector links a read may traverse.
    /// This is shared by catalog admission and execution so an admitted contract
    /// cannot exceed the host's bounded selection work.
    /// </summary>
    public const int MaximumDeclaredSelectionLinks = 4;
    public const string ProjectionExecutor = "projection";
    public const string MechanicProjectionExecutor = "mechanic-projection";
    public const string ObjectProjectionExecutor = "object-projection";
    public string? ObjectCollectionId { get; init; }
    public string ObjectProfileId { get; init; } = RegisteredApplicationObjectContract.ContractProfileId;
    public IReadOnlyDictionary<string, ApplicationQueryRoleBinding>? RoleBindings { get; init; }
    public ApplicationQuerySelection? Selection { get; init; }
    public bool IsObjectProjection => Executor == ObjectProjectionExecutor;
    public bool IsFieldBasedObject => IsObjectProjection
        && ObjectProfileId == RegisteredApplicationObjectContract.FieldBasedContractProfileId;

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
        var executor = String(root, "executor", 63);
        if (executor is not (ProjectionExecutor or MechanicProjectionExecutor or ObjectProjectionExecutor))
            throw Invalid("The query executor kind is not supported.");
        var hasProfile = root.TryGetProperty("profile", out _);
        var fieldBasedObject = executor == ObjectProjectionExecutor && hasProfile
            && String(root, "profile", 64) == RegisteredApplicationObjectContract.FieldBasedContractProfileId;
        if (hasProfile && !fieldBasedObject)
            throw Invalid("Only a field-based object query may declare the application-object/v2 profile.");
        var referenceName = executor == ObjectProjectionExecutor ? "object" : "projection";
        var fields = new[] { "id", "category", "name", "description", "matches", "roles", "executor",
            referenceName, fieldBasedObject ? "profile" : "outputSchema", "exposure", "status" };
        var hasInput = root.TryGetProperty("inputSchema", out var inputSchema);
        var hasCampaignSelection = root.TryGetProperty("campaignSelection", out var campaignSelectionElement);
        var hasSelection = root.TryGetProperty("selection", out var selectionElement);
        var hasCollection = root.TryGetProperty("collection", out var collection);
        var hasRoleBindings = root.TryGetProperty("roleBindings", out var roleBindingsElement);
        Exact(root, [.. fields, .. (hasInput ? new[] { "inputSchema" } : []),
            .. (hasCampaignSelection ? new[] { "campaignSelection" } : []),
            .. (hasSelection ? new[] { "selection" } : []),
            .. (hasCollection ? new[] { "collection" } : []),
            .. (hasRoleBindings ? new[] { "roleBindings" } : [])]);
        if (executor != ObjectProjectionExecutor && hasCollection)
            throw Invalid("Only an object-projection query may declare a collection.");
        if (hasRoleBindings && executor is not (ObjectProjectionExecutor or MechanicProjectionExecutor))
            throw Invalid("Only an object or mechanic projection query may declare role bindings.");
        if (hasRoleBindings && hasCampaignSelection)
            throw Invalid("Explicit role bindings cannot be combined with legacy campaign selection.");
        if (hasSelection && hasCampaignSelection)
            throw Invalid("A query cannot combine declared selection with legacy campaign selection.");
        if (hasSelection && !hasRoleBindings)
            throw Invalid("A declared selection requires explicit role bindings.");
        if (executor == ObjectProjectionExecutor && hasInput && !hasRoleBindings)
            throw Invalid("An object-projection input schema requires explicit role bindings.");
        if (inputSchema.ValueKind != JsonValueKind.Undefined &&
            (inputSchema.ValueKind != JsonValueKind.Object
             || Encoding.UTF8.GetByteCount(inputSchema.GetRawText()) > 65_536
             || !inputSchema.TryGetProperty("type", out var inputType) || inputType.ValueKind != JsonValueKind.String || inputType.GetString() != "object"
             || !inputSchema.TryGetProperty("additionalProperties", out var additional)
             || additional.ValueKind != JsonValueKind.False))
            throw Invalid("A query input schema must declare one bounded closed object.");

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
        IReadOnlyDictionary<string, ApplicationQueryRoleBinding>? roleBindings = null;
        if (hasRoleBindings)
        {
            if (roleBindingsElement.ValueKind != JsonValueKind.Object)
                throw Invalid("Query role bindings must be an object.");
            var parsedBindings = new Dictionary<string, ApplicationQueryRoleBinding>(StringComparer.Ordinal);
            foreach (var property in roleBindingsElement.EnumerateObject())
            {
                if (!roles.ContainsKey(property.Name) || !parsedBindings.TryAdd(property.Name,
                        ParseRoleBinding(property.Value, hasInput)))
                    throw Invalid("Query role bindings contain an unknown or duplicate role.");
            }
            if (!parsedBindings.Keys.Order(StringComparer.Ordinal)
                    .SequenceEqual(roles.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw Invalid("Query role bindings must cover every declared role exactly once.");
            if (!parsedBindings.Values.Any(value => value.Source == "route-entity"))
                throw Invalid("Explicit role bindings must bind one declared role from the route entity.");
            if (hasInput && executor == ObjectProjectionExecutor
                && !parsedBindings.Values.Any(value => value.Source == "input"))
                throw Invalid("An object-projection input schema must bind at least one role from input.");
            roleBindings = new ReadOnlyDictionary<string, ApplicationQueryRoleBinding>(parsedBindings);
        }
        ApplicationQueryCampaignSelection? campaignSelection = null;
        if (hasCampaignSelection)
        {
            if (campaignSelectionElement.ValueKind != JsonValueKind.Object ||
                !(roles.Count == 1 || roles.Count == 2 && roles.ContainsKey("campaign")))
                throw Invalid("A campaign-selected query must declare exactly one target role.");
            Exact(campaignSelectionElement, "queryId", "entityIdField");
            var selectionQuery = String(campaignSelectionElement, "queryId", 200);
            var field = String(campaignSelectionElement, "entityIdField", 100);
            if (!selectionQuery.StartsWith(owner.Value + ".", StringComparison.Ordinal)
                || !Segments(selectionQuery[(owner.Value.Length + 1)..], '.') || selectionQuery == id
                || !char.IsAsciiLetter(field[0]) || !field.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
                throw Invalid("Campaign selection must name another query of this application and one top-level field.");
            campaignSelection = new(selectionQuery, field);
        }
        ApplicationQuerySelection? declaredSelection = null;
        if (hasSelection)
        {
            if (selectionElement.ValueKind != JsonValueKind.Object)
                throw Invalid("A declared selection must be an object.");
            Exact(selectionElement, "queryId", "targetRole", "resultPointer", "roleBindings");
            var selectionQuery = String(selectionElement, "queryId", 200);
            var targetRole = String(selectionElement, "targetRole", 200);
            var resultPointer = String(selectionElement, "resultPointer", 1_000);
            if (!selectionQuery.StartsWith(owner.Value + ".", StringComparison.Ordinal)
                || !Segments(selectionQuery[(owner.Value.Length + 1)..], '.') || selectionQuery == id)
                throw Invalid("A declared selection must name another query of this application.");
            if (!roles.ContainsKey(targetRole))
                throw Invalid("A declared selection target must be a parent query role.");
            if (!Pointer(resultPointer))
                throw Invalid("A declared selection result pointer must be a valid JSON pointer.");
            if (!selectionElement.TryGetProperty("roleBindings", out var selectionBindings)
                || selectionBindings.ValueKind != JsonValueKind.Object)
                throw Invalid("Declared selection role bindings must be an object.");
            var parsedSelectionBindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in selectionBindings.EnumerateObject())
            {
                if (!Segments(property.Name, '.') || property.Value.ValueKind != JsonValueKind.String
                    || !parsedSelectionBindings.TryAdd(property.Name, property.Value.GetString()!))
                    throw Invalid("Declared selection role bindings contain an invalid or duplicate selector role.");
                if (!roles.ContainsKey(property.Value.GetString()!))
                    throw Invalid("Declared selection role bindings must map to parent query roles.");
            }
            if (parsedSelectionBindings.Count is < 1 or > 32)
                throw Invalid("Declared selection role bindings must be bounded and nonempty.");
            declaredSelection = new(selectionQuery, targetRole, resultPointer,
                new ReadOnlyDictionary<string, string>(parsedSelectionBindings));
        }
        if (!root.TryGetProperty(referenceName, out var projection) || projection.ValueKind != JsonValueKind.Object)
            throw Invalid("A query requires an exact registered reference.");
        Exact(projection, executor == ObjectProjectionExecutor
            ? ["qualifiedId", "version", "contentFingerprint"]
            : ["qualifiedId", "version", "contentHash", "outputSchemaHash"]);
        var projectionId = String(projection, "qualifiedId", 200);
        if (!projectionId.StartsWith(owner.Value + ".", StringComparison.Ordinal)
            || !Segments(projectionId[(owner.Value.Length + 1)..], '.'))
            throw Invalid("A query projection must be qualified by the same application.");
        if (!projection.TryGetProperty("version", out var versionElement)
            || !versionElement.TryGetInt32(out var version) || version < 1)
            throw Invalid("A query projection version must be positive.");
        var contentHash = Hash(String(projection,
            executor == ObjectProjectionExecutor ? "contentFingerprint" : "contentHash", 64));
        var schemaHash = executor == ObjectProjectionExecutor ? "" : Hash(String(projection, "outputSchemaHash", 64));
        var schemaJson = RegisteredApplicationObjectContract.TransportSchemaJson;
        if (!fieldBasedObject)
        {
            if (!root.TryGetProperty("outputSchema", out var schema) || schema.ValueKind != JsonValueKind.Object)
                throw Invalid("A query output schema must be a JSON object.");
            if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > 65_536)
                throw Invalid("A query output schema exceeds the closed interaction bound.");
            schemaJson = schema.GetRawText();
        }

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
            contentHash, fieldBasedObject ? RegisteredApplicationObjectContract.TransportSchemaHash : schemaHash,
            schemaJson, exposure, status,
            inputSchema.ValueKind == JsonValueKind.Undefined ? null : inputSchema.GetRawText(), campaignSelection)
        {
            ObjectCollectionId = hasCollection ? String(root, "collection", 200) : null,
            ObjectProfileId = fieldBasedObject
                ? RegisteredApplicationObjectContract.FieldBasedContractProfileId
                : RegisteredApplicationObjectContract.ContractProfileId,
            RoleBindings = roleBindings,
            Selection = declaredSelection
        };
    }

    private static ApplicationQueryRoleBinding ParseRoleBinding(JsonElement value, bool hasInput)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("A query role binding must be an object.");
        var source = String(value, "source", 32);
        if (source == "route-entity")
        {
            Exact(value, "source");
            return new(source);
        }
        if (source == "input")
        {
            Exact(value, "source", "pointer");
            var pointer = String(value, "pointer", 1_000);
            if (!hasInput || !Pointer(pointer))
                throw Invalid("An input role binding requires a valid pointer and query input schema.");
            return new(source, Pointer: pointer);
        }
        if (source == "authorized-context")
        {
            Exact(value, "source", "key");
            var key = String(value, "key", 200);
            if (!Segments(key, '.'))
                throw Invalid("An authorized-context role binding requires a bounded opaque key.");
            return new(source, Key: key);
        }
        throw Invalid("A query role binding source is not supported.");
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

    private static bool Pointer(string value)
    {
        if (!value.StartsWith("/", StringComparison.Ordinal)) return false;
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsControl(value[index])) return false;
            if (value[index] != '~') continue;
            if (++index >= value.Length || value[index] is not ('0' or '1')) return false;
        }
        return true;
    }

    private static string Hash(string value) => value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F')
            ? value
            : throw Invalid("A query projection requires uppercase SHA-256 hashes.");

    private static ArgumentException Invalid(string message) => new(message, "json");
}
