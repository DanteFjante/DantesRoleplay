using DantesRoleplay.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.SystemConversations;

internal static class SystemConversationsComponentRegistration
{
    internal static IServiceCollection AddSystemConversationsComponent(this IServiceCollection services)
    {
        services.AddScoped<ISystemConversationContextMaterializer, SystemConversationContextMaterializer>();
        services.AddScoped<ISystemConversationService, SystemConversationService>();
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        return services;
    }
}
