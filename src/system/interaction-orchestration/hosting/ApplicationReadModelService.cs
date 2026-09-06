using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Interactions;

internal sealed class ApplicationReadModelService(
    IPublicApplicationCatalogProvider catalogs,
    IApplicationActivationReader activations,
    IStateSpaceRegistry stateSpaces,
    IApplicationMechanicProjectionMappingResolver mappings,
    IApplicationMechanicEvaluator evaluator,
    IBoundedJsonSchemaValidator schemas,
    ObjectProjectionInteractionQueryExecutor? objectQueries = null) : IApplicationReadModelService
{
    public async Task<ApplicationReadModelResult> ReadAsync(
        ApplicationReadModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        if (!Token(request.StateSpaceId) || !Token(request.QualifiedQueryId)
            || request.RoleBindings is null || request.RoleBindings.Count > 32
            || request.RoleBindings.Any(value => !Token(value.Key) || !Token(value.Value))
            || request.Audience is not null && !request.Audience.IsValid)
            throw Failure("READ_MODEL_REQUEST_INVALID", "The read-model request is invalid or unbounded.");

        var stateSpace = stateSpaces.Get(request.StateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != request.ApplicationId)
            throw Failure("READ_MODEL_STATE_SPACE_UNKNOWN",
                "The read-model state space is unavailable for this application.");
        var activation = activations.Current(request.ApplicationId);
        if (activation is null
            || activation.ActivationFingerprint != stateSpace.ManifestFingerprint
            || activation.ResolutionFingerprint != stateSpace.ResolutionFingerprint)
            throw Failure("READ_MODEL_STATE_SPACE_STALE",
                "The state space is not bound to the current application resolution.");
        if (!catalogs.TryGet(request.ApplicationId, out var catalog))
            throw Failure("READ_MODEL_CATALOG_UNAVAILABLE",
                "The current application catalog is unavailable.");

        CatalogRecordView queryRecord;
        try
        {
            queryRecord = catalog.Inspect(new(request.ApplicationId, request.ApplicationId.Value,
                request.QualifiedQueryId));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw Failure("READ_MODEL_UNKNOWN", "The requested read model is unavailable.");
        }
        if (queryRecord.Summary.Kind != ApplicationQueryContract.CatalogKind
            || queryRecord.Summary.Status != "active")
            throw Failure("READ_MODEL_INACTIVE", "The requested read model is not active.");

        ApplicationQueryContract contract;
        try
        {
            contract = ApplicationQueryContract.Parse(queryRecord.ContentJson, request.ApplicationId);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw Failure("READ_MODEL_CONTRACT_INVALID",
                "The requested read-model contract is invalid.", exception);
        }
        if (!contract.Roles.Keys.Order(StringComparer.Ordinal)
            .SequenceEqual(request.RoleBindings.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw Failure("READ_MODEL_ROLES_INVALID",
                "The read-model request does not bind its exact declared roles.");

        var input = ApplicationReadModelInput.Normalize(request.InputJson);
        if (contract.InputSchemaJson is null)
        {
            if (input != "{}")
                throw Failure("READ_MODEL_INPUT_INVALID", "The request is invalid.");
        }
        else
        {
            var inputSchema = schemas.Compile(contract.InputSchemaJson);
            if (!inputSchema.IsAccepted || schemas.Validate(inputSchema.ProfileId,
                    inputSchema.NormalizedSchema, input).Status != SchemaValueStatus.Valid)
                throw Failure("READ_MODEL_INPUT_INVALID", "The request is invalid.");
        }

        if (contract.Executor == ApplicationQueryContract.ObjectProjectionExecutor)
        {
            if (objectQueries is null)
                throw Failure("READ_MODEL_UNAVAILABLE", "The registered object reader is unavailable.");
            var outputSchema = schemas.Compile(contract.OutputSchemaJson);
            if (!outputSchema.IsAccepted)
                throw Failure("READ_MODEL_SCHEMA_STALE", "The read-model output schema is invalid.");
            InteractionQueryExecutionResult projection;
            try
            {
                projection = await objectQueries.ExecuteAsync(new(
                    request.StateSpaceId,
                    request.ApplicationId,
                    request.QualifiedQueryId,
                    new InteractionQueryContractReference(
                        contract.Executor,
                        contract.ProjectionQualifiedId,
                        contract.ProjectionVersion,
                        contract.ProjectionContentHash,
                        outputSchema.SchemaHash,
                        outputSchema.NormalizedSchema,
                        contract.Exposure,
                        contract.Roles.Keys,
                        contract.ObjectCollectionId),
                    request.RoleBindings,
                    request.Audience,
                    request.Cursor,
                    request.PageSize), cancellationToken);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw Failure("READ_MODEL_FORBIDDEN", "The requested view is unavailable.", exception);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("CURSOR", StringComparison.Ordinal))
            {
                throw Failure("READ_MODEL_SOURCE_STALE", "The view changed. Refresh to continue.", exception);
            }
            if (schemas.Validate(outputSchema.ProfileId, outputSchema.NormalizedSchema,
                    projection.OutputJson).Status != SchemaValueStatus.Valid)
                throw Failure("READ_MODEL_OUTPUT_INVALID", "The registered object returned data outside its query schema.");
            return new(request.ApplicationId.Value, request.StateSpaceId, request.QualifiedQueryId,
                stateSpace.ManifestFingerprint, stateSpace.ResolutionFingerprint, outputSchema.SchemaHash,
                projection.ResultFingerprint, projection.SourceRevisionFingerprint, projection.OutputJson);
        }
        if (contract.Executor != ApplicationQueryContract.MechanicProjectionExecutor)
            throw Failure("READ_MODEL_EXECUTOR_UNSUPPORTED",
                "The requested read model is not backed by a supported projection.");

        CatalogRecordView mechanicRecord;
        try
        {
            mechanicRecord = catalog.Inspect(new(request.ApplicationId, request.ApplicationId.Value,
                contract.ProjectionQualifiedId));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw Failure("READ_MODEL_PROJECTION_UNKNOWN",
                "The exact catalog projection is unavailable.");
        }
        if (mechanicRecord.Summary.Kind != "mechanic" || mechanicRecord.Summary.Status != "active"
            || mechanicRecord.Summary.Version != contract.ProjectionVersion
            || mechanicRecord.Summary.ContentFingerprint != contract.ProjectionContentHash)
            throw Failure("READ_MODEL_PROJECTION_STALE",
                "The registered read model does not match the active catalog projection.");

        MechanicRequirements requirements;
        try
        {
            using var document = JsonDocument.Parse(mechanicRecord.ContentJson);
            if (!document.RootElement.TryGetProperty("requirements", out var declared)
                || declared.ValueKind != JsonValueKind.String)
                throw new JsonException();
            requirements = MechanicRequirements.Parse(declared.GetString()!);
        }
        catch (JsonException exception)
        {
            throw Failure("READ_MODEL_PROJECTION_INVALID",
                "The catalog projection requirements are invalid.", exception);
        }
        if (requirements.Event is not null
            || requirements.ProjectionProblems().Count > 0
            || requirements.CompositionProblems().Count > 0)
            throw Failure("READ_MODEL_PROJECTION_INVALID",
                "The catalog projection is not a valid read-only projection.");
        if (contract.InputSchemaJson is not null && (requirements.InputSchema is null
            || InteractionCanonicalJson.CanonicalizeObject(requirements.InputSchema.Value.GetRawText())
                != InteractionCanonicalJson.CanonicalizeObject(contract.InputSchemaJson)))
            throw Failure("READ_MODEL_CONTRACT_INVALID", "The query and projection input contracts disagree.");

        var mapping = await mappings.ResolveAsync(request.StateSpaceId, request.ApplicationId,
            mechanicRecord.Summary.QualifiedId, requirements, cancellationToken);
        if (!mapping.Resolved)
            throw Failure(mapping.Problems.FirstOrDefault()?.Code ?? "READ_MODEL_MAPPING_FAILED",
                mapping.Problems.FirstOrDefault()?.SafeMessage
                    ?? "The read-model projection could not resolve its installed component mapping.");

        var evaluation = await evaluator.EvaluateAsync(new(
            request.StateSpaceId,
            request.ApplicationId,
            mechanicRecord.Summary.QualifiedId,
            mechanicRecord.Summary.ContentFingerprint,
            mapping.Mapping!,
            request.RoleBindings,
            input,
            0,
            Audience: request.Audience, ReadModelQueryId: request.QualifiedQueryId), cancellationToken);
        if (!evaluation.Ok || evaluation.Run is null || evaluation.Projection is null)
        {
            var safeCode = evaluation.Problems.FirstOrDefault();
            if (requirements.AuthorizedContext is not null && safeCode is
                ("READ_MODEL_FORBIDDEN" or "READ_MODEL_UNAVAILABLE" or "READ_MODEL_SELECTION_UNAVAILABLE"
                 or "READ_MODEL_SOURCE_STALE" or "READ_MODEL_INPUT_INVALID"))
                throw Failure(safeCode, "The requested view is unavailable.");
            throw Failure("READ_MODEL_EVALUATION_FAILED",
                "The catalog projection could not produce a read model.");
        }
        var output = evaluation.Run.Output;
        if (!output.HasData || output.Effects.Count != 0 || output.Events.Count != 0
            || output.Notifications.Count != 0 || evaluation.Proposal.Effects.Count != 0
            || evaluation.Proposal.Events.Count != 0 || evaluation.Proposal.Notifications.Count != 0)
            throw Failure("READ_MODEL_OUTPUT_UNSAFE",
                "A read model must return structured data without effects, events, or notifications.");

        var compilation = schemas.Compile(contract.OutputSchemaJson);
        if (!compilation.IsAccepted || compilation.SchemaHash != contract.OutputSchemaHash)
            throw Failure("READ_MODEL_SCHEMA_STALE",
                "The read-model output schema is invalid or does not match its registered fingerprint.");
        string data;
        try
        {
            data = InteractionCanonicalJson.CanonicalizeObject(output.Data);
        }
        catch (InteractionContractException exception)
        {
            throw Failure("READ_MODEL_OUTPUT_INVALID",
                "The catalog projection did not return one bounded JSON object.", exception);
        }
        if (schemas.Validate(compilation.ProfileId, compilation.NormalizedSchema, data).Status
            != SchemaValueStatus.Valid)
            throw Failure("READ_MODEL_OUTPUT_INVALID",
                "The catalog projection returned data outside its closed schema.");

        var resultFingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionQueryFingerprintDomains.Result,
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                request.QualifiedQueryId,
                contract.ProjectionQualifiedId,
                contract.ProjectionVersion,
                contract.ProjectionContentHash,
                contract.OutputSchemaHash,
                output = JsonSerializer.Deserialize<JsonElement>(data)
            })));
        var sourceRevisionJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            components = evaluation.Projection.ComponentRevisions
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(entity => new
                {
                    entityId = entity.Key,
                    revisions = entity.Value.OrderBy(value => value.Key, StringComparer.Ordinal)
                        .Select(value => new { componentId = value.Key, revision = value.Value })
                }),
            containments = evaluation.Projection.ContainmentRevisions
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(container => new
                {
                    containerId = container.Key,
                    entries = container.Value.OrderBy(value => value.EntityId, StringComparer.Ordinal)
                        .Select(value => new { value.EntityId, value.Slot, value.Revision })
                })
        }));
        var sourceFingerprint = evaluation.Projection.AuthorizedSourceRevision
            ?? InteractionCanonicalJson.Fingerprint(
                InteractionQueryFingerprintDomains.SourceRevisions, sourceRevisionJson);
        using var inputDocument = JsonDocument.Parse(input);
        if (requirements.AuthorizedContext is not null &&
            inputDocument.RootElement.TryGetProperty("expectedSourceRevision", out var expected)
            && expected.ValueKind != JsonValueKind.Null && expected.GetString() != sourceFingerprint)
            throw Failure("READ_MODEL_SOURCE_STALE", "The view changed. Refresh to continue.");

        return new(request.ApplicationId.Value, request.StateSpaceId, request.QualifiedQueryId,
            stateSpace.ManifestFingerprint, stateSpace.ResolutionFingerprint,
            contract.OutputSchemaHash, resultFingerprint, sourceFingerprint, data);
    }

    private static bool Token(string? value) => value is { Length: >= 1 and <= 200 }
        && value == value.Trim() && !value.Any(char.IsControl);

    private static ApplicationReadModelException Failure(
        string code, string message, Exception? inner = null) =>
        new(code, message, inner);
}
