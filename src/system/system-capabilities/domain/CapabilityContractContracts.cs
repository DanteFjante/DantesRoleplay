using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DantesRoleplay.Capabilities;

public static class CapabilityContractLifecycle
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Deprecated = "deprecated";
    public const string Retired = "retired";

    public static bool IsValid(string value) => value is Draft or Active or Deprecated or Retired;
}

public static class CapabilityContractSchemaStatus
{
    public const string Authored = "authored";
    public const string Generated = "generated";
    public const string Generic = "generic";

    public static bool IsValid(string value) => value is Authored or Generated or Generic;
}

public sealed record CapabilitySchemaContract(
    string Profile,
    string SchemaJson,
    string SchemaHash,
    string Status);

public sealed record CapabilityOperationContract(
    bool ReadsState,
    bool SupportsPreview,
    bool ChangesState);

public sealed record CapabilityScopeContract(
    string Kind,
    bool RequiresApplicationId,
    bool RequiresStateSpaceId,
    IReadOnlyList<string> RequiredContext);

public sealed record CapabilityRoleContract(
    string Name,
    bool Required,
    string Description,
    IReadOnlyList<string> RequiredComponentIds);

public sealed record CapabilityAuthorizationContract(
    string Policy,
    string RequiredCapability,
    string Sensitivity);

public sealed record CapabilityExampleContract(
    string Name,
    string InputJson,
    string? OutputJson = null,
    bool ExpectedValid = true);

public sealed record CapabilityErrorContract(
    string Code,
    string Message,
    string Recovery);

public sealed record CapabilityRecoveryActionContract(
    string CapabilityId,
    string Description,
    string InputJson);

public sealed record CapabilityObjectSourceContract(
    string Reference,
    IReadOnlyList<string> InputPath,
    string Role,
    bool Required,
    string QualifiedComponentId,
    int ComponentVersion,
    CapabilitySchemaContract Schema);

public sealed record CapabilityObjectFieldContract(
    string Path,
    string SourceReference,
    string SourcePath);

public sealed record CapabilityObjectWritePathContract(
    string Path,
    IReadOnlyList<string> Operations,
    string? SourceReference,
    string? SourcePath,
    string? RelationshipId);

/// <summary>
/// Discovery-only provenance for one application-declared role parameter. Source names are
/// structural host concepts; role names and authorized-context keys remain opaque catalog data.
/// </summary>
public sealed record CapabilityObjectRoleBindingContract(
    string Role,
    string Source,
    string? Pointer,
    string? Key);

/// <summary>
/// Registry-derived structural-object discovery. Component schemas remain authoritative; fields
/// describe declared copies and do not form another assembled-value admission schema.
/// </summary>
public sealed record CapabilityObjectDiscoveryContract(
    string Profile,
    string QualifiedId,
    int Version,
    string ContentFingerprint,
    IReadOnlyList<CapabilityObjectSourceContract> Sources,
    IReadOnlyList<CapabilityObjectFieldContract> Fields,
    IReadOnlyList<CapabilityObjectWritePathContract> Writes)
{
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CapabilityObjectRoleBindingContract>? RoleBindings { get; init; }
}

/// <summary>
/// One transport-neutral description of something the system can do. The descriptor points back
/// to its owning registry through SourceKind and SourceFingerprint; it never replaces that owner.
/// </summary>
public sealed record CapabilityContractDescriptor(
    string Id,
    int Version,
    string Fingerprint,
    string SourceKind,
    string SourceFingerprint,
    string Owner,
    string Name,
    string Description,
    string Lifecycle,
    CapabilityOperationContract Operations,
    CapabilitySchemaContract Input,
    CapabilitySchemaContract Output,
    CapabilityScopeContract Scope,
    IReadOnlyList<CapabilityRoleContract> Roles,
    CapabilityAuthorizationContract Authorization,
    bool RequiresConfirmation,
    bool RequiresIdempotencyKey,
    IReadOnlyList<string> ProcedureIds,
    IReadOnlyList<CapabilityExampleContract> Examples,
    IReadOnlyList<CapabilityErrorContract> Errors,
    IReadOnlyList<CapabilityRecoveryActionContract> RecoveryActions)
{
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CapabilityObjectDiscoveryContract? ObjectDiscovery { get; init; }
}

