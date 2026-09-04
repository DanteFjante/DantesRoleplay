using System.Text.Json;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Stages the typed structural event for a generic authoritative-clock effect. The effect, event,
/// and operation record share the caller's transaction, so none can commit without the others.
/// </summary>
public sealed class ApplicationClockEventTransactionParticipant(
    IEventTypeStore eventTypes,
    IEventLedger events,
    IBoundedJsonSchemaValidator schemas) : IApplicationEcsTransactionParticipant
{
    public async Task StageAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var clock = batch.Effects
            .Select((effect, index) => new { Effect = effect, Index = index })
            .SingleOrDefault(value => value.Effect.Type == ApplicationEcsEffectType.ClockAdvance);
        if (clock is null) return;

        var receipt = receipts.SingleOrDefault(value => value.Index == clock.Index)
            ?? throw new ApplicationEcsTransactionParticipantException(
                "The authoritative clock effect has no operation receipt.");
        var registered = await eventTypes.GetAsync(clock.Effect.EventTypeId,
            cancellationToken: cancellationToken);
        if (registered is null || registered.Status != EventTypeStatus.Active)
            throw new ApplicationEcsTransactionParticipantException(
                "The authoritative clock event type is not registered and active.");

        var payload = JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            worldId = clock.Effect.EntityId,
            calendarId = clock.Effect.CalendarId,
            beforeMinute = clock.Effect.PreviousMinute,
            deltaMinutes = clock.Effect.DeltaMinutes,
            afterMinute = clock.Effect.ResultingMinute,
            beforeRevision = clock.Effect.PreviousClockRevision,
            afterRevision = clock.Effect.ResultingClockRevision,
            causeCapabilityId = batch.MechanicId,
            subjectEntityId = clock.Effect.SubjectEntityId,
            activityId = clock.Effect.ActivityId,
            idempotencyKey = batch.ExecutionIdentity!.RequestFingerprint,
            operationReceipt = operationId
        });
        var validation = schemas.Validate(
            EventPayloadRoleMetadata.WithoutExtension(registered.PayloadSchema), payload);
        if (validation.Status != SchemaValueStatus.Valid)
            throw new ApplicationEcsTransactionParticipantException(
                "The authoritative clock event does not satisfy its registered payload contract.");

        await events.WriteAcceptedAsync(
            [new ProposedEvent(clock.Effect.EventTypeId, payload,
                new[] { clock.Effect.EntityId, clock.Effect.SubjectEntityId }
                    .Distinct(StringComparer.Ordinal).ToArray(),
                clock.Effect.EntityId, clock.Index)],
            operationId,
            cancellationToken);
    }
}
