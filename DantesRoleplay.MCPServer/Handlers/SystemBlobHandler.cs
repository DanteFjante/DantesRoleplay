using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Blobs;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemBlobHandler
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public Task<ToolEnvelope> QueryAsync(
        IBlobTransferService? blobs,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? sha256,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(log, "query", async () =>
    {
        var decision = authorization?.Authorize(PrivateOperatorCapability.Read);
        if (decision is null || !decision.Allowed)
            return Denied(decision, "system.blobs");
        if (blobs is null)
            return ToolOutcome.Fail("BLOB_STORAGE_UNAVAILABLE", "Blob storage is not configured.",
                "query(kind: \"capabilities\")", "Blob storage was unavailable.");
        if (string.IsNullOrWhiteSpace(sha256))
            return ToolOutcome.Fail("BLOB_SHA256_REQUIRED", "system.blobs requires an exact SHA-256 digest in id.",
                "query(kind: \"system.blobs\", id: \"<64-hex-sha256>\")", "Rejected a blob query without an exact digest.");
        try
        {
            var asset = await blobs.FindAsync(sha256, cancellationToken);
            if (asset is null)
                return ToolOutcome.Fail("BLOB_NOT_FOUND", "No finalized blob matched that SHA-256 digest.",
                    "query(kind: \"system.blobs\", id: \"<64-hex-sha256>\")", "No finalized blob matched the digest.");
            return new ToolOutcome(
                Describe(asset),
                $"Returned blob metadata for {asset.Sha256}.",
                [$"Read MCP resource {asset.ResourceUri} or GET {asset.DownloadPath}."],
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
        }
        catch (BlobTransferException exception)
        {
            return ToolOutcome.Fail(exception.Code, exception.Message,
                "query(kind: \"system.blobs\", id: \"<64-hex-sha256>\")", "Rejected an invalid blob query.");
        }
    });

    public Task<ToolEnvelope> BeginAsync(
        IBlobTransferService? blobs,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? proceduresUsed,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(
            log, "commit", intent, "commit:system.blob-upload.begin", proceduresUsed,
            async () =>
            {
                var decision = authorization?.Authorize(PrivateOperatorCapability.Modify);
                if (decision is null || !decision.Allowed) return Denied(decision, "system.blob-upload.begin");
                if (blobs is null)
                    return ToolOutcome.Fail("BLOB_STORAGE_UNAVAILABLE", "Blob storage is not configured.",
                        "query(kind: \"capabilities\")", "Blob storage was unavailable.");
                try
                {
                    var request = JsonSerializer.Deserialize<BeginPayload>(payload, Json)
                        ?? throw new BlobTransferException("INVALID_PAYLOAD", "Payload must be a JSON object.");
                    var result = await blobs.BeginUploadAsync(
                        new(request.Sha256 ?? string.Empty, request.MediaType ?? string.Empty, request.ByteLength),
                        cancellationToken);
                    return new ToolOutcome(
                        new
                        {
                            result.UploadId,
                            result.UploadToken,
                            PutUrl = result.UploadPath,
                            UploadHeader = "X-DantesRoleplay-Upload-Token",
                            result.ExpiresAtUtc
                        },
                        "Created a short-lived one-use blob upload capability.",
                        [$"PUT raw bytes to {result.UploadPath} with header X-DantesRoleplay-Upload-Token, then commit(kind: \"system.blob-upload.finalize\", payload: \"{{...}}\")."],
                        GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
                }
                catch (JsonException exception)
                {
                    return Invalid("INVALID_PAYLOAD", exception.Message, "system.blob-upload.begin");
                }
                catch (BlobTransferException exception)
                {
                    return Invalid(exception.Code, exception.Message, "system.blob-upload.begin");
                }
            }, consumesReadEvidence: false);

    public Task<ToolEnvelope> FinalizeAsync(
        IBlobTransferService? blobs,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? proceduresUsed,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(
            log, "commit", intent, "commit:system.blob-upload.finalize", proceduresUsed,
            async () =>
            {
                var decision = authorization?.Authorize(PrivateOperatorCapability.Modify);
                if (decision is null || !decision.Allowed) return Denied(decision, "system.blob-upload.finalize");
                if (blobs is null)
                    return ToolOutcome.Fail("BLOB_STORAGE_UNAVAILABLE", "Blob storage is not configured.",
                        "query(kind: \"capabilities\")", "Blob storage was unavailable.");
                try
                {
                    var request = JsonSerializer.Deserialize<FinalizePayload>(payload, Json)
                        ?? throw new BlobTransferException("INVALID_PAYLOAD", "Payload must be a JSON object.");
                    var asset = await blobs.FinalizeUploadAsync(
                        request.UploadId ?? string.Empty, request.UploadToken ?? string.Empty, cancellationToken);
                    return new ToolOutcome(
                        Describe(asset),
                        $"Finalized immutable blob {asset.Sha256}.",
                        [$"query(kind: \"system.blobs\", id: \"{asset.Sha256}\")",
                         "Attach assetKey through the existing world-media component using commit(kind: \"effects\", ...)."],
                        GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
                }
                catch (JsonException exception)
                {
                    return Invalid("INVALID_PAYLOAD", exception.Message, "system.blob-upload.finalize");
                }
                catch (BlobTransferException exception)
                {
                    return Invalid(exception.Code, exception.Message, "system.blob-upload.finalize");
                }
            }, consumesReadEvidence: false);

    private static object Describe(BlobAsset asset) => new
    {
        asset.Sha256,
        asset.AssetKey,
        asset.MediaType,
        asset.ByteLength,
        asset.CreatedAtUtc,
        asset.ResourceUri,
        DownloadUrl = asset.DownloadPath
    };

    private static ToolOutcome Invalid(string code, string why, string kind) =>
        ToolOutcome.Fail(code, why, McpVerbCatalog.CommitCall(kind), $"Rejected {kind} payload.");

    private static ToolOutcome Denied(PrivateOperatorAuthorizationDecision? decision, string subject)
    {
        var why = decision is null
            ? "Private operator authorization is unavailable."
            : "Private-operator authorization is required.";
        return new ToolOutcome(null, $"Denied access to {subject}.", ["orient()"],
            new ToolError("AUTHORIZATION_DENIED", why, "orient()"),
            GuardEvidenceJson: decision is null ? string.Empty : JsonSerializer.Serialize(decision.Evidence));
    }

    private sealed record BeginPayload(string? Sha256, string? MediaType, long ByteLength);
    private sealed record FinalizePayload(string? UploadId, string? UploadToken);
}
