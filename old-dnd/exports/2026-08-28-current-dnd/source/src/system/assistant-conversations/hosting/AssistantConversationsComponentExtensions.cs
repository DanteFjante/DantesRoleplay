using DantesRoleplay.Assistants;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

public static class AssistantConversationsComponentExtensions
{
    public static IServiceCollection AddAssistantConversationsComponent(this IServiceCollection services)
    {
        services.AddScoped<IAssistantConversationStore, AssistantConversationStore>();
        services.AddScoped<IAssistantConversationService, AssistantConversationService>();
        return services;
    }
}
