using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class CatalogComponentRegistration
{
    internal static IServiceCollection AddCatalogComponent(this IServiceCollection services)
    {
        services.AddScoped<ProcedureSeeder>();
        services.AddScoped<EventTypeSeeder>();
        services.AddScoped<MechanicSeeder>();
        services.AddScoped<ContentHashBackfill>();
        services.AddScoped<CatalogExporter>();
        services.AddScoped<CatalogImporter>();
        services.AddScoped<CatalogSynchronizationService>();
        services.AddScoped<ICatalogSynchronizationService>(provider =>
            provider.GetRequiredService<CatalogSynchronizationService>());
        services.AddScoped<IApplicationCatalogSynchronizationEvidenceReader>(provider =>
            provider.GetRequiredService<CatalogSynchronizationService>());
        services.AddScoped<SqliteCatalogNamespaceRegistry>();
        services.AddScoped<ICatalogNamespaceRegistry>(provider =>
            provider.GetRequiredService<SqliteCatalogNamespaceRegistry>());
        services.AddScoped<ICatalogNamespaceOverlayRegistry>(provider =>
            provider.GetRequiredService<SqliteCatalogNamespaceRegistry>());
        return services;
    }
}
