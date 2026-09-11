using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemCapabilities;

internal sealed class ApplicationCandidateCatalogCompareCapabilityHandler(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    ICatalogSynchronizationService synchronization) : ISystemReadCapabilityHandler
{
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.ApplicationCandidateCatalogCompare, 1, "catalog-synchronization",
        "Compare a bounded selected set of authored catalog records with the current application database and common ancestor.",
        SystemCapabilityMode.Read, InputSchema, ApplicationCandidateCapabilitySchemas.InspectOutput,
        ["procedure.system.application-candidate.catalog-compare"], PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.PrivateOperatorMetadata, false, false);

    public Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input, CancellationToken cancellationToken = default) =>
        Task.FromResult(ApplicationCandidateCapabilitySchemas.ReadFailure("APPLICATION_CONTEXT_REQUIRED"));

    public async Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input, SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wire = ApplicationCandidateCapabilitySchemas.Deserialize<CompareWire>(input);
            var applicationId = ApplicationIdentifier.Parse(wire.ApplicationId);
            var request = new CatalogSynchronizationCompareRequest(wire.AllowedRootId,
                wire.ExpectedActiveFingerprint, wire.Records.Select(value => new CatalogSynchronizationRecordSelection(
                    Kind(value.Kind), value.Id)).ToArray());
            var command = ApplicationCandidateCapabilityHost.ReadCommand(
                SystemCapabilityIds.ApplicationCandidateCatalogCompare, context, input);
            var selection = await ApplicationCandidateCapabilityHost.CreateAsync(
                db, applications, context, applicationId,
                [StandingGrantCapability.Read, StandingGrantCapability.Author], command,
                InteractionExecutionProfile.Atomic, 1,
                grant => Covers(grant.Definitions, request.Records), cancellationToken);
            if (selection.Hosts.Count == 0)
                return ApplicationCandidateCapabilitySchemas.ReadFailure(selection.Code);

            var result = await synchronization.CompareAsync(selection.Hosts[0], request, cancellationToken);
            if (result.Tag == InteractionInvocationResultTag.Completed && result.DataJson is not null
                && result.CompletionEvidenceReference is not null)
                return SystemCapabilityHandlerResult.Success(JsonSerializer.SerializeToElement(new
                {
                    status = result.WireTag,
                    code = result.Code,
                    message = result.SafeMessage,
                    dataJson = result.DataJson,
                    evidenceReference = result.CompletionEvidenceReference
                }));
            return ApplicationCandidateCapabilitySchemas.ReadFailure(result.Code, result.SafeMessage);
        }
        catch (OperationCanceledException) { throw; }
        catch (InteractionContractException exception)
        {
            return ApplicationCandidateCapabilitySchemas.ReadFailure(exception.Code,
                "The catalog comparison request is invalid.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return ApplicationCandidateCapabilitySchemas.ReadFailure("INVALID_PAYLOAD");
        }
    }

    private static CatalogRecordKind Kind(string value) => value switch
    {
        "mechanic" => CatalogRecordKind.Mechanic,
        "procedure" => CatalogRecordKind.Procedure,
        _ => throw new ArgumentException("The selected catalog record kind is unsupported.")
    };

    private static bool Covers(StandingGrantDefinitionAllowance allowance,
        IReadOnlyList<CatalogSynchronizationRecordSelection> records)
    {
        if (allowance.Mode == StandingGrantDefinitionMode.ExactIds)
            return records.All(record => allowance.ExactIds.Contains(record.Id, StringComparer.Ordinal));
        return records.All(record =>
        {
            var kind = record.Kind == CatalogRecordKind.Mechanic
                ? CatalogNamespaceKinds.Mechanic
                : CatalogNamespaceKinds.Procedure;
            var namespaceId = CatalogNamespaceIdentity.NamespaceOf(record.Id);
            return allowance.ApplicationOwnedNamespaces.Any(boundary =>
                boundary.DefinitionKinds.Contains(kind, StringComparer.Ordinal)
                && (boundary.NamespaceId == namespaceId || boundary.IncludeDescendants
                    && namespaceId.StartsWith(boundary.NamespaceId + ".", StringComparison.Ordinal)));
        });
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record CompareWire(
        [property: JsonRequired] string ApplicationId,
        [property: JsonRequired] string AllowedRootId,
        [property: JsonRequired] string? ExpectedActiveFingerprint,
        [property: JsonRequired] IReadOnlyList<RecordWire> Records);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RecordWire(
        [property: JsonRequired] string Kind,
        [property: JsonRequired] string Id);

    internal const string InputSchema = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"applicationId\",\"allowedRootId\",\"expectedActiveFingerprint\",\"records\"],\"properties\":{\"applicationId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":63},\"allowedRootId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"expectedActiveFingerprint\":{\"anyOf\":[{\"type\":\"string\",\"minLength\":64,\"maxLength\":64},{\"type\":\"null\"}]},\"records\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":16,\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"kind\",\"id\"],\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"mechanic\",\"procedure\"]},\"id\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}}}}}}";
}
