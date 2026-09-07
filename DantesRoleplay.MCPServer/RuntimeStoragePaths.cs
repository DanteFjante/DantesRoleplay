namespace DantesRoleplay.MCPServer;

internal sealed record RuntimeStoragePaths(
    string DatabasePath,
    string BlobStorageRoot,
    string DerivedDataRoot)
{
    public static RuntimeStoragePaths Resolve(
        string contentRoot,
        string? configuredDatabasePath,
        string? configuredBlobStorageRoot,
        string? configuredDerivedDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        var root = Path.GetFullPath(contentRoot);
        var databasePath = ResolvePath(
            root,
            configuredDatabasePath,
            Path.Combine("data", "dantesroleplay.db"));
        var databaseDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("The runtime database path has no parent directory.");

        return new RuntimeStoragePaths(
            databasePath,
            string.IsNullOrWhiteSpace(configuredBlobStorageRoot)
                ? Path.GetFullPath(Path.Combine(databaseDirectory, "blobs"))
                : ResolvePath(root, configuredBlobStorageRoot, "."),
            string.IsNullOrWhiteSpace(configuredDerivedDataRoot)
                ? Path.GetFullPath(Path.Combine(databaseDirectory, "derived"))
                : ResolvePath(root, configuredDerivedDataRoot, "."));
    }

    public static IReadOnlyDictionary<string, string> ResolveSourceRoots(
        string contentRoot,
        IEnumerable<KeyValuePair<string, string?>> configuredRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(configuredRoots);

        var root = Path.GetFullPath(contentRoot);
        return configuredRoots.ToDictionary(
            pair => pair.Key,
            pair => ResolvePath(root, pair.Value, "."),
            StringComparer.Ordinal);
    }

    internal static string ResolvePath(string baseDirectory, string? configuredPath, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath.Trim();
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
    }
}
