using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.StateSpaceAdministration;

internal static class StateSpaceAdministrationComponentRegistration
{
    internal static IServiceCollection AddStateSpaceAdministrationComponent(this IServiceCollection services)
    {
        services.AddScoped<StateSpaceAdministrationService>();
        services.AddScoped<IStateSpaceAdministrationService>(provider =>
            provider.GetRequiredService<StateSpaceAdministrationService>());
        services.AddScoped<IStateSpaceAdministrationReader>(provider =>
            provider.GetRequiredService<StateSpaceAdministrationService>());
        return services;
    }
}
