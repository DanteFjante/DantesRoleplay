using DantesRoleplay.Information;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class InformationComponentRegistration
{
    internal static IServiceCollection AddInformationComponent(this IServiceCollection services)
    {
        services.AddScoped<IInformationStore, InformationStore>();
        return services;
    }
}
