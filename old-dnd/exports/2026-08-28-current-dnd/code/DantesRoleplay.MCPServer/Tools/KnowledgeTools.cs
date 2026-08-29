using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Protocol adapter for the separate player-safe knowledge surface.</summary>
public sealed class KnowledgeTools
{
    public Task<ToolEnvelope> AnswerAsync(
        IAuthorizedKnowledgeAnswerCoordinator coordinator,
        IOperationLog log,
        AuthorizedKnowledgeAnswerRequest request,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", request.Question, request.CampaignId,
            ["procedure.game.core.world.knowledge"], async () =>
            {
                var answer = await coordinator.AnswerAsync(request, cancellationToken);
                return answer.Status == "denied"
                    ? ToolOutcome.Fail(
                        "KNOWLEDGE_AUDIENCE_DENIED",
                        "This local development seat is not configured for that campaign.",
                        "Configure the local development knowledge audience for the intended campaign, then retry.",
                        "Knowledge audience denied the request.")
                    : answer.Status == "unavailable"
                        ? ToolOutcome.Fail(
                            "KNOWLEDGE_AUDIENCE_UNAVAILABLE",
                            "Knowledge answers require an explicitly enabled local development audience or a future authentication provider.",
                            "Enable the documented local development audience, then retry.",
                            "Knowledge audience was unavailable.")
                        : ToolOutcome.OkAbout(
                        request.CampaignId,
                        answer,
                        "Returned only knowledge available to the configured audience.",
                        "query(kind: \"knowledge-answer\", campaignId: \"...\", question: \"...\")");
            }, consumesReadEvidence: false);
}
