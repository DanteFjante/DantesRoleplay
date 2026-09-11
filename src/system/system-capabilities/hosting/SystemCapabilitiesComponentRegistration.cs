using DantesRoleplay.Authorization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.Ecs;
using DantesRoleplay.LegacyStateAdoption;
using DantesRoleplay.Projections;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.SystemCapabilities;

internal static class SystemCapabilitiesComponentRegistration
{
    internal static IServiceCollection AddSystemCapabilitiesComponent(this IServiceCollection services)
    {
        services.AddScoped<ISystemReadCapabilityHandler, ApplicationsSystemCapabilityHandler>();
        services.AddScoped<ISystemReadCapabilityHandler, SourcesSystemCapabilityHandler>();
        services.AddScoped<ISystemReadCapabilityHandler, ApplicationPreviewSystemCapabilityHandler>();
        services.AddScoped<ISystemReadCapabilityHandler, DependenciesSystemCapabilityHandler>();
        services.AddScoped<ISystemReadCapabilityHandler, ApplicationCandidateInspectCapabilityHandler>();
        foreach (var id in new[]
        {
            SystemCapabilityIds.ApplicationRegister,
            SystemCapabilityIds.SourceRegister,
            SystemCapabilityIds.ExtensionRegister,
            SystemCapabilityIds.ComponentTypeRegister,
            SystemCapabilityIds.ApplicationActivate,
            SystemCapabilityIds.ApplicationCandidateWrite,
            SystemCapabilityIds.ApplicationCandidateIntentUpdate,
            SystemCapabilityIds.ApplicationCandidateValidate,
            SystemCapabilityIds.ApplicationCandidateActivate,
            SystemCapabilityIds.ApplicationCandidateRecover,
            SystemCapabilityIds.StandingGrantAdmin,
            SystemCapabilityIds.StateSpaceCreate,
            SystemCapabilityIds.StateSpaceUpgrade,
            SystemCapabilityIds.StateSpaceAdoptLegacy
        })
        {
            var capabilityId = id;
            services.AddScoped<ISystemWriteCapabilityHandler>(provider => Write(provider, capabilityId));
        }
        services.AddScoped<ISystemCapabilityCatalog, SystemCapabilityCatalog>();
        services.AddScoped<IntentMatchAssociationService>();
        services.AddScoped<IApplicationCandidateCapabilityGateway, ApplicationCandidateCapabilityGateway>();
        services.AddScoped<ISystemAiToolSource, SystemCapabilityAiToolSource>();
        services.AddScoped<ISystemAiToolSource, ApplicationCandidateCapabilityAiToolSource>();
        services.AddScoped<ISystemAiToolSource, EcsLifecycleAiToolSource>();
        services.AddScoped<ISystemAiAgentService, SystemAiAgentService>();
        services.TryAddScoped<ISystemInnerWorkerService, UnavailableSystemInnerWorkerService>();
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        return services;
    }

    private static ISystemWriteCapabilityHandler Write(
        IServiceProvider provider,
        string id) => id == SystemCapabilityIds.ApplicationCandidateIntentUpdate
        ? new ApplicationCandidateIntentUpdateCapabilityHandler(
            provider.GetRequiredService<DantesRoleplay.DataAccess.DantesRoleplayDbContext>(),
            provider.GetRequiredService<IApplicationRegistry>(),
            provider.GetRequiredService<IntentMatchAssociationService>())
        : id is SystemCapabilityIds.ApplicationCandidateWrite
            or SystemCapabilityIds.ApplicationCandidateValidate
            or SystemCapabilityIds.ApplicationCandidateActivate
            or SystemCapabilityIds.ApplicationCandidateRecover
        ? new ApplicationCandidateWriteCapabilityHandler(
            id,
            provider.GetRequiredService<DantesRoleplay.DataAccess.DantesRoleplayDbContext>(),
            provider.GetRequiredService<IApplicationRegistry>(),
            provider.GetRequiredService<IApplicationAuthoringService>())
        : id == SystemCapabilityIds.StandingGrantAdmin
        ? new StandingGrantAdministrationSystemCapabilityHandler(provider)
        : new SystemAdministrationWriteCapabilityHandler(
            id,
            provider.GetRequiredService<IApplicationRegistry>(),
            provider.GetRequiredService<ISourceRegistry>(),
            provider.GetRequiredService<IAllowedSourceRootCatalog>(),
            provider.GetRequiredService<IRegistryAdministrationService>(),
            provider.GetRequiredService<IApplicationComponentTypeRegistry>(),
            provider.GetRequiredService<IComponentTypeAdministrationService>(),
            provider.GetRequiredService<IBoundedJsonSchemaValidator>(),
            provider.GetRequiredService<IApplicationPreviewService>(),
            provider.GetRequiredService<IApplicationActivationService>(),
            provider.GetRequiredService<IProjectionImpactService>(),
            provider.GetRequiredService<IStateSpaceAdministrationService>(),
            provider.GetRequiredService<ILegacyStateAdoptionService>(),
            provider.GetRequiredService<IApplicationExtensionRegistry>());
}
