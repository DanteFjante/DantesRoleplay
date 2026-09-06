using DantesRoleplay.Applications;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Interactions;

public static class InteractionQueryFingerprintDomains
{
    public const string Result = "dantes-roleplay/interaction-query-result/v1";
    public const string SourceRevisions = "dantes-roleplay/interaction-query-source-revisions/v1";
}

public sealed record InteractionQueryExecutionRequest(
    string StateSpaceId,
    ApplicationIdentifier ApplicationId,
    string QualifiedQueryId,
    InteractionQueryContractReference Contract,
    IReadOnlyDictionary<string, string> RoleBindings,
    MechanicAudienceContext? Audience = null,
    string? Cursor = null,
    int? PageSize = null);

public sealed record InteractionQueryExecutionResult(
    string OutputJson,
    string OutputSchemaHash,
    string ResultFingerprint,
    string SourceRevisionFingerprint);

public interface IInteractionQueryExecutor
{
    string Kind { get; }
    Task<InteractionQueryExecutionResult> ExecuteAsync(
        InteractionQueryExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IInteractionQueryExecutorRegistry
{
    bool TryGet(string kind, out IInteractionQueryExecutor executor);
}
