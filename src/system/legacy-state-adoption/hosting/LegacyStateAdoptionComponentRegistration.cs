using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.LegacyStateAdoption;

public static class LegacyStateAdoptionComponentRegistration
{
    public static IServiceCollection AddLegacyStateAdoptionComponent(this IServiceCollection services) =>
        services.AddScoped<ILegacyStateAdoptionService, LegacyStateAdoptionService>();
}
