using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Projections;

internal static class ProjectionMaterializationComponentRegistration
{
    internal static IServiceCollection AddProjectionMaterializationComponent(this IServiceCollection services)
    {
        services.AddSingleton<ProjectionPlanCache>();
        services.AddSingleton<IProjectionPlanCacheDiagnostics>(provider =>
            provider.GetRequiredService<ProjectionPlanCache>());
        services.AddScoped<IProjectionDefinitionRegistry, SqliteProjectionDefinitionRegistry>();
        services.AddScoped<IProjectionReadTransaction, SqliteProjectionReadTransaction>();
        services.AddScoped<IProjectionSourceSnapshotReader, SqliteProjectionSourceSnapshotReader>();
        services.AddScoped<IProjectionMaterializer, ProjectionMaterializer>();
        services.AddScoped<IProjectionCollectionMaterializer, ProjectionCollectionMaterializer>();
        services.AddScoped<IProjectionImpactSnapshotReader, SqliteProjectionImpactSnapshotReader>();
        services.AddScoped<IProjectionImpactService, ProjectionImpactService>();
        return services;
    }
}
