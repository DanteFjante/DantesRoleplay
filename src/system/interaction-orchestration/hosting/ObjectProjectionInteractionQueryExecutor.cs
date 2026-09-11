using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Projections;

namespace DantesRoleplay.Interactions;

/// <summary>Executes one exact registered object through the shared prepared read engine.</summary>
internal sealed class ObjectProjectionInteractionQueryExecutor(IProjectionCollectionMaterializer collections,
    IProjectionDefinitionRegistry? definitions = null,
    IProjectionMaterializer? roots = null)
    : IInteractionQueryExecutor
{
    public string Kind => ApplicationQueryContract.ObjectProjectionExecutor;

    public Task<InteractionQueryExecutionResult> ExecuteAsync(
        InteractionQueryExecutionRequest request,
        CancellationToken cancellationToken = default) => ExecuteCoreAsync(request, false, false, cancellationToken);

    // The public read-model service has already resolved the active query and audience.
    // This method is deliberately absent from the generic execution interface.
    internal Task<InteractionQueryExecutionResult> ExecuteReadAsync(
        InteractionQueryExecutionRequest request, bool fieldBased, bool includeEvidence = false,
        CancellationToken cancellationToken = default)
    {
        var definition = definitions?.Get(request.Contract.ProjectionQualifiedId, request.Contract.ProjectionVersion);
        if (definition?.ObjectContract is null || definition.ObjectContract.IsFieldBased != fieldBased)
            throw new InteractionContractException("QUERY_OBJECT_STALE",
                "The active query and registered object profiles do not agree.");
        return ExecuteCoreAsync(request, fieldBased, includeEvidence, cancellationToken);
    }

    private async Task<InteractionQueryExecutionResult> ExecuteCoreAsync(
        InteractionQueryExecutionRequest request, bool display, bool includeEvidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        ArgumentNullException.ThrowIfNull(request.Contract);
        if (request.Contract.Executor != Kind
            || !request.Contract.ProjectionQualifiedId.StartsWith(request.ApplicationId.Value + ".", StringComparison.Ordinal)
            || !request.Contract.Roles.Order(StringComparer.Ordinal)
                .SequenceEqual(request.RoleBindings.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InteractionContractException("QUERY_EXECUTION_SCOPE_INVALID",
                "The object query does not match its application or declared roles.");

        var definition = definitions?.Get(request.Contract.ProjectionQualifiedId, request.Contract.ProjectionVersion);
        if (display)
        {
            if (definition?.ObjectContract?.IsFieldBased != true || definition.Owner != request.ApplicationId ||
                definition.ContentHash != request.Contract.ProjectionContentHash ||
                definition.OutputSchemaHash != request.Contract.OutputSchemaHash)
                throw new InteractionContractException("QUERY_OBJECT_STALE",
                    "The active display query requires its registered field-based object authority.");
        }

        var perspective = request.Audience?.Perspective ?? "player";
        var reference = new ProjectionReference(request.Contract.ProjectionQualifiedId,
            request.Contract.ProjectionVersion, request.Contract.ProjectionContentHash);
        string outputJson;
        string sourceRevisionFingerprint;
        IReadOnlyList<ProjectionObservedSource> observedSources;
        IReadOnlyList<ProjectionMappedFieldEvidence> fields;
        ProjectionReference materializedReference;
        if (request.Contract.CollectionId is { } collectionId)
        {
            var projection = await collections.MaterializeAsync(new(
                request.StateSpaceId, reference, request.RoleBindings, collectionId, perspective,
                request.Cursor, request.PageSize)
            { Purpose = display ? ProjectionReadPurpose.Display : ProjectionReadPurpose.Exact }, cancellationToken);
            materializedReference = projection.Projection;
            outputJson = projection.OutputJson;
            sourceRevisionFingerprint = projection.SourceRevisionFingerprint;
            observedSources = projection.ObservedSources;
            fields = projection.Fields;
        }
        else
        {
            if (request.Cursor is not null || request.PageSize is not null)
                throw new InteractionContractException("QUERY_ROOT_PAGING_UNSUPPORTED",
                    "A scalar object query cannot carry collection paging parameters.");
            if (roots is null || definition?.ObjectContract is null || definition.Owner != request.ApplicationId
                || definition.ContentHash != request.Contract.ProjectionContentHash)
                throw new InteractionContractException("QUERY_OBJECT_STALE",
                    "The exact scalar object query is unavailable.");
            if (!definition.ObjectContract.Access.ReadPerspectives.Contains(perspective, StringComparer.Ordinal))
                throw new UnauthorizedAccessException(
                    "The scalar application object is unavailable to this perspective.");
            var projection = await roots.MaterializeAsync(new(
                request.StateSpaceId, reference, request.RoleBindings)
            { Purpose = display ? ProjectionReadPurpose.Display : ProjectionReadPurpose.Exact }, cancellationToken);
            materializedReference = projection.Projection;
            outputJson = projection.OutputJson;
            sourceRevisionFingerprint = Fingerprint(projection.SourceRevisions);
            observedSources = projection.ObservedSources;
            fields = projection.Fields;
        }
        if (materializedReference.QualifiedId != request.Contract.ProjectionQualifiedId
            || materializedReference.Version != request.Contract.ProjectionVersion
            || materializedReference.ContentHash != request.Contract.ProjectionContentHash)
            throw new InteractionContractException("QUERY_OBJECT_STALE",
                "The prepared engine returned a different object authority.");

        var output = InteractionCanonicalJson.Canonicalize(outputJson);
        var resultFingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionQueryFingerprintDomains.Result,
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                request.Contract.ProjectionQualifiedId,
                request.Contract.ProjectionVersion,
                request.Contract.ProjectionContentHash,
                request.Contract.CollectionId,
                request.Contract.OutputSchemaHash,
                output = JsonSerializer.Deserialize<JsonElement>(output)
            })));
        var evidence = display && includeEvidence
            ? definitions?.DiscoverRead(materializedReference, observedSources, fields, outputJson)
            : null;
        return new(output, request.Contract.OutputSchemaHash, resultFingerprint,
            sourceRevisionFingerprint)
        { ObjectReadEvidence = evidence };
    }

    private static string Fingerprint(IReadOnlyList<ProjectionSourceRevision> sources)
    {
        var revisions = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            sources = sources.OrderBy(value => value.EntityId, StringComparer.Ordinal)
                .ThenBy(value => value.Type.QualifiedTypeId, StringComparer.Ordinal)
                .Select(value => new
                {
                    value.EntityId,
                    value.Type.QualifiedTypeId,
                    value.Type.TypeVersion,
                    value.Type.SchemaHash,
                    value.Revision
                })
        }));
        return InteractionCanonicalJson.Fingerprint(
            InteractionQueryFingerprintDomains.SourceRevisions, revisions);
    }
}
