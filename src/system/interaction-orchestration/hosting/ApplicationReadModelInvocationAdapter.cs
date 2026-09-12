using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Interactions;

/// <summary>Host-facing read adapter. Authored JSON is limited to query input; scope is host-owned.</summary>
internal sealed class ApplicationReadModelInvocationAdapter(
    IInteractionAuthorizationPolicy authorization,
    IStateSpaceRegistry stateSpaces,
    IApplicationReadModelService reads) : IApplicationReadModelInvocationAdapter
{
    private readonly ApplicationReadModelInvocationCore core = new(stateSpaces, reads);

    public Task<InteractionInvocationResult> ReadAsync(ApplicationReadModelInvocationRequest request,
        CancellationToken cancellationToken = default) =>
        core.ReadAsync(request, AuthorizeAsync, cancellationToken: cancellationToken);

    private Task<InteractionInvocationResult?> AuthorizeAsync(
        ApplicationReadModelInvocationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Host.StateSpaceId is not { } stateSpaceId || request.Host.StateRevision is null)
            return Task.FromResult<InteractionInvocationResult?>(InteractionInvocationResult.Failed(
                "INVOCATION_STATE_SCOPE_REQUIRED", "The read requires a state scope."));
        var decision = authorization.Evaluate(new(
            request.Host.Principal,
            request.Host.ApplicationRevision.ApplicationId,
            stateSpaceId,
            InteractionCapability.Read,
            request.Host.CommandId));
        var allowed = decision.Allowed && decision.Capability == InteractionCapability.Read
            && decision.PrincipalReference == request.Host.Principal.PrincipalId
            && decision.ApplicationId == request.Host.ApplicationRevision.ApplicationId
            && decision.StateSpaceId == request.Host.StateSpaceId
            && decision.EvidenceReference == request.Host.GrantReference;
        return Task.FromResult<InteractionInvocationResult?>(allowed
            ? null
            : InteractionInvocationResult.Failed(
                "INVOCATION_NOT_AUTHORIZED",
                "The read is not authorized for this scope."));
    }
}

internal sealed class ApplicationReadModelInvocationCore(
    IStateSpaceRegistry stateSpaces,
    IApplicationReadModelService reads)
{
    internal async Task<InteractionInvocationResult> ReadAsync(
        ApplicationReadModelInvocationRequest request,
        Func<ApplicationReadModelInvocationRequest, CancellationToken, Task<InteractionInvocationResult?>> authorize,
        Func<ApplicationReadModelInvocationRequest, CancellationToken, Task<InteractionInvocationResult?>>? reauthorize = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(authorize);
            if (request.Host.StateSpaceId is not { } stateSpaceId || request.Host.StateRevision is not { } stateRevision)
                return InteractionInvocationResult.Failed(
                    "INVOCATION_STATE_SCOPE_REQUIRED", "The read requires a state scope.");
            if (request.Host.Profile != InteractionExecutionProfile.ReadOnly)
                return InteractionInvocationResult.Unavailable("READ_PROFILE_UNSUPPORTED", "The requested read profile is unavailable.");
            if (request.Host.Budget.DeadlineUtc <= DateTime.UtcNow)
                return InteractionInvocationResult.Cancelled("INVOCATION_DEADLINE_EXCEEDED", "The invocation deadline elapsed before the read started.");
            if (!request.Host.Budget.TryConsumeOperation())
                return InteractionInvocationResult.Failed("INVOCATION_BUDGET_EXHAUSTED", "The invocation operation budget is exhausted.");
            using var deadline = Deadline(request.Host.Budget, cancellationToken);
            var denied = await authorize(request, deadline.Token);
            if (denied is not null) return denied;
            var state = stateSpaces.Get(stateSpaceId);
            if (state is null || !ScopeMatches(state, request.Host)
                || InteractionStateRevision.From(state) != stateRevision)
                return InteractionInvocationResult.Failed("INVOCATION_SCOPE_STALE", "The requested state scope is no longer current.");
            var input = ApplicationReadModelInput.Normalize(request.InputJson);
            var roles = InteractionInvocationRoles.Normalize(request.RoleBindings);
            var result = await reads.ReadAsync(new(stateSpaceId, request.Host.ApplicationRevision.ApplicationId,
                request.QualifiedQueryId, roles, request.Audience, input, request.Cursor, request.PageSize)
            { ExpectedContract = request.ExpectedContract }, deadline.Token);
            if (reauthorize is not null)
            {
                denied = await reauthorize(request, deadline.Token);
                if (denied is not null) return denied;
                state = stateSpaces.Get(stateSpaceId);
                if (state is null || !ScopeMatches(state, request.Host)
                    || InteractionStateRevision.From(state) != stateRevision)
                    return InteractionInvocationResult.Failed(
                        "INVOCATION_SCOPE_STALE",
                        "The requested state scope is no longer current.");
            }
            return InteractionInvocationResult.Completed(result.DataJson, new(result.StateSpaceFingerprint,
                result.ResolutionFingerprint, result.OutputSchemaHash, result.ResultFingerprint,
                result.SourceRevisionFingerprint));
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled("INVOCATION_CANCELLED", "The read was cancelled.");
        }
        catch (ApplicationReadModelException exception)
        {
            return InteractionInvocationResult.Failed(exception.Code, "The requested read could not complete.");
        }
        catch (InteractionContractException exception)
        {
            return InteractionInvocationResult.Failed(exception.Code, "The invocation request is invalid.");
        }
        catch
        {
            return InteractionInvocationResult.Unavailable("READ_ADAPTER_UNAVAILABLE", "The read adapter is unavailable.");
        }
    }

    private static bool ScopeMatches(StateSpaceView state, InteractionInvocationHost host) =>
        state.ApplicationRevision.ApplicationId == host.ApplicationRevision.ApplicationId
        && state.ApplicationRevision.Revision == host.ApplicationRevision.Revision
        && state.ApplicationRevision.Fingerprint == host.ApplicationRevision.Fingerprint
        && state.ApplicationRevision.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications);

    private static CancellationTokenSource Deadline(InteractionInvocationBudget budget, CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = budget.DeadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) source.Cancel();
        else if (remaining <= TimeSpan.FromMilliseconds(int.MaxValue)) source.CancelAfter(remaining);
        return source;
    }
}
