using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Live;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using DantesRoleplay.Web.Security;
using DantesRoleplay.Web.Settings;
using DantesRoleplay.Authorization;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.Web.Interactions;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.SystemConversations;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.AI;
using DantesRoleplay.Ecs;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.World;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using System.Threading.RateLimiting;

namespace DantesRoleplay.Web.Hosting;

public static class WebInterfaceServiceCollectionExtensions
{
    private const string MigrationHistoryTable = "__web_migrations_history";

    public static IServiceCollection AddDantesRoleplayWeb(
        this IServiceCollection services,
        string connectionStringOrPath,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringOrPath);
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = NormaliseSqlite(connectionStringOrPath);

        services.AddDbContext<WebContentDbContext>(options =>
            options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsHistoryTable(MigrationHistoryTable)));
        services.AddScoped<IWebPageStore, WebPageStore>();
        services.AddSingleton<WebPageIdentityMigrationState>();
        services.AddScoped<WebPagePublicationService>();
        services.AddScoped<IWebPagePublicationDirectory>(provider =>
            HasPublicationDependencies(provider)
                ? provider.GetRequiredService<WebPagePublicationService>()
                : UnavailableWebPagePublicationDirectory.Instance);
        services.AddScoped<IWebPageIdentityMigration>(provider =>
            HasPublicationDependencies(provider)
                ? provider.GetRequiredService<WebPagePublicationService>()
                : UnavailableWebPagePublicationDirectory.Instance);
        services.AddScoped<WebPublicationDiscovery>();
        services.AddScoped<WebPageAdministration>();
        services.AddScoped<IWebPublicationDiscovery>(provider =>
            HasPublicationDiscoveryDependencies(provider)
                ? provider.GetRequiredService<WebPublicationDiscovery>()
                : UnavailableWebPagePublicationDirectory.Instance);
        services.AddScoped<DynamicDataReader>();
        services.AddScoped<SqliteWebChangeFeed>();
        services.TryAddScoped<IWebChangeScopeAuthorizer, UnavailableWebChangeScopeAuthorizer>();
        services.AddScoped<CommittedEffectHistory>();
        services.AddScoped<ControlStructureExplorer>();
        services.TryAddSingleton<IWebReadableRulesAudienceProvider,
            PublicWebReadableRulesAudienceProvider>();
        services.TryAddSingleton<IHostSettingDefinitionProvider>(
            _ => UnavailableHostSettingDefinitionProvider.Instance);
        services.AddScoped<ControlSettingsExplorer>();
        services.TryAddScoped<IAssistantConversationService, UnavailableAssistantConversationService>();
        services.TryAddScoped<ICodexConversationService, UnavailableCodexConversationService>();
        services.AddScoped<ControlAssistantExplorer>();
        services.TryAddScoped<ISystemConversationService, UnavailableSystemConversationService>();
        services.AddScoped<ControlSystemConversationExplorer>();
        services.TryAddScoped<ISystemTaskService, UnavailableSystemTaskService>();
        services.AddScoped<ControlSystemTaskExplorer>();
        services.AddScoped<ControlSystemCapabilityExplorer>();
        services.TryAddSingleton<IAiAgentProfileRegistry>(_ => new AiAgentProfileRegistry([
            new(
                "web.outer",
                "Outer AI",
                "You are the user-facing planning and conversation agent for DantesRoleplay.",
                "Explain intent and outcomes clearly. Use direct registered tools when system evidence is needed."),
            new(
                "web.inner",
                "Inner AI",
                "You are the direct system-work agent for DantesRoleplay.",
                "Complete bounded work through registered direct tools and report exact validation, confirmation, and task state.")
        ]));
        services.AddScoped<IWebAiGateway>(provider => new WebAiGateway(
            provider.GetService<IAiService>(),
            provider.GetService<ISystemAiAgentService>(),
            provider.GetService<IAiAgentProfileRegistry>(),
            provider.GetService<IAssistantConversationStore>(),
            provider.GetService<IApplicationRegistry>(),
            provider.GetService<IApplicationActivationReader>(),
            provider.GetService<IStateSpaceRegistry>()));
        services.AddSingleton<ApplicationConversationStore>();
        services.AddScoped<ApplicationConversationService>();
        services.AddScoped<ApplicationMechanicWebService>();
        services.TryAddScoped<ITriggerSchedulingAdministrationService,
            UnavailableTriggerSchedulingAdministrationService>();
        services.Configure<WebRemoteAccessOptions>(
            configuration.GetSection(WebRemoteAccessOptions.SectionName));
        services.AddSingleton<WebAccessPolicy>();
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        services.AddSingleton<WebPrivateOperatorGuard>();
        services.AddSingleton<WebInterfaceSecurityFilter>();
        services.AddSingleton<WebControlRequestGuard>();
        services.AddSingleton<WebControlRequestFilter>();
        services.AddScoped<WebObservationRequestGuard>();
        services.AddScoped<WebObservationRequestFilter>();
        services.AddSingleton<ObservationHttpRequestReader>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                WebInterfaceSecurity.ApplyHeaders(context.HttpContext.Response);
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Max(1,
                        (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "WEB_RATE_LIMITED",
                        message = "The local web request limit was reached. Try again shortly."
                    },
                    cancellationToken);
            };
            options.AddPolicy(
                WebInterfaceSecurity.ReadRateLimitPolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    // Keep API polling and catalog traversal from locking users out of the UI.
                    context.Request.Path.StartsWithSegments("/api") &&
                    !(context.Request.Path.Value!.Contains("/media/", StringComparison.Ordinal) &&
                      context.Request.Path.Value.EndsWith("/content", StringComparison.Ordinal))
                        ? "api" : "content",
                    _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = WebInterfaceSecurity.ReadRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                }));
            options.AddFixedWindowLimiter(
                WebInterfaceSecurity.UploadRateLimitPolicy,
                limiter =>
                {
                    limiter.PermitLimit = WebInterfaceSecurity.UploadRequestsPerMinute;
                    limiter.Window = TimeSpan.FromMinutes(1);
                    limiter.QueueLimit = 0;
                    limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    limiter.AutoReplenishment = true;
                });
            options.AddConcurrencyLimiter(
                WebInterfaceSecurity.StreamRateLimitPolicy,
                limiter =>
                {
                    limiter.PermitLimit = WebInterfaceSecurity.ConcurrentStreams;
                    limiter.QueueLimit = 0;
                    limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                });
        });

        return services;
    }

    public static async Task InitialiseDantesRoleplayWebAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebContentDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IWebPageIdentityMigration>()
            .InspectAsync(cancellationToken);
    }

    private static bool HasPublicationDependencies(IServiceProvider provider) =>
        provider.GetService<IApplicationRegistry>() is not null
        && provider.GetService<IStateSpaceRegistry>() is not null
        && provider.GetService<IApplicationComponentTypeRegistry>() is not null
        && provider.GetService<IEntityComponentStore>() is not null
        && provider.GetService<IWorldStore>() is not null;

    private static bool HasPublicationDiscoveryDependencies(IServiceProvider provider) =>
        provider.GetService<IApplicationRegistry>() is not null
        && provider.GetService<IStateSpaceRegistry>() is not null
        && provider.GetService<IEntityComponentStore>() is not null;

    private static string NormaliseSqlite(string connectionStringOrPath)
    {
        if (connectionStringOrPath.Contains('=', StringComparison.Ordinal))
        {
            return connectionStringOrPath;
        }

        var fullPath = Path.GetFullPath(connectionStringOrPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={fullPath}";
    }
}

