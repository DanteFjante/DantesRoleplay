using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Blobs;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;

namespace DantesRoleplay.MCPServer;

internal sealed record FreshInstallationRequest(string ManifestPath, string InstallationRoot, string SourceRoot)
{
    public static bool TryRead(IConfiguration configuration, out FreshInstallationRequest request)
    {
        var manifest = configuration["Installation:Manifest"];
        var root = configuration["Installation:Root"];
        var source = configuration["Installation:SourceRoot"];
        var any = manifest is not null || root is not null || source is not null;
        if (!any) { request = null!; return false; }
        if (string.IsNullOrWhiteSpace(manifest) || string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException(
                "Installation:Manifest, Installation:Root, and Installation:SourceRoot are required together.");
        request = new(Path.GetFullPath(manifest), Path.GetFullPath(root), Path.GetFullPath(source));
        return true;
    }
}

internal static class FreshInstallation
{
    private const string Format = "dantesroleplay.installation/1";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(
        FreshInstallationRequest request, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? staging = null;
        try
        {
            RequireInputs(request);
            var manifestBytes = await File.ReadAllBytesAsync(request.ManifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<FreshInstallationManifest>(manifestBytes, Json)
                ?? throw Invalid("INSTALLATION_MANIFEST_EMPTY", "The installation manifest is empty.");
            ValidateManifest(manifest, request, manifestBytes);

            var parent = Path.GetDirectoryName(request.InstallationRoot)!;
            Directory.CreateDirectory(parent);
            staging = Path.Combine(parent, $".{Path.GetFileName(request.InstallationRoot)}.installing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(Path.Combine(staging, "blobs"));
            Directory.CreateDirectory(Path.Combine(staging, "derived"));

            var stageDatabase = Path.Combine(staging, "database.db");
            var environment = Environment(manifest, request, stageDatabase, staging);
            await ProvisionAsync(manifest, request, stageDatabase, staging, environment, cancellationToken);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Move(staging, request.InstallationRoot);
            staging = request.InstallationRoot;
            var finalDatabase = Path.Combine(request.InstallationRoot, "database.db");
            var finalEnvironment = Environment(manifest, request, finalDatabase, request.InstallationRoot);
            var receipt = await VerifyAsync(manifest, request, finalDatabase, request.InstallationRoot,
                finalEnvironment, cancellationToken);
            var receiptPath = Path.Combine(request.InstallationRoot, "installation.json");
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt, Json) + System.Environment.NewLine,
                new UTF8Encoding(false), cancellationToken);
            staging = null;

            await output.WriteLineAsync(JsonSerializer.Serialize(receipt, Json));
            return 0;
        }
        catch (Exception exception)
        {
            Exception? cleanupFailure = null;
            if (staging is not null && Directory.Exists(staging))
            {
                try
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailure = cleanupException;
                }
            }
            await error.WriteLineAsync($"Fresh installation failed: {exception.Message}");
            if (cleanupFailure is not null)
                await error.WriteLineAsync($"Fresh installation cleanup failed: {cleanupFailure.Message}");
            return 1;
        }
    }

    private static async Task ProvisionAsync(
        FreshInstallationManifest manifest, FreshInstallationRequest request,
        string databasePath, string installationRoot, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var services = Services(manifest, request, databasePath, installationRoot, configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false
        });
        await provider.InitialiseDantesRoleplayAsync(cancellationToken);
        await provider.InitialiseDantesRoleplayWebAsync(cancellationToken);

        var catalogRoot = SafePath(request.SourceRoot, manifest.CatalogRoot);
        var validation = await CatalogValidator.ValidateAsync(catalogRoot, cancellationToken);
        if (!validation.IsValid)
            throw Invalid("INSTALLATION_CATALOG_INVALID", $"The catalog has {validation.Errors} validation error(s).");
        await using (var scope = provider.CreateAsyncScope())
        {
            RestoreCatalogNamespaces(scope.ServiceProvider, catalogRoot);
            var importer = scope.ServiceProvider.GetRequiredService<CatalogImporter>();
            var imported = await importer.ApplyAsync(catalogRoot,
                new CatalogImportOptions(Force: CatalogForce.Files), cancellationToken);
            if (imported.Aborted) throw Invalid("INSTALLATION_CATALOG_CONFLICT", "The fresh catalog import was not clean.");
            if (!(await importer.PlanAsync(catalogRoot, cancellationToken)).IsClean)
                throw Invalid("INSTALLATION_CATALOG_DRIFT", "The imported catalog does not agree with its source files.");
        }

