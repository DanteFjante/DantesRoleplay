using System.Collections.ObjectModel;
using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Projections;

namespace DantesRoleplay.Interactions;

internal sealed class InteractionQueryExecutorRegistry(IEnumerable<IInteractionQueryExecutor> executors)
    : IInteractionQueryExecutorRegistry
{
    private readonly IReadOnlyDictionary<string, IInteractionQueryExecutor> _executors = Build(executors);

    public bool TryGet(string kind, out IInteractionQueryExecutor executor) =>
        _executors.TryGetValue(kind, out executor!);

    private static IReadOnlyDictionary<string, IInteractionQueryExecutor> Build(
        IEnumerable<IInteractionQueryExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        var values = executors.ToArray();
        if (values.Any(value => value is null || string.IsNullOrWhiteSpace(value.Kind))
            || values.Select(value => value.Kind).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException("Interaction query executor registrations must have distinct bounded kinds.");
        return new ReadOnlyDictionary<string, IInteractionQueryExecutor>(values.ToDictionary(
            value => value.Kind, StringComparer.Ordinal));
    }
}

/// <summary>Read-only adapter over the existing exact structural projection materializer.</summary>
internal sealed class ProjectionInteractionQueryExecutor(IProjectionMaterializer materializer)
    : IInteractionQueryExecutor
{
    public string Kind => ApplicationQueryContract.ProjectionExecutor;

    public async Task<InteractionQueryExecutionResult> ExecuteAsync(
        InteractionQueryExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        ArgumentNullException.ThrowIfNull(request.Contract);
        if (request.Contract.Executor != Kind
            || !request.Contract.ProjectionQualifiedId.StartsWith(request.ApplicationId.Value + ".", StringComparison.Ordinal)
            || !request.Contract.Roles.Order(StringComparer.Ordinal)
                .SequenceEqual(request.RoleBindings.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InteractionContractException("QUERY_EXECUTION_SCOPE_INVALID",
                "The query request does not match its application, executor, or exact roles.");

        var projection = await materializer.MaterializeAsync(new(
            request.StateSpaceId,
            new(request.Contract.ProjectionQualifiedId, request.Contract.ProjectionVersion,
                request.Contract.ProjectionContentHash),
            request.RoleBindings), cancellationToken);
        if (projection.Projection.QualifiedId != request.Contract.ProjectionQualifiedId
            || projection.Projection.Version != request.Contract.ProjectionVersion
            || projection.Projection.ContentHash != request.Contract.ProjectionContentHash)
            throw new InteractionContractException("QUERY_PROJECTION_STALE",
                "The query materializer returned a different projection authority.");

        var output = InteractionCanonicalJson.Canonicalize(projection.OutputJson);
        var resultFingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionQueryFingerprintDomains.Result,
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                request.Contract.ProjectionQualifiedId,
                request.Contract.ProjectionVersion,
                request.Contract.ProjectionContentHash,
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
        var revisionFingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionQueryFingerprintDomains.SourceRevisions, revisions);
        return new(output, request.Contract.OutputSchemaHash, resultFingerprint, revisionFingerprint);
    }
}

/// <summary>
/// Read-only adapter over application-owned JavaScript projections. The application read-model
/// service resolves the exact activated query and mechanic, validates the closed output schema,
/// and binds the result to the current application resolution.
/// </summary>
internal sealed class MechanicProjectionInteractionQueryExecutor(IApplicationReadModelService readModels)
    : IInteractionQueryExecutor
{
    public string Kind => ApplicationQueryContract.MechanicProjectionExecutor;

    public async Task<InteractionQueryExecutionResult> ExecuteAsync(
        InteractionQueryExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        ArgumentNullException.ThrowIfNull(request.Contract);
        if (request.Contract.Executor != Kind
            || !request.QualifiedQueryId.StartsWith(request.ApplicationId.Value + ".", StringComparison.Ordinal)
            || !request.Contract.Roles.Order(StringComparer.Ordinal)
                .SequenceEqual(request.RoleBindings.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InteractionContractException("QUERY_EXECUTION_SCOPE_INVALID",
                "The query request does not match its application, executor, or exact roles.");

        var result = await readModels.ReadAsync(new(
            request.StateSpaceId,
            request.ApplicationId,
            request.QualifiedQueryId,
            request.RoleBindings,
            request.Audience), cancellationToken);
        if (result.QualifiedQueryId != request.QualifiedQueryId
            || result.OutputSchemaHash != request.Contract.OutputSchemaHash)
            throw new InteractionContractException("QUERY_READ_MODEL_STALE",
                "The query read model returned a different contract authority.");

        return new(result.DataJson, result.OutputSchemaHash,
            result.ResultFingerprint, result.SourceRevisionFingerprint);
    }
}
