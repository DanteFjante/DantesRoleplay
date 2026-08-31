using System.Security.Cryptography;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Blobs;

public sealed record BlobStorageOptions(string RootPath)
{
    public const long MaximumByteLength = 10 * 1024 * 1024;
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Keeps immutable bytes outside SQLite while recording authoritative metadata and upload state in it.
/// All filesystem names are derived from server-issued IDs or validated SHA-256 digests.
/// </summary>
public sealed class FileBlobTransferService(
    DantesRoleplayDbContext db,
    BlobStorageOptions options) : IBlobTransferService
{
    public async Task<BeginBlobUploadResult> BeginUploadAsync(
        BeginBlobUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var sha256 = NormalizeSha256(request.Sha256);
        var mediaType = NormalizeMediaType(request.MediaType);
        if (request.ByteLength is < 1 or > BlobStorageOptions.MaximumByteLength)
            throw new BlobTransferException("BLOB_LENGTH_INVALID",
                $"byteLength must be between 1 and {BlobStorageOptions.MaximumByteLength} bytes.");

        var now = DateTimeOffset.UtcNow;
        await CleanupExpiredSessionsAsync(now, cancellationToken);
        var id = $"blob-upload.{RandomHex(16)}";
        var token = RandomHex(32);
        var session = new BlobUploadSession(
            id, HashToken(token), sha256, mediaType, request.ByteLength, now,
            now.Add(BlobStorageOptions.UploadLifetime));
        db.BlobUploadSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return new(id, token, $"/api/blob-uploads/{id}", session.ExpiresAtUtc);
    }

    public async Task UploadAsync(
        string uploadId,
        string uploadToken,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(uploadId, uploadToken, cancellationToken);
        if (session.State != BlobUploadStates.Pending)
            throw new BlobTransferException("BLOB_UPLOAD_ALREADY_USED", "This upload capability has already been used.");

        Directory.CreateDirectory(UploadDirectory);
        var temporaryPath = UploadPath(session.Id);
        try
        {
            await using var destination = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            var signature = new byte[12];
            var signatureLength = 0;
            long total = 0;
            while (true)
            {
                var read = await content.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > session.ExpectedByteLength || total > BlobStorageOptions.MaximumByteLength)
                    throw new BlobTransferException("BLOB_LENGTH_MISMATCH", "Uploaded bytes exceed the declared byteLength.");
                if (signatureLength < signature.Length)
                {
                    var copy = Math.Min(read, signature.Length - signatureLength);
                    buffer.AsSpan(0, copy).CopyTo(signature.AsSpan(signatureLength));
                    signatureLength += copy;
                }
                hasher.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await destination.FlushAsync(cancellationToken);

            if (total != session.ExpectedByteLength)
                throw new BlobTransferException("BLOB_LENGTH_MISMATCH", "Uploaded bytes do not match the declared byteLength.");
            var observedHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(observedHash), Convert.FromHexString(session.ExpectedSha256)))
                throw new BlobTransferException("BLOB_HASH_MISMATCH", "Uploaded bytes do not match the declared SHA-256 digest.");
            if (!SignatureMatches(session.MediaType, signature.AsSpan(0, signatureLength)))
                throw new BlobTransferException("BLOB_MEDIA_TYPE_MISMATCH", "Uploaded bytes do not match the declared mediaType.");

            session.MarkUploaded(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    public async Task<BlobAsset> FinalizeUploadAsync(
        string uploadId,
        string uploadToken,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(uploadId, uploadToken, cancellationToken, allowFinalized: true);
        var existing = await db.BlobAssets.FindAsync([session.ExpectedSha256], cancellationToken);
        if (session.State == BlobUploadStates.Finalized)
            return existing ?? throw new BlobTransferException("BLOB_CONTENT_MISSING", "Finalized blob metadata is missing.");
        if (session.State != BlobUploadStates.Uploaded)
            throw new BlobTransferException("BLOB_UPLOAD_INCOMPLETE", "Upload the declared bytes before finalizing.");

        var temporaryPath = UploadPath(session.Id);
        if (!File.Exists(temporaryPath))
            throw new BlobTransferException("BLOB_CONTENT_MISSING", "Uploaded temporary bytes are missing.");
        var finalPath = ContentPath(session.ExpectedSha256);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (!File.Exists(finalPath))
        {
            File.Move(temporaryPath, finalPath);
        }
        else
        {
            var existingInfo = new FileInfo(finalPath);
            if (existingInfo.Length != session.ExpectedByteLength ||
                !string.Equals(await FileSha256Async(finalPath, cancellationToken), session.ExpectedSha256,
                    StringComparison.Ordinal))
                throw new BlobTransferException("BLOB_CONTENT_CORRUPT",
                    "Existing content-addressed bytes do not match their path.");
            File.Delete(temporaryPath);
        }

        existing ??= new BlobAsset(
            session.ExpectedSha256, session.MediaType, session.ExpectedByteLength, DateTimeOffset.UtcNow);
        if (existing.MediaType != session.MediaType || existing.ByteLength != session.ExpectedByteLength)
            throw new BlobTransferException("BLOB_METADATA_CONFLICT",
                "Existing blob metadata conflicts with the verified bytes.");
        if (db.Entry(existing).State == EntityState.Detached) db.BlobAssets.Add(existing);
        session.MarkFinalized(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public Task<BlobAsset?> FindAsync(string sha256, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSha256(sha256);
        return db.BlobAssets.AsNoTracking().SingleOrDefaultAsync(value => value.Sha256 == normalized, cancellationToken);
    }

    public async Task<BlobReadResult?> OpenReadAsync(string sha256, CancellationToken cancellationToken = default)
    {
        var asset = await FindAsync(sha256, cancellationToken);
        if (asset is null) return null;
        var path = ContentPath(asset.Sha256);
        if (!File.Exists(path)) throw new BlobTransferException("BLOB_CONTENT_MISSING", "Blob metadata exists but its bytes are missing.");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new(asset, stream);
    }

    private async Task<BlobUploadSession> RequireSessionAsync(
        string uploadId,
        string uploadToken,
        CancellationToken cancellationToken,
        bool allowFinalized = false)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || string.IsNullOrWhiteSpace(uploadToken))
            throw new BlobTransferException("BLOB_UPLOAD_UNAUTHORIZED", "uploadId and uploadToken are required.");
        var session = await db.BlobUploadSessions.SingleOrDefaultAsync(value => value.Id == uploadId, cancellationToken)
            ?? throw new BlobTransferException("BLOB_UPLOAD_UNAUTHORIZED", "The upload capability is invalid.");
        var presented = HashToken(uploadToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(presented), Convert.FromHexString(session.TokenHash)))
            throw new BlobTransferException("BLOB_UPLOAD_UNAUTHORIZED", "The upload capability is invalid.");
        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow && !(allowFinalized && session.State == BlobUploadStates.Finalized))
            throw new BlobTransferException("BLOB_UPLOAD_EXPIRED", "The upload capability has expired.");
        return session;
    }

