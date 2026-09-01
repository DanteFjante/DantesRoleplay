using System.Text.Json;

namespace DantesRoleplay.Ecs;

public enum EcsStateSpaceScope
{
    Runtime,
    ApplicationPublication
}

public enum EcsEntitySelectorKind
{
    SemanticRole,
    Component
}

public sealed record EcsEntitySelector(EcsEntitySelectorKind Kind, string Value);

public sealed record EcsEntityUniquenessKey(
    string Name,
    EcsEntitySelector Source,
    string JsonPointer);

public sealed record EcsEntityRoleConstraint(
    string Id,
    EcsStateSpaceScope Scope,
    EcsEntitySelector Selector,
    int MinimumEnabled,
    int? MaximumEnabled,
    IReadOnlyList<EcsEntitySelector> Requires,
    IReadOnlyList<EcsEntityUniquenessKey> UniqueKeys);

public sealed record EcsComponentRolePolicy(
    IReadOnlyList<string> SemanticRoles,
    IReadOnlyList<EcsEntityRoleConstraint> Constraints);

public sealed class EcsRoleConstraintException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Validates enabled entities against the immutable role policy declared by their registered
/// component schemas. Implementations run after staged writes and before their transaction commits.
/// </summary>
public interface IEcsRoleConstraintValidator
{
    Task ValidateStateSpaceAsync(string stateSpaceId, CancellationToken cancellationToken = default);
}

/// <summary>Strict reader for the bounded, annotation-only ECS policy vocabulary.</summary>
public static class EcsComponentRolePolicyParser
{
    public const string RolesKeyword = "x-dantes-entity-roles";
    public const string ConstraintsKeyword = "x-dantes-role-constraints";

