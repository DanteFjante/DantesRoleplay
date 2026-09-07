using System.Security.Cryptography;
using DantesRoleplay.CatalogNavigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.DataAccess.Composition;

internal static class CatalogNavigationComponentRegistration
{
    internal static IServiceCollection AddCatalogNavigationComponent(this IServiceCollection services)
    {
        services.TryAddSingleton<IPublicApplicationCatalogPolicy, EmptyPublicApplicationCatalogPolicy>();
        services.TryAddSingleton(new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)));
        services.TryAddSingleton<ActivatedApplicationCatalogSnapshotCache>();
        services.TryAddSingleton<ActivatedApplicationCatalogCacheAuthority>();
        services.AddScoped(provider => ActivatorUtilities
            .CreateInstance<ActivatedApplicationCatalogMaterializer>(provider)
            .UsePreparationCache(
                provider.GetRequiredService<ActivatedApplicationCatalogSnapshotCache>(),
                provider.GetRequiredService<ActivatedApplicationCatalogCacheAuthority>()));
        services.AddScoped<ActivatedApplicationCatalogProvider>();
        services.AddScoped<IPublicApplicationCatalogProvider>(provider =>
            provider.GetRequiredService<ActivatedApplicationCatalogProvider>());
        services.AddScoped<IActiveCatalogFeatureSnapshotProvider>(provider =>
            provider.GetRequiredService<ActivatedApplicationCatalogProvider>());
        return services;
    }
}