    private string Root => Path.GetFullPath(options.RootPath);
    private string UploadDirectory => Path.Combine(Root, "uploads");
    private string UploadPath(string id) => Path.Combine(UploadDirectory, $"{id}.tmp");
    private string ContentPath(string sha256) => Path.Combine(Root, "content", sha256[..2], sha256[2..4], sha256);

    private static string NormalizeSha256(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new BlobTransferException("BLOB_SHA256_INVALID", "sha256 must be exactly 64 hexadecimal characters.");
        return normalized;
    }

    private static string NormalizeMediaType(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!BlobMediaTypes.IsAllowed(normalized))
            throw new BlobTransferException("BLOB_MEDIA_TYPE_INVALID", "mediaType must be image/png, image/jpeg, or image/webp.");
        return normalized;
    }

    private static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static async Task<string> FileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private async Task CleanupExpiredSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var finalizedCutoff = now.AddHours(-24);
        // SQLite cannot compare DateTimeOffset in SQL. Bound the candidate reads by state, then
        // apply the time predicate in memory; each begin call removes at most 100 stale rows.
        var unfinished = await db.BlobUploadSessions
            .Where(value => value.State != BlobUploadStates.Finalized)
            .Take(1_000)
            .ToListAsync(cancellationToken);
        var finalized = await db.BlobUploadSessions
            .Where(value => value.State == BlobUploadStates.Finalized)
            .Take(1_000)
            .ToListAsync(cancellationToken);
        var stale = unfinished.Where(value => value.ExpiresAtUtc <= now)
            .Concat(finalized.Where(value => value.FinalizedAtUtc < finalizedCutoff))
            .Take(100)
            .ToList();
        foreach (var session in stale)
        {
            var path = UploadPath(session.Id);
            if (File.Exists(path)) File.Delete(path);
        }
        if (stale.Count == 0) return;
        db.BlobUploadSessions.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool SignatureMatches(string mediaType, ReadOnlySpan<byte> bytes) => mediaType switch
    {
        BlobMediaTypes.Png => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        BlobMediaTypes.Jpeg => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
        BlobMediaTypes.WebP => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
