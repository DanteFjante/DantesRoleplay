using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.ApplicationActivation;

internal static class ApplicationActivationComponentRegistration
{
    internal static IServiceCollection AddApplicationActivationComponent(this IServiceCollection services)
    {
        services.AddScoped<ApplicationActivationService>();
        services.AddScoped<IApplicationActivationService>(provider =>
            provider.GetRequiredService<ApplicationActivationService>());
        services.AddScoped<IApplicationActivationReader>(provider =>
            provider.GetRequiredService<ApplicationActivationService>());
        return services;
    }
}
