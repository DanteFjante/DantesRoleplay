using DantesRoleplay.Knowledge;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// Returns the one ambient audience binding selected by the host. Callers cannot nominate any
/// identity, scope, or participant; all values are revalidated from existing owners per read.
/// </summary>
internal sealed class SystemAudienceContextTools
{
    public Task<ToolEnvelope> CurrentAsync(
        ILocalKnowledgeSeatProvider? seats,
        IAuthorizedKnowledgeAudiencePolicy? audiences,
        IKnowledgeApplicationBindingResolver? bindings,
        IKnowledgeActorParticipationVerifier? participation,
        IOperationLog log,
        CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "query", () => ResolveAsync(
            seats, audiences, bindings, participation, cancellationToken));

    internal static async Task<ToolOutcome> ResolveAsync(
        ILocalKnowledgeSeatProvider? seats,
        IAuthorizedKnowledgeAudiencePolicy? audiences,
        IKnowledgeApplicationBindingResolver? bindings,
        IKnowledgeActorParticipationVerifier? participation,
        CancellationToken cancellationToken)
    {
        if (seats is null || audiences is null || bindings is null || participation is null)
            return Unavailable();

        var configured = seats.Current();
        if (!Configured(configured)) return Denied();

        var audience = await audiences.ResolveAsync(configured.CampaignId, cancellationToken);
        var grant = audience.Grant;
        if (!audience.Granted || grant is null || grant.PrincipalId != configured.PrincipalId ||
            grant.CampaignId != configured.CampaignId || grant.Role != configured.Role ||
            (grant.Role == KnowledgeAudienceRole.Actor && grant.ActorId != configured.ActorId) ||
            (grant.Role == KnowledgeAudienceRole.GameMaster && grant.ActorId is not null))
            return Denied();

        var binding = await bindings.ResolveAsync(grant.CampaignId, cancellationToken);
        if (binding is null || binding.ApplicationId != configured.ApplicationId ||
            binding.CampaignEntityId != grant.CampaignId)
            return Denied();

        try { binding.Validate(); }
        catch (ArgumentException) { return Denied(); }

        if (grant.Role == KnowledgeAudienceRole.GameMaster)
        {
            return ToolOutcome.OkAbout(grant.CampaignId, new
            {
                status = "bound",
                applicationId = binding.ApplicationId,
                stateSpaceId = binding.StateSpaceId,
                campaignId = grant.CampaignId,
                role = "game-master",
                roleHints = new { },
                policyRevision = grant.PolicyRevision,
                bindingRevision = binding.BindingRevision
            }, "Returned the current host-authorized audience context.",
            "query(kind: \"system.interaction-plan\", applicationId: \"...\", request: \"{...}\")");
        }

        if (grant.Role != KnowledgeAudienceRole.Actor || grant.ActorId is null) return Denied();
        var member = await participation.ResolveAsync(binding, grant.ActorId, cancellationToken);
        if (!member.Active)
            return member.ActorMissing ? CharacterCreationRequired(binding, grant) : Denied();
        if (member.ActorMissing || string.IsNullOrWhiteSpace(member.Revision)) return Denied();

        return ToolOutcome.OkAbout(grant.ActorId, new
        {
            status = "bound",
            applicationId = binding.ApplicationId,
            stateSpaceId = binding.StateSpaceId,
            campaignId = grant.CampaignId,
            actorId = grant.ActorId,
            role = "actor",
            roleHints = new { actor = grant.ActorId },
            policyRevision = grant.PolicyRevision,
            bindingRevision = binding.BindingRevision,
            participationRevision = member.Revision
        }, "Returned the current host-authorized audience context.",
        "query(kind: \"system.interaction-plan\", applicationId: \"...\", request: \"{...}\")");
    }

    private static ToolOutcome CharacterCreationRequired(
        KnowledgeApplicationBinding binding,
        KnowledgeAudienceGrant grant) => ToolOutcome.OkAbout(grant.CampaignId, new
        {
            status = "character-creation-required",
            applicationId = binding.ApplicationId,
            stateSpaceId = binding.StateSpaceId,
            campaignId = grant.CampaignId,
            characterCreation = new { characterId = grant.ActorId },
            roleHints = new { },
            policyRevision = grant.PolicyRevision,
            bindingRevision = binding.BindingRevision
        }, "The current player needs a character before play can begin.",
        "query(kind: \"system.interaction-plan\", applicationId: \"...\", request: \"{...}\")");

    private static bool Configured(LocalKnowledgeSeatSnapshot value) =>
        value.Enabled && Token(value.PrincipalId) && Token(value.ApplicationId) &&
        Token(value.CampaignId) && Enum.IsDefined(value.Role) &&
        (value.Role == KnowledgeAudienceRole.GameMaster
            ? value.ActorId is null
            : Token(value.ActorId));

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() && value.Length <= 200 && !value.Any(char.IsWhiteSpace);

    private static ToolOutcome Denied() => ToolOutcome.Fail(
        "AUDIENCE_CONTEXT_DENIED",
        "No current host-authorized table context is available.",
        "Configure the local table seat and active campaign participation when using an actor seat, then retry.",
        "Denied audience context before exposing any binding.");

    private static ToolOutcome Unavailable() => ToolOutcome.Fail(
        "AUDIENCE_CONTEXT_UNAVAILABLE",
        "Audience context is not configured in this host.",
        "Configure the local table seat and active campaign participation when using an actor seat, then retry.",
        "Audience context dependencies were unavailable.");
}
