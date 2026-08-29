using Microsoft.Extensions.DependencyInjection;
namespace DantesRoleplay.ComponentTypeAdministration;
internal static class ComponentTypeAdministrationComponentRegistration { internal static IServiceCollection AddComponentTypeAdministrationComponent(this IServiceCollection services) { services.AddScoped<IComponentTypeAdministrationService, ComponentTypeAdministrationService>(); return services; } }
