using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Interactions;

/// <summary>
/// Executes an application read only through a freshly resolved query target and a current standing
/// grant. The legacy private-host authorization policy is deliberately absent from this adapter.
/// </summary>
internal sealed class StandingGrantApplicationReadModelInvocationAdapter(
    IStandingGrantPolicy? grants,
    IStandingGrantTargetResolver? targets,
    IStateSpaceRegistry stateSpaces,
    IApplicationReadModelService reads) : IStandingGrantApplicationReadModelInvocationAdapter
{
    private const string QueryKind = "query";
    private readonly ApplicationReadModelInvocationCore core = new(stateSpaces, reads);

    public Task<InteractionInvocationResult> ReadAsync(
        ApplicationReadModelInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        StandingGrantReadAuthorization? initial = null;
        return core.ReadAsync(
            request,
            async (candidate, token) =>
            {
                var authorized = await AuthorizeInitialAsync(candidate, token);
                initial = authorized.Authorization;
                return authorized.Failure;
            },
            (candidate, token) => ReauthorizeAsync(candidate, initial, token),
            cancellationToken);
    }

    private async Task<StandingGrantReadAuthorizationResult> AuthorizeInitialAsync(
        ApplicationReadModelInvocationRequest request,
        CancellationToken cancellationToken)
    {
        if (targets is null)
            return Unavailable("STANDING_GRANT_TARGET_RESOLVER_UNAVAILABLE");
        if (grants is null)
            return Unavailable("STANDING_GRANT_POLICY_UNAVAILABLE");

        var resolution = await targets.ResolveCurrentAsync(
            request.Host,
            request.QualifiedQueryId,
            QueryKind,
            cancellationToken);
        var targetFailure = ResolutionFailure(resolution);
        if (targetFailure is not null)
            return new(null, targetFailure);
        var target = resolution.Target!;
        if (!ExactQueryTarget(request, target))
            return Unavailable("STANDING_GRANT_QUERY_TARGET_UNAVAILABLE");

        var requirement = Requirement(target);
        StandingGrantContractRules.ValidateRequirement(request.Host, requirement);
        var decision = await grants.EvaluateAsync(request.Host, requirement, cancellationToken);
        if (!ExactAllowedDecision(request.Host, target, decision))
            return Forbidden();
        return new(new(target, GrantIdentity.From(decision.Grant!)), null);
    }

    private async Task<InteractionInvocationResult?> ReauthorizeAsync(
        ApplicationReadModelInvocationRequest request,
        StandingGrantReadAuthorization? initial,
        CancellationToken cancellationToken)
    {
        if (initial is null || targets is null || grants is null)
            return InteractionInvocationResult.Unavailable(
                "STANDING_GRANT_REAUTHORIZATION_UNAVAILABLE",
                "The standing read authority could not be revalidated.");

        var resolution = await targets.ResolveCurrentAsync(
            request.Host,
            request.QualifiedQueryId,
            QueryKind,
            cancellationToken);
        var targetFailure = ResolutionFailure(resolution);
        if (targetFailure is not null) return targetFailure;
        var currentTarget = resolution.Target!;
        if (!ExactQueryTarget(request, currentTarget) || currentTarget != initial.Target)
            return AuthorityChanged();

        var requirement = Requirement(initial.Target);
        StandingGrantContractRules.ValidateRequirement(request.Host, requirement);
        var decision = await grants.EvaluateAsync(request.Host, requirement, cancellationToken);
        if (!ExactAllowedDecision(request.Host, initial.Target, decision))
            return Forbidden().Failure;
        if (GrantIdentity.From(decision.Grant!) != initial.Grant)
            return AuthorityChanged();
        return null;
    }

    private static StandingGrantRequirement Requirement(StandingGrantDefinitionTarget target) =>
        new(
            StandingGrantCapability.Read,
            StandingGrantScope.StateSpace,
            [target],
            []);

    private static InteractionInvocationResult? ResolutionFailure(StandingGrantTargetResolution resolution)
    {
        if (resolution is null || !Enum.IsDefined(resolution.Status))
            return InteractionInvocationResult.Unavailable(
                "STANDING_GRANT_QUERY_TARGET_UNAVAILABLE",
                "The current query target could not be resolved.");
        return resolution.Status switch
        {
            StandingGrantTargetResolutionStatus.Available when resolution.Target is not null => null,
            StandingGrantTargetResolutionStatus.Denied => InteractionInvocationResult.Failed(
                "INVOCATION_NOT_AUTHORIZED",
                "The read is not authorized for this query."),
            _ => InteractionInvocationResult.Unavailable(
                "STANDING_GRANT_QUERY_TARGET_UNAVAILABLE",
                "The current query target could not be resolved.")
        };
    }

    private static bool ExactQueryTarget(
        ApplicationReadModelInvocationRequest request,
        StandingGrantDefinitionTarget target) =>
        target.DefinitionId == request.QualifiedQueryId
        && target.Kind == QueryKind
        && target.OwnerApplicationId == request.Host.ApplicationRevision.ApplicationId;

    private static bool ExactAllowedDecision(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target,
        StandingGrantDecision? decision)
    {
        if (decision is not { Allowed: true, Grant: not null }
            || !decision.Evidence.Allowed
            || decision.Evidence.PrincipalReference != host.Principal.PrincipalId
            || decision.Evidence.AuthenticationMethod != host.Principal.AuthenticationMethod)
            return false;
        var grant = decision.Grant;
        try
        {
            StandingGrantContractRules.ValidateConfiguration(grant);
        }
        catch (InteractionContractException)
        {
            return false;
        }
        return grant.GrantReference == host.GrantReference
            && grant.PrincipalReference == host.Principal.PrincipalId
            && grant.ApplicationId == host.ApplicationRevision.ApplicationId
            && grant.Scope == StandingGrantScope.StateSpace
            && grant.StateSpaceId == host.StateSpaceId
            && !grant.Revoked
            && grant.ExpiresAtUtc > DateTime.UtcNow
            && host.Budget.DeadlineUtc <= grant.ExpiresAtUtc
            && host.Budget.MaximumOperations <= grant.MaximumOperations
            && grant.Capabilities.Contains(StandingGrantCapability.Read)
            && StandingGrantContractRules.MatchesDefinitionAllowance(
                host.ApplicationRevision.ApplicationId,
                grant.Definitions,
                target);
    }

    private static StandingGrantReadAuthorizationResult Forbidden() =>
        new(
            null,
            InteractionInvocationResult.Failed(
                "INVOCATION_NOT_AUTHORIZED",
                "The read is not authorized by the current standing grant."));

    private static StandingGrantReadAuthorizationResult Unavailable(string code) =>
        new(
            null,
            InteractionInvocationResult.Unavailable(
                code,
                "Standing-grant read authorization is unavailable."));

    private static InteractionInvocationResult AuthorityChanged() =>
        InteractionInvocationResult.Failed(
            "INVOCATION_AUTHORITY_CHANGED",
            "The read authority changed before the result could be returned.");

    private sealed record StandingGrantReadAuthorization(
        StandingGrantDefinitionTarget Target,
        GrantIdentity Grant);

    private sealed record StandingGrantReadAuthorizationResult(
        StandingGrantReadAuthorization? Authorization,
        InteractionInvocationResult? Failure);

    private sealed record GrantIdentity(
        string GrantReference,
        string GrantId,
        int Revision,
        string ContentFingerprint,
        string PrincipalReference,
        string ApplicationId,
        StandingGrantScope Scope,
        string? StateSpaceId)
    {
        internal static GrantIdentity From(StandingGrantRevision grant) =>
            new(
                grant.GrantReference,
                grant.GrantId,
                grant.Revision,
                grant.ContentFingerprint,
                grant.PrincipalReference,
                grant.ApplicationId.Value,
                grant.Scope,
                grant.StateSpaceId);
    }
}
