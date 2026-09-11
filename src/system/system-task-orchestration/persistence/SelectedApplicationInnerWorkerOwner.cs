using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Builds fresh state-scoped hosts for the selected-application gateway, then delegates every
/// procedure and task decision to the existing durable worker owner.
/// </summary>
internal sealed class SelectedApplicationInnerWorkerOwner(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces,
    Func<SystemInnerWorkerService> workerFactory,
    TimeProvider timeProvider) : ISelectedApplicationInnerWorkerOwner
{
    public async Task<InteractionInvocationResult> SubmitAsync(
        SystemCapabilityInvocationContext context,
        string commandId,
        SelectedApplicationInnerWorkerSubmission request,
        CancellationToken cancellationToken = default)
    {
        var selection = await HostsAsync(context, request.ApplicationId, request.StateSpaceId,
            StandingGrantCapability.Execute, "inner-worker." + commandId, commandId,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        var workers = workerFactory();
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await workers.SubmitEphemeralRootAsync(new(host, request.Procedure,
                request.AssignmentJson, request.ResultSchemaJson, request.DependencyHandles),
                cancellationToken);
            if (result.Tag == InteractionInvocationResultTag.Pending || !Retryable(result.Code))
                return result;
        }
        return result ?? Failure(selection.Code);
    }

    public async Task<InteractionInvocationResult> ReadAsync(
        SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerHandle request,
        CancellationToken cancellationToken = default)
    {
        if (context.ApplicationId is not { IsSystem: false } applicationId)
            return Failure("APPLICATION_CONTEXT_REQUIRED");
        var selection = await HostsAsync(context, applicationId, request.StateSpaceId,
            StandingGrantCapability.ReadTask, ReadCommand(context, request), null,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        var workers = workerFactory();
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await workers.GetAsync(host, request.Handle, cancellationToken);
            if (!Retryable(result.Code)) return result;
        }
        return result ?? Failure(selection.Code);
    }

    public async Task<InteractionInvocationResult> CancelAsync(
        SystemCapabilityInvocationContext context,
        string commandId,
        SelectedApplicationInnerWorkerHandle request,
        CancellationToken cancellationToken = default)
    {
        if (context.ApplicationId is not { IsSystem: false } applicationId)
            return Failure("APPLICATION_CONTEXT_REQUIRED");
        var selection = await HostsAsync(context, applicationId, request.StateSpaceId,
            StandingGrantCapability.CancelTask, "inner-worker-cancel." + commandId, null,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        var workers = workerFactory();
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await workers.CancelAsync(host, request.Handle, cancellationToken);
            if (!Retryable(result.Code)) return result;
        }
        return result ?? Failure(selection.Code);
    }

    private async Task<(IReadOnlyList<InteractionInvocationHost> Hosts, string Code)> HostsAsync(
        SystemCapabilityInvocationContext context,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        StandingGrantCapability capability,
        string commandId,
        string? parentCommandId,
        CancellationToken cancellationToken)
    {
        if (!context.Principal.Verified || context.ApplicationId != applicationId)
            return ([], "APPLICATION_CONTEXT_REQUIRED");
        StateSpaceView? state;
        try { state = stateSpaces.Get(stateSpaceId); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return ([], "STATE_SPACE_UNAVAILABLE"); }
        if (state is null || state.ApplicationRevision.ApplicationId != applicationId)
            return ([], "STATE_SPACE_NOT_AUTHORIZED");

        try
        {
            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var rows = await (from record in db.Set<StandingGrantRevisionRecord>().AsNoTracking()
                              join current in db.Set<StandingGrantCurrentRecord>().AsNoTracking()
                                  on new { record.GrantId, record.Revision }
                                  equals new { current.GrantId, current.Revision }
                              where record.PrincipalReference == context.Principal.PrincipalId
                                  && record.ApplicationId == applicationId.Value
                                  && record.Scope == "stateSpace"
                                  && record.StateSpaceId == stateSpaceId
                                  && !record.Revoked && record.ExpiresAtUtc > now
                              orderby record.GrantId, record.Revision
                              select record).Take(33).ToArrayAsync(cancellationToken);
            if (rows.Length > 32) return ([], "STANDING_GRANT_CANDIDATES_UNAVAILABLE");
            var grants = rows
                .Where(value => Encoding.UTF8.GetByteCount(value.PermissionsJson) <= 16_000)
                .Select(SqliteStandingGrantPolicy.Parse)
                .Where(value => value.Capabilities.Contains(capability) && value.MaximumOperations >= 1)
                .OrderBy(value => value.Definitions.Mode == StandingGrantDefinitionMode.ExactIds ? 0 : 1)
                .ThenBy(value => value.GrantId, StringComparer.Ordinal)
                .ThenBy(value => value.Revision)
                .ToArray();
            if (grants.Length == 0) return ([], "STANDING_GRANT_DENIED");
            var hosts = grants.Select(grant =>
            {
                var deadline = grant.ExpiresAtUtc;
                var maximum = now.AddMinutes(10);
                if (deadline > maximum) deadline = maximum;
                return new InteractionInvocationHost(context.Principal, state.ApplicationRevision,
                    state.StateSpaceId, grant.GrantReference, commandId,
                    InteractionStateRevision.From(state), InteractionExecutionProfile.Workflow,
                    new InteractionInvocationBudget(Math.Min(grant.MaximumOperations,
                        StandingGrantLimits.MaximumOperations), deadline), parentCommandId);
            }).ToArray();
            return (Array.AsReadOnly(hosts), "STANDING_GRANT_SELECTED");
        }
        catch (OperationCanceledException) { throw; }
        catch { return ([], "STANDING_GRANT_CANDIDATES_UNAVAILABLE"); }
    }

    private static bool Retryable(string code) => code is
        "SYSTEM_TASK_NOT_AUTHORIZED" or
        "STANDING_GRANT_DENIED" or
        "STANDING_GRANT_TARGET_DENIED" or
        "STANDING_GRANT_NOT_CURRENT" or
        "STANDING_GRANT_INVALID";

    private static InteractionInvocationResult Failure(string code) =>
        code.Contains("UNAVAILABLE", StringComparison.Ordinal)
            ? InteractionInvocationResult.Unavailable(code,
                "The selected-application worker owner is temporarily unavailable.")
            : InteractionInvocationResult.Failed(code,
                "The selected-application worker request is not authorized in this scope.");

    private static string ReadCommand(SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerHandle request) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            "dantes-roleplay/inner-worker-read/v1\n" + context.Principal.PrincipalId + "\n"
            + context.ApplicationId!.Value + "\n" + request.StateSpaceId + "\n"
            + request.Handle.TaskId + "\n" + request.Handle.CommandId + "\n"
            + context.CorrelationId)))[..32];
}
