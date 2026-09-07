using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DantesRoleplay.Web.Interactions;

internal sealed record BrowserComponentAsset(
    byte[] Content,
    byte[]? BrotliContent,
    byte[]? GzipContent,
    string EntityTag,
    DateTimeOffset LastModified);

/// <summary>Loads reviewed browser-component assets without compiling application vocabulary into the host.</summary>
public static class BrowserComponentAssets
{
    public const int MaximumNameLength = 80;
    private const int MaximumCachedAssets = 32;
    private const string DirectoryName = "BrowserComponents";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly SemaphoreSlim LoadGate = new(1, 1);
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    public static async Task<string?> ReadAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        var asset = await GetAsync(name, cancellationToken);
        return asset is null ? null : StrictUtf8.GetString(asset.Content);
    }

    internal static async Task<BrowserComponentAsset?> GetAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidName(name)) return null;
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, DirectoryName, name + ".js"));
        if (!TryStamp(path, out var stamp))
        {
            lock (CacheGate) Cache.Remove(path);
            return null;
        }
        lock (CacheGate)
            if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                return cached.Asset;

        await LoadGate.WaitAsync(cancellationToken);
        try
        {
            if (!TryStamp(path, out stamp))
            {
                lock (CacheGate) Cache.Remove(path);
                return null;
            }
            lock (CacheGate)
                if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                    return cached.Asset;

            var content = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!TryStamp(path, out var afterRead) || afterRead != stamp)
                return await LoadChangedAsync(path, cancellationToken);
            _ = StrictUtf8.GetCharCount(content);
            var asset = new BrowserComponentAsset(
                content,
                Compress(content, brotli: true),
                Compress(content, brotli: false),
                $"W/\"{Convert.ToHexString(SHA256.HashData(content))}\"",
                new DateTimeOffset(new DateTime(stamp.LastWriteTimeUtcTicks, DateTimeKind.Utc))
                    .AddTicks(-(stamp.LastWriteTimeUtcTicks % TimeSpan.TicksPerSecond)));
            lock (CacheGate)
            {
                if (Cache.Count < MaximumCachedAssets || Cache.ContainsKey(path))
                    Cache[path] = new(stamp, asset);
            }
            return asset;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or DecoderFallbackException or InvalidDataException)
        {
            lock (CacheGate) Cache.Remove(path);
            return null;
        }
        finally { LoadGate.Release(); }
    }

    private static async Task<BrowserComponentAsset?> LoadChangedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!TryStamp(path, out var stamp)) return null;
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!TryStamp(path, out var afterRead) || afterRead != stamp) return null;
        _ = StrictUtf8.GetCharCount(content);
        var asset = new BrowserComponentAsset(
            content,
            Compress(content, brotli: true),
            Compress(content, brotli: false),
            $"W/\"{Convert.ToHexString(SHA256.HashData(content))}\"",
            new DateTimeOffset(new DateTime(stamp.LastWriteTimeUtcTicks, DateTimeKind.Utc))
                .AddTicks(-(stamp.LastWriteTimeUtcTicks % TimeSpan.TicksPerSecond)));
        lock (CacheGate)
        {
            if (Cache.Count < MaximumCachedAssets || Cache.ContainsKey(path))
                Cache[path] = new(stamp, asset);
        }
        return asset;
    }

    private static byte[]? Compress(byte[] content, bool brotli)
    {
        using var output = new MemoryStream();
        using (Stream compressor = brotli
                   ? new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
                   : new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            compressor.Write(content);
        var compressed = output.ToArray();
        return compressed.Length < content.Length ? compressed : null;
    }

    private static bool TryStamp(string path, out AssetStamp stamp)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            stamp = default;
            return false;
        }
        stamp = new(file.Length, file.LastWriteTimeUtc.Ticks);
        return true;
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= MaximumNameLength &&
        name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private readonly record struct AssetStamp(long Length, long LastWriteTimeUtcTicks);
    private sealed record CacheEntry(AssetStamp Stamp, BrowserComponentAsset Asset);
}
