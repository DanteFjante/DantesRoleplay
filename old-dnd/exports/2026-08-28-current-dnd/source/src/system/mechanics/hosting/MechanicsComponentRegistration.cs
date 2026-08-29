using DantesRoleplay.Mechanics;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class MechanicsComponentRegistration
{
    internal static IServiceCollection AddMechanicsComponent(this IServiceCollection services)
    {
        services.AddScoped<IMechanicStore, MechanicStore>();
        services.AddScoped<IProjectionResolver, ProjectionResolver>();
        services.AddScoped<IMechanicComposer, MechanicComposer>();
        return services;
    }
}
