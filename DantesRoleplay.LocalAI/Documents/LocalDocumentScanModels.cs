namespace DantesRoleplay.LocalAI;

public sealed record LocalDocumentScanRequest(IReadOnlyList<string> PathSpecifications);

public sealed class LocalDocumentScanOptions
{
    public int MaxFiles { get; init; } = 10_000;
    public int MaxDiscoveredPaths { get; init; } = 50_000;
    public long MaxFileBytes { get; init; } = 10 * 1024 * 1024;
    public long MaxTotalBytes { get; init; } = 256 * 1024 * 1024;
    public bool IncludeHidden { get; init; }
    public IReadOnlyList<string> AllowedRoots { get; init; } = [];

    internal string? Validate()
    {
        if (MaxFiles is < 1 or > 100_000) return "MaxFiles must be between 1 and 100000.";
        if (MaxDiscoveredPaths < MaxFiles || MaxDiscoveredPaths > 1_000_000)
            return "MaxDiscoveredPaths must be at least MaxFiles and no more than 1000000.";
        if (MaxFileBytes is < 1 or > 1024L * 1024 * 1024)
            return "MaxFileBytes must be between 1 byte and 1 GiB.";
        if (MaxTotalBytes < MaxFileBytes || MaxTotalBytes > 16L * 1024 * 1024 * 1024)
            return "MaxTotalBytes must be at least MaxFileBytes and no more than 16 GiB.";
        if (AllowedRoots.Count > 128 || AllowedRoots.Any(string.IsNullOrWhiteSpace))
            return "AllowedRoots must contain at most 128 nonblank paths.";
        return null;
    }
}

public sealed record ScannedLocalDocument(
    string Path,
    long Length,
    string Sha256,
    string MediaType,
    byte[] Content,
    string? Text);

public sealed record LocalDocumentScanProblem(
    string Code,
    string PathSpecification,
    string Message,
    string? Path = null);

public sealed record LocalDocumentScanResult(
    IReadOnlyList<ScannedLocalDocument> Documents,
    IReadOnlyList<LocalDocumentScanProblem> Problems)
{
    public bool Complete => Problems.Count == 0;

    public static LocalDocumentScanResult Invalid(string message) =>
        new([], [new("SCAN_REQUEST_INVALID", "", message)]);
}

public interface ILocalDocumentScanner
{
    Task<LocalDocumentScanResult> ScanAsync(
        LocalDocumentScanRequest request,
        LocalDocumentScanOptions? options = null,
        CancellationToken cancellationToken = default);
}
