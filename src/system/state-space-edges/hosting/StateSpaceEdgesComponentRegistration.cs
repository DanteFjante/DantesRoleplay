using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Ecs;

public static class StateSpaceEdgesComponentRegistration
{
    public static IServiceCollection AddStateSpaceEdgesComponent(this IServiceCollection services)
    {
        services.AddScoped<SqliteStateSpaceEdgeStore>();
        services.AddScoped<IStateSpaceEdgeStore>(provider => provider.GetRequiredService<SqliteStateSpaceEdgeStore>());
        services.AddScoped<IRelationshipCollectionReader>(provider => provider.GetRequiredService<SqliteStateSpaceEdgeStore>());
        return services;
    }
}
