using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.ApplicationActivation;

internal static class ApplicationActivationComponentRegistration
{
    internal static IServiceCollection AddApplicationActivationComponent(this IServiceCollection services)
    {
        services.AddScoped<ApplicationActivationService>();
        services.AddScoped<IApplicationActivationService>(provider =>
            provider.GetRequiredService<ApplicationActivationService>());
        services.AddScoped<IApplicationActivationReader>(provider =>
            provider.GetRequiredService<ApplicationActivationService>());
        services.AddScoped<IActivatedApplicationEvidenceReader>(provider =>
            provider.GetRequiredService<ApplicationActivationService>());
        services.AddScoped<IApplicationDefinitionChangeReader>(provider =>
            provider.GetRequiredService<ApplicationActivationService>());
        services.AddScoped<IActivatedApplicationDocumentReader, ActivatedApplicationDocumentReader>();
        services.AddScoped<ApplicationCandidateReviewedPureUpdateReader>();
        services.AddScoped<ApplicationCandidateReviewedPureUpdateValidation>();
        services.AddScoped<ApplicationCandidateStatefulReviewClosureReader>();
        services.AddScoped<IApplicationCandidateReviewClosureReader>(provider =>
            provider.GetRequiredService<ApplicationCandidateStatefulReviewClosureReader>());
        services.AddScoped<ApplicationCandidateReviewedStatefulUpdateReader>();
        services.AddScoped<ApplicationCandidateReviewedClosureReceiptReader>();
        services.AddScoped<IApplicationCandidateReviewClosureReader, ApplicationCandidateProcedureClosureReader>();
        services.AddScoped<ApplicationCandidateReviewedProcedureUpdateReader>();
        services.AddScoped<ApplicationCandidateReviewedProcedureUpdateValidation>();
        services.AddScoped<ApplicationCandidateWorkflowReviewClosureReader>();
        services.AddScoped<IApplicationCandidateReviewClosureReader>(provider =>
            provider.GetRequiredService<ApplicationCandidateWorkflowReviewClosureReader>());
        services.AddScoped<SqliteApplicationAuthoringService>(provider => new(
            provider.GetRequiredService<DataAccess.DantesRoleplayDbContext>(),
            provider.GetRequiredService<Applications.IApplicationRegistry>(),
            provider.GetRequiredService<IApplicationActivationReader>(),
            provider.GetRequiredService<IActivatedApplicationEvidenceReader>(),
            provider.GetRequiredService<Sources.ISourceRegistry>(),
            provider.GetRequiredService<Authorization.IStandingGrantPolicy>(),
            provider.GetRequiredService<Authorization.IStandingGrantTargetResolver>(),
            provider.GetRequiredService<Operations.IOperationLog>(),
            provider.GetService<IApplicationCandidatePreparation>(),
            provider.GetService<Interactions.IInteractionManualContextService>(),
            reviewedPureUpdates: provider.GetRequiredService<ApplicationCandidateReviewedPureUpdateReader>(),
            synchronization: provider.GetRequiredService<DataAccess.Catalog.IApplicationCatalogSynchronizationEvidenceReader>(),
            statefulRuntime: provider.GetService<ApplicationExecution.ApplicationCandidateStatefulRuntimeValidator>(),
            statefulReviewClosures: provider.GetRequiredService<ApplicationCandidateStatefulReviewClosureReader>(),
            reviewedStatefulUpdates: provider.GetRequiredService<ApplicationCandidateReviewedStatefulUpdateReader>(),
            reviewedProcedureUpdates: provider.GetRequiredService<ApplicationCandidateReviewedProcedureUpdateReader>()));
        services.AddScoped<IApplicationAuthoringService>(provider =>
            provider.GetRequiredService<SqliteApplicationAuthoringService>());
        return services;
    }
}