        var app = ApplicationIdentifier.Parse(manifest.Application.Id);
        await using (var scope = provider.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<IRegistryAdministrationService>();
            foreach (var basis in manifest.BaseApplications)
                await RegisterApplicationAsync(registry, basis, cancellationToken);
            await RegisterApplicationAsync(registry, manifest.Application, cancellationToken);
            foreach (var source in manifest.Application.Sources)
            {
                var registration = new SourceRegistration(app, source.Id, source.AllowedRootId,
                    CombineSpecification(source.RelativeRoot, source.IncludePattern), ParseTrust(source.Trust),
                    source.Precedence, source.LogicalIdentity);
                var context = RegistryContext(Token(manifest.ManifestHash, "source:" + source.Id));
                await registry.PreviewSourceAsync(registration, context, cancellationToken);
                await registry.RegisterSourceAsync(registration, context, cancellationToken);
            }
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var types = scope.ServiceProvider.GetRequiredService<IFreshComponentTypeBaselineInstaller>();
            var schemas = scope.ServiceProvider.GetRequiredService<IBoundedJsonSchemaValidator>();
            foreach (var component in manifest.ComponentTypes)
            {
                var componentRoot = component.Root switch
                {
                    "source" => request.SourceRoot,
                    "package" => Path.GetDirectoryName(request.ManifestPath)!,
                    _ => throw Invalid("INSTALLATION_COMPONENT_TYPE_INVALID", "Component type root must be source or package.")
                };
                var bytes = await ReadPinnedAsync(componentRoot, component.SchemaPath, component.Sha256,
                    cancellationToken);
                var owner = component.OwnerApplicationId == "system"
                    ? ApplicationIdentifier.System : ApplicationIdentifier.Parse(component.OwnerApplicationId);
                var schemaJson = Encoding.UTF8.GetString(bytes);
                var expectedSchemaHash = component.SchemaHash;
                if (expectedSchemaHash is null)
                {
                    var compilation = schemas.Compile(schemaJson);
                    if (!compilation.IsAccepted)
                        throw Invalid("INSTALLATION_COMPONENT_TYPE_INVALID",
                            $"Component type '{component.QualifiedTypeId}' does not compile under the bounded schema profile.");
                    expectedSchemaHash = compilation.SchemaHash;
                }
                var registered = types.Restore(
                    new(owner, component.QualifiedTypeId, schemaJson),
                    component.Version, expectedSchemaHash);
                if (registered.Version != component.Version
                    || !string.Equals(registered.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
                    throw Invalid("INSTALLATION_COMPONENT_TYPE_MISMATCH",
                        $"Component type '{component.QualifiedTypeId}' did not register at its pinned version and schema hash.");
            }
        }

        ActiveApplicationManifest active;
        await using (var scope = provider.CreateAsyncScope())
        {
            var previewer = scope.ServiceProvider.GetRequiredService<IApplicationPreviewService>();
            var preview = await previewer.PreviewAsync(app, manifest.Application.SelectedSourceIds, cancellationToken);
            if (!preview.IsValid) throw Invalid("INSTALLATION_APPLICATION_INVALID", "The selected application source preview is invalid.");
            var activations = scope.ServiceProvider.GetRequiredService<IApplicationActivationService>();
            var context = ActivationContext(Token(manifest.ManifestHash, "activate"));
            var activationRequest = new ApplicationActivationRequest(app, preview.PreviewFingerprint, null,
                manifest.Application.SelectedSourceIds);
            await activations.PreviewAsync(activationRequest, context, cancellationToken);
            active = (await activations.ActivateAsync(activationRequest, context, cancellationToken)).Activation;
            if (!scope.ServiceProvider.GetRequiredService<IPublicApplicationCatalogProvider>().TryGet(app, out _))
                throw Invalid("INSTALLATION_CATALOG_UNAVAILABLE", "The activated application catalog did not materialize.");
        }

        foreach (var state in manifest.StateSpaces)
            await CreateStateSpaceAsync(provider, manifest, request, app, active, state, cancellationToken);

        await ImportMediaAsync(provider, manifest, request, cancellationToken);
        await PublishPagesAsync(provider, manifest, request, app, cancellationToken);

        SetLocalWebsiteContext(provider, app.Value);
        var readiness = await ReadReadinessAsync(provider, app.Value, cancellationToken);
        if (readiness.Status != "ready")
            throw Invalid("INSTALLATION_NOT_READY", string.Join("; ", readiness.Checks
                .Where(value => value.Status != "ready").Select(value => $"{value.Name}:{value.Code}")));
    }

    private static void RestoreCatalogNamespaces(IServiceProvider provider, string catalogRoot)
    {
        var namespaceRoot = SafePath(catalogRoot, "namespaces");
        var files = Directory.EnumerateFiles(namespaceRoot, "*.json", SearchOption.AllDirectories)
            .Select(path => (Path: path, Definition: CatalogNamespaceFile.Parse(File.ReadAllText(path), path)))
            .OrderBy(value => value.Definition.Id.Count(character => character == '.'))
            .ThenBy(value => value.Definition.Id, StringComparer.Ordinal)
            .ToArray();
        var registry = provider.GetRequiredService<ICatalogNamespaceRegistry>();
        foreach (var file in files)
        {
            RequireNoLinks(file.Path);
            registry.Register(file.Definition.Registration());
        }
    }

    private static async Task CreateStateSpaceAsync(
        ServiceProvider provider, FreshInstallationManifest manifest, FreshInstallationRequest request,
        ApplicationIdentifier app, ActiveApplicationManifest active, FreshStateSpace state,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<IStateSpaceAdministrationService>();
        var create = new StateSpaceCreationRequest(state.Id, app, active.ActivationFingerprint, null)
        {
            Scope = ParseScope(state.Scope)
        };
        var context = StateContext(Token(manifest.ManifestHash, "state:" + state.Id));
        await administration.PreviewCreateAsync(create, context, cancellationToken);
        await administration.CreateAsync(create, context, cancellationToken);

        var types = scope.ServiceProvider.GetRequiredService<IApplicationComponentTypeRegistry>();
        var rootEffects = new List<ApplicationEcsEffect>
        {
            new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = state.Root.EntityId, Name = state.Root.Name }
        };
        foreach (var component in state.Root.Components)
        {
            var type = types.GetLatest(component.QualifiedTypeId)
                ?? throw Invalid("INSTALLATION_COMPONENT_TYPE_MISSING", $"Component type '{component.QualifiedTypeId}' is unavailable.");
            rootEffects.Add(new()
            {
                Type = ApplicationEcsEffectType.ComponentAdd,
                EntityId = state.Root.EntityId,
                ComponentType = new(type.QualifiedId, type.Version, type.SchemaHash),
                DataJson = component.Value.GetRawText(),
                ExpectedRevision = 0
            });
        }
        var batch = new ApplicationEcsEffectBatch
        {
            StateSpaceId = state.Id,
            Effects = rootEffects,
            Intent = "Provision the reviewed initial state-space root.",
            ExecutionIdentity = new(Token(manifest.ManifestHash, "root-dry:" + state.Id),
                Fingerprint(Encoding.UTF8.GetBytes(state.Id + "\n" + state.Root.EntityId)))
        };
        var applier = scope.ServiceProvider.GetRequiredService<IApplicationEcsEffectApplier>();
        RequireEffects(await applier.ApplyAsync(batch, true, cancellationToken), "root dry run");
        RequireEffects(await applier.ApplyAsync(batch with
        {
            ExecutionIdentity = new(Token(manifest.ManifestHash, "root-apply:" + state.Id),
                batch.ExecutionIdentity.RequestFingerprint)
        }, false, cancellationToken), "root creation");

        var synchronizer = scope.ServiceProvider.GetRequiredService<IApplicationWorldAuthoringSynchronizer>();
        foreach (var packageRef in state.WorldPackages)
        {
            var packageRoot = packageRef.Root switch
            {
                "package" => Path.GetDirectoryName(request.ManifestPath)!,
                "source" => request.SourceRoot,
                _ => throw Invalid("INSTALLATION_WORLD_PACKAGE_INVALID", "World package root must be source or package.")
            };
            var bytes = await ReadPinnedAsync(packageRoot, packageRef.Path,
                packageRef.Sha256, cancellationToken);
            var package = JsonSerializer.Deserialize<FreshWorldPackage>(bytes, Json)
                ?? throw Invalid("INSTALLATION_WORLD_PACKAGE_EMPTY", $"World package '{packageRef.Path}' is empty.");
            if (package.Format != "dantesroleplay.world-package/1" || package.RootEntityId != state.Root.EntityId)
                throw Invalid("INSTALLATION_WORLD_PACKAGE_INVALID", $"World package '{packageRef.Path}' does not match its state-space root.");
            var worldRequest = new ApplicationWorldAuthoringRequest(
                Token(manifest.ManifestHash, "world:" + state.Id + ":" + packageRef.Path), app.Value, state.Id,
                package.RootEntityId,
                package.Entities.Select(ToWorldEntity).ToArray(),
                package.Relationships.Select(ToWorldRelationship).ToArray());
            var worldContext = new ApplicationWorldAuthoringContext(
                "Provision the reviewed starter world package.", []);
            RequireWorld(await synchronizer.SynchronizeAsync(worldRequest, worldContext, true, cancellationToken), packageRef.Path);
            RequireWorld(await synchronizer.SynchronizeAsync(worldRequest, worldContext, false, cancellationToken), packageRef.Path);
        }
    }

