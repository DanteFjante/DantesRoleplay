using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DantesRoleplay.LocalAI;

public sealed class LocalDocumentScanner : ILocalDocumentScanner
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<LocalDocumentScanResult> ScanAsync(
        LocalDocumentScanRequest request,
        LocalDocumentScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        var invalid = Validate(request, options);
        if (invalid is not null) return LocalDocumentScanResult.Invalid(invalid);

        var problems = new List<LocalDocumentScanProblem>();
        var candidates = new Dictionary<string, string>(PathComparer);
        var allowedRoots = CanonicalRoots(options.AllowedRoots, problems);
        if (options.AllowedRoots.Count > 0 && allowedRoots.Count == 0)
            return new([], problems);

        foreach (var specification in request.PathSpecifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Discover(specification, options, allowedRoots, candidates, problems);
            if (candidates.Count > options.MaxDiscoveredPaths)
            {
                problems.Add(new(
                    "SCAN_DISCOVERY_LIMIT_EXCEEDED",
                    specification,
                    $"Discovery exceeded the {options.MaxDiscoveredPaths} path limit."));
                break;
            }
        }

        var documents = new List<ScannedLocalDocument>();
        long totalBytes = 0;
        foreach (var candidate in candidates.OrderBy(pair => pair.Key, PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (documents.Count == options.MaxFiles)
            {
                problems.Add(new(
                    "SCAN_FILE_LIMIT_EXCEEDED",
                    candidate.Value,
                    $"The scan reached the {options.MaxFiles} file limit."));
                break;
            }

            try
            {
                var info = new FileInfo(candidate.Key);
                if (!info.Exists)
                {
                    problems.Add(new("SCAN_FILE_MISSING", candidate.Value,
                        "The discovered file no longer exists.", candidate.Key));
                    continue;
                }
                if (IsReparse(info.Attributes))
                {
                    problems.Add(new("SCAN_REPARSE_POINT_SKIPPED", candidate.Value,
                        "Reparse points are not scanned.", candidate.Key));
                    continue;
                }
                if (info.Length > options.MaxFileBytes)
                {
                    problems.Add(new("SCAN_FILE_TOO_LARGE", candidate.Value,
                        $"The file exceeds the {options.MaxFileBytes} byte limit.", candidate.Key));
                    continue;
                }
                if (info.Length > options.MaxTotalBytes - totalBytes)
                {
                    problems.Add(new("SCAN_TOTAL_SIZE_EXCEEDED", candidate.Value,
                        $"Reading the file would exceed the {options.MaxTotalBytes} byte aggregate limit.",
                        candidate.Key));
                    break;
                }

                var content = await File.ReadAllBytesAsync(candidate.Key, cancellationToken);
                if (content.LongLength != info.Length || content.LongLength > options.MaxFileBytes)
                {
                    problems.Add(new("SCAN_FILE_CHANGED", candidate.Value,
                        "The file changed while it was being scanned.", candidate.Key));
                    continue;
                }

                totalBytes += content.LongLength;
                documents.Add(new(
                    candidate.Key,
                    content.LongLength,
                    Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                    MediaType(candidate.Key),
                    content,
                    DecodeText(content)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add(new("SCAN_FILE_UNREADABLE", candidate.Value,
                    Safe(exception.Message), candidate.Key));
            }
        }

        return new(documents, problems);
    }

    private static string? Validate(LocalDocumentScanRequest? request, LocalDocumentScanOptions options)
    {
        var invalidOptions = options.Validate();
        if (invalidOptions is not null) return invalidOptions;
        if (request is null || request.PathSpecifications is null ||
            request.PathSpecifications.Count is < 1 or > 128)
            return "PathSpecifications must contain between 1 and 128 paths.";
        if (request.PathSpecifications.Any(value => string.IsNullOrWhiteSpace(value) ||
                value != value.Trim() || value.Length > 4_096))
            return "Every path specification must be trimmed, nonblank, and at most 4096 characters.";
        return null;
    }

    private static void Discover(
        string specification,
        LocalDocumentScanOptions options,
        IReadOnlyList<string> allowedRoots,
        IDictionary<string, string> candidates,
        ICollection<LocalDocumentScanProblem> problems)
    {
        string fullSpecification;
        try { fullSpecification = Path.GetFullPath(specification); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problems.Add(new("SCAN_PATH_INVALID", specification, Safe(exception.Message)));
            return;
        }

        if (!HasWildcard(fullSpecification))
        {
            if (File.Exists(fullSpecification))
            {
                AddFile(fullSpecification, specification, options, allowedRoots, candidates, problems);
                return;
            }
            if (Directory.Exists(fullSpecification))
            {
                AddDirectory(fullSpecification, specification, options, allowedRoots, candidates, problems);
                return;
            }
            problems.Add(new("SCAN_PATH_NOT_FOUND", specification, "The file or directory does not exist."));
            return;
        }

        var root = ExpansionRoot(fullSpecification);
        if (!Directory.Exists(root))
        {
            problems.Add(new("SCAN_PATH_NOT_FOUND", specification,
                "The non-wildcard directory prefix does not exist.", root));
            return;
        }
        if (!Allowed(root, allowedRoots))
        {
            problems.Add(new("SCAN_PATH_OUTSIDE_ALLOWED_ROOT", specification,
                "The wildcard root is outside the allowed roots.", root));
            return;
        }

        var regex = GlobRegex(fullSpecification);
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", Enumeration(options)))
            {
                if (!regex.IsMatch(Normalize(entry))) continue;
                var attributes = File.GetAttributes(entry);
                if (IsReparse(attributes))
                {
                    problems.Add(new("SCAN_REPARSE_POINT_SKIPPED", specification,
                        "Reparse points are not scanned.", Path.GetFullPath(entry)));
                }
                else if ((attributes & FileAttributes.Directory) != 0)
                {
                    AddDirectory(entry, specification, options, allowedRoots, candidates, problems);
                }
                else
                {
                    AddFile(entry, specification, options, allowedRoots, candidates, problems);
                }
                if (candidates.Count > options.MaxDiscoveredPaths) return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add(new("SCAN_PATH_UNREADABLE", specification, Safe(exception.Message), root));
        }
    }

    private static void AddDirectory(
        string directory,
        string specification,
        LocalDocumentScanOptions options,
        IReadOnlyList<string> allowedRoots,
        IDictionary<string, string> candidates,
        ICollection<LocalDocumentScanProblem> problems)
    {
        var full = Path.GetFullPath(directory);
        if (!Allowed(full, allowedRoots))
        {
            problems.Add(new("SCAN_PATH_OUTSIDE_ALLOWED_ROOT", specification,
                "The directory is outside the allowed roots.", full));
            return;
        }
        try
        {
            if (IsReparse(File.GetAttributes(full)))
            {
                problems.Add(new("SCAN_REPARSE_POINT_SKIPPED", specification,
                    "Reparse points are not scanned.", full));
                return;
            }
            foreach (var file in Directory.EnumerateFiles(full, "*", Enumeration(options)))
            {
                AddFile(file, specification, options, allowedRoots, candidates, problems);
                if (candidates.Count > options.MaxDiscoveredPaths) return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add(new("SCAN_PATH_UNREADABLE", specification, Safe(exception.Message), full));
        }
    }

    private static void AddFile(
        string file,
        string specification,
        LocalDocumentScanOptions options,
        IReadOnlyList<string> allowedRoots,
        IDictionary<string, string> candidates,
        ICollection<LocalDocumentScanProblem> problems)
    {
        var full = Path.GetFullPath(file);
        if (!Allowed(full, allowedRoots))
        {
            problems.Add(new("SCAN_PATH_OUTSIDE_ALLOWED_ROOT", specification,
                "The file is outside the allowed roots.", full));
            return;
        }
        try
        {
            var attributes = File.GetAttributes(full);
            if (IsReparse(attributes))
            {
                problems.Add(new("SCAN_REPARSE_POINT_SKIPPED", specification,
                    "Reparse points are not scanned.", full));
                return;
            }
            if (!options.IncludeHidden && IsHidden(full, attributes)) return;
            candidates.TryAdd(full, specification);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add(new("SCAN_FILE_UNREADABLE", specification, Safe(exception.Message), full));
        }
    }

    private static IReadOnlyList<string> CanonicalRoots(
        IReadOnlyList<string> roots,
        ICollection<LocalDocumentScanProblem> problems)
    {
        var result = new HashSet<string>(PathComparer);
        foreach (var root in roots)
        {
            try { result.Add(Path.GetFullPath(root)); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                problems.Add(new("SCAN_ALLOWED_ROOT_INVALID", root, Safe(exception.Message)));
            }
        }
        return result.OrderBy(value => value, PathComparer).ToArray();
    }

    private static bool Allowed(string path, IReadOnlyList<string> roots) =>
        roots.Count == 0 || roots.Any(root => Contains(root, path));

    private static bool Contains(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static EnumerationOptions Enumeration(LocalDocumentScanOptions options) => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint |
                           (options.IncludeHidden ? (FileAttributes)0 : FileAttributes.Hidden)
    };

    private static string ExpansionRoot(string fullPattern)
    {
        var wildcard = fullPattern.IndexOfAny(['*', '?']);
        var separator = fullPattern.LastIndexOfAny(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], wildcard);
        if (separator < 0) return Path.GetPathRoot(fullPattern) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(fullPattern[..(separator + 1)]);
    }

    private static Regex GlobRegex(string fullPattern)
    {
        var pattern = Normalize(fullPattern);
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    index++;
                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:.*/)?");
                    }
                    else expression.Append(".*");
                }
                else expression.Append("[^/]*");
            }
            else if (current == '?') expression.Append("[^/]");
            else expression.Append(Regex.Escape(current.ToString()));
        }
        expression.Append('$');
        var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (OperatingSystem.IsWindows()) options |= RegexOptions.IgnoreCase;
        return new(expression.ToString(), options, TimeSpan.FromSeconds(2));
    }

    private static bool HasWildcard(string value) => value.IndexOfAny(['*', '?']) >= 0;
    private static bool IsReparse(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) != 0;
    private static bool IsHidden(string path, FileAttributes attributes) =>
        (attributes & FileAttributes.Hidden) != 0 ||
        Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);
    private static string Normalize(string path) => Path.GetFullPath(path).Replace('\\', '/');
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".md" => "text/markdown",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".html" or ".htm" => "text/html",
        ".xml" => "application/xml",
        ".yaml" or ".yml" => "application/yaml",
        _ => "application/octet-stream"
    };

    private static string? DecodeText(byte[] content)
    {
        try
        {
            if (content.Length >= 3 && content[0] == 0xef && content[1] == 0xbb && content[2] == 0xbf)
                return StrictUtf8.GetString(content, 3, content.Length - 3);
            if (content.Length >= 2 && content[0] == 0xff && content[1] == 0xfe)
                return Encoding.Unicode.GetString(content, 2, content.Length - 2);
            if (content.Length >= 2 && content[0] == 0xfe && content[1] == 0xff)
                return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
            if (content.Contains((byte)0)) return null;
            return StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException) { return null; }
    }

    private static string Safe(string value) => value.Length <= 500 ? value : value[..500];
}
