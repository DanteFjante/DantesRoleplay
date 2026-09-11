using DantesRoleplay.Information;
using DantesRoleplay.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class InformationComponentRegistration
{
    internal static IServiceCollection AddInformationComponent(this IServiceCollection services)
    {
        services.AddScoped<InformationStore>();
        services.AddScoped<IInformationStore>(provider => provider.GetRequiredService<InformationStore>());
        services.AddScoped<IConditionalInformationStore, SqliteConditionalInformationStore>();
        return services;
    }
}