    private static async Task ImportMediaAsync(
        ServiceProvider provider, FreshInstallationManifest manifest, FreshInstallationRequest request,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobTransferService>();
        foreach (var media in manifest.Media)
        {
            var bytes = await ReadPinnedAsync(Path.GetDirectoryName(request.ManifestPath)!, media.Path,
                media.Sha256, cancellationToken);
            if (bytes.LongLength != media.ByteLength)
                throw Invalid("INSTALLATION_FILE_LENGTH_MISMATCH", $"Media '{media.Path}' has an unexpected length.");
            var existing = await blobs.FindAsync(media.Sha256.ToLowerInvariant(), cancellationToken);
            if (existing is null)
            {
                var begin = await blobs.BeginUploadAsync(new(media.Sha256.ToLowerInvariant(), media.MediaType, bytes.LongLength), cancellationToken);
                await using var stream = new MemoryStream(bytes, writable: false);
                await blobs.UploadAsync(begin.UploadId, begin.UploadToken, stream, cancellationToken);
                existing = await blobs.FinalizeUploadAsync(begin.UploadId, begin.UploadToken, cancellationToken);
            }
            if (!string.Equals(existing.Sha256, media.Sha256, StringComparison.OrdinalIgnoreCase)
                || existing.ByteLength != media.ByteLength || existing.MediaType != media.MediaType)
                throw Invalid("INSTALLATION_MEDIA_MISMATCH", $"Media '{media.Path}' was not imported exactly.");
        }
    }

