using DantesRoleplay.MCPServer;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;
using System.Text;
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

    [Theory]
    [InlineData("source")]
    [InlineData("package")]
    public async Task Pinned_extension_packages_retain_source_membership_classification_and_selected_precedence(string fileRoot)
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var package = Directory.CreateDirectory(Path.Combine(root, "package")).FullName;
        var bytes = ExtensionPackage();
        await File.WriteAllBytesAsync(Path.Combine(fileRoot == "source" ? source : package, "extension.json"), bytes);
        var application = new FreshApplication
        {
            Id = "example", DisplayName = "Example", Description = "Extension installation",
            ExtensionPackages = [new("extension.json", FreshInstallation.Fingerprint(bytes), fileRoot)],
            SelectedSourceIds = ["base"], SelectedExtensionIds = ["homebrew"]
        };
        var request = new FreshInstallationRequest(Path.Combine(package, "manifest.json"), Path.Combine(root, "installed"), source);

        var registrations = await FreshInstallation.ReadExtensionPackagesAsync(application, request, default);
        var app = ApplicationIdentifier.Parse(application.Id);
        var sources = new InMemorySourceRegistry();
        sources.Register(new(app, "extra", "workspace", "extra/**/*", SourceTrust.Trusted, 1, "extra"));
        var registry = new InMemoryApplicationExtensionRegistry(sources);
        registry.Register(Assert.Single(registrations));
        var compiled = ApplicationExtensionSetCompiler.Compile(app, registry.For(app), application.SelectedExtensionIds);

        var extension = Assert.Single(compiled.Extensions);
        Assert.Equal("homebrew", extension.Classification);
        Assert.Equal("Example Homebrew", extension.DisplayName);
        Assert.Equal(["extra"], extension.SourceIds);
        Assert.Equal(["example.extension.homebrew"], extension.NamespaceIds);
        Assert.Equal(["homebrew", "base"], compiled.PriorityOrder);
    }

    [Theory]
    [InlineData("version", "INSTALLATION_EXTENSION_INVALID")]
    [InlineData("application", "INSTALLATION_EXTENSION_INVALID")]
    [InlineData("changed-bytes", "INSTALLATION_FILE_HASH_MISMATCH")]
    public async Task Extension_package_identity_and_file_pins_are_checked_before_registration(string fault, string code)
    {
        var original = ExtensionPackage();
        var json = Encoding.UTF8.GetString(original);
        if (fault == "version") json = json.Replace("\"schemaVersion\":2", "\"schemaVersion\":1", StringComparison.Ordinal);
        if (fault == "application") json = json.Replace("\"applicationId\":\"example\"", "\"applicationId\":\"other\"", StringComparison.Ordinal);
        if (fault == "changed-bytes") json += " ";
        var bytes = Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(Path.Combine(root, "extension.json"), bytes);
        var application = new FreshApplication
        {
            Id = "example", DisplayName = "Example", Description = "Invalid extension test",
            ExtensionPackages = [new("extension.json", FreshInstallation.Fingerprint(fault == "changed-bytes" ? original : bytes), "source")]
        };
        var request = new FreshInstallationRequest(Path.Combine(root, "manifest.json"), Path.Combine(root, "installed"), root);

        var error = await Assert.ThrowsAsync<FreshInstallationException>(() =>
            FreshInstallation.ReadExtensionPackagesAsync(application, request, default));
        Assert.Equal(code, error.Code);
    }

    private static byte[] ExtensionPackage() => Encoding.UTF8.GetBytes("""
        {"schemaVersion":2,"applicationId":"example","extensionId":"homebrew","displayName":"Example Homebrew","description":"A reviewed extension.","classification":"homebrew","sourceIds":["extra"],"namespaceIds":["example.extension.homebrew"],"dependencies":[],"conflictsWith":[],"higherPriorityThan":[],"overridesBase":true}
        """);

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void Installation_bounds_allow_large_worlds_without_unbounded_package_lists(int packageCount, bool allowed)
    {
        Directory.CreateDirectory(Path.Combine(root, "catalog"));
        var manifest = new FreshInstallationManifest
        {
            Format = "dantesroleplay.installation/1",
            CatalogRoot = "catalog",
            Source = new("source"),
            Application = new()
            {
                Id = "example", DisplayName = "Example", Description = "Package bounds test",
                Sources = [new("content", "source", "catalog", "**/*", "trusted", 0, "example")]
            },
            StateSpaces = [new()
            {
                Id = "example-main", Scope = "runtime-state-space",
                Root = new("world.example", "Example", []),
                WorldPackages = Enumerable.Range(0, packageCount)
                    .Select(index => new FreshPinnedFile($"world/part-{index}.json", new string('0', 64))).ToArray()
            }],
            Pages = [],
            Runtime = new()
            {
                PublishedApplications = ["example"],
                Environment = new() { ["Codex:ExecutablePath"] = "codex" }
            }
        };
        var request = new FreshInstallationRequest(Path.Combine(root, "manifest.json"), Path.Combine(root, "installed"), root);

        if (allowed)
            FreshInstallation.ValidateManifest(manifest, request, []);
        else
        {
            var error = Assert.Throws<FreshInstallationException>(() => FreshInstallation.ValidateManifest(manifest, request, []));
            Assert.Equal("INSTALLATION_STATE_INVALID", error.Code);
        }
    }
}
