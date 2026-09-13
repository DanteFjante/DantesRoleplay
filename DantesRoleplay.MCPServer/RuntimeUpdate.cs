using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.MCPServer;

internal sealed record RuntimeUpdateRequest(
    string ManifestPath, string ProfilePath, string RuntimeRoot, string SourceRoot)
{
    public static bool TryRead(IConfiguration configuration, out RuntimeUpdateRequest request)
    {
        var manifest = configuration["RuntimeUpdate:Manifest"];
        var profile = configuration["RuntimeUpdate:Profile"];
        var root = configuration["RuntimeUpdate:Root"];
        var source = configuration["RuntimeUpdate:SourceRoot"];
        var any = manifest is not null || profile is not null || root is not null || source is not null;
        if (!any) { request = null!; return false; }
        if (new[] { manifest, profile, root, source }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException(
                "RuntimeUpdate:Manifest, RuntimeUpdate:Profile, RuntimeUpdate:Root, and RuntimeUpdate:SourceRoot are required together.");
        request = new(Path.GetFullPath(manifest!), Path.GetFullPath(profile!),
            Path.GetFullPath(root!), Path.GetFullPath(source!));
        return true;
    }
}

internal static class RuntimeUpdate
{
    private const string Format = "dantesroleplay.runtime-update/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(RuntimeUpdateRequest request, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? owned = null;
        try
        {
            RequireInputs(request);
            var profile = DeserializeProfile(await File.ReadAllBytesAsync(request.ProfilePath, cancellationToken));
            ValidateProfile(profile, request);
            var manifestBytes = await File.ReadAllBytesAsync(request.ManifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<FreshInstallationManifest>(manifestBytes, FreshInstallation.Json)
                ?? throw Fail("RUNTIME_UPDATE_MANIFEST_EMPTY", "The installation manifest is empty.");
            FreshInstallation.ValidateManifest(manifest,
                new(request.ManifestPath, request.RuntimeRoot, request.SourceRoot), manifestBytes);
            if (!string.Equals(profile.ApplicationId, manifest.Application.Id, StringComparison.Ordinal))
                throw Fail("RUNTIME_UPDATE_APPLICATION_MISMATCH", "The update package targets a different application.");

            PrepareThreeWayBase(profile.SourceRoot, request.SourceRoot);
            var parent = Path.GetDirectoryName(request.RuntimeRoot)!;
            Directory.CreateDirectory(parent);
            owned = Path.Combine(parent, $".{Path.GetFileName(request.RuntimeRoot)}.updating-{Guid.NewGuid():N}");
            Directory.CreateDirectory(owned);
            var stageDatabase = Path.Combine(owned, "database.db");
            Snapshot(profile.Database, stageDatabase);
            await CopyBlobsAsync(profile.BlobRoot, Path.Combine(owned, "blobs"), cancellationToken);
            Directory.CreateDirectory(Path.Combine(owned, "derived"));

            var environment = Configuration(EffectiveEnvironment(profile, request, stageDatabase, owned, offline: true));
            await UpdateCandidateAsync(manifest, profile, request, stageDatabase, owned, environment, cancellationToken);

            SqliteConnection.ClearAllPools();
            Directory.Move(owned, request.RuntimeRoot);
            owned = request.RuntimeRoot;
            var finalDatabase = Path.Combine(request.RuntimeRoot, "database.db");
            var finalEnvironment = Configuration(EffectiveEnvironment(profile, request, finalDatabase, request.RuntimeRoot, offline: true));
            var receipt = await VerifyAsync(manifest, profile, request, finalDatabase, request.RuntimeRoot,
                finalEnvironment, cancellationToken);
            var receiptPath = Path.Combine(request.RuntimeRoot, "update.json");
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt, Json) + Environment.NewLine,
                new UTF8Encoding(false), cancellationToken);
            owned = null;
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                status = "updated",
                applicationId = receipt.ApplicationId,
                receiptPath,
                targetCount = receipt.Targets.Count
            }, Json));
            return 0;
        }
        catch (Exception exception)
        {
            if (owned is not null && Directory.Exists(owned))
            {
                try { SqliteConnection.ClearAllPools(); Directory.Delete(owned, true); }
                catch (Exception cleanup) { await error.WriteLineAsync($"Runtime update cleanup failed: {cleanup.Message}"); }
            }
            await error.WriteLineAsync($"Runtime update failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task UpdateCandidateAsync(FreshInstallationManifest manifest, RuntimeUpdateProfile profile,
        RuntimeUpdateRequest request, string databasePath, string runtimeRoot, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var services = Services(manifest, profile, request, databasePath, runtimeRoot, configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.InitialiseDantesRoleplayAsync(cancellationToken);
        await provider.InitialiseDantesRoleplayWebAsync(cancellationToken);
        var catalogRoot = FreshInstallation.SafePath(request.SourceRoot, manifest.CatalogRoot);
        var validation = await CatalogValidator.ValidateAsync(catalogRoot, cancellationToken);
        if (!validation.IsValid)
            throw Fail("RUNTIME_UPDATE_CATALOG_INVALID", $"The catalog has {validation.Errors} validation error(s).");

        await using (var scope = provider.CreateAsyncScope())
        {
            ReconcileNamespaces(scope.ServiceProvider, profile.SourceRoot, request.SourceRoot, manifest.CatalogRoot);
            var importer = scope.ServiceProvider.GetRequiredService<CatalogImporter>();
            var imported = await importer.ApplyAsync(catalogRoot,
                new CatalogImportOptions(Scope: CatalogImportScope.AuthoredDefinitions), cancellationToken);
            if (imported.Aborted)
                throw Fail("RUNTIME_UPDATE_CATALOG_CONFLICT", "Catalog and database edits conflict; no side was forced.");
        }

        await using (var scope = provider.CreateAsyncScope())
            await ReconcileTypesAsync(scope.ServiceProvider, manifest, request, cancellationToken);

        var application = ApplicationIdentifier.Parse(profile.ApplicationId);
        ActiveApplicationManifest active;
        await using (var scope = provider.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IApplicationRegistry>();
            _ = applications.Describe(application)
                ?? throw Fail("RUNTIME_UPDATE_APPLICATION_MISSING", "The installed application is not registered.");
            var sources = scope.ServiceProvider.GetRequiredService<ISourceRegistry>();
            var prior = scope.ServiceProvider.GetRequiredService<IApplicationActivationReader>().Current(application)
                ?? throw Fail("RUNTIME_UPDATE_APPLICATION_INACTIVE", "The installed application has no active source selection.");
            var sourceIds = prior.Sources.Select(value => value.SourceId).ToArray();
            if (sourceIds.Length == 0 || sourceIds.Any(sourceId => sources.Get(application, sourceId) is null))
                throw Fail("RUNTIME_UPDATE_SOURCE_MISSING", "The installed active source selection is incomplete.");
            var extensionIds = prior.Extensions.Select(value => value.ExtensionId).ToArray();
            var preview = await scope.ServiceProvider.GetRequiredService<IApplicationPreviewService>()
                .PreviewAsync(application, sourceIds, extensionIds, cancellationToken);
            if (!preview.IsValid) throw Fail("RUNTIME_UPDATE_APPLICATION_INVALID", "The retained application selection does not preview cleanly.");
            var activations = scope.ServiceProvider.GetRequiredService<IApplicationActivationService>();
            var requestValue = new ApplicationActivationRequest(application, preview.PreviewFingerprint,
                prior.ActivationFingerprint, sourceIds) { ExtensionIds = extensionIds };
            var activationSeed = string.Join("\n", new[]
            {
                manifest.ManifestHash, prior.ActivationFingerprint, preview.PreviewFingerprint,
                string.Join("\n", sourceIds.Order(StringComparer.Ordinal)),
                string.Join("\n", extensionIds.Order(StringComparer.Ordinal))
            });
            var contextValue = ActivationContext(Token(activationSeed, "activate"));
            await activations.PreviewAsync(requestValue, contextValue, cancellationToken);
            active = (await activations.ActivateAsync(requestValue, contextValue, cancellationToken)).Activation;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var states = scope.ServiceProvider.GetRequiredService<IStateSpaceAdministrationService>();
            var installedStates = states.List(application, 100);
            if (installedStates.Count == 100)
                throw Fail("RUNTIME_UPDATE_STATE_LIMIT", "The installation has too many state spaces for a complete bounded update.");
            foreach (var state in installedStates)
            {
                if (state.ActiveFingerprint == active.ActivationFingerprint) continue;
                var upgrade = new StateSpaceUpgradeRequest(state.StateSpaceId, application,
                    active.ActivationFingerprint, state.BindingFingerprint);
                var stateSeed = string.Join("\n", manifest.ManifestHash, state.StateSpaceId,
                    state.BindingFingerprint, active.ActivationFingerprint);
                var context = new StateSpaceUpgradeContext(Token(stateSeed, "state-upgrade"),
                    "Rebind preserved state to the reviewed updated application.", [], Evidence(manifest.ManifestHash));
                await states.PreviewUpgradeAsync(upgrade, context, cancellationToken);
                await states.UpgradeAsync(upgrade, context, cancellationToken);
            }
        }

        await PublishPagesAsync(provider, manifest, request, application, cancellationToken);
        SetWebsiteContext(provider, profile.ApplicationId, profile.Targets[0].Origin, configuration);
        await using var readyScope = provider.CreateAsyncScope();
        var readiness = await readyScope.ServiceProvider.GetRequiredService<ApplicationReadinessService>()
            .ReadAsync(application.Value, cancellationToken);
        if (readiness.Status != "ready") throw Fail("RUNTIME_UPDATE_NOT_READY", "The candidate runtime is not ready.");
    }

    private static async Task ReconcileTypesAsync(IServiceProvider provider, FreshInstallationManifest manifest,
        RuntimeUpdateRequest request, CancellationToken cancellationToken)
    {
        var types = provider.GetRequiredService<IApplicationComponentTypeRegistry>();
        var schemas = provider.GetRequiredService<IBoundedJsonSchemaValidator>();
        foreach (var component in manifest.ComponentTypes)
        {
            var root = component.Root == "source" ? request.SourceRoot : Path.GetDirectoryName(request.ManifestPath)!;
            var bytes = await FreshInstallation.ReadPinnedAsync(root, component.SchemaPath, component.Sha256, cancellationToken);
            var schema = Encoding.UTF8.GetString(bytes);
            var compiled = schemas.Compile(schema);
            if (!compiled.IsAccepted || component.SchemaHash is not null && compiled.SchemaHash != component.SchemaHash)
                throw Fail("RUNTIME_UPDATE_COMPONENT_TYPE_INVALID", $"Component type '{component.QualifiedTypeId}' has an invalid schema pin.");
            var exact = types.Get(component.QualifiedTypeId, component.Version);
            if (exact is not null)
            {
                if (exact.SchemaHash != compiled.SchemaHash) throw Fail("RUNTIME_UPDATE_COMPONENT_TYPE_CONFLICT",
                    $"Component type '{component.QualifiedTypeId}' version {component.Version} differs from the package.");
                continue;
            }
            var latest = types.GetLatest(component.QualifiedTypeId);
            if ((latest?.Version ?? 0) + 1 != component.Version)
                throw Fail("RUNTIME_UPDATE_COMPONENT_TYPE_GAP", $"Component type '{component.QualifiedTypeId}' is not the next append-only version.");
            var owner = component.OwnerApplicationId == "system" ? ApplicationIdentifier.System
                : ApplicationIdentifier.Parse(component.OwnerApplicationId);
            var added = types.Define(new(owner, component.QualifiedTypeId, schema));
            if (added.Version != component.Version || added.SchemaHash != compiled.SchemaHash)
                throw Fail("RUNTIME_UPDATE_COMPONENT_TYPE_MISMATCH", $"Component type '{component.QualifiedTypeId}' did not append exactly.");
        }
    }

    private static async Task PublishPagesAsync(ServiceProvider provider, FreshInstallationManifest manifest,
        RuntimeUpdateRequest request, ApplicationIdentifier app, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWebPageStore>();
        var admin = scope.ServiceProvider.GetRequiredService<WebPageAdministration>();
        var reader = new WebPageBundleReader();
        foreach (var page in manifest.Pages)
        {
            var bytes = await FreshInstallation.ReadPinnedAsync(Path.GetDirectoryName(request.ManifestPath)!,
                page.Bundle.Path, page.Bundle.Sha256, cancellationToken);
            await using var stream = new MemoryStream(bytes, false);
            var bundle = await reader.ReadAsync(stream, bytes.LongLength, cancellationToken);
            string pageId;
            if (page.Owner == "system") pageId = page.PageId;
            else
            {
                if (page.ApplicationId != app.Value) throw Fail("RUNTIME_UPDATE_PAGE_INVALID", "An application page targets another application.");
                var view = await admin.GetAsync(app, page.EntityId, cancellationToken)
                    ?? throw Fail("RUNTIME_UPDATE_PAGE_MISSING", $"Existing page '{page.EntityId}' is missing.");
                pageId = view.ContentPageId;
            }
            var summary = await store.GetSummaryAsync(pageId, cancellationToken)
                ?? throw Fail("RUNTIME_UPDATE_PAGE_MISSING", $"Existing page '{pageId}' is missing.");
            var current = await store.GetRevisionAsync(pageId, summary.ActiveRevision, cancellationToken)
                ?? throw Fail("RUNTIME_UPDATE_PAGE_MISSING", $"Active page '{pageId}' is unavailable.");
            if (Same(current, bundle)) continue;
            var draft = await store.AppendBundleDraftAsync(pageId, summary.LatestRevision, bundle, cancellationToken);
            await store.ActivateRevisionAsync(pageId, draft.Summary.Revision, summary.ActiveRevision, cancellationToken);
        }
    }

    private static bool Same(WebPageRevisionDocument current, WebPageBundle bundle) =>
        current.Html == bundle.Html && current.Assets.Count == bundle.Assets.Count
        && current.Assets.OrderBy(value => value.Path, StringComparer.Ordinal).Zip(
            bundle.Assets.OrderBy(value => value.Path, StringComparer.Ordinal))
            .All(pair => pair.First.Path == pair.Second.Path
                && pair.First.Content.AsSpan().SequenceEqual(pair.Second.Content));

    private static async Task<RuntimeUpdateReceipt> VerifyAsync(FreshInstallationManifest manifest,
        RuntimeUpdateProfile profile, RuntimeUpdateRequest request, string databasePath, string runtimeRoot,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var services = Services(manifest, profile, request, databasePath, runtimeRoot, configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var targets = new List<RuntimeUpdateTarget>();
        foreach (var prior in profile.Targets)
        {
            SetWebsiteContext(provider, profile.ApplicationId, prior.Origin, configuration);
            var readiness = await FreshInstallation.ReadReadinessAsync(provider, profile.ApplicationId, cancellationToken);
            if (readiness.Status != "ready") throw Fail("RUNTIME_UPDATE_FINAL_NOT_READY", $"Target '{prior.Origin}' is not ready.");
            await using var scope = provider.CreateAsyncScope();
            var audience = await SystemAudienceContextHandler.ResolveAsync(
                scope.ServiceProvider.GetRequiredService<ILocalKnowledgeSeatProvider>(),
                scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeAudiencePolicy>(),
                scope.ServiceProvider.GetRequiredService<IKnowledgeApplicationBindingResolver>(),
                scope.ServiceProvider.GetRequiredService<IKnowledgeActorParticipationVerifier>(), cancellationToken);
            if (audience.Error is not null) throw Fail("RUNTIME_UPDATE_AUDIENCE_NOT_READY", audience.Error.Code);
            var audienceElement = JsonSerializer.SerializeToElement(audience.Data, Json);
            RequireSameAudienceAuthority(prior.Expected, audienceElement, prior.Origin);
            var checks = readiness.Checks.ToDictionary(value => value.Name,
                value => new FreshExpectedCheck(value.Code, value.Evidence?.Revision, value.Evidence?.Fingerprint), StringComparer.Ordinal);
            targets.Add(new(prior.Origin, new(checks, audienceElement)));
        }
        return new(Format, manifest.ManifestHash, profile.ApplicationId, profile.SourceRoot, request.SourceRoot,
            DateTime.UtcNow, EffectiveEnvironment(profile, request, databasePath, runtimeRoot, offline: false), targets);
    }

    private static ServiceCollection Services(FreshInstallationManifest manifest, RuntimeUpdateProfile profile,
        RuntimeUpdateRequest request, string databasePath, string runtimeRoot, IConfiguration configuration)
    {
        var roots = SourceRoots(profile, request.SourceRoot);
        var published = PublishedApplications(profile, manifest.Application.Id);
        var services = new ServiceCollection();
        services.AddLogging(value => value.SetMinimumLevel(LogLevel.Warning));
        services.AddDantesRoleplayMcpServer(databasePath, DatabaseProvider.Sqlite, "runtime-update.*",
            roots, published, configuration, Path.Combine(runtimeRoot, "blobs"));
        services.AddDantesRoleplayWeb(databasePath, configuration);
        services.AddScoped<ApplicationReadinessService>();
        return services;
    }

    internal static Dictionary<string, string?> EffectiveEnvironment(RuntimeUpdateProfile profile,
        RuntimeUpdateRequest request, string databasePath, string runtimeRoot, bool offline)
    {
        var values = profile.Environment.ToDictionary(
            value => value.Key.Replace("__", ":", StringComparison.Ordinal), value => value.Value,
            StringComparer.Ordinal);
        values["ConnectionStrings:Kernel"] = databasePath;
        values["BlobStorage:Root"] = Path.Combine(runtimeRoot, "blobs");
        values["Retrieval:DerivedDataDirectory"] = Path.Combine(runtimeRoot, "derived");
        foreach (var key in values.Keys.Where(key => key.StartsWith("Sources:AllowedRoots:", StringComparison.Ordinal)).ToArray())
            if (PathEquals(values[key], profile.SourceRoot)) values[key] = request.SourceRoot;
        if (!values.Keys.Any(key => key.StartsWith("Sources:AllowedRoots:", StringComparison.Ordinal)))
            values["Sources:AllowedRoots:repository"] = request.SourceRoot;
        if (offline)
        {
            values["ApplicationValidationWorker:Enabled"] = "false";
            values["InteractionPlanning:Remote:Enabled"] = "false";
            values["Knowledge:Completion:Enabled"] = "false";
            values["Retrieval:Embedding:Enabled"] = "false";
        }
        return values;
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string> SourceRoots(RuntimeUpdateProfile profile, string newRoot)
    {
        var values = EffectiveEnvironment(profile,
            new("x", "x", "x", newRoot), profile.Database, Path.GetDirectoryName(profile.Database)!, offline: false)
            .Where(value => value.Key.StartsWith("Sources:AllowedRoots:", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(value.Value))
            .ToDictionary(value => value.Key["Sources:AllowedRoots:".Length..], value => value.Value!, StringComparer.Ordinal);
        if (values.Count == 0) throw Fail("RUNTIME_UPDATE_SOURCE_CONFIGURATION_MISSING", "The profile has no allowed source roots.");
        return values;
    }

    internal static string[] PublishedApplications(RuntimeUpdateProfile profile, string required)
    {
        var result = profile.Environment.Select(value => new KeyValuePair<string, string?>(
                value.Key.Replace("__", ":", StringComparison.Ordinal), value.Value))
            .Where(value => value.Key.StartsWith("Catalogs:PublishedApplications:", StringComparison.Ordinal))
            .OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => value.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        if (!result.Contains(required, StringComparer.Ordinal)) result.Add(required);
        return result.ToArray();
    }

    private static void SetWebsiteContext(IServiceProvider provider, string applicationId, string origin,
        IConfiguration configuration)
    {
        var uri = new Uri(origin, UriKind.Absolute);
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Scheme = uri.Scheme;
        context.Request.Host = uri.IsDefaultPort ? new HostString(uri.Host) : new HostString(uri.Host, uri.Port);
        context.Request.Path = $"/api/readiness/applications/{applicationId}";
        var loopbackHost = IPAddress.TryParse(uri.Host, out var parsed) && IPAddress.IsLoopback(parsed)
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        context.Connection.RemoteIpAddress = loopbackHost ? IPAddress.Loopback : IPAddress.Parse("192.0.2.1");
        var tailscaleHost = configuration["WebInterface:RemoteAccess:TailscaleHost"];
        if (!loopbackHost && uri.Host.Equals(tailscaleHost, StringComparison.OrdinalIgnoreCase))
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            var login = configuration.GetSection("WebInterface:RemoteAccess:AllowedTailscaleUsers").GetChildren()
                .Select(value => value.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (login is not null) context.Request.Headers["Tailscale-User-Login"] = login;
        }
        var access = provider.GetRequiredService<WebAccessPolicy>().Evaluate(context);
        if (!access.Allowed) throw Fail("RUNTIME_UPDATE_TARGET_DENIED", $"Target '{origin}' is denied: {access.ErrorCode}.");
        context.User = WebAccessPolicy.CreatePrincipal(access);
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
    }

    private static void RequireSameAudienceAuthority(JsonElement priorExpected, JsonElement current, string origin)
    {
        if (!priorExpected.TryGetProperty("audience", out var prior))
            throw Fail("RUNTIME_UPDATE_PROFILE_INVALID", $"Target '{origin}' has no preserved audience pin.");
        foreach (var name in new[] { "applicationId", "stateSpaceId", "campaignId", "role", "actorId" })
        {
            var before = prior.TryGetProperty(name, out var beforeValue) ? beforeValue.GetRawText() : "null";
            var after = current.TryGetProperty(name, out var afterValue) ? afterValue.GetRawText() : "null";
            if (!string.Equals(before, after, StringComparison.Ordinal))
                throw Fail("RUNTIME_UPDATE_AUDIENCE_CHANGED", $"Target '{origin}' changed its '{name}' audience authority.");
        }
    }

    private static void PrepareThreeWayBase(string oldSourceRoot, string newSourceRoot)
    {
        var oldManifest = FreshInstallation.SafePath(oldSourceRoot, "catalog/manifest.json");
        var newManifest = FreshInstallation.SafePath(newSourceRoot, "catalog/manifest.json");
        if (!File.Exists(oldManifest) || !File.Exists(newManifest))
            throw Fail("RUNTIME_UPDATE_BASE_MISSING", "Both the prior and updated catalog manifests are required.");
        FreshInstallation.RequireNoLinks(oldManifest);
        FreshInstallation.RequireNoLinks(newManifest);
        File.Copy(oldManifest, newManifest, true);
    }

    private static void ReconcileNamespaces(IServiceProvider provider, string oldSourceRoot, string newSourceRoot,
        string catalogRelativeRoot)
    {
        var oldRoot = FreshInstallation.SafePath(oldSourceRoot, Path.Combine(catalogRelativeRoot, "namespaces"));
        var newRoot = FreshInstallation.SafePath(newSourceRoot, Path.Combine(catalogRelativeRoot, "namespaces"));
        var oldFiles = NamespaceFiles(oldRoot);
        var newFiles = NamespaceFiles(newRoot);
        var stored = provider.GetRequiredService<ICatalogNamespaceRegistry>().List(true)
            .ToDictionary(value => value.Id, ToFile, StringComparer.Ordinal);
        foreach (var (id, current) in stored)
        {
            oldFiles.TryGetValue(id, out var old);
            newFiles.TryGetValue(id, out var newer);
            if (newer is null) continue;
            if (old is null)
            {
                if (!SameNamespace(current, newer.File))
                    throw Fail("RUNTIME_UPDATE_NAMESPACE_CONFLICT",
                        $"Namespace '{id}' exists in the database without a reliable catalog base.");
                continue;
            }
            var databaseMoved = !SameNamespace(current, old.File);
            var fileMoved = !SameNamespace(newer.File, old.File);
            if (databaseMoved && fileMoved && !SameNamespace(current, newer.File))
                throw Fail("RUNTIME_UPDATE_NAMESPACE_CONFLICT", $"Namespace '{id}' changed in both the database and catalog.");
            if (databaseMoved && !fileMoved)
                File.WriteAllText(newer.Path, current.ToJson(), new UTF8Encoding(false));
        }
    }

    private static Dictionary<string, NamespaceSource> NamespaceFiles(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(path => new NamespaceSource(path, CatalogNamespaceFile.Parse(File.ReadAllText(path), path)))
            .ToDictionary(value => value.File.Id, StringComparer.Ordinal)
        : new(StringComparer.Ordinal);

    private static CatalogNamespaceFile ToFile(CatalogNamespaceDefinition value) => new(value.Id, value.Owner,
        value.Description, value.AllowedKinds, value.Aliases, value.IsEnabled, value.ReviewStatus, value.ReviewNote);
    private static bool SameNamespace(CatalogNamespaceFile left, CatalogNamespaceFile right) =>
        string.Equals(left.ToJson(), right.ToJson(), StringComparison.Ordinal);

    internal static void Snapshot(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using (var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False"))
        using (var destination = new SqliteConnection($"Data Source={destinationPath};Mode=ReadWriteCreate;Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
        using var verification = new SqliteConnection($"Data Source={destinationPath};Mode=ReadOnly;Pooling=False");
        verification.Open();
        using var command = verification.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar()?.ToString();
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw Fail("RUNTIME_UPDATE_BACKUP_INVALID",
                $"The candidate database snapshot failed integrity_check: {result ?? "no result"}.");
    }

    internal static async Task CopyBlobsAsync(string sourceRoot, string destinationRoot, CancellationToken token)
    {
        FreshInstallation.RequireNoLinks(sourceRoot);
        Directory.CreateDirectory(destinationRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            FreshInstallation.RequireNoLinks(source);
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = NewChildPath(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, token);
                await output.FlushAsync(token);
            }
            var sourceHash = await HashFileAsync(source, token);
            var destinationHash = await HashFileAsync(destination, token);
            if (new FileInfo(source).Length != new FileInfo(destination).Length || sourceHash != destinationHash)
                throw Fail("RUNTIME_UPDATE_BLOB_COPY_INVALID", $"Blob '{relative}' did not copy exactly.");
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    private static string NewChildPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')
            || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "" or "." or ".."))
            throw Fail("RUNTIME_UPDATE_BLOB_PATH_INVALID", "A blob path is not a safe relative path.");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw Fail("RUNTIME_UPDATE_BLOB_PATH_INVALID", "A blob path escapes its destination root.");
        return path;
    }

    private static void RequireInputs(RuntimeUpdateRequest request)
    {
        if (!File.Exists(request.ManifestPath) || !File.Exists(request.ProfilePath))
            throw Fail("RUNTIME_UPDATE_INPUT_MISSING", "Manifest and profile must be existing absolute files.");
        if (!Directory.Exists(request.SourceRoot)) throw Fail("RUNTIME_UPDATE_SOURCE_MISSING", "The updated source root is missing.");
        if (Directory.Exists(request.RuntimeRoot) || File.Exists(request.RuntimeRoot))
            throw Fail("RUNTIME_UPDATE_TARGET_EXISTS", "RuntimeUpdate:Root must be absent.");
        FreshInstallation.RequireNoLinks(request.SourceRoot);
        FreshInstallation.RequireNoLinks(Path.GetDirectoryName(request.ManifestPath)!);
    }

    internal static RuntimeUpdateProfile DeserializeProfile(ReadOnlySpan<byte> bytes) =>
        JsonSerializer.Deserialize<RuntimeUpdateProfile>(bytes, Json)
        ?? throw Fail("RUNTIME_UPDATE_PROFILE_EMPTY", "The preserved runtime profile is empty.");

    internal static void ValidateProfile(RuntimeUpdateProfile profile, RuntimeUpdateRequest request)
    {
        if (!Path.IsPathFullyQualified(profile.Database) || !Path.IsPathFullyQualified(profile.BlobRoot)
            || !Path.IsPathFullyQualified(profile.SourceRoot) || !Path.IsPathFullyQualified(profile.HostRoot)
            || !File.Exists(profile.Database) || !Directory.Exists(profile.BlobRoot)
            || !Directory.Exists(profile.SourceRoot) || !Directory.Exists(profile.HostRoot))
            throw Fail("RUNTIME_UPDATE_PROFILE_STORAGE_MISSING", "The preserved profile storage is unavailable.");
        if (profile.SchemaVersion != 1 || profile.Targets.Count is < 1 or > 16 || string.IsNullOrWhiteSpace(profile.ApplicationId)
            || string.IsNullOrWhiteSpace(profile.ListenUrl) || string.IsNullOrWhiteSpace(profile.Executable)
            || profile.HostFiles.Count == 0 || profile.SourceFiles.Count == 0)
            throw Fail("RUNTIME_UPDATE_PROFILE_INVALID", "The preserved profile has invalid application targets.");
        if (PathEquals(profile.SourceRoot, request.SourceRoot))
            throw Fail("RUNTIME_UPDATE_SOURCE_NOT_FROZEN", "The update source must be distinct from the prior frozen source.");
        FreshInstallation.RequireNoLinks(profile.Database);
        FreshInstallation.RequireNoLinks(profile.BlobRoot);
        FreshInstallation.RequireNoLinks(profile.SourceRoot);
        FreshInstallation.RequireNoLinks(profile.HostRoot);
        var packageRoot = Path.GetDirectoryName(request.ManifestPath)!;
        if (Overlaps(request.RuntimeRoot, profile.BlobRoot) || Overlaps(request.SourceRoot, profile.BlobRoot)
            || Overlaps(packageRoot, profile.BlobRoot) || Overlaps(request.RuntimeRoot, profile.SourceRoot)
            || Overlaps(request.RuntimeRoot, profile.HostRoot) || Overlaps(request.SourceRoot, profile.SourceRoot)
            || Overlaps(request.SourceRoot, profile.HostRoot))
            throw Fail("RUNTIME_UPDATE_PATH_OVERLAP", "Update inputs must be outside preserved runtime, source, host, and blob roots.");
    }

    private static string Combine(string root, string pattern) => root == "." ? pattern : $"{root.TrimEnd('/', '\\')}/{pattern}";
    private static SourceTrust ParseTrust(string value) => value == "trusted" ? SourceTrust.Trusted
        : value == "untrusted" ? SourceTrust.Untrusted : throw Fail("RUNTIME_UPDATE_SOURCE_INVALID", "Source trust is invalid.");
    private static bool PathEquals(string? left, string right) => left is not null
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static bool Overlaps(string left, string right)
    {
        var first = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var second = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return first.StartsWith(second, StringComparison.OrdinalIgnoreCase)
            || second.StartsWith(first, StringComparison.OrdinalIgnoreCase);
    }
    private static string Token(string seed, string phase) => FreshInstallation.Fingerprint(Encoding.UTF8.GetBytes(seed + "\n" + phase))[..32].ToLowerInvariant();
    private static Authorization.AuthorizationAuditEvidence Evidence(string token) => new(
        "principal." + new string('0', 64), "runtime-update", "installation.update", "candidate-runtime", token, true, "OFFLINE_CANDIDATE_COPY");
    private static RegistryAdministrationContext RegistryContext(string token) => new(token, null,
        "Register an update source that is absent from the preserved installation.", [], Evidence(token));
    private static ApplicationActivationContext ActivationContext(string token) => new(token,
        "Activate the reviewed updated source while preserving the installed selection.", [], Evidence(token));
    private static RuntimeUpdateException Fail(string code, string message) => new(code, message);
    private sealed record NamespaceSource(string Path, CatalogNamespaceFile File);
}

internal sealed class RuntimeUpdateException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal sealed class RuntimeUpdateProfile
{
    public int SchemaVersion { get; init; }
    public required string HostRoot { get; init; }
    public required string Database { get; init; }
    public required string BlobRoot { get; init; }
    public required string SourceRoot { get; init; }
    public required string Executable { get; init; }
    public required string ApplicationId { get; init; }
    public required string ListenUrl { get; init; }
    public Dictionary<string, string?> Environment { get; init; } = new(StringComparer.Ordinal);
    public IReadOnlyList<RuntimeUpdatePriorTarget> Targets { get; init; } = [];
    public IReadOnlyList<RuntimeUpdateFilePin> HostFiles { get; init; } = [];
    public IReadOnlyList<RuntimeUpdateFilePin> SourceFiles { get; init; } = [];
}
internal sealed record RuntimeUpdateFilePin(string Path, long Length, string Sha256);
internal sealed record RuntimeUpdatePriorTarget(string Origin, JsonElement Expected);
internal sealed record RuntimeUpdateTarget(string Origin, FreshExpectedState Expected);
internal sealed record RuntimeUpdateReceipt(string Format, string ManifestHash, string ApplicationId,
    string PreviousSourceRoot, string SourceRoot, DateTime UpdatedAtUtc,
    IReadOnlyDictionary<string, string?> Environment, IReadOnlyList<RuntimeUpdateTarget> Targets);
