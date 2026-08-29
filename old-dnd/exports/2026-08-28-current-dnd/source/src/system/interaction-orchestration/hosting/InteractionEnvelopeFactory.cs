using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Interactions;

internal sealed class InteractionEnvelopeFactory(
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IStateSpaceRegistry stateSpaces,
    IInteractionAuthorizationPolicy authorization) : IInteractionEnvelopeFactory
{
    public AuthorizedInteractionEnvelope Create(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string sessionContextId,
        string intentJson,
        InteractionAiRole role,
        string? conversationId = null,
        string? parentDelegationId = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(applicationId);
        var application = applications.Get(applicationId)
            ?? throw new InteractionContractException("APPLICATION_UNKNOWN", "The requested application is unavailable.");
        var activation = activations.Current(applicationId)
            ?? throw new InteractionContractException("APPLICATION_INACTIVE", "The requested application is not active.");
        var stateSpace = stateSpaces.Get(stateSpaceId)
            ?? throw new InteractionContractException("STATE_SPACE_UNKNOWN", "The requested state space is unavailable.");
        if (!SameRevision(stateSpace.ApplicationRevision, application)
            || stateSpace.ManifestFingerprint != activation.ActivationFingerprint)
            throw new InteractionContractException("STATE_SPACE_APPLICATION_MISMATCH",
                "The requested state space is not bound to the current application activation.");
        var request = new InteractionAuthorizationRequest(principal, applicationId, stateSpaceId,
            InteractionCapability.Plan, "interaction.envelope.create");
        var decision = authorization.Evaluate(request);
        if (!decision.Allowed || decision.Capability != InteractionCapability.Plan
            || decision.PrincipalReference != principal.PrincipalId
            || decision.ApplicationId != applicationId || decision.StateSpaceId != stateSpaceId)
            throw new InteractionContractException("PLAN_NOT_AUTHORIZED", "Planning is not authorized for this application context.");
        var host = new InteractionHostContext(principal, application, stateSpaceId, sessionContextId,
            InteractionStateRevision.From(stateSpace), activation.ActivationFingerprint,
            InteractionRoleProfile.For(role),
            new(InteractionContractLimits.ProposalSteps, InteractionContractLimits.JsonBytes,
                InteractionContractLimits.JsonBytes),
            decision, conversationId, parentDelegationId);
        return AuthorizedInteractionEnvelope.Create(InteractionIntent.Parse(intentJson), host);
    }

    private static bool SameRevision(ApplicationRevision left, ApplicationRevision right) =>
        left.ApplicationId == right.ApplicationId && left.Revision == right.Revision
        && left.Fingerprint == right.Fingerprint
        && left.BaseApplications.SequenceEqual(right.BaseApplications);
}
