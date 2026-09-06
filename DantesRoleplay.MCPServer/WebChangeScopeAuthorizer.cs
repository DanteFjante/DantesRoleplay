using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Web.Live;

namespace DantesRoleplay.MCPServer;

/// <summary>Authorizes generic object-change scopes from the same ambient table seat as reads.</summary>
internal sealed class WebChangeScopeAuthorizer(
    ILocalKnowledgeSeatProvider seats,
    IAuthorizedKnowledgeAudiencePolicy audiences,
    IKnowledgeApplicationBindingResolver bindings,
    IKnowledgeActorParticipationVerifier participation) : IWebChangeScopeAuthorizer
{
    public async Task<bool> AuthorizeAsync(
        WebChangeSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var seat = seats.Current();
        if (!seat.Enabled || seat.ApplicationId != subscription.ApplicationId
            || subscription.Perspective == "dm" && seat.Role != KnowledgeAudienceRole.GameMaster)
            return false;
        var authorization = await SystemAudienceContextHandler.ResolveAsync(
            seats, audiences, bindings, participation, seat.CampaignId, cancellationToken);
        if (authorization.Error is not null) return false;
        var binding = await bindings.ResolveAsync(seat.CampaignId, cancellationToken);
        return binding is not null
            && binding.ApplicationId == subscription.ApplicationId
            && binding.StateSpaceId == subscription.StateSpaceId;
    }
}