public static class CapabilityContractBuilder
{
    public const string JsonSchemaProfile = "capability-json-schema/v1";

    public static CapabilityContractDescriptor Create(
        string id,
        int version,
        string sourceKind,
        string sourceFingerprint,
        string owner,
        string name,
        string description,
        string lifecycle,
        CapabilityOperationContract operations,
        string inputSchemaJson,
        string inputSchemaStatus,
        string outputSchemaJson,
        string outputSchemaStatus,
        CapabilityScopeContract scope,
        IReadOnlyList<CapabilityRoleContract>? roles,
        CapabilityAuthorizationContract authorization,
        bool requiresConfirmation,
        bool requiresIdempotencyKey,
        IReadOnlyList<string>? procedureIds,
        IReadOnlyList<CapabilityExampleContract>? examples,
        IReadOnlyList<CapabilityErrorContract>? errors,
        IReadOnlyList<CapabilityRecoveryActionContract>? recoveryActions,
        string inputSchemaProfile = JsonSchemaProfile,
        string outputSchemaProfile = JsonSchemaProfile,
        string? inputSchemaHash = null,
        string? outputSchemaHash = null,
        CapabilityObjectDiscoveryContract? objectDiscovery = null)
    {
        if (!Identifier(id, 240) || version < 1 || !Identifier(sourceKind, 80)
            || !FingerprintOrIdentifier(sourceFingerprint) || !Identifier(owner, 200)
            || !Text(name, 400) || !Text(description, 5_000)
            || !CapabilityContractLifecycle.IsValid(lifecycle))
            throw new ArgumentException("A capability contract has invalid identity or lifecycle metadata.", nameof(id));
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!Identifier(scope.Kind, 80) || scope.RequiredContext is null
            || scope.RequiredContext.Count > 16 || scope.RequiredContext.Distinct(StringComparer.Ordinal).Count() != scope.RequiredContext.Count
            || scope.RequiredContext.Any(value => !Identifier(value, 120))
            || !Identifier(authorization.Policy, 120) || !Identifier(authorization.RequiredCapability, 160)
            || !Identifier(authorization.Sensitivity, 80))
            throw new ArgumentException("A capability contract has invalid scope or authorization metadata.", nameof(scope));
        if (!operations.ReadsState && !operations.SupportsPreview && !operations.ChangesState)
            throw new ArgumentException("A capability contract must declare at least one operation mode.", nameof(operations));
        if (!operations.ChangesState && (requiresConfirmation || requiresIdempotencyKey))
            throw new ArgumentException("A read-only capability cannot require confirmation or idempotency.", nameof(requiresConfirmation));

