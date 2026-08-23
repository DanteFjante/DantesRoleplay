using DantesRoleplay.DataAccess.Bootstrap;
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
        return services;
    }
}
