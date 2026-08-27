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
        services.AddSingleton<WebPageBundleReader>();
        services.AddSingleton<WebHtmlReader>();
        services.AddScoped<DynamicDataReader>();
        services.AddScoped<SqliteWebChangeFeed>();
        services.AddScoped<CommittedEffectHistory>();
        services.AddScoped<ControlStructureExplorer>();
        services.AddScoped<ControlPageEditor>();
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
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "WEB_RATE_LIMITED",
                        message = "The local web request limit was reached. Try again shortly."
                    },
                    cancellationToken);
            };
            options.AddFixedWindowLimiter(
                WebInterfaceSecurity.ReadRateLimitPolicy,
                limiter =>
                {
                    limiter.PermitLimit = 240;
                    limiter.Window = TimeSpan.FromMinutes(1);
                    limiter.QueueLimit = 0;
                    limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    limiter.AutoReplenishment = true;
                });
            options.AddFixedWindowLimiter(
                WebInterfaceSecurity.UploadRateLimitPolicy,
                limiter =>
                {
                    limiter.PermitLimit = 10;
                    limiter.Window = TimeSpan.FromMinutes(1);
                    limiter.QueueLimit = 0;
                    limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    limiter.AutoReplenishment = true;
                });
            options.AddConcurrencyLimiter(
                WebInterfaceSecurity.StreamRateLimitPolicy,
                limiter =>
                {
                    limiter.PermitLimit = 4;
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
    }

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