        var input = Schema(inputSchemaProfile, inputSchemaJson, inputSchemaStatus, inputSchemaHash);
        var output = Schema(outputSchemaProfile, outputSchemaJson, outputSchemaStatus, outputSchemaHash);
        var copiedRoles = (roles ?? []).OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        if (copiedRoles.Length > 32 || copiedRoles.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != copiedRoles.Length
            || copiedRoles.Any(value => !Identifier(value.Name, 120) || !Text(value.Description, 1_000)
                || value.RequiredComponentIds is null || value.RequiredComponentIds.Count > 64
                || value.RequiredComponentIds.Distinct(StringComparer.Ordinal).Count() != value.RequiredComponentIds.Count
                || value.RequiredComponentIds.Any(component => !Identifier(component, 240))))
            throw new ArgumentException("A capability contract has invalid role metadata.", nameof(roles));
        var procedures = (procedureIds ?? []).Order(StringComparer.Ordinal).ToArray();
        if (procedures.Length > 32 || procedures.Distinct(StringComparer.Ordinal).Count() != procedures.Length
            || procedures.Any(value => !Identifier(value, 240)))
            throw new ArgumentException("A capability contract has invalid procedure references.", nameof(procedureIds));
        var copiedExamples = (examples ?? []).ToArray();
        var copiedErrors = (errors ?? []).ToArray();
        var copiedRecovery = (recoveryActions ?? []).ToArray();
        if (copiedExamples.Length is < 2 or > 16 || !copiedExamples.Any(value => value.ExpectedValid)
            || !copiedExamples.Any(value => !value.ExpectedValid)
            || copiedExamples.Any(value => !Text(value.Name, 120)
                || !JsonObject(value.InputJson) || value.OutputJson is not null && !ValidJsonValue(value.OutputJson))
            || copiedErrors.Length is < 1 or > 32 || copiedErrors.Select(value => value.Code).Distinct(StringComparer.Ordinal).Count() != copiedErrors.Length
            || copiedErrors.Any(value => !Identifier(value.Code, 120) || !Text(value.Message, 500) || !Text(value.Recovery, 1_000))
            || copiedRecovery.Length is < 1 or > 16 || copiedRecovery.Any(value => !Identifier(value.CapabilityId, 240)
                || !Text(value.Description, 500) || !JsonObject(value.InputJson)))
            throw new ArgumentException("A capability contract requires bounded examples, stable errors, and recovery actions.", nameof(examples));

