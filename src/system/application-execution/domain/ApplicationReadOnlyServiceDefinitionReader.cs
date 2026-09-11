using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>Parses a service declaration from an exact host-resolved mechanic record, never invocation input.</summary>
public sealed class ApplicationReadOnlyServiceDefinitionReader(IBoundedJsonSchemaValidator schemas)
    : IApplicationReadOnlyServiceDefinitionReader
{
    public ApplicationReadOnlyServiceDefinition ReadRetained(
        SystemTaskSelectedDefinition selectedDefinition, CatalogRecordView retainedMechanic)
    {
        ArgumentNullException.ThrowIfNull(selectedDefinition);
        ArgumentNullException.ThrowIfNull(retainedMechanic);
        var record = retainedMechanic.Summary;
        if (record.Kind != "mechanic" || record.QualifiedId != selectedDefinition.ExactDefinitionId
            || record.Version != selectedDefinition.Version || record.ContentFingerprint != selectedDefinition.Fingerprint
            || retainedMechanic.ContentJson.Length > CatalogNavigationLimits.MaximumContentLength
            || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(retainedMechanic.ContentJson))) != selectedDefinition.Fingerprint)
            throw Invalid("SERVICE_DEFINITION_STALE", "The selected mechanic does not match retained content.");
        try
        {
            using var content = JsonDocument.Parse(retainedMechanic.ContentJson,
                new JsonDocumentOptions { MaxDepth = InteractionContractLimits.JsonDepth });
            RejectDuplicates(content.RootElement);
            var requirementsJson = String(content.RootElement, "requirements");
            using var requirements = JsonDocument.Parse(InteractionCanonicalJson.CanonicalizeObject(requirementsJson));
            if (!requirements.RootElement.TryGetProperty("service", out var declaration))
                throw Invalid("SERVICE_DECLARATION_UNAVAILABLE", "The selected mechanic declares no read-only service.");
            // Other mechanic requirements stay with their existing owner; only this closed property
            // is interpreted here. Canonicalization checks the entire declaration's byte/depth bound.
            using var bounded = JsonDocument.Parse(InteractionCanonicalJson.CanonicalizeObject(declaration.GetRawText()));
            var value = bounded.RootElement;
            ExactService(value);
            var reads = value.GetProperty("reads");
            if (reads.ValueKind != JsonValueKind.Array || reads.GetArrayLength() > ApplicationReadOnlyServiceLimits.MaximumReads)
                throw Invalid();
            var actions = value.TryGetProperty("actions", out var declaredActions) ? declaredActions : default;
            if (actions.ValueKind != JsonValueKind.Undefined
                && (actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() > ApplicationReadOnlyServiceLimits.MaximumActions))
                throw Invalid();
            return new(String(value, "inputSchemaHash"), String(value, "inputSchemaJson"),
                String(value, "outputSchemaHash"), String(value, "outputSchemaJson"),
                reads.EnumerateArray().Select(ReadDeclaration).ToArray(), schemas,
                actions.ValueKind == JsonValueKind.Array
                    ? actions.EnumerateArray().Select(ActionDeclaration).ToArray()
                    : []);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw Invalid();
        }
    }

    private static ApplicationServiceActionDeclaration ActionDeclaration(JsonElement value)
    {
        Exact(value, "alias", "qualifiedMechanicId", "mechanicVersion", "contentFingerprint", "roleMappings");
        if (!value.GetProperty("mechanicVersion").TryGetInt32(out var version)) throw Invalid();
        var mappings = value.GetProperty("roleMappings");
        if (mappings.ValueKind != JsonValueKind.Object) throw Invalid();
        return new(String(value, "alias"), String(value, "qualifiedMechanicId"), version,
            String(value, "contentFingerprint"), mappings.EnumerateObject().ToDictionary(
                property => property.Name, property => Text(property.Value), StringComparer.Ordinal));
    }

    private static void ExactService(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        var expected = new[] { "inputSchemaHash", "inputSchemaJson", "outputSchemaHash", "outputSchemaJson", "reads" };
        if (names.Length == expected.Length && !names.Except(expected, StringComparer.Ordinal).Any()) return;
        var withActions = expected.Append("actions").ToArray();
        if (names.Length != withActions.Length || names.Except(withActions, StringComparer.Ordinal).Any()) throw Invalid();
    }

    private ApplicationServiceReadDeclaration ReadDeclaration(JsonElement value)
    {
        Exact(value, "alias", "qualifiedQueryId", "contract", "roleMappings");
        var contract = value.GetProperty("contract");
        Exact(contract, "executor", "projectionQualifiedId", "projectionVersion", "projectionContentHash",
            "outputSchemaHash", "outputSchemaJson", "exposure", "roles", "collectionId");
        if (!contract.GetProperty("projectionVersion").TryGetInt32(out var version)
            || !contract.GetProperty("exposure").TryGetInt32(out var exposure)) throw Invalid();
        var roles = contract.GetProperty("roles");
        if (roles.ValueKind != JsonValueKind.Array || roles.GetArrayLength() > InteractionContractLimits.RoleHints)
            throw Invalid();
        var collection = contract.GetProperty("collectionId");
        if (collection.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) throw Invalid();
        var reference = new InteractionQueryContractReference(String(contract, "executor"),
            String(contract, "projectionQualifiedId"), version, String(contract, "projectionContentHash"),
            String(contract, "outputSchemaHash"), String(contract, "outputSchemaJson"),
            (ApplicationQueryExposure)exposure, roles.EnumerateArray().Select(Text),
            collection.ValueKind == JsonValueKind.Null ? null : collection.GetString());
        var mappings = value.GetProperty("roleMappings");
        if (mappings.ValueKind != JsonValueKind.Object) throw Invalid();
        return new(String(value, "alias"), String(value, "qualifiedQueryId"), reference,
            mappings.EnumerateObject().ToDictionary(property => property.Name,
                property => Text(property.Value), StringComparer.Ordinal), schemas, String(contract, "outputSchemaJson"));
    }

    private static void Exact(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != fields.Length || names.Except(fields, StringComparer.Ordinal).Any()) throw Invalid();
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw Invalid("DUPLICATE_JSON_PROPERTY", "The retained mechanic contains duplicate properties.");
            foreach (var property in properties) RejectDuplicates(property.Value);
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    private static string String(JsonElement value, string name) => Text(value.GetProperty(name));
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw Invalid();
    private static InteractionContractException Invalid(string code = "INVALID_SERVICE_DECLARATION",
        string message = "The retained service declaration is not a valid closed contract.") => new(code, message);
}
