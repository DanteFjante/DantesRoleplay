using DantesRoleplay.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.SystemTasks;

internal static class SystemTaskOrchestrationComponentRegistration
{
    internal static IServiceCollection AddSystemTaskOrchestrationComponent(this IServiceCollection services)
    {
        services.AddScoped<ISystemTaskContextMaterializer, SystemTaskContextMaterializer>();
        services.AddScoped<ISystemTaskService, SystemTaskService>();
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        return services;
    }
}
