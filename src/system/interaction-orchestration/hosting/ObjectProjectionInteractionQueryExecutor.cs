using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Projections;

namespace DantesRoleplay.Interactions;

/// <summary>Executes one exact registered object through the shared prepared read engine.</summary>
internal sealed class ObjectProjectionInteractionQueryExecutor(IProjectionMaterializer materializer)
    : IInteractionQueryExecutor
{
    public string Kind => ApplicationQueryContract.ObjectProjectionExecutor;

    public async Task<InteractionQueryExecutionResult> ExecuteAsync(
        InteractionQueryExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        ArgumentNullException.ThrowIfNull(request.Contract);
        if (request.Contract.Executor != Kind || request.Contract.CollectionId is null
            || !request.Contract.ProjectionQualifiedId.StartsWith(request.ApplicationId.Value + ".", StringComparison.Ordinal)
            || request.RoleBindings.Keys.Any(role => !request.Contract.Roles.Contains(role, StringComparer.Ordinal)))
            throw new InteractionContractException("QUERY_EXECUTION_SCOPE_INVALID",
                "The object query does not match its application, collection, or declared roles.");

        var projection = await materializer.MaterializeAsync(new(
            request.StateSpaceId,
            new(request.Contract.ProjectionQualifiedId, request.Contract.ProjectionVersion,
                request.Contract.ProjectionContentHash),
            request.RoleBindings), cancellationToken);
        if (projection.Projection.QualifiedId != request.Contract.ProjectionQualifiedId
            || projection.Projection.Version != request.Contract.ProjectionVersion
            || projection.Projection.ContentHash != request.Contract.ProjectionContentHash)
            throw new InteractionContractException("QUERY_OBJECT_STALE",
                "The prepared engine returned a different object authority.");

        var output = InteractionCanonicalJson.Canonicalize(projection.OutputJson);
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
        var revisions = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            sources = projection.SourceRevisions.OrderBy(value => value.EntityId, StringComparer.Ordinal)
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
        return new(output, request.Contract.OutputSchemaHash, resultFingerprint,
            InteractionCanonicalJson.Fingerprint(InteractionQueryFingerprintDomains.SourceRevisions, revisions));
    }
}
