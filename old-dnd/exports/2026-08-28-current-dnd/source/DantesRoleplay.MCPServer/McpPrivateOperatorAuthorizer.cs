using System.Net;
using DantesRoleplay.Authorization;
using DantesRoleplay.Web.Security;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Private-host MCP adapter. Remote Tailscale requests remain web-only; only a direct loopback MCP
/// request may construct the local operator principal.
/// </summary>
public sealed class McpPrivateOperatorAuthorizer(
    IHttpContextAccessor accessor,
    IPrivateOperatorAuthorizationPolicy policy) : IPrivateOperatorRequestAuthorizer
{
    public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
    {
        var context = accessor.HttpContext;
        var isLoopbackMcp = context is not null
            && IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None)
            && context.Request.Path.StartsWithSegments(ServerConfiguration.McpEndpoint)
            && !WebAccessPolicy.IsRemoteCandidate(context.Request);
        var principal = isLoopbackMcp
            ? PrivateOperatorPrincipal.Create("local-loopback-mcp", "local-operator")
            : TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED");
        return policy.Evaluate(new(
            principal,
            capability,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            Correlation(context?.TraceIdentifier)));
    }

    private static string Correlation(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "mcp-request" : value.Length <= 128 ? value : value[..128];
}
