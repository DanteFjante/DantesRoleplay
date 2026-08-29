using DantesRoleplay.Effects;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class EffectsAndTransactionsComponentRegistration
{
    internal static IServiceCollection AddEffectsAndTransactionsComponent(this IServiceCollection services)
    {
        services.AddScoped<IEffectApplier, EffectApplier>();
        return services;
    }
}
