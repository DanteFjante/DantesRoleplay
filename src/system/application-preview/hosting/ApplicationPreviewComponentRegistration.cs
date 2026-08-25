using DantesRoleplay.ApplicationPreview;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class ApplicationPreviewComponentRegistration
{
    internal static IServiceCollection AddApplicationPreviewComponent(this IServiceCollection services)
    {
        services.AddScoped<IApplicationPreviewService, ApplicationPreviewService>();
        return services;
    }
}
