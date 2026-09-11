using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DantesRoleplay.DataAccess.Composition;

namespace DantesRoleplay.SystemTasks;

internal static class SystemTaskOrchestrationComponentRegistration
{
    internal static IServiceCollection AddSystemTaskOrchestrationComponent(this IServiceCollection services)
    {
        services.AddScoped<ISystemTaskContextMaterializer, SystemTaskContextMaterializer>();
        services.AddScoped<ISystemTaskService, SystemTaskService>();
        services.AddScoped<SqliteSystemTaskDurableService>();
        services.Replace(ServiceDescriptor.Scoped<ISystemTaskDurableService>(provider =>
            provider.GetRequiredService<SqliteSystemTaskDurableService>()));
        services.AddScoped<ISystemTaskDurableReadbackService>(provider =>
            provider.GetRequiredService<SqliteSystemTaskDurableService>());
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
        services.AddScoped<SystemInnerWorkerPreparation>();
        services.AddScoped<SystemInnerWorkerHostPolicy>();
        services.AddScoped<SystemInnerWorkerProcedureResolver>();
        services.AddScoped<ISystemInnerWorkerProcedureResolver>(provider =>
            provider.GetRequiredService<SystemInnerWorkerProcedureResolver>());
        services.AddScoped<SystemInnerWorkerProcedureInvoker>();
        services.AddScoped<SystemInnerWorkerProcedureExecutor>();
        services.AddScoped<SystemInnerWorkerService>();
        services.AddScoped<ISelectedApplicationInnerWorkerOwner>(provider =>
            new SelectedApplicationInnerWorkerOwner(
                provider.GetRequiredService<DataAccess.DantesRoleplayDbContext>(),
                provider.GetRequiredService<DantesRoleplay.Ecs.IStateSpaceRegistry>(),
                () => provider.GetRequiredService<SystemInnerWorkerService>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ISystemReadCapabilityHandler, SelectedApplicationInnerWorkerReadCapabilityHandler>();
        services.AddScoped<ISystemWriteCapabilityHandler>(provider =>
            new SelectedApplicationInnerWorkerWriteCapabilityHandler(
                SystemCapabilityIds.InnerWorkerSubmit,
                provider.GetRequiredService<ISelectedApplicationInnerWorkerOwner>()));
        services.AddScoped<ISystemWriteCapabilityHandler>(provider =>
            new SelectedApplicationInnerWorkerWriteCapabilityHandler(
                SystemCapabilityIds.InnerWorkerCancel,
                provider.GetRequiredService<ISelectedApplicationInnerWorkerOwner>()));
        services.Replace(ServiceDescriptor.Scoped<ISystemInnerWorkerService>(provider =>
            provider.GetRequiredService<SystemInnerWorkerService>()));
        services.AddHostedService<SystemTaskWorkflowBackgroundWorker>();
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        return services;
    }
}
