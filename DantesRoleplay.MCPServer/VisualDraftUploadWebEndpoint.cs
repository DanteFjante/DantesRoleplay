using System.Buffers.Binary;
using System.Security.Cryptography;
using DantesRoleplay.Authorization;
using DantesRoleplay.Blobs;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Operations;
using DantesRoleplay.Web.Security;

namespace DantesRoleplay.MCPServer;

/// <summary>Bounded private image transfer only. This endpoint never attaches media or executes an action.</summary>
public static class VisualDraftUploadWebEndpoint
{
    public static async Task<IResult> UploadAsync(string applicationId, HttpContext context,
        ILocalKnowledgeSeatProvider seats, IBlobTransferService blobs, WebPrivateOperatorGuard authorization,
        IOperationLog log, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var seat = seats.Current();
        if (!seat.Enabled || seat.ApplicationId != applicationId || seat.Role != KnowledgeAudienceRole.GameMaster ||
            !authorization.Evaluate(context, PrivateOperatorCapability.Modify).Allowed)
            return Error("DRAFT_IMAGE_DENIED", 403);
        if (context.Request.ContentType != "image/png") return Error("DRAFT_IMAGE_TYPE", 415);
        if (!int.TryParse(context.Request.Headers["X-Image-Width"], out var expectedWidth) ||
            !int.TryParse(context.Request.Headers["X-Image-Height"], out var expectedHeight) ||
            expectedWidth is < 1 or > 10_000 || expectedHeight is < 1 or > 10_000)
            return Error("DRAFT_IMAGE_DIMENSIONS", 400);
        if (context.Request.ContentLength is > BlobStorageOptions.MaximumByteLength) return Error("DRAFT_IMAGE_LENGTH", 413);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16_384];
            int count;
            while ((count = await context.Request.Body.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + count > BlobStorageOptions.MaximumByteLength) return Error("DRAFT_IMAGE_LENGTH", 413);
                await buffer.WriteAsync(chunk.AsMemory(0, count), deadline.Token);
            }
            var bytes = buffer.ToArray();
            var size = PngDimensions(bytes);
            if (size is null) return Error("DRAFT_IMAGE_INVALID", 422);
            if (size.Value.Width != expectedWidth || size.Value.Height != expectedHeight)
                return Error("DRAFT_IMAGE_DIMENSIONS", 422);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var upload = await blobs.BeginUploadAsync(new(hash, BlobMediaTypes.Png, bytes.Length), deadline.Token);
            buffer.Position = 0;
            await blobs.UploadAsync(upload.UploadId, upload.UploadToken, buffer, deadline.Token);
            var asset = await blobs.FinalizeUploadAsync(upload.UploadId, upload.UploadToken, deadline.Token);
            await log.RecordAsync("visual-draft-upload", "Stored a private finalized image for explicit review; no game state changed.",
                true, subject: asset.Sha256, cancellationToken: cancellationToken);
            return Results.Json(new { asset.Sha256, asset.MediaType, asset.ByteLength, size.Value.Width, size.Value.Height });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Error("DRAFT_IMAGE_TIMEOUT", 504); }
        catch (BlobTransferException) { return Error("DRAFT_IMAGE_REJECTED", 422); }
    }

    public static (int Width, int Height)? PngDimensions(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13 || !bytes[12..16].SequenceEqual("IHDR"u8)) return null;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        return width is > 0 and <= 10_000 && height is > 0 and <= 10_000 ? ((int)width, (int)height) : null;
    }

    private static IResult Error(string code, int status) => Results.Json(new { code,
        message = "The draft image is unavailable. Keep the structured grid and retry with a valid PNG." }, statusCode: status);
}
