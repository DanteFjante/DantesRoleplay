using DantesRoleplay.Mechanics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.DataAccess.Composition;

internal static class MechanicsComponentRegistration
{
    internal static IServiceCollection AddMechanicsComponent(this IServiceCollection services)
    {
        services.AddScoped<IMechanicStore, MechanicStore>();
        services.TryAddSingleton<IMechanicEngine, JintMechanicEngine>();
        services.AddScoped<IProjectionResolver, ProjectionResolver>();
        services.AddScoped<IMechanicComposer, MechanicComposer>();
        return services;
    }
}
