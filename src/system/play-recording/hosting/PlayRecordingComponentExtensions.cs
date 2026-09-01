using DantesRoleplay.Play;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

public static class PlayRecordingComponentExtensions
{
    public static IServiceCollection AddPlayRecordingComponent(this IServiceCollection services)
    {
        services.AddScoped<IApplicationPlayRecordStore, ApplicationPlayRecordStore>();
        return services;
    }
}
