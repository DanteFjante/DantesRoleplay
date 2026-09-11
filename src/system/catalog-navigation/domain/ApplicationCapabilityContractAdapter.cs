using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Capabilities;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;

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
        => Create(applicationId, record, stateSpaceId, null);

    public static CapabilityContractDescriptor Create(
        ApplicationIdentifier applicationId,
        CatalogRecordDefinition record,
        string? stateSpaceId,
        ApplicationObjectDiscovery? objectDiscovery)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(record);
        if (objectDiscovery is not null && record.Kind != ApplicationQueryContract.CatalogKind)
            throw new ArgumentException("Object discovery applies only to an application query.", nameof(objectDiscovery));
        return record.Kind switch
        {
            "mechanic" => CreateMechanic(applicationId, record.QualifiedId, record.Name,
                record.Description, record.Version, record.ContentFingerprint, record.Status,
                record.ContentJson, stateSpaceId),
            ApplicationQueryContract.CatalogKind => CreateQuery(applicationId, record, stateSpaceId, objectDiscovery),
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
        string? stateSpaceId,
        ApplicationObjectDiscovery? objectDiscovery)
    {
        var query = ApplicationQueryContract.Parse(record.ContentJson, applicationId);
        var discovery = ObjectDiscovery(query, objectDiscovery);
        var roles = query.Roles.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new CapabilityRoleContract(value.Key, true, value.Value,
                discovery?.Sources.Where(source => source.Role == value.Key && source.Required)
                    .Select(source => source.QualifiedComponentId).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray() ?? []))
            .ToArray();
        var inputSchema = query.InputSchemaJson ?? EmptyInputSchema;
        var example = query.InputSchemaJson is null ? "{}" : CapabilityContractBuilder.MinimalExample(inputSchema);
        var invalidExample = query.InputSchemaJson is null ? "{\"__unexpected\":true}" : CapabilityContractBuilder.MinimalInvalidExample(inputSchema);
        return CapabilityContractBuilder.Create(
            query.Id, record.Version, "application-query", record.ContentFingerprint,
            applicationId.Value, query.Name, query.Description, Lifecycle(query.Status),
            new(true, false, false), inputSchema, CapabilityContractSchemaStatus.Authored,
            query.OutputSchemaJson, query.IsFieldBasedObject
                ? CapabilityContractSchemaStatus.Generated
                : CapabilityContractSchemaStatus.Authored,
            Scope(stateSpaceId), roles,
            new(query.Exposure == ApplicationQueryExposure.ModelVisible ? "model-visible-query" : "binding-only-query",
                "read-application-state", "application-state"),
            false, false, ["procedure.system.inspect"],
            [
                new("valid-read", example),
                new("invalid-unknown-property", invalidExample, ExpectedValid: false)
            ],
            [new("APPLICATION_QUERY_STALE", "The selected query no longer matches the active application catalog.",
                "Rediscover the current query and retry the read.")],
            [new(query.Id, "Rediscover and run the current query contract.", example)],
            objectDiscovery: discovery);
    }

    private static CapabilityObjectDiscoveryContract? ObjectDiscovery(
        ApplicationQueryContract query,
        ApplicationObjectDiscovery? discovery)
    {
        if (discovery is null) return null;
        var queryObject = new ProjectionReference(query.ProjectionQualifiedId,
            query.ProjectionVersion, query.ProjectionContentHash);
        if (!query.IsFieldBasedObject
            || discovery.ProfileId != RegisteredApplicationObjectContract.FieldBasedContractProfileId
            || discovery.Object != queryObject
            || discovery.Sources.Any(source => !query.Roles.ContainsKey(source.EntityRole)
                && !IsResolvedObjectSource(source.InputPath)))
            throw new ArgumentException("Object discovery must match the query's exact field-based object reference.", nameof(discovery));

        var sources = discovery.Sources.Select(source => new CapabilityObjectSourceContract(
            source.SourceId, source.InputPath, source.EntityRole, source.Required,
            source.Component.QualifiedTypeId, source.Component.TypeVersion,
            new(source.SchemaProfileId, source.SchemaJson, source.Component.SchemaHash,
                CapabilityContractSchemaStatus.Authored))).ToArray();
        var sourceIds = discovery.Sources.ToDictionary(source =>
            (Path: string.Join("\u001F", source.InputPath), source.EntityRole, source.Required, source.Component.QualifiedTypeId,
                source.Component.TypeVersion, source.Component.SchemaHash),
            source => source.SourceId);
        var fields = discovery.Fields.Select(field => new CapabilityObjectFieldContract(
            field.ObjectPointer,
            sourceIds[(string.Join("\u001F", field.InputPath), field.EntityRole, field.Required, field.Component.QualifiedTypeId,
                field.Component.TypeVersion, field.Component.SchemaHash)],
            field.ComponentPointer)).ToArray();
        var writes = discovery.Writes.GroupBy(value => value.ObjectPointer, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                string? sourceId = null;
                if (first.InputId is not null && first.SourcePointer is not null)
                {
                    var field = discovery.Fields.Single(value => value.ObjectPointer == first.ObjectPointer
                        && value.InputPath.Count == 1 && value.InputPath[0] == first.InputId
                        && value.ComponentPointer == first.SourcePointer);
                    sourceId = sourceIds[(string.Join("\u001F", field.InputPath), field.EntityRole, field.Required, field.Component.QualifiedTypeId,
                        field.Component.TypeVersion, field.Component.SchemaHash)];
                }
                if (group.Any(value => value.InputId != first.InputId
                        || value.SourcePointer != first.SourcePointer
                        || value.RelationshipId != first.RelationshipId))
                    throw new ArgumentException("Object discovery write provenance is ambiguous.", nameof(discovery));
                return new CapabilityObjectWritePathContract(group.Key,
                    group.Select(value => value.Operation).Order(StringComparer.Ordinal).ToArray(),
                    sourceId, first.SourcePointer, first.RelationshipId);
            }).ToArray();
        var roleBindings = query.RoleBindings?.Select(value => new CapabilityObjectRoleBindingContract(
            value.Key, value.Value.Source, value.Value.Pointer, value.Value.Key)).ToArray();
        return new(discovery.ProfileId, discovery.Object.QualifiedId, discovery.Object.Version,
            discovery.Object.ContentHash, sources, fields, writes)
        {
            RoleBindings = roleBindings
        };
    }

    private static bool IsResolvedObjectSource(IReadOnlyList<string> inputPath) =>
        inputPath.Count > 1 && (inputPath[0] != "collection"
            || inputPath.Count == 4 && inputPath[3] is "from" or "to");

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
