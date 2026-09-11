using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Validates and stages application-mechanic event assertions inside the same transaction as the
/// typed ECS effects and operation audit. Game-specific event vocabulary remains catalog-owned.
/// </summary>
public sealed class ApplicationDeclaredEventTransactionParticipant(
    DantesRoleplayDbContext db,
    IEventLedger events,
    IStateSpaceRegistry stateSpaces) : IApplicationEcsEventSourceParticipant
{
    public async Task<IReadOnlyList<EventDetail>> StageEventsAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        ApplicationEcsEventEmissionContext emission,
        CancellationToken cancellationToken = default)
    {
        if (batch.DeclaredEvents.Count == 0) return [];

        var proposed = await DerivedEvents.ProposeAsync(
            db,
            batch.DeclaredEvents,
            "application action",
            emission.ProducerExecutionId,
            emission.RootOperationId,
            causationEventId: emission.CausationEventId,
            depth: emission.Depth,
            cancellationToken,
            applicationStateSpaceId: batch.StateSpaceId);
        if (!proposed.Ok)
            throw new ApplicationEcsTransactionParticipantException(
                $"{proposed.Code}: {proposed.Reason}");

        return await events.WriteAcceptedAsync(proposed.Proposals, emission.RootOperationId, cancellationToken,
            ApplicationEventSourceContext.Require(stateSpaces, batch));
    }
}
