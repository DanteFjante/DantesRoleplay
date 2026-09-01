using DantesRoleplay.Sources;
using DantesRoleplay.LocalAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.DataAccess.Composition;

internal static class SourceRegistryComponentRegistration
{
    internal static IServiceCollection AddSourceRegistryComponent(this IServiceCollection services)
    {
        services.AddScoped<ISourceRegistry, SqliteSourceRegistry>();
        services.AddScoped<IApplicationExtensionRegistry, SqliteApplicationExtensionRegistry>();
        services.AddScoped<ISourceScanReceiptStore, SqliteSourceScanReceiptStore>();
        services.TryAddSingleton<IAllowedSourceRootResolver, EmptyAllowedSourceRootResolver>();
        services.TryAddSingleton<IAllowedSourceRootCatalog>(provider =>
            provider.GetRequiredService<IAllowedSourceRootResolver>() as IAllowedSourceRootCatalog
            ?? new EmptyAllowedSourceRootResolver());
        services.TryAddSingleton<ILocalDocumentScanner, LocalDocumentScanner>();
        services.TryAddSingleton<ISourceOverlayResolver, SourceOverlayResolver>();
        services.AddScoped<IRegisteredSourceScanner, RegisteredSourceScanner>();
        return services;
    }
}
