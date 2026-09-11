using System.Buffers.Binary;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>Host-facing root-atomic action adapter; child and workflow execution are not implicit.</summary>
internal sealed class ApplicationActionInvocationAdapter(
    IInteractionAuthorizationPolicy authorization,
    IStateSpaceRegistry stateSpaces,
    IApplicationActionRunner actions,
    IOperationLog operations) : IApplicationActionInvocationAdapter
{
    public async Task<InteractionInvocationResult> ExecuteAsync(ApplicationActionInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ApplicationEcsExecutionIdentity? executionIdentity = null;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Host.Profile != InteractionExecutionProfile.Atomic)
                return InteractionInvocationResult.Unavailable("ACTION_PROFILE_UNSUPPORTED", "The requested action profile is unavailable.");
            if (request.Host.ParentCommandId is not null)
                return InteractionInvocationResult.Unavailable("ATOMIC_CHILD_UNSUPPORTED", "Atomic child proposals are not executable in this adapter.");
            if (request.Host.Budget.DeadlineUtc <= DateTime.UtcNow)
                return InteractionInvocationResult.Cancelled("INVOCATION_DEADLINE_EXCEEDED", "The invocation deadline elapsed before the action started.");
            if (!request.Host.Budget.TryConsumeOperation())
                return InteractionInvocationResult.Failed("INVOCATION_BUDGET_EXHAUSTED", "The invocation operation budget is exhausted.");
            var decision = authorization.Evaluate(new(request.Host.Principal, request.Host.ApplicationRevision.ApplicationId,
                request.Host.StateSpaceId, InteractionCapability.Execute, request.Host.CommandId));
            if (!decision.Allowed || decision.Capability != InteractionCapability.Execute
                || decision.PrincipalReference != request.Host.Principal.PrincipalId
                || decision.ApplicationId != request.Host.ApplicationRevision.ApplicationId
                || decision.StateSpaceId != request.Host.StateSpaceId
                || decision.EvidenceReference != request.Host.GrantReference)
                return InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "The action is not authorized for this scope.");
            var state = stateSpaces.Get(request.Host.StateSpaceId);
            if (state is null || !ScopeMatches(state, request.Host)
                || InteractionStateRevision.From(state) != request.Host.StateRevision)
                return InteractionInvocationResult.Failed("INVOCATION_SCOPE_STALE", "The requested state scope is no longer current.");
            var input = InteractionCanonicalJson.CanonicalizeObject(request.InputJson);
            var roles = InteractionInvocationRoles.Normalize(request.RoleEntityIds);
            var fingerprint = InteractionInvocationIdentity.Fingerprint(request.Host, request.QualifiedMechanicId,
                request.MechanicVersion, request.ContentFingerprint, roles, input);
            var seed = BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(fingerprint[..16]));
            executionIdentity = new(InteractionInvocationIdentity.OperationId(request.Host), fingerprint);
            using var deadline = Deadline(request.Host.Budget, cancellationToken);
            var result = await actions.RunAsync(new(request.Host.StateSpaceId, request.Host.ApplicationRevision.ApplicationId,
                request.QualifiedMechanicId, request.MechanicVersion, request.ContentFingerprint, roles,
                input, seed, executionIdentity), deadline.Token);
            if (!result.Successful)
            {
                var problem = result.Problems.FirstOrDefault();
                return InteractionInvocationResult.Failed(problem?.Code ?? "APPLICATION_ACTION_FAILED",
                    problem?.SafeMessage ?? "The action did not commit.");
            }
            var audit = await operations.GetAsync(executionIdentity.OperationId, CancellationToken.None);
            if (audit is null || !audit.Success || audit.Tool != ApplicationEcsExecutionIdentity.AuditTool
                || audit.Subject != executionIdentity.AuditSubject)
                return InteractionInvocationResult.Unavailable("COMMIT_RECEIPT_UNAVAILABLE",
                    "The action commit receipt is unavailable.", executionIdentity);
            return InteractionInvocationResult.Committed(new(executionIdentity.OperationId,
                executionIdentity.RequestFingerprint, result.EffectReceipts,
                EffectDetailsAvailable: result.Disposition != ApplicationActionExecutionDisposition.Replayed
                    || result.EffectReceipts.Count > 0));
        }
        catch (OperationCanceledException)
        {
            return executionIdentity is null
                ? InteractionInvocationResult.Cancelled("INVOCATION_CANCELLED", "The action was cancelled before execution started.")
                : await ReconcileUnknownAsync(executionIdentity, cancelled: true);
        }
        catch (InteractionContractException exception)
        {
            return InteractionInvocationResult.Failed(exception.Code, "The invocation request is invalid.");
        }
        catch
        {
            return executionIdentity is null
                ? InteractionInvocationResult.Unavailable("ACTION_ADAPTER_UNAVAILABLE", "The action adapter is unavailable.")
                : await ReconcileUnknownAsync(executionIdentity, cancelled: false);
        }
    }

    private async Task<InteractionInvocationResult> ReconcileUnknownAsync(
        ApplicationEcsExecutionIdentity executionIdentity,
        bool cancelled)
    {
        try
        {
            var audit = await operations.GetAsync(executionIdentity.OperationId, CancellationToken.None);
            if (audit is { Success: true, Tool: ApplicationEcsExecutionIdentity.AuditTool }
                && audit.Subject == executionIdentity.AuditSubject)
                return InteractionInvocationResult.Committed(new(executionIdentity.OperationId,
                    executionIdentity.RequestFingerprint, [], EffectDetailsAvailable: false));
        }
        catch
        {
            return Unknown(executionIdentity);
        }

        return cancelled
            ? InteractionInvocationResult.Cancelled("INVOCATION_CANCELLED", "The action was cancelled.",
                recoveryIdentity: executionIdentity)
            : Unknown(executionIdentity);
    }

    private static InteractionInvocationResult Unknown(ApplicationEcsExecutionIdentity executionIdentity) =>
        InteractionInvocationResult.Unavailable("ACTION_OUTCOME_UNKNOWN",
            "The action outcome is unavailable; reconcile its recovery identity before retrying.", executionIdentity);

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
