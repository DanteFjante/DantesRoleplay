using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Ecs;

internal static class EcsComponentRegistration
{
    internal static IServiceCollection AddEcsComponent(this IServiceCollection services)
    {
        services.AddScoped<IApplicationComponentTypeRegistry, SqliteComponentTypeRegistry>();
        services.AddScoped<IStateSpaceRegistry, SqliteStateSpaceRegistry>();
        services.AddScoped<IEntityComponentStore, SqliteEntityComponentStore>();
        return services;
    }
}
