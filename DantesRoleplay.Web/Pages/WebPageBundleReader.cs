using System.IO.Compression;
using System.Text;

namespace DantesRoleplay.Web.Pages;

public sealed class WebPageBundleException(
    string code,
    string message,
    int statusCode = 400,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}

public sealed class WebPageBundleReader
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<WebPageBundle> ReadAsync(
        Stream input,
        long? declaredLength = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (declaredLength > WebPageBundleLimits.MaximumCompressedBytes)
        {
            throw TooLarge("The ZIP upload exceeds the 10 MiB compressed limit.");
        }

        await using var buffered = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffered.Length + read > WebPageBundleLimits.MaximumCompressedBytes)
            {
                throw TooLarge("The ZIP upload exceeds the 10 MiB compressed limit.");
            }

            await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (buffered.Length == 0)
        {
            throw Invalid("EMPTY_BUNDLE", "The ZIP upload cannot be empty.");
        }

        buffered.Position = 0;
        try
        {
            using var archive = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: true);
            return await MaterializeAsync(archive, cancellationToken);
        }
        catch (WebPageBundleException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or NotSupportedException)
        {
            throw Invalid("INVALID_ZIP", "The upload is not a readable ZIP archive.", exception);
        }
    }

    private static async Task<WebPageBundle> MaterializeAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        string? html = null;
        string? composition = null;
        var assets = new List<WebPageAssetUpload>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var entryCount = 0;
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0)
            {
                continue;
            }

            entryCount++;
            if (entryCount > WebPageBundleLimits.MaximumEntries)
            {
                throw TooLarge("The ZIP contains more than 256 regular files.");
            }

            if (!WebPageAssetPath.TryValidate(entry.FullName, out var path))
            {
                throw Invalid("UNSAFE_ASSET_PATH", $"The ZIP path '{entry.FullName}' is not safe.");
            }

            if (!paths.Add(path))
            {
                throw Invalid("DUPLICATE_ASSET_PATH", $"The ZIP path '{path}' appears more than once.");
            }

            var entryLimit = path is "index.html" or "composition.json"
                ? WebPageBundleLimits.MaximumHtmlBytes
                : WebPageBundleLimits.MaximumEntryBytes;
            if (path is not "index.html" and not "composition.json" && !path.StartsWith("assets/", StringComparison.Ordinal))
            {
                throw Invalid(
                    "ASSET_ROOT_REQUIRED",
                    $"The ZIP asset '{path}' must be below the root assets/ directory.");
            }

            if (entry.Length > entryLimit)
            {
                throw TooLarge($"The ZIP entry '{path}' exceeds its size limit.");
            }

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > WebPageBundleLimits.MaximumUncompressedBytes)
            {
                throw TooLarge("The ZIP exceeds the 25 MiB uncompressed limit.");
            }

            var content = await ReadEntryAsync(entry, entryLimit, cancellationToken);
            if (path is "index.html" or "composition.json")
            {
                try
                {
                    var text = StrictUtf8.GetString(content);
                    if (path == "index.html") html = text;
                    else composition = text;
                }
                catch (DecoderFallbackException exception)
                {
                    throw path == "index.html"
                        ? Invalid("INVALID_HTML_ENCODING", "index.html must be valid UTF-8.", exception)
                        : Invalid("INVALID_COMPOSITION_ENCODING", "composition.json must be valid UTF-8.", exception);
                }
            }
            else
            {
                assets.Add(new WebPageAssetUpload(path, content));
            }
        }

        if (html is not null && composition is not null)
        {
            throw Invalid("AMBIGUOUS_CONTENT", "A bundle must contain exactly one of index.html or composition.json.");
        }
        if (html is null && composition is null)
        {
            throw Invalid("MISSING_INDEX", "The ZIP must contain one root index.html or composition.json file.");
        }
        return WebPageContentValidator.Normalize(html is not null
            ? new WebPageBundle(html, assets)
            : new WebPageBundle(string.Empty, assets)
            {
                ContentFormat = WebPageContentFormat.Composition,
                CompositionJson = composition
            });
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var source = entry.Open();
        await using var destination = new MemoryStream(
            entry.Length is > 0 and <= int.MaxValue ? (int)entry.Length : 0);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw TooLarge($"The ZIP entry '{entry.FullName}' exceeds its size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
    }

    private static WebPageBundleException Invalid(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, 400, innerException);

    private static WebPageBundleException TooLarge(string message) =>
        new("BUNDLE_TOO_LARGE", message, 413);
}
