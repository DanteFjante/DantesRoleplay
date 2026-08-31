using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.EcsEffects;

internal static class EcsEffectsComponentRegistration
{
    internal static IServiceCollection AddEcsEffectsComponent(this IServiceCollection services)
    {
        services.AddScoped<IApplicationEcsEffectApplier, ApplicationEcsEffectApplier>();
        services.AddScoped<IApplicationWorldAuthoringSynchronizer, ApplicationWorldAuthoringSynchronizer>();
        return services;
    }
}
