using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.ApplicationExecution;

public static class ApplicationExecutionComponentRegistration
{
    public static IServiceCollection AddApplicationExecutionComponent(this IServiceCollection services) => services
        .AddScoped<IApplicationMechanicProjectionResolver, ApplicationMechanicProjectionResolver>()
        .AddScoped<IApplicationMechanicEvaluator, ApplicationMechanicEvaluator>()
        .AddScoped<IApplicationActionRunner, ApplicationActionRunner>();
}