    private static async Task PublishPagesAsync(
        ServiceProvider provider, FreshInstallationManifest manifest, FreshInstallationRequest request,
        ApplicationIdentifier app, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var reader = new WebPageBundleReader();
        var pages = scope.ServiceProvider.GetRequiredService<IWebPageStore>();
        var administration = scope.ServiceProvider.GetRequiredService<WebPageAdministration>();
        foreach (var page in manifest.Pages)
        {
            var bytes = await ReadPinnedAsync(Path.GetDirectoryName(request.ManifestPath)!, page.Bundle.Path,
                page.Bundle.Sha256, cancellationToken);
            await using var stream = new MemoryStream(bytes, writable: false);
            var bundle = await reader.ReadAsync(stream, bytes.LongLength, cancellationToken);
            if (bundle.ContentFormat != WebPageContentFormat.Html)
                throw Invalid("INSTALLATION_PAGE_FORMAT_INVALID", "Fresh installation currently requires an HTML page bundle.");
            if (page.Owner == "system")
            {
                if (string.IsNullOrWhiteSpace(page.PageId))
                    throw Invalid("INSTALLATION_PAGE_INVALID", "A system page requires pageId.");
                await pages.SaveBundleAndActivateAsync(page.PageId, bundle, cancellationToken);
            }
            else if (page.Owner == "application")
            {
                if (page.ApplicationId != app.Value || string.IsNullOrWhiteSpace(page.EntityId))
                    throw Invalid("INSTALLATION_PAGE_INVALID", "An application page must match the installed application and identify an entity.");
                await administration.CreateAsync(app, new(page.EntityId, page.Title, page.NavigationLabel,
                    page.Slug, page.Order, page.Visibility, bundle.Html, page.IsIndexPage), cancellationToken);
                if (bundle.Assets.Count > 0) await administration.PublishBundleAsync(app, page.EntityId, bundle, cancellationToken);
            }
            else throw Invalid("INSTALLATION_PAGE_INVALID", "Page owner must be system or application.");
        }
    }

