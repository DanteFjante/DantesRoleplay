using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.ApplicationExecution;

public static class ApplicationExecutionComponentRegistration
{
    public static IServiceCollection AddApplicationExecutionComponent(this IServiceCollection services) => services
        .AddScoped<IApplicationMechanicProjectionResolver, ApplicationMechanicProjectionResolver>()
        .AddScoped<IApplicationGraphSnapshotReader, ApplicationGraphSnapshotReader>()
        .AddScoped<IApplicationMechanicObjectProjectionResolver, ApplicationMechanicObjectProjectionResolver>()
        .AddScoped<IApplicationMechanicProjectionMappingResolver, ApplicationMechanicProjectionMappingResolver>()
        .AddScoped<IApplicationMechanicEvaluator, ApplicationMechanicEvaluator>()
        .AddScoped<IApplicationEcsEffectBatchBuilder, ApplicationEcsEffectBatchBuilder>()
        // Keep the concrete runner available to the generic event adapter so both paths reuse its
        // exact typed-effect translation and expectation builder.
        .AddScoped<ApplicationActionRunner>()
        .AddScoped<IApplicationActionRunner>(provider => provider.GetRequiredService<ApplicationActionRunner>());
}
