namespace DantesRoleplay.Web.Pages;

public sealed record WebPageBundle(
    string Html,
    IReadOnlyList<WebPageAssetUpload> Assets);

public sealed record WebPageAssetUpload(
    string Path,
    byte[] Content);

public static class WebPageBundleLimits
{
    public const int MaximumCompressedBytes = 10 * 1024 * 1024;
    public const int MaximumEntries = 256;
    public const int MaximumEntryBytes = 5 * 1024 * 1024;
    public const int MaximumUncompressedBytes = 25 * 1024 * 1024;
    public const int MaximumHtmlBytes = 1024 * 1024;
    public const int MaximumAssetPathLength = 240;
}

public static class WebPageAssetPath
{
    public static bool TryValidate(string? path, out string validated)
    {
        validated = string.Empty;
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > WebPageBundleLimits.MaximumAssetPathLength ||
            path[0] == '/' ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.Contains(':', StringComparison.Ordinal) ||
            path.Contains('?', StringComparison.Ordinal) ||
            path.Contains('#', StringComparison.Ordinal) ||
            path.Contains('%', StringComparison.Ordinal) ||
            path.Any(char.IsControl))
        {
            return false;
        }

        var segments = path.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0 ||
                segment is "." or ".." ||
                !string.Equals(segment, segment.Trim(), StringComparison.Ordinal)))
        {
            return false;
        }

        validated = string.Join('/', segments);
        return true;
    }
}
