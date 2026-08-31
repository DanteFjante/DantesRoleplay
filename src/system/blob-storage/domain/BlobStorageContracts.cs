namespace DantesRoleplay.Blobs;

/// <summary>Metadata for one immutable, content-addressed binary object.</summary>
public sealed class BlobAsset
{
    private BlobAsset() { }

    public BlobAsset(string sha256, string mediaType, long byteLength, DateTimeOffset createdAtUtc)
    {
        Sha256 = sha256;
        MediaType = mediaType;
        ByteLength = byteLength;
        CreatedAtUtc = createdAtUtc;
    }

    public string Sha256 { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = string.Empty;
    public long ByteLength { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string AssetKey => $"sha256.{Sha256}.{BlobMediaTypes.Extension(MediaType)}";
    public string ResourceUri => $"media://blob/sha256/{Sha256}";
    public string DownloadPath => $"/api/blobs/sha256/{Sha256}";
}

/// <summary>Short-lived capability for transferring bytes into a declared immutable blob.</summary>
public sealed class BlobUploadSession
{
    private BlobUploadSession() { }

    public BlobUploadSession(
        string id,
        string tokenHash,
        string expectedSha256,
        string mediaType,
        long expectedByteLength,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Id = id;
        TokenHash = tokenHash;
        ExpectedSha256 = expectedSha256;
        MediaType = mediaType;
        ExpectedByteLength = expectedByteLength;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        State = BlobUploadStates.Pending;
    }

    public string Id { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public string ExpectedSha256 { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = string.Empty;
    public long ExpectedByteLength { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public string State { get; private set; } = BlobUploadStates.Pending;
    public DateTimeOffset? UploadedAtUtc { get; private set; }
    public DateTimeOffset? FinalizedAtUtc { get; private set; }

    public void MarkUploaded(DateTimeOffset uploadedAtUtc)
    {
        State = BlobUploadStates.Uploaded;
        UploadedAtUtc = uploadedAtUtc;
    }

    public void MarkFinalized(DateTimeOffset finalizedAtUtc)
    {
        State = BlobUploadStates.Finalized;
        FinalizedAtUtc = finalizedAtUtc;
    }
}

public static class BlobUploadStates
{
    public const string Pending = "pending";
    public const string Uploaded = "uploaded";
    public const string Finalized = "finalized";
}

public static class BlobMediaTypes
{
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";
    public const string WebP = "image/webp";

    public static bool IsAllowed(string mediaType) => mediaType is Png or Jpeg or WebP;

    public static string Extension(string mediaType) => mediaType switch
    {
        Png => "png",
        Jpeg => "jpg",
        WebP => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Unsupported blob media type.")
    };
}

public sealed record BeginBlobUploadRequest(string Sha256, string MediaType, long ByteLength);
public sealed record BeginBlobUploadResult(string UploadId, string UploadToken, string UploadPath, DateTimeOffset ExpiresAtUtc);
public sealed record BlobReadResult(BlobAsset Asset, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IBlobTransferService
{
    Task<BeginBlobUploadResult> BeginUploadAsync(BeginBlobUploadRequest request, CancellationToken cancellationToken = default);
    Task UploadAsync(string uploadId, string uploadToken, Stream content, CancellationToken cancellationToken = default);
    Task<BlobAsset> FinalizeUploadAsync(string uploadId, string uploadToken, CancellationToken cancellationToken = default);
    Task<BlobAsset?> FindAsync(string sha256, CancellationToken cancellationToken = default);
    Task<BlobReadResult?> OpenReadAsync(string sha256, CancellationToken cancellationToken = default);
}

public sealed class BlobTransferException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
