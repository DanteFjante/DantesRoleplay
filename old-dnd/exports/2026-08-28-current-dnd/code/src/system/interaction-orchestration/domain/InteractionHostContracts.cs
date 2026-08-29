using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Interactions;

public static class InteractionStateRevision
{
    public static string From(StateSpaceView stateSpace)
    {
        ArgumentNullException.ThrowIfNull(stateSpace);
        return $"state-binding.{stateSpace.BindingRevision}.{stateSpace.ManifestFingerprint.ToLowerInvariant()}";
    }
}

public interface IInteractionEnvelopeFactory
{
    AuthorizedInteractionEnvelope Create(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string sessionContextId,
        string intentJson,
        InteractionAiRole role,
        string? conversationId = null,
        string? parentDelegationId = null);
}