    private static async Task<FreshInstallationReceipt> VerifyAsync(
        FreshInstallationManifest manifest, FreshInstallationRequest request,
        string databasePath, string installationRoot, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var services = Services(manifest, request, databasePath, installationRoot, configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        SetLocalWebsiteContext(provider, manifest.Application.Id);
        var readiness = await ReadReadinessAsync(provider, manifest.Application.Id, cancellationToken);
        if (readiness.Status != "ready")
            throw Invalid("INSTALLATION_FINAL_NOT_READY", "The promoted installation did not retain ready state.");
        await using var scope = provider.CreateAsyncScope();
        var audience = await SystemAudienceContextHandler.ResolveAsync(
            scope.ServiceProvider.GetRequiredService<ILocalKnowledgeSeatProvider>(),
            scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>(),
            scope.ServiceProvider.GetRequiredService<IKnowledgeApplicationBindingResolver>(),
            scope.ServiceProvider.GetRequiredService<IKnowledgeActorParticipationVerifier>(), cancellationToken);
        if (audience.Error is not null)
            throw Invalid("INSTALLATION_AUDIENCE_NOT_READY", audience.Error.Code);
        var checks = readiness.Checks.ToDictionary(value => value.Name,
            value => new FreshExpectedCheck(value.Code, value.Evidence?.Revision, value.Evidence?.Fingerprint),
            StringComparer.Ordinal);
        return new(Format, manifest.ManifestHash, manifest.Application.Id, request.SourceRoot,
            DateTime.UtcNow, new(checks, JsonSerializer.SerializeToElement(audience.Data, Json)),
            EffectiveEnvironment(manifest, request, databasePath, installationRoot));
    }

    private static void SetLocalWebsiteContext(IServiceProvider provider, string applicationId)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = new HostString("127.0.0.1");
        context.Request.Path = $"/api/applications/{applicationId}/readiness";
        context.RequestServices = provider;
        var access = provider.GetRequiredService<WebAccessPolicy>().Evaluate(context);
        if (!access.Allowed)
            throw Invalid("INSTALLATION_LOCAL_ACCESS_DENIED", access.ErrorCode ?? "LOCAL_ACCESS_REQUIRED");
        context.User = WebAccessPolicy.CreatePrincipal(access);
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
    }

