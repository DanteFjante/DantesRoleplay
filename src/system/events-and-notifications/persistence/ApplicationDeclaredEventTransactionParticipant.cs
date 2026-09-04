using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Validates and stages application-mechanic event assertions inside the same transaction as the
/// typed ECS effects and operation audit. Game-specific event vocabulary remains catalog-owned.
/// </summary>
public sealed class ApplicationDeclaredEventTransactionParticipant(
    DantesRoleplayDbContext db,
    IEventLedger events) : IApplicationEcsTransactionParticipant
{
    public async Task StageAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        if (batch.DeclaredEvents.Count == 0) return;

        var proposed = await DerivedEvents.ProposeAsync(
            db,
            batch.DeclaredEvents,
            "application action",
            "application-action:" + operationId,
            operationId,
            causationEventId: string.Empty,
            depth: 0,
            cancellationToken,
            applicationStateSpaceId: batch.StateSpaceId);
        if (!proposed.Ok)
            throw new ApplicationEcsTransactionParticipantException(
                $"{proposed.Code}: {proposed.Reason}");

        await events.WriteAcceptedAsync(proposed.Proposals, operationId, cancellationToken);
    }
}
