using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.TriggerScheduling;

internal static class TriggerSchedulingComponentRegistration
{
    internal static IServiceCollection AddTriggerSchedulingComponent(this IServiceCollection services)
    {
        services.TryAddSingleton<ITriggerClock, SystemTriggerClock>();
        services.TryAddSingleton<ITriggerObservationRateLimiter, InMemoryTriggerObservationRateLimiter>();
        services.TryAddSingleton<IConditionalTriggerAdapter, ClosedScalarConditionalTriggerAdapter>();
        services.TryAddSingleton<IObservationMatchAdapter, ClosedScalarsObservationMatchAdapter>();
        services.TryAddSingleton<IPhoneCompanionCredentialGenerator, RandomPhoneCompanionCredentialGenerator>();
        services.TryAddScoped<ITriggerFireTransactionParticipant, TriggerNotificationTransactionParticipant>();
        services.AddScoped<SqliteConditionalTriggerStore>();
        services.AddScoped<IConditionalTriggerStore>(provider =>
            provider.GetRequiredService<SqliteConditionalTriggerStore>());
        services.AddScoped<SqliteObservationTriggerStore>();
        services.AddScoped<IObservationTriggerStore>(provider =>
            provider.GetRequiredService<SqliteObservationTriggerStore>());
        services.AddScoped<IObservationAppendTransactionParticipant, ObservationTriggerAppendParticipant>();
        services.AddScoped<SqlitePhoneCompanionRegistry>();
        services.AddScoped<IPhoneCompanionRegistry>(provider =>
            provider.GetRequiredService<SqlitePhoneCompanionRegistry>());
        services.AddScoped<IPhoneCompanionAuthenticator, SqlitePhoneCompanionAuthenticator>();
        services.AddScoped<IObservationIngestionPolicy, PhoneCompanionObservationIngestionPolicy>();
        services.AddScoped<IApplicationEcsTransactionParticipant, ConditionalTriggerEcsTransactionParticipant>();
        services.AddScoped<ITriggerSchedulingStore, SqliteTriggerSchedulingStore>();
        services.AddScoped<IObservationIngestionService, SqliteObservationIngestionService>();
        services.AddScoped<ITriggerScheduleStatusReader, SqliteTriggerScheduleStatusReader>();
        services.AddScoped<IRecurringTriggerStatusReader, SqliteRecurringTriggerStatusReader>();
        services.AddScoped<IConditionalTriggerStatusReader, SqliteConditionalTriggerStatusReader>();
        services.AddScoped<IObservationTriggerStatusReader, SqliteObservationTriggerStatusReader>();
        services.AddScoped<ITriggerSchedulingAdministrationService,
            SqliteTriggerSchedulingAdministrationService>();
        services.AddScoped<IOneTimeTriggerWorker, SqliteOneTimeTriggerWorker>();
        services.AddScoped<IRecurringTriggerWorker, SqliteRecurringTriggerWorker>();
        services.AddScoped<IConditionalTriggerWorker, SqliteConditionalTriggerWorker>();
        services.AddScoped<IObservationTriggerWorker, SqliteObservationTriggerWorker>();
        services.AddHostedService<TriggerSchedulingBackgroundWorker>();
        services.AddScoped<ISystemAiToolSource, ScheduledAiTaskToolSource>();
        services.AddHostedService<ScheduledAiTaskWorker>();
        return services;
    }
}
