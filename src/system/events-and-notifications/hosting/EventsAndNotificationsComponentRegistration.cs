using DantesRoleplay.Events;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class EventsAndNotificationsComponentRegistration
{
    internal static IServiceCollection AddEventsAndNotificationsComponent(this IServiceCollection services)
    {
        services.AddScoped<IEventTypeStore, EventTypeStore>();
        services.AddScoped<ISubscriptionStore, SubscriptionStore>();
        services.AddScoped<IGuardRouter, GuardRouter>();
        services.AddScoped<IEventLedger, EventLedger>();
        services.AddScoped<IEventRouter, EventRouter>();
        services.AddScoped<IApplicationEcsTransactionParticipant,
            ApplicationClockEventTransactionParticipant>();
        services.AddScoped<INotificationStore, NotificationStore>();
        return services;
    }
}
