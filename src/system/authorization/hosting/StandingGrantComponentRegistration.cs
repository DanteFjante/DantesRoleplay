using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.Authorization;

internal static class StandingGrantComponentRegistration
{
    internal static IServiceCollection AddStandingGrantComponent(this IServiceCollection services)
    {
        services.TryAddScoped<SqliteStandingGrantTargetResolver>();
        services.TryAddScoped<IStandingGrantTargetResolver>(provider => new ResourceStandingGrantTargetResolver(
            provider.GetRequiredService<SqliteStandingGrantTargetResolver>(),
            provider.GetServices<IStandingGrantResourceTargetOwner>()));
        services.TryAddScoped<IStandingGrantPolicy, SqliteStandingGrantPolicy>();
        return services;
    }
}
