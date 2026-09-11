using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Ecs;
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
    IStateSpaceRegistry stateSpaces,
    IBoundedJsonSchemaValidator schemas) : IApplicationEcsEventSourceParticipant
{
    public async Task<IReadOnlyList<EventDetail>> StageEventsAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        ApplicationEcsEventEmissionContext emission,
        CancellationToken cancellationToken = default)
    {
        var clock = batch.Effects
            .Select((effect, index) => new { Effect = effect, Index = index })
            .SingleOrDefault(value => value.Effect.Type == ApplicationEcsEffectType.ClockAdvance);
        if (clock is null) return [];

        var receipt = receipts.SingleOrDefault(value => value.BatchEffectIndex == clock.Index)
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
            idempotencyKey = IdempotencyKey(batch, emission, clock.Index),
            operationReceipt = emission.RootOperationId
        });
        var validation = schemas.Validate(
            EventPayloadRoleMetadata.WithoutExtension(registered.PayloadSchema), payload);
        if (validation.Status != SchemaValueStatus.Valid)
            throw new ApplicationEcsTransactionParticipantException(
                "The authoritative clock event does not satisfy its registered payload contract.");

        return await events.WriteAcceptedAsync(
            [new ProposedEvent(clock.Effect.EventTypeId, payload,
                new[] { clock.Effect.EntityId, clock.Effect.SubjectEntityId }
                    .Distinct(StringComparer.Ordinal).ToArray(),
                clock.Effect.EntityId, clock.Index, emission.Depth, emission.RootOperationId,
                emission.CausationEventId, emission.ProducerExecutionId)],
            emission.RootOperationId,
            cancellationToken,
            ApplicationEventSourceContext.Require(stateSpaces, batch));
    }

    private static string IdempotencyKey(
        ApplicationEcsEffectBatch batch,
        ApplicationEcsEventEmissionContext emission,
        int effectIndex)
    {
        // Root actions retain their caller-supplied idempotency fingerprint. Reaction batches have
        // no execution identity by construction, so derive the same fixed-width fingerprint from
        // immutable, applier-validated provenance rather than dereferencing an absent caller value.
        if (batch.ExecutionIdentity is { } identity) return identity.RequestFingerprint;
        var source = string.Join("\u001f", emission.RootOperationId, emission.CausationEventId,
            emission.ProducerExecutionId, batch.StateSpaceId, effectIndex);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}
