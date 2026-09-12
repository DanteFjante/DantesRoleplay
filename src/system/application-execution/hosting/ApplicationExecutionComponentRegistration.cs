using Microsoft.Extensions.DependencyInjection;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.TriggerScheduling;

namespace DantesRoleplay.ApplicationExecution;

public static class ApplicationExecutionComponentRegistration
{
    public static IServiceCollection AddApplicationExecutionComponent(this IServiceCollection services) => services
        .AddScoped<ApplicationCandidatePureMechanicClassifier>()
        .AddScoped<ApplicationCandidatePureRuntimeClosureReader>()
        .AddScoped<ApplicationCandidateRuntimeValidator>()
        .AddScoped<ApplicationCandidateStatefulRuntimeValidator>()
        .AddScoped<IApplicationCandidatePreparation>(provider =>
            provider.GetRequiredService<ApplicationCandidateRuntimeValidator>())
        .AddScoped<IApplicationMechanicProjectionResolver, ApplicationMechanicProjectionResolver>()
        .AddScoped<IApplicationGraphSnapshotReader, ApplicationGraphSnapshotReader>()
        .AddScoped<IApplicationMechanicObjectProjectionResolver, ApplicationMechanicObjectProjectionResolver>()
        .AddScoped<IApplicationMechanicProjectionMappingResolver, ApplicationMechanicProjectionMappingResolver>()
        .AddScoped<ApplicationMechanicEvaluator>()
        .AddScoped<IApplicationMechanicEvaluator>(provider =>
            provider.GetRequiredService<ApplicationMechanicEvaluator>())
        .AddScoped<CatalogJavaScriptObserverPredicateAdapter>()
        .AddScoped<IApplicationObserverPredicateInputCapture>(provider =>
            provider.GetRequiredService<CatalogJavaScriptObserverPredicateAdapter>())
        .AddScoped<IApplicationObserverPredicateEvaluator>(provider =>
            provider.GetRequiredService<CatalogJavaScriptObserverPredicateAdapter>())
        .AddScoped<IApplicationEcsEffectBatchBuilder, ApplicationEcsEffectBatchBuilder>()
        .AddScoped<IApplicationPureActionExecutor, ApplicationPureActionExecutor>()
        .AddScoped<ApplicationActionInvocationAdapter>()
        .AddScoped<IApplicationActionInvocationAdapter>(provider =>
            provider.GetRequiredService<ApplicationActionInvocationAdapter>())
        .AddScoped<IStandingGrantApplicationActionInvocationAdapter>(provider =>
            provider.GetRequiredService<ApplicationActionInvocationAdapter>())
        .AddScoped<SystemInnerWorkerApplicationToolFactory>()
        .AddScoped<IApplicationReadOnlyServiceDefinitionReader, ApplicationReadOnlyServiceDefinitionReader>()
        .AddScoped<ApplicationReadOnlyServiceInvocationAdapter>()
        .AddScoped<IApplicationReadOnlyServiceInvocationAdapter>(provider =>
            provider.GetRequiredService<ApplicationReadOnlyServiceInvocationAdapter>())
        .AddScoped<IApplicationWorkflowServiceInvocationAdapter>(provider =>
            provider.GetRequiredService<ApplicationReadOnlyServiceInvocationAdapter>())
        // Keep the concrete runner available to the generic event adapter so both paths reuse its
        // exact typed-effect translation and expectation builder.
        .AddScoped<ApplicationActionRunner>()
        .AddScoped<IApplicationActionRunner>(provider => provider.GetRequiredService<ApplicationActionRunner>());
}
