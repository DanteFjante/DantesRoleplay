using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer.Mcp;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Read-only local-web adapter for the ambient, server-selected game audience. It deliberately
/// accepts no identifiers: the existing host policy and campaign participation check decide every
/// value before it can cross into a companion UI.
/// </summary>
internal static class AudienceContextWebEndpoint
{
    public static async Task<IResult> CurrentAsync(
        HttpContext context,
        ILocalKnowledgeSeatProvider seats,
        IAuthorizedKnowledgeAudiencePolicy audiences,
        IKnowledgeApplicationBindingResolver bindings,
        IKnowledgeActorParticipationVerifier participation,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var outcome = await SystemAudienceContextHandler.ResolveAsync(
            seats, audiences, bindings, participation, cancellationToken);
        if (outcome.Error is null)
        {
            return Results.Json(outcome.Data);
        }

        var statusCode = outcome.Error.Code == "AUDIENCE_CONTEXT_UNAVAILABLE"
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status403Forbidden;
        return Results.Json(new
        {
            status = statusCode == StatusCodes.Status503ServiceUnavailable ? "unavailable" : "denied",
            error = outcome.Error.Code,
            message = outcome.Error.Why
        }, statusCode: statusCode);
    }
}
