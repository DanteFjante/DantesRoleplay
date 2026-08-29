using DantesRoleplay.RegistryAdministration;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class RegistryAdministrationComponentRegistration
{
    internal static IServiceCollection AddRegistryAdministrationComponent(this IServiceCollection services)
    {
        services.AddScoped<IRegistryAdministrationService, RegistryAdministrationService>();
        return services;
    }
}
