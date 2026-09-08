namespace DantesRoleplay.MCPServer;

internal static class ServerStartupDiagnostics
{
    internal static void LogReady(ILogger logger, IEnumerable<string> addresses, TimeSpan elapsed)
    {
        logger.LogInformation("Server ready in {ElapsedSeconds:F2} s.", elapsed.TotalSeconds);
        foreach (var address in addresses.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Wildcards are listener addresses, not destinations a browser can open.
            var localAddress = address.Replace("://*:", "://localhost:", StringComparison.Ordinal)
                .Replace("://+:", "://localhost:", StringComparison.Ordinal);
            if (!Uri.TryCreate(localAddress, UriKind.Absolute, out var uri))
            {
                logger.LogInformation("Listening on {Address}.", address);
                continue;
            }
            var website = new UriBuilder(uri);
            if (uri.Host is "0.0.0.0" or "[::]" or "::") website.Host = "localhost";
            logger.LogInformation("Website: {WebsiteUrl} (listening on {Address}).", website.Uri.AbsoluteUri, address);
            logger.LogInformation("MCP: {McpUrl}", new Uri(website.Uri, ServerConfiguration.McpEndpoint).AbsoluteUri);
        }
    }
}
