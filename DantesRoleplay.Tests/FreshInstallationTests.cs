using DantesRoleplay.MCPServer;
using Microsoft.Extensions.Configuration;

namespace DantesRoleplay.Tests;

public sealed class FreshInstallationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"fresh-install-{Guid.NewGuid():N}");

    public FreshInstallationTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Installation_mode_requires_all_three_absolute_inputs()
    {
        Assert.False(FreshInstallationRequest.TryRead(new ConfigurationBuilder().Build(), out _));
        var partial = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Installation:Manifest"] = Path.Combine(root, "manifest.json")
        }).Build();

        Assert.Throws<InvalidOperationException>(() => FreshInstallationRequest.TryRead(partial, out _));
    }

    [Fact]
    public async Task Existing_installation_is_refused_without_changing_its_bytes()
    {
        var manifest = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(manifest, "{}");
        var target = Path.Combine(root, "installed");
        Directory.CreateDirectory(target);
        var witness = Path.Combine(target, "witness.bin");
        await File.WriteAllBytesAsync(witness, [1, 2, 3, 4]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await FreshInstallation.RunAsync(new(manifest, target, root), output, error);

        Assert.Equal(1, exit);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(witness));
        Assert.Contains("must be an absent", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_manifest_leaves_no_installation_or_staging_directory()
    {
        var package = Path.Combine(root, "package");
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "installed");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(source);
        var manifest = Path.Combine(package, "installation.json");
        await File.WriteAllTextAsync(manifest, "{\"format\":\"wrong\"}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await FreshInstallation.RunAsync(new(manifest, target, source), output, error);

        Assert.Equal(1, exit);
        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.GetDirectories(root, ".installed.installing-*", SearchOption.TopDirectoryOnly));
    }
}
