using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.Authorization;

internal static class StandingGrantComponentRegistration
{
    internal static IServiceCollection AddStandingGrantComponent(this IServiceCollection services)
    {
        services.TryAddScoped<IStandingGrantTargetResolver, SqliteStandingGrantTargetResolver>();
        services.TryAddScoped<IStandingGrantPolicy, SqliteStandingGrantPolicy>();
        return services;
    }
}
