using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Ecs;

public static class StateSpaceEdgesComponentRegistration
{
    public static IServiceCollection AddStateSpaceEdgesComponent(this IServiceCollection services) =>
        services.AddScoped<IStateSpaceEdgeStore, SqliteStateSpaceEdgeStore>();
}