    internal static ServiceCollection Services(
        FreshInstallationManifest manifest, FreshInstallationRequest request,
        string databasePath, string installationRoot, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDantesRoleplayMcpServer(databasePath, DatabaseProvider.Sqlite, "installation.*",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [manifest.Source.AllowedRootId] = request.SourceRoot
            }, manifest.Runtime.PublishedApplications, configuration, Path.Combine(installationRoot, "blobs"));
        services.AddDantesRoleplayWeb(databasePath, configuration);
        services.AddScoped<ApplicationReadinessService>();
        services.AddScoped<IFreshComponentTypeBaselineInstaller, SqliteFreshComponentTypeBaselineInstaller>();
        return services;
    }

    internal static async Task<ApplicationReadinessReport> ReadReadinessAsync(
        ServiceProvider provider, string applicationId, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationReadinessService>()
            .ReadAsync(applicationId, cancellationToken);
    }

    private static IConfiguration Environment(
        FreshInstallationManifest manifest, FreshInstallationRequest request,
        string databasePath, string installationRoot)
    {
        var values = EffectiveEnvironment(manifest, request, databasePath, installationRoot);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    internal static Dictionary<string, string?> EffectiveEnvironment(
        FreshInstallationManifest manifest, FreshInstallationRequest request,
        string databasePath, string installationRoot)
    {
        var values = new Dictionary<string, string?>(manifest.Runtime.Environment, StringComparer.Ordinal)
        {
            ["ConnectionStrings:Kernel"] = databasePath,
            ["BlobStorage:Root"] = Path.Combine(installationRoot, "blobs"),
            ["Retrieval:DerivedDataDirectory"] = Path.Combine(installationRoot, "derived"),
            [$"Sources:AllowedRoots:{manifest.Source.AllowedRootId}"] = request.SourceRoot,
            ["WebInterface:RemoteAccess:AllowAnonymousPublicAccess"] = "false",
            ["Codex:PinnedVersion"] = CodexBridgeVersions.CurrentPinnedVersion,
            ["ApplicationValidationWorker:Enabled"] = "false",
            ["InteractionPlanning:Remote:Enabled"] = "false",
            ["Knowledge:Completion:Enabled"] = "false",
            ["Retrieval:Embedding:Enabled"] = "false"
        };
        for (var index = 0; index < manifest.Runtime.PublishedApplications.Count; index++)
            values[$"Catalogs:PublishedApplications:{index}"] = manifest.Runtime.PublishedApplications[index];
        return values;
    }

    private static async Task RegisterApplicationAsync(
        IRegistryAdministrationService registry, FreshApplication application,
        CancellationToken cancellationToken)
    {
        var id = ApplicationIdentifier.Parse(application.Id);
        var registration = new ApplicationRegistration(id, application.DisplayName, application.Description,
            application.Bases.Select(ApplicationIdentifier.Parse).ToArray());
        var context = RegistryContext(Token(Fingerprint(Encoding.UTF8.GetBytes(application.Id)), "application"));
        await registry.PreviewApplicationAsync(registration, context, cancellationToken);
        await registry.RegisterApplicationAsync(registration, context, cancellationToken);
    }

    private static ApplicationWorldAuthoringEntity ToWorldEntity(FreshWorldEntity entity) => new(
        entity.EntityId, entity.Name, 0,
        entity.Components.Select(value => new ApplicationWorldAuthoringComponent(
            value.QualifiedTypeId, 0, value.Value.GetRawText())).ToArray(),
        entity.Containment is null ? null : new(entity.Containment.ContainerEntityId, entity.Containment.Slot, 0));

    private static ApplicationWorldAuthoringRelationship ToWorldRelationship(FreshWorldRelationship value) =>
        new(value.FromEntityId, value.ToEntityId, value.QualifiedKind, 0, value.Value.GetRawText());

    private static void RequireEffects(ApplicationEcsEffectResult result, string phase)
    {
        if (!result.Valid) throw Invalid("INSTALLATION_WORLD_INVALID",
            $"The {phase} failed: {string.Join("; ", result.Problems.Select(value => value.Code))}");
    }

    private static void RequireWorld(ApplicationWorldAuthoringResult result, string path)
    {
        if (!result.Accepted) throw Invalid("INSTALLATION_WORLD_INVALID",
            $"World package '{path}' failed: {result.ErrorCode}.");
    }

    private static void RequireInputs(FreshInstallationRequest request)
    {
        if (!Path.IsPathFullyQualified(request.ManifestPath) || !File.Exists(request.ManifestPath))
            throw Invalid("INSTALLATION_MANIFEST_MISSING", "Installation:Manifest must name an existing absolute file.");
        if (!Path.IsPathFullyQualified(request.InstallationRoot) || Directory.Exists(request.InstallationRoot)
            || File.Exists(request.InstallationRoot))
            throw Invalid("INSTALLATION_TARGET_EXISTS", "Installation:Root must be an absent absolute path.");
        if (!Path.IsPathFullyQualified(request.SourceRoot) || !Directory.Exists(request.SourceRoot))
            throw Invalid("INSTALLATION_SOURCE_MISSING", "Installation:SourceRoot must name an existing absolute directory.");
        var packageRoot = Path.GetDirectoryName(request.ManifestPath)!;
        if (Overlaps(request.InstallationRoot, request.SourceRoot) || Overlaps(request.InstallationRoot, packageRoot))
            throw Invalid("INSTALLATION_TARGET_OVERLAP",
                "Installation:Root must be outside the source and installation-package directories.");
        RequireNoLinks(request.SourceRoot);
        RequireNoLinks(packageRoot);
    }

    internal static void ValidateManifest(
        FreshInstallationManifest manifest, FreshInstallationRequest request, byte[] bytes)
    {
        if (manifest.Format != Format) throw Invalid("INSTALLATION_FORMAT_INVALID", $"format must be '{Format}'.");
        manifest.ManifestHash = Fingerprint(bytes);
        if (manifest.BaseApplications.Count > 16 || manifest.Application.Sources.Count is < 1 or > 32
            || manifest.ComponentTypes.Count > 1024 || manifest.StateSpaces.Count is < 1 or > 16
            || manifest.Media.Count > 256 || manifest.Pages.Count > 32)
            throw Invalid("INSTALLATION_MANIFEST_BOUNDS", "The installation manifest exceeds a bounded collection limit.");
        if (manifest.Source.AllowedRootId.Length is < 1 or > 100 || manifest.CatalogRoot.Length is < 1 or > 240)
            throw Invalid("INSTALLATION_MANIFEST_INVALID", "The source or catalog root is invalid.");
        _ = SafePath(request.SourceRoot, manifest.CatalogRoot);
        if (!manifest.Runtime.PublishedApplications.Contains(manifest.Application.Id, StringComparer.Ordinal))
            throw Invalid("INSTALLATION_RUNTIME_INVALID", "The installed application must be in publishedApplications.");
        if (!manifest.Runtime.Environment.TryGetValue("Codex:ExecutablePath", out var codex)
            || string.IsNullOrWhiteSpace(codex))
            throw Invalid("INSTALLATION_RUNTIME_INVALID", "runtime.environment must declare a portable Codex:ExecutablePath.");
        foreach (var state in manifest.StateSpaces)
            if (state.Root is null || state.WorldPackages.Count > 64) throw Invalid("INSTALLATION_STATE_INVALID", "A bounded state-space root is required.");
        if (manifest.Application.Sources.Any(value => value.AllowedRootId != manifest.Source.AllowedRootId))
            throw Invalid("INSTALLATION_SOURCE_INVALID", "Every application source must use the declared installation source root.");
        if (manifest.StateSpaces.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != manifest.StateSpaces.Count
            || manifest.ComponentTypes.Select(value => value.QualifiedTypeId).Distinct(StringComparer.Ordinal).Count()
                != manifest.ComponentTypes.Count)
            throw Invalid("INSTALLATION_MANIFEST_INVALID", "State-space and component-type identities must be unique.");
    }

    internal static async Task<byte[]> ReadPinnedAsync(
        string root, string relativePath, string sha256, CancellationToken cancellationToken)
    {
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw Invalid("INSTALLATION_FILE_HASH_INVALID", $"'{relativePath}' has an invalid SHA-256 pin.");
        var path = SafePath(root, relativePath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!string.Equals(Fingerprint(bytes), sha256, StringComparison.OrdinalIgnoreCase))
            throw Invalid("INSTALLATION_FILE_HASH_MISMATCH", $"'{relativePath}' does not match its SHA-256 pin.");
        return bytes;
    }

    internal static string SafePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath)
            || relativePath.Contains(':', StringComparison.Ordinal)
            || relativePath.StartsWith('\\') || relativePath.StartsWith('/'))
            throw Invalid("INSTALLATION_PATH_INVALID", "Installation file paths must be nonblank and relative.");
        RequireNoLinks(root);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw Invalid("INSTALLATION_PATH_ESCAPE", $"'{relativePath}' escapes its declared root.");
        var cursor = fullRoot.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var segment in Path.GetRelativePath(cursor, full).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, segment);
            if (File.Exists(cursor) || Directory.Exists(cursor)) RequireNoLinks(cursor);
        }
        return File.Exists(full) || Directory.Exists(full)
            ? full : throw Invalid("INSTALLATION_FILE_MISSING", $"'{relativePath}' does not exist.");
    }

    private static bool Overlaps(string left, string right)
    {
        var first = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var second = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return first.StartsWith(second, StringComparison.OrdinalIgnoreCase)
            || second.StartsWith(first, StringComparison.OrdinalIgnoreCase);
    }

    internal static void RequireNoLinks(string path)
    {
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null; current = current switch
             {
                 FileInfo file => file.Directory,
                 DirectoryInfo directory => directory.Parent,
                 _ => null
             })
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Invalid("INSTALLATION_PATH_LINK", $"Installation path '{path}' traverses a filesystem link.");
        }
    }

    private static string CombineSpecification(string relativeRoot, string includePattern)
    {
        var root = relativeRoot.Replace('\\', '/').Trim('/');
        var pattern = includePattern.Replace('\\', '/').TrimStart('/');
        return root is "" or "." ? pattern : root + "/" + pattern;
    }

    private static SourceTrust ParseTrust(string value) => value switch
    {
        "trusted" => SourceTrust.Trusted,
        "untrusted" => SourceTrust.Untrusted,
        _ => throw Invalid("INSTALLATION_SOURCE_INVALID", "Source trust must be trusted or untrusted.")
    };

    private static EcsStateSpaceScope ParseScope(string value) => value switch
    {
        "runtime-state-space" => EcsStateSpaceScope.Runtime,
        "application-publication" => EcsStateSpaceScope.ApplicationPublication,
        _ => throw Invalid("INSTALLATION_STATE_INVALID", "State-space scope is invalid.")
    };

    private static RegistryAdministrationContext RegistryContext(string token) => new(
        token, null, "Provision a reviewed fresh installation registry entry.", [], Evidence(token));
    private static ApplicationActivationContext ActivationContext(string token) => new(
        token, "Activate the reviewed source included with a fresh installation.", [], Evidence(token));
    private static StateSpaceCreationContext StateContext(string token) => new(
        token, "Create a reviewed fresh installation state space.", [], Evidence(token));
    private static AuthorizationAuditEvidence Evidence(string token) => new(
        "principal." + new string('0', 64), "fresh-install", "installation.provision",
        "new-runtime", token, true, "FRESH_INSTALL_TARGET_ABSENT");

    private static string Token(string seed, string phase) =>
        Fingerprint(Encoding.UTF8.GetBytes(seed + "\n" + phase))[..32].ToLowerInvariant();
    internal static string Fingerprint(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static FreshInstallationException Invalid(string code, string message) => new(code, message);
}

