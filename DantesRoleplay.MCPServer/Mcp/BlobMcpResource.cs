using DantesRoleplay.Authorization;
using DantesRoleplay.Blobs;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using DantesRoleplay.Operations;
using System.Text.Json;

namespace DantesRoleplay.MCPServer.Mcp;

[McpServerResourceType]
public sealed class BlobMcpResource
{
    [McpServerResource(
        Name = "content-addressed-image",
        Title = "Content-addressed image",
        UriTemplate = "media://blob/sha256/{sha256}")]
    public async Task<BlobResourceContents> ReadAsync(
        string sha256,
        IBlobTransferService blobs,
        IPrivateOperatorRequestAuthorizer authorization,
        IOperationLog log,
        CancellationToken cancellationToken = default)
    {
        var decision = authorization.Authorize(PrivateOperatorCapability.Read);
        if (!decision.Allowed) throw new UnauthorizedAccessException("Private-operator authorization is required.");
        await using var result = await blobs.OpenReadAsync(sha256, cancellationToken)
            ?? throw new FileNotFoundException("No finalized blob matched that SHA-256 digest.");
        using var memory = new MemoryStream();
        await result.Content.CopyToAsync(memory, cancellationToken);
        await log.RecordAsync("blob-resource", "Returned blob bytes through MCP resources.", true,
            subject: result.Asset.Sha256, cancellationToken: cancellationToken,
            guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
        return BlobResourceContents.FromBytes(memory.ToArray(), result.Asset.ResourceUri, result.Asset.MediaType);
    }
}