        var normalizedDiscovery = ObjectDiscovery(objectDiscovery);
        var withoutFingerprint = new CapabilityContractDescriptor(
            id, version, "", sourceKind, sourceFingerprint, owner, name, description, lifecycle,
            operations, input, output, scope, Array.AsReadOnly(copiedRoles), authorization,
            requiresConfirmation, requiresIdempotencyKey, Array.AsReadOnly(procedures),
            Array.AsReadOnly(copiedExamples), Array.AsReadOnly(copiedErrors), Array.AsReadOnly(copiedRecovery))
        { ObjectDiscovery = normalizedDiscovery };
        return withoutFingerprint with { Fingerprint = Fingerprint(withoutFingerprint) };
    }

    public static string MinimalExample(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson) as JsonObject
            ?? throw new ArgumentException("A capability schema must be a JSON object.", nameof(schemaJson));
        return Example(root, root, 0, 0)?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
    }

    public static string MinimalInvalidExample(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson) as JsonObject
            ?? throw new ArgumentException("A capability schema must be a JSON object.", nameof(schemaJson));
        const string unexpectedProperty = "{\"__unexpected\":true}";
        if (RejectsUnknownProperty(root))
            return unexpectedProperty;

        var maximum = root["maxProperties"]?.GetValue<int>();
        if (maximum is null or < 0 or > 64)
            throw new ArgumentException("A capability input schema must close or bound its top-level object.", nameof(schemaJson));
        var oversized = new JsonObject();
        for (var index = 0; index <= maximum; index++) oversized[$"property{index}"] = true;
        return oversized.ToJsonString();
    }

    private static bool RejectsUnknownProperty(JsonObject schema)
    {
        if (schema["additionalProperties"]?.GetValue<bool>() == false) return true;
        return schema["anyOf"] is JsonArray alternatives && alternatives.Count > 0
            && alternatives.All(value => value is JsonObject alternative && RejectsUnknownProperty(alternative));
    }

    public static string SchemaHash(string schemaJson) => Hash(Normalize(schemaJson));

    public static string Fingerprint(CapabilityContractDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var legacy = JsonSerializer.Serialize(new
        {
            descriptor.Id,
            descriptor.Version,
            descriptor.SourceKind,
            descriptor.SourceFingerprint,
            descriptor.Owner,
            descriptor.Name,
            descriptor.Description,
            descriptor.Lifecycle,
            descriptor.Operations,
            input = new { descriptor.Input.Profile, descriptor.Input.SchemaHash, descriptor.Input.Status },
            output = new { descriptor.Output.Profile, descriptor.Output.SchemaHash, descriptor.Output.Status },
            descriptor.Scope,
            descriptor.Roles,
            descriptor.Authorization,
            descriptor.RequiresConfirmation,
            descriptor.RequiresIdempotencyKey,
            descriptor.ProcedureIds,
            descriptor.Examples,
            descriptor.Errors,
            descriptor.RecoveryActions
        });
        if (descriptor.ObjectDiscovery is null) return Hash(legacy);
        return Hash(JsonSerializer.Serialize(new
        {
            legacy = JsonSerializer.Deserialize<JsonElement>(legacy),
            descriptor.ObjectDiscovery
        }));
    }

    private static CapabilityObjectDiscoveryContract? ObjectDiscovery(
        CapabilityObjectDiscoveryContract? discovery)
    {
        if (discovery is null) return null;
        if (!Identifier(discovery.Profile, 120) || !Identifier(discovery.QualifiedId, 240)
            || discovery.Version < 1 || !FingerprintOrIdentifier(discovery.ContentFingerprint)
            || discovery.Sources is null || discovery.Sources.Count > 256
            || discovery.Fields is null || discovery.Fields.Count > 1_024
            || discovery.Writes is null || discovery.Writes.Count > 128)
            throw new ArgumentException("Object discovery metadata is invalid or unbounded.", nameof(discovery));

        var sources = discovery.Sources.OrderBy(value => value.Reference, StringComparer.Ordinal).ToArray();
        if (sources.Select(value => value.Reference).Distinct(StringComparer.Ordinal).Count() != sources.Length
            || sources.Any(value => !Identifier(value.Reference, 120) || !Identifier(value.Role, 120)
                || value.InputPath is null || value.InputPath.Count is < 1 or > 17
                || value.InputPath.Any(segment => !Identifier(segment, 200))
                || !Identifier(value.QualifiedComponentId, 240) || value.ComponentVersion < 1
                || value.Schema is null))
            throw new ArgumentException("Object discovery sources are invalid or ambiguous.", nameof(discovery));
        var normalizedSources = sources.Select(value => value with
        {
            InputPath = Array.AsReadOnly(value.InputPath.ToArray()),
            Schema = Schema(value.Schema.Profile, value.Schema.SchemaJson, value.Schema.Status,
                value.Schema.SchemaHash)
        }).ToArray();
        if (normalizedSources.Sum(value => Encoding.UTF8.GetByteCount(value.Schema.SchemaJson)) > 2 * 1024 * 1024)
            throw new ArgumentException("Object discovery component schemas exceed the fixed byte bound.", nameof(discovery));
        var sourceIds = normalizedSources.Select(value => value.Reference).ToHashSet(StringComparer.Ordinal);

        var fields = discovery.Fields.OrderBy(value => value.Path, StringComparer.Ordinal)
            .ThenBy(value => value.SourceReference, StringComparer.Ordinal)
            .ThenBy(value => value.SourcePath, StringComparer.Ordinal).ToArray();
        if (fields.Any(value => !Pointer(value.Path) || !Pointer(value.SourcePath)
                || !sourceIds.Contains(value.SourceReference))
            || fields.Select(value => (value.Path, value.SourceReference, value.SourcePath)).Distinct().Count() != fields.Length)
            throw new ArgumentException("Object discovery fields have invalid source provenance.", nameof(discovery));

        var writes = discovery.Writes.OrderBy(value => value.Path, StringComparer.Ordinal).ToArray();
        if (writes.Select(value => value.Path).Distinct(StringComparer.Ordinal).Count() != writes.Length
            || writes.Any(value => !Pointer(value.Path) || value.Operations is null
                || value.Operations.Count is < 1 or > 4
                || value.Operations.Distinct(StringComparer.Ordinal).Count() != value.Operations.Count
                || value.Operations.Any(operation => !Identifier(operation, 80))
                || value.SourceReference is not null && !sourceIds.Contains(value.SourceReference)
                || value.SourcePath is not null && !Pointer(value.SourcePath)
                || (value.SourceReference is null) != (value.SourcePath is null)
                || value.RelationshipId is not null && !Identifier(value.RelationshipId, 200)
                || value.RelationshipId is not null && value.SourceReference is not null))
            throw new ArgumentException("Object discovery write paths are invalid or ambiguous.", nameof(discovery));
        var roleBindings = discovery.RoleBindings?.OrderBy(value => value.Role, StringComparer.Ordinal).ToArray();
        if (roleBindings is not null && (roleBindings.Length is < 1 or > 32
            || roleBindings.Select(value => value.Role).Distinct(StringComparer.Ordinal).Count() != roleBindings.Length
            || roleBindings.Any(value => !Identifier(value.Role, 120)
                || value.Source is not ("route-entity" or "input" or "authorized-context")
                || value.Source == "route-entity" && (value.Pointer is not null || value.Key is not null)
                || value.Source == "input" && (!Pointer(value.Pointer ?? "invalid") || value.Key is not null)
                || value.Source == "authorized-context" && (value.Pointer is not null
                    || !OpaqueKey(value.Key)))))
            throw new ArgumentException("Object discovery role bindings are invalid or ambiguous.", nameof(discovery));
        return discovery with
        {
            Sources = Array.AsReadOnly(normalizedSources),
            Fields = Array.AsReadOnly(fields),
            Writes = Array.AsReadOnly(writes.Select(value => value with
            { Operations = Array.AsReadOnly(value.Operations.Order(StringComparer.Ordinal).ToArray()) }).ToArray()),
            RoleBindings = roleBindings is null ? null : Array.AsReadOnly(roleBindings)
        };
    }

    private static CapabilitySchemaContract Schema(string profile, string json, string status, string? expectedHash)
    {
        if (!Identifier(profile, 120) || !CapabilityContractSchemaStatus.IsValid(status))
            throw new ArgumentException("A capability schema has invalid metadata.", nameof(profile));
        var normalized = Normalize(json);
        var hash = Hash(normalized);
        if (expectedHash is not null)
        {
            var profiledHash = Hash($"{{\"profile\":{JsonSerializer.Serialize(profile)},\"schema\":{normalized}}}");
            if (!string.Equals(expectedHash, hash, StringComparison.Ordinal)
                && !string.Equals(expectedHash, profiledHash, StringComparison.Ordinal))
                throw new ArgumentException("A capability schema hash does not match its JSON and profile.", nameof(expectedHash));
            hash = expectedHash;
        }
        return new(profile, normalized, hash, status);
    }

    private static JsonNode? Example(JsonObject schema, JsonObject root, int variant, int depth)
    {
        if (depth > 64)
            throw new ArgumentException("A capability schema has a cyclic or excessively deep local reference.", nameof(schema));
        if (schema["$ref"]?.GetValue<string>() is { } reference)
            return Example(ResolveLocalReference(root, reference), root, variant, depth + 1);
        if (schema["default"] is JsonNode declaredDefault) return declaredDefault.DeepClone();
        if (schema["const"] is JsonNode constant) return constant.DeepClone();
        if (schema["enum"] is JsonArray choices && choices.Count > 0 && choices[0] is JsonNode choice)
            return choice.DeepClone();
        if (schema["anyOf"] is JsonArray alternatives && alternatives.Count > 0
            && alternatives[0] is JsonObject firstAlternative) return Example(firstAlternative, root, variant, depth + 1);
        var type = schema["type"]?.GetValue<string>();
        if (type == "null") return null;
        if (type == "object" || schema["properties"] is JsonObject)
        {
            var result = new JsonObject();
            var properties = schema["properties"] as JsonObject;
            var required = (schema["required"] as JsonArray)?.Select(value => value?.GetValue<string>() ?? "")
                .Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal) ?? [];
            if (properties is not null)
                foreach (var property in properties.Where(value => required.Contains(value.Key)))
                    if (property.Value is JsonObject propertySchema)
                        result[property.Key] = Example(propertySchema, root, variant, depth + 1);
            var minimumProperties = schema["minProperties"]?.GetValue<int>() ?? 0;
            if (properties is not null && result.Count < minimumProperties)
                foreach (var property in properties.Where(value => !result.ContainsKey(value.Key)))
                {
                    if (property.Value is JsonObject propertySchema)
                        result[property.Key] = Example(propertySchema, root, variant, depth + 1);
                    if (result.Count >= minimumProperties) break;
                }
            if (result.Count < minimumProperties && schema["additionalProperties"] is JsonObject additional)
                while (result.Count < minimumProperties)
                    result[$"property{result.Count}"] = Example(additional, root, result.Count, depth + 1);
            return result;
        }
        if (type == "array")
        {
            var result = new JsonArray();
            var minimum = schema["minItems"]?.GetValue<int>() ?? 0;
            if (schema["items"] is JsonObject item)
                for (var index = 0; index < minimum; index++)
                    result.Add(Example(item, root, index, depth + 1));
            return result;
        }
        if (type == "boolean") return JsonValue.Create(false)!;
        if (type == "integer")
            return JsonValue.Create(schema["minimum"]?.GetValue<int>() ?? 0)!;
        if (type == "number")
            return JsonValue.Create(schema["minimum"]?.GetValue<double>() ?? 0)!;
        var minimumLength = schema["minLength"]?.GetValue<int>() ?? 1;
        var maximumLength = schema["maxLength"]?.GetValue<int>() ?? 64;
        var suffix = variant == 0 ? "" : variant.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var length = Math.Clamp(Math.Max(minimumLength, suffix.Length + 1), 1, Math.Min(maximumLength, 64));
        var text = new string('x', length);
        if (suffix.Length > 0 && suffix.Length < text.Length)
            text = text[..^suffix.Length] + suffix;
        return JsonValue.Create(text)!;
    }

    private static JsonObject ResolveLocalReference(JsonObject root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            throw new ArgumentException("Capability examples support only local JSON Schema references.", nameof(reference));
        JsonNode? current = root;
        foreach (var rawSegment in reference[2..].Split('/'))
        {
            var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current is JsonObject objectValue && objectValue.TryGetPropertyValue(segment, out var next)
                ? next
                : null;
            if (current is null)
                throw new ArgumentException($"Capability schema reference '{reference}' cannot be resolved.", nameof(reference));
        }
        return current as JsonObject
            ?? throw new ArgumentException($"Capability schema reference '{reference}' does not target an object schema.", nameof(reference));
    }

    private static string Normalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 262_144)
            throw new ArgumentException("A capability schema is empty or too large.", nameof(json));
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new ArgumentException("A capability schema must be a JSON object.", nameof(json));
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool JsonObject(string value) { try { return JsonNode.Parse(value) is JsonObject; } catch (JsonException) { return false; } }
    private static bool ValidJsonValue(string value) { try { return JsonNode.Parse(value) is not null; } catch (JsonException) { return false; } }
    private static bool Identifier(string value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && value == value.Trim() && !value.Any(char.IsControl);
    private static bool FingerprintOrIdentifier(string value) => Identifier(value, 240);
    private static bool OpaqueKey(string? value) => value is { Length: > 0 and <= 200 }
        && value == value.Trim() && value.Split('.').All(segment => segment.Length > 0
            && char.IsAsciiLetter(segment[0])
            && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
    private static bool Text(string value, int maximum) => Identifier(value, maximum);
    private static bool Pointer(string value)
    {
        if (value.Length > 1_000 || value.Any(char.IsControl)) return false;
        if (value == "") return true;
        if (!value.StartsWith("/", StringComparison.Ordinal)) return false;
        foreach (var segment in value.Split('/').Skip(1))
        {
            if (segment.Contains('~') && segment.Replace("~0", "", StringComparison.Ordinal)
                    .Replace("~1", "", StringComparison.Ordinal).Contains('~')) return false;
            var decoded = segment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (decoded is "__proto__" or "prototype" or "constructor") return false;
        }
        return true;
    }
}
