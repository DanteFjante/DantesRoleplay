using DantesRoleplay.Applications;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class ApplicationRegistryComponentRegistration
{
    internal static IServiceCollection AddApplicationRegistryComponent(this IServiceCollection services)
    {
        services.AddScoped<IApplicationRegistry, SqliteApplicationRegistry>();
        return services;
    }
}
