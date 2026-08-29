using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Projections;

internal static class ProjectionMaterializationComponentRegistration
{
    internal static IServiceCollection AddProjectionMaterializationComponent(this IServiceCollection services)
    {
        services.AddScoped<IProjectionDefinitionRegistry, SqliteProjectionDefinitionRegistry>();
        services.AddScoped<IProjectionMaterializer, ProjectionMaterializer>();
        services.AddScoped<IProjectionImpactSnapshotReader, SqliteProjectionImpactSnapshotReader>();
        services.AddScoped<IProjectionImpactService, ProjectionImpactService>();
        return services;
    }
}
