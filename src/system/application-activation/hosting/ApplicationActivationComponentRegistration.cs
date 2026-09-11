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
            provider.GetRequiredService<ApplicationCandidateReviewedPureUpdateReader>()));
        services.AddScoped<IApplicationAuthoringService>(provider =>
            provider.GetRequiredService<SqliteApplicationAuthoringService>());
        return services;
    }
}
