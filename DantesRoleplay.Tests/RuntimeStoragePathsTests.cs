using DantesRoleplay.MCPServer;

namespace DantesRoleplay.Tests;

public sealed class RuntimeStoragePathsTests
{
    [Fact]
    public void Relative_runtime_paths_are_anchored_to_the_host_content_root()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "roleplay-host", "DantesRoleplay.MCPServer");

        var paths = RuntimeStoragePaths.Resolve(
            contentRoot,
            Path.Combine("data", "runtime.db"),
            null,
            Path.Combine("indexes", "retrieval"));

        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "data", "runtime.db")), paths.DatabasePath);
        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "data", "blobs")), paths.BlobStorageRoot);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, "indexes", "retrieval")),
            paths.DerivedDataRoot);
    }

    [Fact]
    public void Explicit_absolute_storage_paths_are_preserved()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "roleplay-host");
        var databasePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "runtime", "roleplay.db"));
        var blobRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "runtime-blobs"));
        var derivedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "runtime-derived"));

        var paths = RuntimeStoragePaths.Resolve(contentRoot, databasePath, blobRoot, derivedRoot);

        Assert.Equal(databasePath, paths.DatabasePath);
        Assert.Equal(blobRoot, paths.BlobStorageRoot);
        Assert.Equal(derivedRoot, paths.DerivedDataRoot);
    }

    [Fact]
    public void Relative_source_roots_are_anchored_to_the_host_content_root()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "repo", "DantesRoleplay.MCPServer");

        var roots = RuntimeStoragePaths.ResolveSourceRoots(
            contentRoot,
            [new KeyValuePair<string, string?>("repository", "..")]);

        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "..")), roots["repository"]);
    }
}
