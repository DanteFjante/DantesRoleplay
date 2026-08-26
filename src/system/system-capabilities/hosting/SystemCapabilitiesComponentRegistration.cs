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
        foreach (var id in new[]
        {
            SystemCapabilityIds.ApplicationRegister,
            SystemCapabilityIds.SourceRegister,
            SystemCapabilityIds.ComponentTypeRegister,
            SystemCapabilityIds.ApplicationActivate,
            SystemCapabilityIds.StateSpaceCreate,
            SystemCapabilityIds.StateSpaceUpgrade,
            SystemCapabilityIds.StateSpaceAdoptLegacy
        })
        {
            var capabilityId = id;
            services.AddScoped<ISystemWriteCapabilityHandler>(provider => Write(provider, capabilityId));
        }
        services.AddScoped<ISystemCapabilityCatalog, SystemCapabilityCatalog>();
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        return services;
    }

    private static SystemAdministrationWriteCapabilityHandler Write(
        IServiceProvider provider,
        string id) => new(
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
            provider.GetRequiredService<ILegacyStateAdoptionService>());
}
