using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
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
        "catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/dnd2024.extension.legacy-equipment.item.hempen-rope-50-foot.v1.json";
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Legacy_equipment_package_maps_directly_to_runtime_registration()
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
        Assert.Equal(2, value.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("legacy-equipment", value.GetProperty("extensionId").GetString());
        Assert.Equal("dnd2024", value.GetProperty("applicationId").GetString());
        Assert.Equal([ExtensionSourceId], value.GetProperty("sourceIds").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Equal(["dnd2024.extension.legacy-equipment"], value.GetProperty("namespaceIds").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Equal("compatibility", value.GetProperty("classification").GetString());
        Assert.False(value.GetProperty("overridesBase").GetBoolean());
        Assert.Empty(value.GetProperty("dependencies").EnumerateArray());

        AssertInvalid(manifest, node => node["extensionId"] = "dnd2024.legacy-equipment");
        AssertInvalid(manifest, node => node["classification"] = "official-core");
        AssertInvalid(manifest, node => node["namespaceIds"] = new JsonArray("other.extension"));
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
            "catalog/extensions/dnd2024/legacy-equipment/content/**/*", SourceTrust.Trusted, 100,
            ExtensionSourceId));
        sources.Register(new(application, "retired-flat-path", "workspace",
            "catalog/retired-flat-path/**/*", SourceTrust.Trusted, 0, "retired-flat-path"));
        var extensions = new SqliteApplicationExtensionRegistry(db, sources);
        var registeredExtension = extensions.Register(new(application, "legacy-equipment",
            "Legacy Equipment", "Reviewed compatibility equipment.",
            ApplicationExtensionClassifications.Compatibility, [ExtensionSourceId],
            ["dnd2024.extension.legacy-equipment"], [], [], [], false));
        var previews = new ApplicationPreviewService(
            applications,
            sources,
            extensions,
            new RegisteredSourceScanner(sources, new WorkspaceRoot(), new LocalDocumentScanner()),
            new SourceOverlayResolver());

        var coreOnly = await previews.PreviewAsync(application, [CoreSourceId], []);
        var extended = await previews.PreviewAsync(application, [CoreSourceId], ["legacy-equipment"]);
        var reordered = await previews.PreviewAsync(application, [CoreSourceId], ["legacy-equipment"]);

        Assert.True(coreOnly.IsValid);
        Assert.Equal(CoreSourceId, Assert.Single(coreOnly.Sources).SourceId);
        Assert.DoesNotContain(coreOnly.Winners,
            winner => winner.RelativePath.StartsWith("catalog/extensions/", StringComparison.Ordinal));
        Assert.DoesNotContain(coreOnly.Sources, source => source.SourceId == "retired-flat-path");
        Assert.DoesNotContain("catalog/extensions/", core.RelativePathOrGlob, StringComparison.Ordinal);
        Assert.Equal("catalog/extensions/dnd2024/legacy-equipment/content/**/*", extension.RelativePathOrGlob);
        Assert.True(extended.IsValid);
        Assert.Equal(2, extended.Sources.Count);
        Assert.Equal(["legacy-equipment"], extended.ExtensionIds);
        Assert.Contains(extended.Winners, winner =>
            winner.SourceId == ExtensionSourceId && winner.RelativePath == RopePath);
        Assert.NotEqual(coreOnly.PreviewFingerprint, extended.PreviewFingerprint);
        Assert.Equal(extended.PreviewFingerprint, reordered.PreviewFingerprint);
        Assert.Equal(extended.Sources, reordered.Sources);

        var activation = new ActiveApplicationManifest(application, 1,
            extended.ApplicationRevision, extended.ApplicationFingerprint,
            extended.PreviewFingerprint, extended.ScannedDocumentsFingerprint,
            extended.CandidateManifestFingerprint, new string('D', 64), new string('A', 64),
            "test-coverage", false,
            extended.Sources.Select(source => new ActivatedApplicationSource(source.SourceId,
                source.RegistrationFingerprint, source.DocumentCount, source.ProblemCount)).ToArray(),
            extended.Winners.Where(winner =>
                    !winner.RelativePath.Contains("/objects/", StringComparison.Ordinal)
                    && !winner.RelativePath.EndsWith("dnd2024.query.campaign-summary.json", StringComparison.Ordinal)
                    && !winner.RelativePath.EndsWith("dnd2024.query.faction-directory-page.json", StringComparison.Ordinal))
                .Select(winner => new ActivatedApplicationDocument(winner.LogicalIdentity,
                winner.SourceId, winner.Trust, winner.Precedence, winner.RelativePath,
                winner.MediaType, winner.ContentFingerprint, winner.Length, winner.IsText)).ToArray(),
            "test-operation", DateTime.UtcNow)
        {
            ResolutionFingerprint = extended.ResolutionFingerprint,
            Extensions = [new("legacy-equipment",
                ApplicationExtensionRegistrationFingerprint.Compute(registeredExtension),
                registeredExtension.SourceIds, registeredExtension.NamespaceIds,
                registeredExtension.HigherPriorityThan, registeredExtension.OverridesBase)]
        };
        var snapshot = new ActivatedApplicationCatalogMaterializer(applications,
            new StaticActivation(activation), sources, new WorkspaceRoot(), extensions)
            .BuildFeatureSnapshot(application);
        var navigator = new InMemoryCatalogNavigator(snapshot.Manifest,
            new CatalogCursorCodec(System.Text.Encoding.UTF8.GetBytes(
                "dnd2024-extension-packaging-test-signing-key")), snapshot.Resolution);
        var content = navigator.EffectiveContent(new(application, 100));
        Assert.Equal(extended.ResolutionFingerprint, content.ResolutionFingerprint);
        Assert.Equal("compatibility", Assert.Single(content.ActiveExtensions).Classification);
        var additiveRecords = new List<EffectiveApplicationContentRecord>(content.AdditiveExtensionContent);
        while (content.NextCursor is not null)
        {
            content = navigator.EffectiveContent(new(application, 100, content.NextCursor));
            additiveRecords.AddRange(content.AdditiveExtensionContent);
        }
        var additive = Assert.Single(additiveRecords,
            value => value.Record.QualifiedId.Contains("hempen-rope-50-foot", StringComparison.Ordinal));
        Assert.Equal("legacy-equipment", additive.OwnerId);
        Assert.Equal("Legacy Equipment", additive.SourceLabel);
        Assert.True(additive.IsAdditive);

        var extensionAsBase = await Assert.ThrowsAsync<ApplicationPreviewException>(() =>
            previews.PreviewAsync(application, [CoreSourceId, ExtensionSourceId], ["legacy-equipment"]));
        Assert.Equal("BASE_SOURCE_SELECTION_INCLUDES_EXTENSION", extensionAsBase.Code);
    }

    [Fact]
    public async Task Caldris_package_fields_register_without_an_adapter_contract()
    {
        var root = RepositoryRoot();
        var schema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "extensions",
            "dnd2024", "extension-package.schema.json"));
        var manifest = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "extensions",
            "dnd2024", "caldris-homebrew", "extension-package.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, manifest).Status);

        using var document = JsonDocument.Parse(manifest);
        var value = document.RootElement;
        var application = ApplicationIdentifier.Parse(value.GetProperty("applicationId").GetString()!);
        var sources = new InMemorySourceRegistry();
        foreach (var sourceId in value.GetProperty("sourceIds").EnumerateArray().Select(item => item.GetString()!))
            sources.Register(new(application, sourceId, "workspace", "caldris/**/*",
                SourceTrust.Trusted, 0, sourceId));
        var registration = new ApplicationExtensionRegistration(application,
            value.GetProperty("extensionId").GetString()!, value.GetProperty("displayName").GetString()!,
            value.GetProperty("description").GetString()!, value.GetProperty("classification").GetString()!,
            value.GetProperty("sourceIds").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            value.GetProperty("namespaceIds").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            value.GetProperty("dependencies").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            value.GetProperty("conflictsWith").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            value.GetProperty("higherPriorityThan").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            value.GetProperty("overridesBase").GetBoolean());

        var registered = new InMemoryApplicationExtensionRegistry(sources).Register(registration);
        Assert.Equal("caldris-homebrew", registered.ExtensionId);
        Assert.Equal("dnd2024.extension.caldris", Assert.Single(registered.NamespaceIds));
        Assert.Equal(ApplicationExtensionClassifications.Homebrew, registered.Classification);
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
            "content/entities/adventuring-gear/dnd2024.extension.legacy-equipment.item.hempen-rope-50-foot.v1.json",
            "extension-package.json"
        ], files);
        Assert.DoesNotContain(files, path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, path => path.Contains("components/", StringComparison.Ordinal)
            || path.Contains("mechanics/", StringComparison.Ordinal)
            || path.Contains("procedures/", StringComparison.Ordinal)
            || path.Contains("queries/", StringComparison.Ordinal));
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

    private sealed class StaticActivation(ActiveApplicationManifest activation)
        : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == activation.ApplicationId ? activation : null;
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
