using DantesRoleplay.Authorization;
using DantesRoleplay.Blobs;
using DantesRoleplay.Web.Security;
using DantesRoleplay.Operations;
using System.Text.Json;

namespace DantesRoleplay.MCPServer;

public static class BlobTransferWebEndpoints
{
    public static async Task<IResult> UploadAsync(
        string uploadId,
        HttpContext context,
        IBlobTransferService blobs,
        WebPrivateOperatorGuard authorization,
        IOperationLog log,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var decision = authorization.Evaluate(context, PrivateOperatorCapability.Modify);
        if (!decision.Allowed)
        {
            await log.RecordAsync("blob-upload", "Denied blob byte upload.", false, subject: uploadId,
                error: decision.ErrorCode ?? "AUTHORIZATION_DENIED", cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            return Results.Json(new { code = decision.ErrorCode, why = decision.ErrorMessage }, statusCode: 403);
        }
        if (request.ContentLength is > BlobStorageOptions.MaximumByteLength)
        {
            await log.RecordAsync("blob-upload", "Rejected oversized blob byte upload.", false,
                subject: uploadId, error: "BLOB_LENGTH_INVALID", cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            return Results.Json(new { code = "BLOB_LENGTH_INVALID", why = "Request body is too large." }, statusCode: 413);
        }
        try
        {
            var token = request.Headers["X-DantesRoleplay-Upload-Token"].ToString();
            await blobs.UploadAsync(uploadId, token, request.Body, cancellationToken);
            await log.RecordAsync("blob-upload", "Transferred verified blob bytes.", true, subject: uploadId,
                cancellationToken: cancellationToken, guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            return Results.NoContent();
        }
        catch (BlobTransferException exception)
        {
            await log.RecordAsync("blob-upload", $"Rejected blob byte upload: {exception.Code}.", false,
                subject: uploadId, error: exception.Code, cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            return Results.Json(new { code = exception.Code, why = exception.Message }, statusCode: Status(exception.Code));
        }
    }

    public static async Task<IResult> DownloadAsync(
        string sha256,
        HttpContext context,
        IBlobTransferService blobs,
        WebPrivateOperatorGuard authorization,
        IOperationLog log,
        CancellationToken cancellationToken)
    {
        var decision = authorization.Evaluate(context, PrivateOperatorCapability.Read);
        if (!decision.Allowed)
        {
            await log.RecordAsync("blob-download", "Denied blob byte download.", false, subject: sha256,
                error: decision.ErrorCode ?? "AUTHORIZATION_DENIED", cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            return Results.Json(new { code = decision.ErrorCode, why = decision.ErrorMessage }, statusCode: 403);
        }
        try
        {
            var result = await blobs.OpenReadAsync(sha256, cancellationToken);
            await log.RecordAsync("blob-download",
                result is null ? "Blob bytes were not found." : "Returned blob bytes.", result is not null,
                subject: sha256, error: result is null ? "BLOB_NOT_FOUND" : string.Empty,
                cancellationToken: cancellationToken, guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            return result is null
                ? Results.NotFound(new { code = "BLOB_NOT_FOUND", why = "No finalized blob matched that SHA-256 digest." })
                : Results.Stream(result.Content, result.Asset.MediaType, enableRangeProcessing: true);
        }
        catch (BlobTransferException exception)
        {
            await log.RecordAsync("blob-download", $"Failed blob byte download: {exception.Code}.", false,
                subject: sha256, error: exception.Code, cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
            return Results.Json(new { code = exception.Code, why = exception.Message }, statusCode: Status(exception.Code));
        }
    }

    private static int Status(string code) => code switch
    {
        "BLOB_UPLOAD_UNAUTHORIZED" => 401,
        "BLOB_UPLOAD_EXPIRED" => 410,
        "BLOB_LENGTH_INVALID" or "BLOB_LENGTH_MISMATCH" => 413,
        "BLOB_CONTENT_MISSING" => 500,
        _ => 400
    };
}
