using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.LocalAI;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024ExtensionPackagingTests : IDisposable
{
    private const string CoreSourceId = "dnd2024-core";
    private const string ExtensionSourceId = "dnd2024-extension.legacy-equipment";
    private const string ExtensionPath =
        "catalog/extensions/dnd2024/legacy-equipment/extension-package.json";
    private const string RopePath =
        "catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/dnd2024.item.hempen-rope-50-foot.v1.json";
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Legacy_equipment_package_is_closed_disabled_and_requires_core()
    {
        var root = RepositoryRoot();
        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "extensions",
            "dnd2024", "extension-package.schema.json"));
        var manifest = await File.ReadAllTextAsync(Path.Combine(root,
            ExtensionPath.Replace('/', Path.DirectorySeparatorChar)));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);

        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        var validation = validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, manifest);
        Assert.Equal(SchemaValueStatus.Valid, validation.Status);
        using var document = JsonDocument.Parse(manifest);
        var value = document.RootElement;
        Assert.Equal("dnd2024.legacy-equipment", value.GetProperty("extensionId").GetString());
        Assert.Equal("dnd2024", value.GetProperty("applicationId").GetString());
        Assert.Equal(ExtensionSourceId, value.GetProperty("sourceId").GetString());
        Assert.Equal("compatibility", value.GetProperty("classification").GetString());
        Assert.False(value.GetProperty("enabledByDefault").GetBoolean());
        Assert.Equal([CoreSourceId], value.GetProperty("requiredSourceIds").EnumerateArray()
            .Select(item => item.GetString()));

        AssertInvalid(manifest, node => node["enabledByDefault"] = true);
        AssertInvalid(manifest, node => node["classification"] = "official-core");
        AssertInvalid(manifest, node => node["requiredSourceIds"] = new JsonArray("extension.other"));
        AssertInvalid(manifest, node => node["unexpected"] = true);

        void AssertInvalid(string source, Action<JsonObject> mutate)
        {
            var node = JsonNode.Parse(source)!.AsObject();
            mutate(node);
            Assert.NotEqual(SchemaValueStatus.Valid,
                validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
                    node.ToJsonString()).Status);
        }
    }

    [Fact]
    public async Task Core_only_excludes_extension_while_opt_in_profile_is_distinct_and_deterministic()
    {
        await using var db = _fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("dnd2024");
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        applications.Register(new(application, "D&D 2024", "SRD-faithful core with optional extensions.", []));
        var core = sources.Register(new(application, CoreSourceId, "workspace",
            "catalog/applications/dnd2024/**/*", SourceTrust.Trusted, 0, "dnd2024-core-catalog"));
        var extension = sources.Register(new(application, ExtensionSourceId, "workspace",
            "catalog/extensions/dnd2024/legacy-equipment/**/*", SourceTrust.Trusted, 100,
            ExtensionSourceId));
        var previews = new ApplicationPreviewService(
            applications,
            sources,
            new RegisteredSourceScanner(sources, new WorkspaceRoot(), new LocalDocumentScanner()),
            new SourceOverlayResolver());

        var coreOnly = await previews.PreviewAsync(application, [CoreSourceId]);
        var extended = await previews.PreviewAsync(application, [CoreSourceId, ExtensionSourceId]);
        var reordered = await previews.PreviewAsync(application, [ExtensionSourceId, CoreSourceId]);

        Assert.True(coreOnly.IsValid);
        Assert.Equal(CoreSourceId, Assert.Single(coreOnly.Sources).SourceId);
        Assert.DoesNotContain(coreOnly.Winners,
            winner => winner.RelativePath.StartsWith("catalog/extensions/", StringComparison.Ordinal));
        Assert.DoesNotContain("catalog/extensions/", core.RelativePathOrGlob, StringComparison.Ordinal);
        Assert.Equal("catalog/extensions/dnd2024/legacy-equipment/**/*", extension.RelativePathOrGlob);
        Assert.True(extended.IsValid);
        Assert.Equal(2, extended.Sources.Count);
        Assert.Contains(extended.Winners, winner =>
            winner.SourceId == ExtensionSourceId && winner.RelativePath == ExtensionPath);
        Assert.Contains(extended.Winners, winner =>
            winner.SourceId == ExtensionSourceId && winner.RelativePath == RopePath);
        Assert.NotEqual(coreOnly.PreviewFingerprint, extended.PreviewFingerprint);
        Assert.Equal(extended.PreviewFingerprint, reordered.PreviewFingerprint);
        Assert.Equal(extended.Sources, reordered.Sources);
    }

    [Fact]
    public void Package_contains_only_manifest_and_reviewed_static_content()
    {
        var directory = Path.Combine(RepositoryRoot(), "catalog", "extensions", "dnd2024",
            "legacy-equipment");
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal([
            "content/entities/adventuring-gear/dnd2024.item.hempen-rope-50-foot.v1.json",
            "extension-package.json"
        ], files);
        Assert.DoesNotContain(files, path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, path => path.Contains("components/", StringComparison.Ordinal)
            || path.Contains("mechanics/", StringComparison.Ordinal)
            || path.Contains("procedures/", StringComparison.Ordinal)
            || path.Contains("queries/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Optional_rope_is_provenance_locked_and_never_claims_core_provenance()
    {
        var root = RepositoryRoot();
        var inventoryPath = Path.Combine(root, "ruleset", "dnd2024", "adoption", "evidence",
            "retained-archive-inventory-13a.json");
        using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(inventoryPath));
        var archivedRope = inventory.RootElement.GetProperty("archive").GetProperty("files")
            .EnumerateArray().Single(row => row.GetProperty("path").GetString() ==
                "old-dnd/catalog/world/entities/item.dnd2024.hempen-rope-50-foot.v1.json");
        var archiveHash = archivedRope.GetProperty("sha256").GetString();
        Assert.Equal("5103289F8A87B8CDC057ADD232C0995FF00B2AF49A73EEBFC8A770D3D4B27779",
            archiveHash);

        var coreRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content", "entities");
        Assert.DoesNotContain(Directory.GetFiles(coreRoot, "*.json", SearchOption.AllDirectories),
            path => Path.GetFileName(path) is "dnd2024.item.hempen-rope-50-foot.v1.json" or
                "item.dnd2024.quiver.v1.json");

        var targetPath = Path.Combine(root, RopePath.Replace('/', Path.DirectorySeparatorChar));
        var targetEntity = EntityFile.Parse(await File.ReadAllTextAsync(targetPath), RopePath);
        Assert.Equal("dnd2024.item.hempen-rope-50-foot.v1", targetEntity.Id);
        Assert.Contains("Legacy Compatibility", targetEntity.Name, StringComparison.Ordinal);
        var targetComponent = Assert.Single(targetEntity.Components);
        Assert.Equal("dnd2024.item-definition", targetComponent.DefinitionId);

        var targetData = JsonNode.Parse(targetComponent.Data)!.AsObject();

        Assert.Equal(5, targetData["massPounds"]!["numerator"]!.GetValue<int>());
        Assert.Equal(1, targetData["massPounds"]!["denominator"]!.GetValue<int>());
        Assert.Equal(50, targetData["lengthFeet"]!["numerator"]!.GetValue<int>());
        Assert.Equal(1, targetData["lengthFeet"]!["denominator"]!.GetValue<int>());
        Assert.Equal(ExtensionSourceId, targetData["sourceRef"]!["sourceId"]!.GetValue<string>());
        Assert.Equal("Legacy archive > item.dnd2024.hempen-rope-50-foot.v1",
            targetData["sourceRef"]!["locator"]!.GetValue<string>());
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class WorkspaceRoot : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = allowedRootId == "workspace" ? RepositoryRoot() : "";
            return canonicalPath.Length > 0;
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