    public static EcsComponentRolePolicy Parse(string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        using var document = JsonDocument.Parse(schemaJson, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new([], []);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw Invalid("ROLE_POLICY_SCHEMA_INVALID", "A component schema carrying ECS policy must be an object.");

        var roles = document.RootElement.TryGetProperty(RolesKeyword, out var rolesElement)
            ? Strings(rolesElement, RolesKeyword, 32)
            : [];
        var constraints = document.RootElement.TryGetProperty(ConstraintsKeyword, out var constraintsElement)
            ? Constraints(constraintsElement)
            : [];
        return new(roles, constraints);
    }

    public static string ScopeName(EcsStateSpaceScope scope) => scope switch
    {
        EcsStateSpaceScope.Runtime => "runtime-state-space",
        EcsStateSpaceScope.ApplicationPublication => "application-publication",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public static EcsStateSpaceScope ParseScope(string value) => value switch
    {
        "runtime-state-space" => EcsStateSpaceScope.Runtime,
        "application-publication" => EcsStateSpaceScope.ApplicationPublication,
        _ => throw Invalid("ROLE_POLICY_SCOPE_INVALID",
            "Constraint scope must be 'runtime-state-space' or 'application-publication'.")
    };

    private static IReadOnlyList<EcsEntityRoleConstraint> Constraints(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 64)
            throw Invalid("ROLE_POLICY_CONSTRAINTS_INVALID", "Role constraints must be an array of at most 64 entries.");
        var constraints = new List<EcsEntityRoleConstraint>();
        foreach (var item in value.EnumerateArray())
        {
            ClosedObject(item, "id", "scope", "selector", "minimumEnabled", "maximumEnabled", "requires", "uniqueKeys");
            var id = RequiredId(item, "id", 200);
            var scope = ParseScope(RequiredString(item, "scope", 64));
            var selector = Selector(Required(item, "selector"));
            var minimum = OptionalInt(item, "minimumEnabled") ?? 0;
            var maximum = OptionalInt(item, "maximumEnabled");
            if (minimum < 0 || maximum < 0 || maximum is not null && maximum < minimum)
                throw Invalid("ROLE_POLICY_CARDINALITY_INVALID",
                    "Constraint cardinality requires a nonnegative minimum and an optional maximum no smaller than it.");
            var requires = item.TryGetProperty("requires", out var requiresElement)
                ? Selectors(requiresElement, "requires") : [];
            var uniqueKeys = item.TryGetProperty("uniqueKeys", out var keysElement)
                ? Keys(keysElement) : [];
            constraints.Add(new(id, scope, selector, minimum, maximum, requires, uniqueKeys));
        }
        if (constraints.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != constraints.Count)
            throw Invalid("ROLE_POLICY_CONSTRAINT_DUPLICATE", "A component schema may declare each constraint ID only once.");
        return constraints.AsReadOnly();
    }

    private static IReadOnlyList<EcsEntitySelector> Selectors(JsonElement value, string property)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 16)
            throw Invalid("ROLE_POLICY_SELECTOR_INVALID", $"'{property}' must be an array of at most 16 selectors.");
        return Array.AsReadOnly(value.EnumerateArray().Select(Selector).ToArray());
    }

    private static EcsEntitySelector Selector(JsonElement value)
    {
        ClosedObject(value, "semanticRole", "componentTypeId");
        var hasRole = value.TryGetProperty("semanticRole", out var role);
        var hasComponent = value.TryGetProperty("componentTypeId", out var component);
        if (hasRole == hasComponent)
            throw Invalid("ROLE_POLICY_SELECTOR_INVALID",
                "A selector must declare exactly one of semanticRole or componentTypeId.");
        var selected = RequiredBoundedString(hasRole ? role : component, hasRole ? "semanticRole" : "componentTypeId", 200);
        return new(hasRole ? EcsEntitySelectorKind.SemanticRole : EcsEntitySelectorKind.Component, selected);
    }

    private static IReadOnlyList<EcsEntityUniquenessKey> Keys(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 8)
            throw Invalid("ROLE_POLICY_UNIQUENESS_INVALID", "uniqueKeys must be an array of at most eight entries.");
        var keys = new List<EcsEntityUniquenessKey>();
        foreach (var item in value.EnumerateArray())
        {
            ClosedObject(item, "name", "source", "jsonPointer");
            var name = RequiredId(item, "name", 63);
            var source = Selector(Required(item, "source"));
            var pointer = RequiredString(item, "jsonPointer", 400);
            ValidatePointer(pointer);
            keys.Add(new(name, source, pointer));
        }
        if (keys.Select(key => key.Name).Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw Invalid("ROLE_POLICY_UNIQUENESS_INVALID", "A constraint may declare each uniqueness-key name only once.");
        return keys.AsReadOnly();
    }

    private static void ValidatePointer(string value)
    {
        if (value.Length == 0) return;
        if (!value.StartsWith("/", StringComparison.Ordinal))
            throw Invalid("ROLE_POLICY_POINTER_INVALID", "A uniqueness JSON pointer must be empty or begin with '/'.");
        foreach (var segment in value.Split('/').Skip(1))
        {
            for (var index = 0; index < segment.Length; index++)
            {
                if (segment[index] != '~') continue;
                if (++index >= segment.Length || segment[index] is not ('0' or '1'))
                    throw Invalid("ROLE_POLICY_POINTER_INVALID", "A uniqueness JSON pointer contains an invalid escape.");
            }
        }
    }

    private static IReadOnlyList<string> Strings(JsonElement value, string property, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum)
            throw Invalid("ROLE_POLICY_ROLES_INVALID", $"'{property}' must be an array of at most {maximum} roles.");
        var values = value.EnumerateArray()
            .Select(item => RequiredBoundedString(item, property, 200)).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw Invalid("ROLE_POLICY_ROLES_INVALID", "A component schema may grant each semantic role only once.");
        return Array.AsReadOnly(values);
    }

    private static string RequiredId(JsonElement value, string property, int maximum)
    {
        var result = RequiredString(value, property, maximum);
        if (!char.IsAsciiLetterLower(result[0]) || result.Any(character =>
                !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.')))
            throw Invalid("ROLE_POLICY_ID_INVALID", $"'{property}' must use lowercase dotted or hyphenated ASCII identity.");
        return result;
    }

    private static string RequiredString(JsonElement value, string property, int maximum) =>
        RequiredBoundedString(Required(value, property), property, maximum);

    private static string RequiredBoundedString(JsonElement value, string property, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text
            || string.IsNullOrWhiteSpace(text) || text.Length > maximum || text != text.Trim())
            throw Invalid("ROLE_POLICY_VALUE_INVALID", $"'{property}' must be a nonempty trimmed string of at most {maximum} characters.");
        return text;
    }

    private static int? OptionalInt(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var item) || item.ValueKind == JsonValueKind.Null) return null;
        if (!item.TryGetInt32(out var result) || result > 1_000_000)
            throw Invalid("ROLE_POLICY_CARDINALITY_INVALID", $"'{property}' must be a bounded integer.");
        return result;
    }

    private static JsonElement Required(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var result))
            throw Invalid("ROLE_POLICY_VALUE_REQUIRED", $"Role constraint is missing '{property}'.");
        return result;
    }

    private static void ClosedObject(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("ROLE_POLICY_OBJECT_INVALID", "Role policy entries must be objects.");
        var names = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Contains(property.Name))
                throw Invalid("ROLE_POLICY_PROPERTY_UNKNOWN", $"Unknown role-policy property '{property.Name}'.");
        }
    }

    private static EcsRoleConstraintException Invalid(string code, string message) => new(code, message);
}
