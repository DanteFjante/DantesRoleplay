using DantesRoleplay.Play;
using Microsoft.Extensions.DependencyInjection;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.DataAccess.Composition;

public static class PlayRecordingComponentExtensions
{
    public static IServiceCollection AddPlayRecordingComponent(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IApplicationPlayRecordStore, ApplicationPlayRecordStore>();
        services.AddScoped<IConversationMemoryStore, ApplicationConversationMemoryStore>();
        services.AddScoped<IConversationMemoryDreamRecorder, ConversationMemoryDreamRecorder>();
        services.AddScoped<ISystemReadCapabilityHandler, ConversationMemorySystemCapabilityHandler>();
        return services;
    }
}
