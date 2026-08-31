using System.Security.Cryptography;
using DantesRoleplay.Blobs;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests.Blobs;

public sealed class BlobStorageTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dantes-roleplay-blob-tests-{Guid.NewGuid():N}");
    private DantesRoleplayDbContext _db = null!;
    private FileBlobTransferService _service = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "test.db")};Pooling=False")
            .Options;
        _db = new DantesRoleplayDbContext(options);
        await _db.Database.MigrateAsync();
        _service = new FileBlobTransferService(_db, new BlobStorageOptions(Path.Combine(_root, "blobs")));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Upload_finalization_is_content_addressed_and_readable()
    {
        var bytes = PngBytes();
        var digest = Digest(bytes);
        var upload = await _service.BeginUploadAsync(new(digest, BlobMediaTypes.Png, bytes.Length));

        await _service.UploadAsync(upload.UploadId, upload.UploadToken, new MemoryStream(bytes));
        var asset = await _service.FinalizeUploadAsync(upload.UploadId, upload.UploadToken);
        await using var read = await _service.OpenReadAsync(digest);

        Assert.NotNull(read);
        Assert.Equal(digest, asset.Sha256);
        Assert.Equal($"sha256.{digest}.png", asset.AssetKey);
        Assert.Equal($"media://blob/sha256/{digest}", asset.ResourceUri);
        using var copy = new MemoryStream();
        await read!.Content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task Upload_rejects_wrong_hash_and_does_not_publish_metadata()
    {
        var bytes = PngBytes();
        var upload = await _service.BeginUploadAsync(new(new string('a', 64), BlobMediaTypes.Png, bytes.Length));

        var failure = await Assert.ThrowsAsync<BlobTransferException>(() =>
            _service.UploadAsync(upload.UploadId, upload.UploadToken, new MemoryStream(bytes)));

        Assert.Equal("BLOB_HASH_MISMATCH", failure.Code);
        Assert.Null(await _service.FindAsync(new string('a', 64)));
    }

    [Fact]
    public async Task Upload_rejects_bytes_that_do_not_match_declared_media_type()
    {
        var bytes = PngBytes();
        var upload = await _service.BeginUploadAsync(new(Digest(bytes), BlobMediaTypes.Jpeg, bytes.Length));

        var failure = await Assert.ThrowsAsync<BlobTransferException>(() =>
            _service.UploadAsync(upload.UploadId, upload.UploadToken, new MemoryStream(bytes)));

        Assert.Equal("BLOB_MEDIA_TYPE_MISMATCH", failure.Code);
    }

    [Fact]
    public async Task Upload_capability_rejects_a_wrong_secret()
    {
        var bytes = PngBytes();
        var upload = await _service.BeginUploadAsync(new(Digest(bytes), BlobMediaTypes.Png, bytes.Length));

        var failure = await Assert.ThrowsAsync<BlobTransferException>(() =>
            _service.UploadAsync(upload.UploadId, new string('0', 64), new MemoryStream(bytes)));

        Assert.Equal("BLOB_UPLOAD_UNAUTHORIZED", failure.Code);
    }

    [Fact]
    public async Task Finalize_is_idempotent_but_upload_capability_is_one_use()
    {
        var bytes = PngBytes();
        var upload = await _service.BeginUploadAsync(new(Digest(bytes), BlobMediaTypes.Png, bytes.Length));
        await _service.UploadAsync(upload.UploadId, upload.UploadToken, new MemoryStream(bytes));

        var first = await _service.FinalizeUploadAsync(upload.UploadId, upload.UploadToken);
        var second = await _service.FinalizeUploadAsync(upload.UploadId, upload.UploadToken);
        var replay = await Assert.ThrowsAsync<BlobTransferException>(() =>
            _service.UploadAsync(upload.UploadId, upload.UploadToken, new MemoryStream(bytes)));

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal("BLOB_UPLOAD_ALREADY_USED", replay.Code);
        Assert.Single(await _db.BlobAssets.ToListAsync());
    }

    [Fact]
    public async Task Begin_rejects_unsupported_media_and_oversized_content()
    {
        var digest = new string('a', 64);
        var media = await Assert.ThrowsAsync<BlobTransferException>(() =>
            _service.BeginUploadAsync(new(digest, "application/octet-stream", 16)));
        var length = await Assert.ThrowsAsync<BlobTransferException>(() =>
            _service.BeginUploadAsync(new(digest, BlobMediaTypes.Png, BlobStorageOptions.MaximumByteLength + 1)));

        Assert.Equal("BLOB_MEDIA_TYPE_INVALID", media.Code);
        Assert.Equal("BLOB_LENGTH_INVALID", length.Code);
    }

    private static byte[] PngBytes() => [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82];
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
