using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Capabilities;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.CatalogNavigation;

/// <summary>
/// Projects application-owned mechanic and query records into the common discovery contract.
/// The active application catalog remains authoritative for identity, lifecycle, and executable
/// content; this adapter only supplies a transport-neutral description of that record.
/// </summary>
public static class ApplicationCapabilityContractAdapter
{
    private const string EmptyInputSchema = """
        {"type":"object","additionalProperties":false}
        """;
    private const string GenericMechanicInputSchema = """
        {"type":"object","maxProperties":64}
        """;
    private const string GenericMechanicOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["narration","data","effects","events","notifications"],"properties":{"narration":{"type":"string"},"data":{},"effects":{"type":"array","items":{}},"events":{"type":"array","items":{}},"notifications":{"type":"array","items":{}}}}
        """;

    public static CapabilityContractDescriptor Create(
        ApplicationIdentifier applicationId,
        CatalogRecordDefinition record,
        string? stateSpaceId = null)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(record);
        return record.Kind switch
        {
            "mechanic" => CreateMechanic(applicationId, record.QualifiedId, record.Name,
                record.Description, record.Version, record.ContentFingerprint, record.Status,
                record.ContentJson, stateSpaceId),
            ApplicationQueryContract.CatalogKind => CreateQuery(applicationId, record, stateSpaceId),
            _ => throw new ArgumentException("Only application mechanics and queries are capabilities.", nameof(record))
        };
    }

    public static CapabilityContractDescriptor CreateMechanic(
        ApplicationIdentifier applicationId,
        string qualifiedId,
        string name,
        string description,
        int version,
        string contentFingerprint,
        string status,
        string contractJson,
        string? stateSpaceId = null)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        using var document = JsonDocument.Parse(contractJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString())
            || !root.TryGetProperty("requirements", out var requirementsElement)
            || requirementsElement.ValueKind != JsonValueKind.String)
            throw new ArgumentException("The mechanic record does not contain an exact capability contract.", nameof(contractJson));
        var requirements = MechanicRequirements.Parse(requirementsElement.GetString()!);
        if (requirements.Event is not null)
            throw new ArgumentException("Event middleware mechanics are not direct application capabilities.", nameof(contractJson));
        var roles = requirements.Roles.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new CapabilityRoleContract(value.Key, !value.Value.Optional,
                string.IsNullOrWhiteSpace(value.Value.Description) ? $"Application role '{value.Key}'." : value.Value.Description,
                value.Value.Components.Concat(value.Value.OptionalComponents ?? [])
                    .Concat(value.Value.ContentComponentIds ?? [])
                    .Concat((value.Value.ComponentReferences ?? []).Select(item => item.SourceComponentId))
                    .Concat((value.Value.ComponentReferences ?? []).SelectMany(item => item.TargetComponentIds))
                    .Concat((value.Value.ComponentReferences ?? []).SelectMany(item => item.OptionalTargetComponentIds ?? []))
                    .Concat((value.Value.RelationshipComponents ?? []).SelectMany(item => item.TargetComponentIds))
                    .Concat((value.Value.RelationshipComponents ?? []).SelectMany(item => item.OptionalTargetComponentIds ?? []))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        var inputSchema = requirements.InputSchema is JsonElement authoredInput
            ? authoredInput.GetRawText()
            : GenericMechanicInputSchema;
        var inputStatus = requirements.InputSchema is null
            ? CapabilityContractSchemaStatus.Generic
            : CapabilityContractSchemaStatus.Authored;
        var example = CapabilityContractBuilder.MinimalExample(inputSchema);
        var invalidExample = CapabilityContractBuilder.MinimalInvalidExample(inputSchema);
        return CapabilityContractBuilder.Create(
            qualifiedId, version, "application-mechanic", contentFingerprint, applicationId.Value,
            name, description, Lifecycle(status), new(true, true, true),
            inputSchema, inputStatus,
            GenericMechanicOutputSchema, CapabilityContractSchemaStatus.Generated,
            Scope(stateSpaceId), roles,
            new("interaction-authorization", "execute-application-action", "application-state"),
            true, true, ["procedure.system.use"],
            [
                new("valid-minimal", example),
                new("invalid-unknown-property", invalidExample, ExpectedValid: false)
            ],
            [
                new("APPLICATION_ACTION_INPUT_INVALID", "The mechanic input is not one bounded JSON object.", "Correct the input and prepare the action again."),
                new("MECHANIC_CONTRACT_STALE", "The selected mechanic no longer matches the active application catalog.", "Rediscover the current mechanic before preparing it again.")
            ],
            [new(qualifiedId, "Rediscover and prepare the current mechanic contract.", example)]);
    }

    private static CapabilityContractDescriptor CreateQuery(
        ApplicationIdentifier applicationId,
        CatalogRecordDefinition record,
        string? stateSpaceId)
    {
        var query = ApplicationQueryContract.Parse(record.ContentJson, applicationId);
        var roles = query.Roles.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new CapabilityRoleContract(value.Key, true, value.Value, []))
            .ToArray();
        return CapabilityContractBuilder.Create(
            query.Id, record.Version, "application-query", record.ContentFingerprint,
            applicationId.Value, query.Name, query.Description, Lifecycle(query.Status),
            new(true, false, false), EmptyInputSchema, CapabilityContractSchemaStatus.Authored,
            query.OutputSchemaJson, CapabilityContractSchemaStatus.Authored,
            Scope(stateSpaceId), roles,
            new(query.Exposure == ApplicationQueryExposure.ModelVisible ? "model-visible-query" : "binding-only-query",
                "read-application-state", "application-state"),
            false, false, ["procedure.system.inspect"],
            [
                new("valid-read", "{}"),
                new("invalid-unknown-property", "{\"__unexpected\":true}", ExpectedValid: false)
            ],
            [new("APPLICATION_QUERY_STALE", "The selected query no longer matches the active application catalog.",
                "Rediscover the current query and retry the read.")],
            [new(query.Id, "Rediscover and run the current query contract.", "{}")]);
    }

    private static CapabilityScopeContract Scope(string? stateSpaceId) => new(
        "application-state-space", true, true,
        string.IsNullOrWhiteSpace(stateSpaceId)
            ? ["applicationId", "stateSpaceId"]
            : ["applicationId", "stateSpaceId", $"bound:{stateSpaceId}"]);

    private static string Lifecycle(string status) => status switch
    {
        "active" => CapabilityContractLifecycle.Active,
        "draft" => CapabilityContractLifecycle.Draft,
        "deprecated" => CapabilityContractLifecycle.Deprecated,
        "retired" or "archived" => CapabilityContractLifecycle.Retired,
        _ => throw new ArgumentException("The capability lifecycle status is not supported.", nameof(status))
    };
}
