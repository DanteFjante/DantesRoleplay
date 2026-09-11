using System.Buffers.Binary;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>Host-facing atomic action adapter for state commits and application-scoped pure computation.</summary>
internal sealed class ApplicationActionInvocationAdapter(
    IInteractionAuthorizationPolicy authorization,
    IStateSpaceRegistry stateSpaces,
    IApplicationActionRunner actions,
    IOperationLog operations,
    IApplicationPureActionExecutor? pureActions = null,
    IStandingGrantTargetResolver? grantTargets = null,
    IStandingGrantPolicy? standingGrants = null,
    IEcsWriteTransactionFactory? transactions = null) : IApplicationActionInvocationAdapter
{
    public async Task<InteractionInvocationResult> ExecuteAsync(ApplicationActionInvocationRequest request,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(request, trustedWorkflowChild: false, cancellationToken);

    internal async Task<InteractionInvocationResult> ExecuteWorkflowChildAsync(
        ApplicationActionInvocationRequest request,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(request, trustedWorkflowChild: true, cancellationToken);

    private async Task<InteractionInvocationResult> ExecuteAsync(
        ApplicationActionInvocationRequest request,
        bool trustedWorkflowChild,
        CancellationToken cancellationToken)
    {
        ApplicationEcsExecutionIdentity? executionIdentity = null;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Host.Profile != InteractionExecutionProfile.Atomic)
                return InteractionInvocationResult.Unavailable("ACTION_PROFILE_UNSUPPORTED", "The requested action profile is unavailable.");
            if (!trustedWorkflowChild && request.Host.ParentCommandId is not null)
                return InteractionInvocationResult.Unavailable("ATOMIC_CHILD_UNSUPPORTED", "Atomic child proposals are not executable in this adapter.");
            if (trustedWorkflowChild && request.Host.ParentCommandId is null)
                return InteractionInvocationResult.Unavailable("WORKFLOW_CHILD_REQUIRED", "The trusted workflow action path requires a parent command.");
            if (request.Host.StateSpaceId is null && request.Host.StateRevision is null)
                return pureActions is null
                    ? InteractionInvocationResult.Failed(
                        "INVOCATION_STATE_SCOPE_REQUIRED", "The action requires a state scope.")
                    : await pureActions.ExecuteAsync(request, cancellationToken);
            if (request.Host.StateSpaceId is not { } stateSpaceId || request.Host.StateRevision is not { } stateRevision)
                return InteractionInvocationResult.Failed(
                    "INVOCATION_STATE_SCOPE_REQUIRED", "The action requires a state scope.");
            if (request.Host.Budget.DeadlineUtc <= DateTime.UtcNow)
                return InteractionInvocationResult.Cancelled("INVOCATION_DEADLINE_EXCEEDED", "The invocation deadline elapsed before the action started.");
            if (!request.Host.Budget.TryConsumeOperation())
                return InteractionInvocationResult.Failed("INVOCATION_BUDGET_EXHAUSTED", "The invocation operation budget is exhausted.");
            if (trustedWorkflowChild)
            {
                var authority = await AuthorizeWorkflowChildAsync(request, cancellationToken);
                if (authority is not null) return authority;
            }
            else
            {
                var decision = authorization.Evaluate(new(request.Host.Principal, request.Host.ApplicationRevision.ApplicationId,
                    stateSpaceId, InteractionCapability.Execute, request.Host.CommandId));
                if (!decision.Allowed || decision.Capability != InteractionCapability.Execute
                    || decision.PrincipalReference != request.Host.Principal.PrincipalId
                    || decision.ApplicationId != request.Host.ApplicationRevision.ApplicationId
                    || decision.StateSpaceId != request.Host.StateSpaceId
                    || decision.EvidenceReference != request.Host.GrantReference)
                    return InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "The action is not authorized for this scope.");
            }
            var state = stateSpaces.Get(stateSpaceId);
            if (state is null || !ScopeMatches(state, request.Host)
                || InteractionStateRevision.From(state) != stateRevision)
                return InteractionInvocationResult.Failed("INVOCATION_SCOPE_STALE", "The requested state scope is no longer current.");
            var input = InteractionCanonicalJson.CanonicalizeObject(request.InputJson);
            var roles = InteractionInvocationRoles.Normalize(request.RoleEntityIds);
            var fingerprint = InteractionInvocationIdentity.Fingerprint(request.Host, request.QualifiedMechanicId,
                request.MechanicVersion, request.ContentFingerprint, roles, input);
            var seed = BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(fingerprint[..16]));
            executionIdentity = new(InteractionInvocationIdentity.OperationId(request.Host), fingerprint);
            using var deadline = Deadline(request.Host.Budget, cancellationToken);
            var result = await actions.RunAsync(new(stateSpaceId, request.Host.ApplicationRevision.ApplicationId,
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

    private async Task<InteractionInvocationResult?> AuthorizeWorkflowChildAsync(
        ApplicationActionInvocationRequest request,
        CancellationToken cancellationToken)
    {
        if (grantTargets is null || standingGrants is null || transactions is null)
            return InteractionInvocationResult.Unavailable(
                "WORKFLOW_ACTION_AUTHORITY_UNAVAILABLE", "Current workflow action authority is unavailable.");
        using var deadline = Deadline(request.Host.Budget, cancellationToken);
        await using var transaction = await transactions.BeginAsync(deadline.Token);
        if (!transactions.OwnsCurrent(transaction))
            return InteractionInvocationResult.Unavailable(
                "WORKFLOW_ACTION_TRANSACTION_UNAVAILABLE", "The workflow action authorization transaction is unavailable.");
        var selection = new StandingGrantDefinitionReference(request.QualifiedMechanicId,
            CatalogNamespaceKinds.Mechanic, request.MechanicVersion, request.ContentFingerprint);
        var resolution = await grantTargets.ResolveAsync(request.Host, selection, deadline.Token);
        if (resolution is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target })
            return resolution?.Status == StandingGrantTargetResolutionStatus.Denied
                ? InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "The action is not authorized for this scope.")
                : InteractionInvocationResult.Unavailable(
                    "WORKFLOW_ACTION_AUTHORITY_UNAVAILABLE", "Current workflow action authority is unavailable.");
        if (target.DefinitionId != selection.DefinitionId || target.Kind != selection.Kind
            || target.Revision != selection.Revision || target.ContentFingerprint != selection.ContentFingerprint
            || target.OwnerApplicationId != request.Host.ApplicationRevision.ApplicationId
            || target.Candidate is not null || target.RetainedActivation is not null)
            return InteractionInvocationResult.Unavailable(
                "WORKFLOW_ACTION_AUTHORITY_UNAVAILABLE", "Current workflow action authority is unavailable.");
        var decision = await standingGrants.EvaluateAsync(request.Host,
            new(StandingGrantCapability.Execute, StandingGrantScope.StateSpace, [target], []), deadline.Token);
        if (!ExactAllowedDecision(request.Host, target, decision))
            return InteractionInvocationResult.Failed(
                "INVOCATION_NOT_AUTHORIZED", "The action is not authorized for this scope.");
        await transaction.RollbackAsync(CancellationToken.None);
        return null;
    }

    private static bool ExactAllowedDecision(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target,
        StandingGrantDecision? decision)
    {
        if (decision is not { Allowed: true, Grant: not null, Evidence.Allowed: true }
            || decision.Evidence.PrincipalReference != host.Principal.PrincipalId
            || decision.Evidence.AuthenticationMethod != host.Principal.AuthenticationMethod
            || decision.Evidence.Scope != host.StateSpaceId
            || decision.Evidence.CorrelationId != host.CommandId)
            return false;
        var grant = decision.Grant;
        try { StandingGrantContractRules.ValidateConfiguration(grant); }
        catch (InteractionContractException) { return false; }
        return grant.GrantReference == host.GrantReference
            && grant.PrincipalReference == host.Principal.PrincipalId
            && grant.ApplicationId == host.ApplicationRevision.ApplicationId
            && grant.Scope == StandingGrantScope.StateSpace
            && grant.StateSpaceId == host.StateSpaceId
            && !grant.Revoked
            && grant.ExpiresAtUtc > DateTime.UtcNow
            && host.Budget.DeadlineUtc <= grant.ExpiresAtUtc
            && host.Budget.MaximumOperations <= grant.MaximumOperations
            && grant.Capabilities.Contains(StandingGrantCapability.Execute)
            && StandingGrantContractRules.MatchesDefinitionAllowance(
                host.ApplicationRevision.ApplicationId, grant.Definitions, target);
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
