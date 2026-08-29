using DantesRoleplay.World;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class StateComponentRegistration
{
    internal static IServiceCollection AddStateComponent(this IServiceCollection services)
    {
        services.AddScoped<IWorldStore, WorldStore>();
        services.AddScoped<IGraphProjectionReader, GraphProjectionReader>();
        services.AddScoped<IStagedWorldComposer, StagedWorldComposer>();
        return services;
    }
}
