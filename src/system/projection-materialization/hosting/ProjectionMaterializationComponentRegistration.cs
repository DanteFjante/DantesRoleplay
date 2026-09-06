using Microsoft.Extensions.DependencyInjection;
using DantesRoleplay.EcsEffects;

namespace DantesRoleplay.Projections;

internal static class ProjectionMaterializationComponentRegistration
{
    internal static IServiceCollection AddProjectionMaterializationComponent(this IServiceCollection services)
    {
        services.AddSingleton<ProjectionPlanCache>();
        services.AddSingleton<IProjectionPlanCacheDiagnostics>(provider =>
            provider.GetRequiredService<ProjectionPlanCache>());
        services.AddScoped<IProjectionDefinitionRegistry, SqliteProjectionDefinitionRegistry>();
        services.AddScoped<SqliteProjectionReadTransaction>();
        services.AddScoped<IProjectionReadTransaction>(provider =>
            provider.GetRequiredService<SqliteProjectionReadTransaction>());
        services.AddScoped<IProjectionReadSnapshotStatus>(provider =>
            provider.GetRequiredService<SqliteProjectionReadTransaction>());
        services.AddScoped<IProjectionSourceSnapshotReader, SqliteProjectionSourceSnapshotReader>();
        services.AddScoped<IProjectionMaterializer, ProjectionMaterializer>();
        services.AddScoped<IProjectionCollectionMaterializer, ProjectionCollectionMaterializer>();
        services.AddScoped<IApplicationObjectWriteService, ApplicationObjectWriteService>();
        services.AddScoped<IProjectionImpactSnapshotReader, SqliteProjectionImpactSnapshotReader>();
        services.AddScoped<IProjectionImpactService, ProjectionImpactService>();
        services.AddScoped<IApplicationEcsTransactionParticipant,
            ApplicationObjectChangeTransactionParticipant>();
        return services;
    }
}
