using System.Security.Cryptography;
using DantesRoleplay.Blobs;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Imports reviewed image files through the same bounded, signature-checking upload service used
/// by the web and MCP surfaces. This only creates immutable blob assets; entity associations stay
/// a separate, reviewed ECS lifecycle operation.
/// </summary>
public sealed class ImportMediaTool : ITool
{
    public string Name => "import-media";
    public string Summary => "Verify image files and import them into the database-owned blob store.";
    public string Usage => """
        roleplay import-media <image> [<image> ...] [--database <path>] [--blob-root <path>]

        Imports PNG, JPEG, and WebP files through the production blob upload service. The service
        verifies each declared length, SHA-256 digest, and media signature before finalizing it.
        Existing content is reopened and rehashed instead of trusted by metadata alone.

        The blob root defaults to a blobs directory beside the selected database. This command
        does not edit ECS components or delete the source files.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Count == 0)
        {
            context.Error.WriteLine("import-media needs at least one image file.");
            return 2;
        }

        var files = context.Arguments.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in files)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Media source file not found.", path);
            var length = new FileInfo(path).Length;
            if (length is < 1 or > BlobStorageOptions.MaximumByteLength)
                throw new BlobTransferException("BLOB_LENGTH_INVALID",
                    $"'{path}' must contain between 1 and {BlobStorageOptions.MaximumByteLength} bytes.");
            _ = MediaType(path);
        }

        var blobRoot = Path.GetFullPath(context.Option("blob-root")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(context.DatabasePath))!, "blobs"));
        await using var db = context.OpenDatabase();
        var blobs = new FileBlobTransferService(db, new BlobStorageOptions(blobRoot));
        foreach (var path in files)
        {
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken)).ToLowerInvariant();
            source.Position = 0;

            var existing = await blobs.FindAsync(digest, cancellationToken);
            BlobAsset asset;
            if (existing is null)
            {
                var begin = await blobs.BeginUploadAsync(
                    new(digest, MediaType(path), source.Length), cancellationToken);
                await blobs.UploadAsync(begin.UploadId, begin.UploadToken, source, cancellationToken);
                asset = await blobs.FinalizeUploadAsync(begin.UploadId, begin.UploadToken, cancellationToken);
            }
            else
            {
                asset = existing;
            }

            await VerifyFinalizedAsync(blobs, asset, digest, source.Length, cancellationToken);
            context.Out.WriteLine($"{digest}  {asset.MediaType}  {asset.ByteLength}  {path}");
        }

        context.Out.WriteLine($"Verified {files.Length} image file(s) in {blobRoot}.");
        return 0;
    }

    private static async Task VerifyFinalizedAsync(
        IBlobTransferService blobs,
        BlobAsset asset,
        string expectedDigest,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        if (asset.Sha256 != expectedDigest || asset.ByteLength != expectedLength)
            throw new BlobTransferException("BLOB_METADATA_CONFLICT",
                "Finalized blob metadata does not match the reviewed source file.");
        await using var read = await blobs.OpenReadAsync(expectedDigest, cancellationToken)
            ?? throw new BlobTransferException("BLOB_CONTENT_MISSING", "Finalized blob bytes are missing.");
        var observed = Convert.ToHexString(await SHA256.HashDataAsync(read.Content, cancellationToken)).ToLowerInvariant();
        if (observed != expectedDigest)
            throw new BlobTransferException("BLOB_CONTENT_CORRUPT",
                "Finalized blob bytes do not match their SHA-256 identity.");
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => BlobMediaTypes.Png,
        ".jpg" or ".jpeg" => BlobMediaTypes.Jpeg,
        ".webp" => BlobMediaTypes.WebP,
        _ => throw new BlobTransferException("BLOB_MEDIA_TYPE_INVALID",
            $"'{path}' is not a PNG, JPEG, or WebP image.")
    };
}
