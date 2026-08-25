using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.MCPServer;

/// <summary>Basic private-host authorization: verified operator plus an exact application state space.</summary>
public sealed class PrivateHostInteractionAuthorizationPolicy(IStateSpaceRegistry stateSpaces)
    : IInteractionAuthorizationPolicy
{
    public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var evidence = "interaction.private-host." + request.Capability.ToString().ToLowerInvariant();
        if (!request.Principal.Verified)
            return InteractionAuthorizationDecision.Deny(request, "VERIFIED_OPERATOR_REQUIRED", evidence);
        var stateSpace = stateSpaces.Get(request.StateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != request.ApplicationId)
            return InteractionAuthorizationDecision.Deny(request, "INTERACTION_SCOPE_MISMATCH", evidence);
        return InteractionAuthorizationDecision.Allow(request, evidence);
    }
}
