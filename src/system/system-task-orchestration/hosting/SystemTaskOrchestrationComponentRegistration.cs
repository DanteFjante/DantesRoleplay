using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.SystemTasks;

internal static class SystemTaskOrchestrationComponentRegistration
{
    internal static IServiceCollection AddSystemTaskOrchestrationComponent(this IServiceCollection services)
    {
        services.AddScoped<ISystemTaskContextMaterializer, SystemTaskContextMaterializer>();
        services.AddScoped<ISystemTaskService, SystemTaskService>();
        services.TryAddScoped<ISystemTaskDurableService, UnavailableSystemTaskDurableService>();
        services.AddScoped<ISystemAiToolSource, SystemTaskAiToolSource>();
        services.AddScoped<SystemTaskApplicationValidationGate>(provider => new(
            provider.GetRequiredService<DataAccess.DantesRoleplayDbContext>(),
            provider.GetRequiredService<IApplicationRegistry>(),
            provider.GetRequiredService<IApplicationActivationReader>(),
            provider.GetRequiredService<IStandingGrantTargetResolver>(),
            provider.GetRequiredService<IStandingGrantPolicy>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ApplicationCandidatePureRuntimeClosureReader>(),
            provider.GetRequiredService<IInteractionManualContextService>(),
            provider.GetRequiredService<IInteractionFeatureRetriever>()));
        services.AddScoped<SystemTaskApplicationValidationService>();
        services.AddSingleton<SystemTaskAiInvocationLifecycleFactory>();
        services.AddScoped<SystemInnerWorkerValidationInvoker>(provider => new(
            provider.GetService<DantesRoleplay.AI.IAiService>(),
            provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        return services;
    }
}
