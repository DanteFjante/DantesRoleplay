using DantesRoleplay.SystemFeedback;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class FeedbackComponentRegistration
{
    internal static IServiceCollection AddFeedbackComponent(this IServiceCollection services)
    {
        services.AddScoped<ISystemFeedbackService, SystemFeedbackService>();
        services.AddScoped<ISystemFeedbackAdministrationService, SystemFeedbackAdministrationService>();
        services.AddScoped<ISystemFeedbackRetentionService, SystemFeedbackRetentionService>();
        return services;
    }
}