internal sealed class UnavailableWebPagePublicationDirectory
    : IWebPagePublicationDirectory, IWebPageIdentityMigration, IWebPublicationDiscovery
{
    public static UnavailableWebPagePublicationDirectory Instance { get; } = new();

    public Task<PublishedWebPage?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult<PublishedWebPage?>(null);

    public Task<PublishedWebPage?> FindIndexAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default) => Task.FromResult<PublishedWebPage?>(null);

    public Task<WebPageIdentityMigrationReport> InspectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebPageIdentityMigrationReport(0, 0, 0, 0, []));

    public Task<WebPageIdentityMigrationReport> ApplyReviewedAsync(
        WebPageIdentityMigrationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebPageIdentityMigrationReport(0, 0, 0, 0, []));

    public Task<WebPageIdentityMigrationReport?> GetLastReportAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<WebPageIdentityMigrationReport?>(null);

    public Task<WebApplicationPublicationPage> ListApplicationsAsync(
        string? cursor, int limit, bool diagnostics = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebApplicationPublicationPage([], [], null));

    public Task<WebApplicationPublicationView?> GetApplicationAsync(
        ApplicationIdentifier applicationId, bool diagnostics = false,
        CancellationToken cancellationToken = default) => Task.FromResult<WebApplicationPublicationView?>(null);

    public Task<WebPublishedPageView?> GetPageAsync(
        ApplicationIdentifier applicationId, string slug, bool diagnostics = false,
        CancellationToken cancellationToken = default) => Task.FromResult<WebPublishedPageView?>(null);

    public Task<WebPageRouteResolution> ResolvePageRouteAsync(
        string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebPageRouteResolution("application-unavailable"));
}

internal sealed class UnavailableTriggerSchedulingAdministrationService
    : ITriggerSchedulingAdministrationService
{
    public Task<TriggerSchedulingAdministrationView> QueryAsync(TriggerSchedulingAdministrationQuery query,
        CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<TriggerSchedulingAdministrationResult> PreviewAsync(
        TriggerSchedulingAdministrationCommand command, TriggerSchedulingAdministrationContext context,
        CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<TriggerSchedulingAdministrationResult> CommitAsync(
        TriggerSchedulingAdministrationCommand command, TriggerSchedulingAdministrationContext context,
        CancellationToken cancellationToken = default) => throw Unavailable();
    private static TriggerSchedulingAdministrationException Unavailable() => new(
        "TRIGGER_ADMIN_UNAVAILABLE", "Trigger scheduling administration is not configured.");
}