internal sealed class FreshInstallationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal sealed class FreshInstallationManifest
{
    public required string Format { get; init; }
    [JsonIgnore]
    public string ManifestHash { get; set; } = "";
    public required string CatalogRoot { get; init; }
    public required FreshSourceRoot Source { get; init; }
    public IReadOnlyList<FreshApplication> BaseApplications { get; init; } = [];
    public required FreshApplication Application { get; init; }
    public IReadOnlyList<FreshComponentType> ComponentTypes { get; init; } = [];
    public required IReadOnlyList<FreshStateSpace> StateSpaces { get; init; }
    public IReadOnlyList<FreshMedia> Media { get; init; } = [];
    public required IReadOnlyList<FreshPage> Pages { get; init; }
    public required FreshRuntime Runtime { get; init; }
}

internal sealed record FreshSourceRoot(string AllowedRootId);
internal class FreshApplication
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> Bases { get; init; } = [];
    public IReadOnlyList<FreshApplicationSource> Sources { get; init; } = [];
    public IReadOnlyList<string> SelectedSourceIds { get; init; } = [];
}
internal sealed record FreshApplicationSource(
    string Id, string AllowedRootId, string RelativeRoot, string IncludePattern,
    string Trust, int Precedence, string LogicalIdentity);
internal sealed record FreshComponentType(
    string OwnerApplicationId, string QualifiedTypeId, int Version, string SchemaPath, string Sha256,
    string? SchemaHash,
    string Root = "source");
