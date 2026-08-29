using DantesRoleplay.HostSettings;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

public static class HostSettingsComponentExtensions
{
    public static IServiceCollection AddHostSettingsComponent(this IServiceCollection services)
    {
        services.AddScoped<IHostSettingOverrideStore, HostSettingOverrideStore>();
        return services;
    }
}
