using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Ecs;

internal static class EcsComponentRegistration
{
    internal static IServiceCollection AddEcsComponent(this IServiceCollection services)
    {
        services.AddScoped<IApplicationComponentTypeRegistry, SqliteComponentTypeRegistry>();
        services.AddScoped<IStateSpaceRegistry, SqliteStateSpaceRegistry>();
        services.AddScoped<SqliteEntityComponentStore>();
        services.AddScoped<IEntityComponentStore>(provider => provider.GetRequiredService<SqliteEntityComponentStore>());
        services.AddScoped<IEntityComponentSearchStore>(provider => provider.GetRequiredService<SqliteEntityComponentStore>());
        services.AddScoped<IEntityBatchReadStore>(provider => provider.GetRequiredService<SqliteEntityComponentStore>());
        services.AddScoped<IEcsLifecycleStore, SqliteEcsLifecycleStore>();
        services.AddScoped<IEcsWriteTransactionFactory, SqliteEcsWriteTransactionFactory>();
        services.AddScoped<IEcsRoleConstraintValidator, SqliteEcsRoleConstraintValidator>();
        return services;
    }
}