internal sealed record FreshPinnedFile(string Path, string Sha256, string Root = "package");
internal sealed class FreshStateSpace
{
    public required string Id { get; init; }
    public required string Scope { get; init; }
    public required FreshRootEntity Root { get; init; }
    public IReadOnlyList<FreshPinnedFile> WorldPackages { get; init; } = [];
}
internal sealed record FreshRootEntity(string EntityId, string Name, IReadOnlyList<FreshWorldComponent> Components);
internal sealed record FreshWorldComponent(string QualifiedTypeId, JsonElement Value);
internal sealed class FreshWorldPackage
{
    public required string Format { get; init; }
    public required string RootEntityId { get; init; }
    public IReadOnlyList<FreshWorldEntity> Entities { get; init; } = [];
    public IReadOnlyList<FreshWorldRelationship> Relationships { get; init; } = [];
}
internal sealed record FreshWorldEntity(
    string EntityId, string Name, IReadOnlyList<FreshWorldComponent> Components, FreshContainment? Containment);
internal sealed record FreshContainment(string ContainerEntityId, string Slot);
internal sealed record FreshWorldRelationship(
    string FromEntityId, string ToEntityId, string QualifiedKind, JsonElement Value);
internal sealed record FreshMedia(string Path, string Sha256, string MediaType, long ByteLength);
internal sealed class FreshPage
{
    public required string Owner { get; init; }
    public string PageId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string Title { get; init; } = "";
    public string NavigationLabel { get; init; } = "";
    public string Slug { get; init; } = "";
    public int Order { get; init; }
    public string Visibility { get; init; } = "public";
    public bool IsIndexPage { get; init; }
    public required FreshPinnedFile Bundle { get; init; }
}
internal sealed class FreshRuntime
{
    public required IReadOnlyList<string> PublishedApplications { get; init; }
    public Dictionary<string, string?> Environment { get; init; } = new(StringComparer.Ordinal);
}
internal sealed record FreshExpectedCheck(string Code, string? Revision, string? Fingerprint);
internal sealed record FreshExpectedState(
    IReadOnlyDictionary<string, FreshExpectedCheck> Checks, JsonElement Audience);
internal sealed record FreshInstallationReceipt(
    string Format, string ManifestHash, string ApplicationId, string SourceRoot,
    DateTime InstalledAtUtc, FreshExpectedState Expected,
    IReadOnlyDictionary<string, string?> Environment);
