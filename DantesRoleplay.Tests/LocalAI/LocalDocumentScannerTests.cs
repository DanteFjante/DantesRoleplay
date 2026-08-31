using System.Text;
using DantesRoleplay.LocalAI;

namespace DantesRoleplay.Tests;

public sealed class LocalDocumentScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"local-ai-scan-{Guid.NewGuid():N}");

    public LocalDocumentScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "group-a", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "group-b"));
        File.WriteAllText(Path.Combine(_root, "root.txt"), "root", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "group-a", "a.txt"), "alpha", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "group-a", "nested", "note.md"), "note", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(_root, "group-b", "binary.bin"), [0, 1, 2, 0xff]);
    }

    [Fact]
    public async Task Literal_file_returns_content_hash_media_type_and_text()
    {
        var path = Path.Combine(_root, "root.txt");

        var result = await new LocalDocumentScanner().ScanAsync(new([path]));

        var document = Assert.Single(result.Documents);
        Assert.True(result.Complete);
        Assert.Equal(Path.GetFullPath(path), document.Path);
        Assert.Equal("text/plain", document.MediaType);
        Assert.Equal("root", document.Text);
        Assert.Equal(64, document.Sha256.Length);
    }

    [Fact]
    public async Task Literal_directory_recurses_and_keeps_binary_items_without_decoding_them()
    {
        var result = await new LocalDocumentScanner().ScanAsync(new([_root]));

        Assert.Equal(4, result.Documents.Count);
        Assert.Equal(result.Documents.Select(value => value.Path).OrderBy(value => value, PathComparer),
            result.Documents.Select(value => value.Path));
        Assert.Null(Assert.Single(result.Documents,
            value => value.Path.EndsWith("binary.bin", StringComparison.Ordinal)).Text);
    }

    [Fact]
    public async Task Wildcards_match_files_or_directories_with_star_question_and_recursive_star()
    {
        var scanner = new LocalDocumentScanner();
        var directoryGlob = Path.Combine(_root, "group-?");
        var markdownGlob = Path.Combine(_root, "**", "*.md");

        var result = await scanner.ScanAsync(new([directoryGlob, markdownGlob]));

        Assert.Equal(3, result.Documents.Count);
        Assert.Contains(result.Documents, value => value.Path.EndsWith("a.txt"));
        Assert.Contains(result.Documents, value => value.Path.EndsWith("note.md"));
        Assert.Contains(result.Documents, value => value.Path.EndsWith("binary.bin"));
    }

    [Fact]
    public async Task Overlapping_inputs_are_deduplicated_and_allowed_roots_are_enforced()
    {
        var file = Path.Combine(_root, "group-a", "a.txt");
        var result = await new LocalDocumentScanner().ScanAsync(
            new([_root, file, Path.Combine(_root, "**", "*.txt")]),
            new() { AllowedRoots = [_root] });

        Assert.Equal(4, result.Documents.Count);
        Assert.Equal(result.Documents.Count,
            result.Documents.Select(value => value.Path).Distinct(PathComparer).Count());

        var denied = await new LocalDocumentScanner().ScanAsync(
            new([_root]),
            new() { AllowedRoots = [Path.Combine(_root, "group-a")] });
        Assert.Empty(denied.Documents);
        Assert.Contains(denied.Problems, value => value.Code == "SCAN_PATH_OUTSIDE_ALLOWED_ROOT");
    }

    [Fact]
    public async Task Missing_and_size_limits_return_typed_problems_without_partial_file_reads()
    {
        var scanner = new LocalDocumentScanner();
        var missing = await scanner.ScanAsync(new([Path.Combine(_root, "missing")]));
        Assert.Equal("SCAN_PATH_NOT_FOUND", Assert.Single(missing.Problems).Code);

        var bounded = await scanner.ScanAsync(
            new([_root]),
            new() { MaxFileBytes = 4, MaxTotalBytes = 8, MaxFiles = 2, MaxDiscoveredPaths = 10 });

        Assert.Contains(bounded.Problems, value => value.Code == "SCAN_FILE_TOO_LARGE");
        Assert.All(bounded.Documents, value => Assert.True(value.Length <= 4));
    }

    [Fact]
    public async Task Reparse_points_are_never_traversed_when_the_platform_can_create_one()
    {
        var target = Path.Combine(_root, "group-a");
        var link = Path.Combine(_root, "linked");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var result = await new LocalDocumentScanner().ScanAsync(new([link]));

        Assert.Empty(result.Documents);
        Assert.Equal("SCAN_REPARSE_POINT_SKIPPED", Assert.Single(result.Problems).Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
