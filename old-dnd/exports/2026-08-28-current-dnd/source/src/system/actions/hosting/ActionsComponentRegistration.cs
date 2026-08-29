using DantesRoleplay.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class ActionsComponentRegistration
{
    internal static IServiceCollection AddActionsComponent(this IServiceCollection services)
    {
        services.AddScoped<ActionRunner>();
        services.AddScoped<IActionRunner>(provider => provider.GetRequiredService<ActionRunner>());
        return services;
    }
}
