using DantesRoleplay.DataAccess;
using DantesRoleplay.EcsEffects;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

internal sealed class ConditionalTriggerEcsTransactionParticipant(
    DantesRoleplayDbContext db,
    SqliteConditionalTriggerStore store,
    ITriggerClock clock) : IApplicationEcsTransactionParticipant
{
    public const int MaximumCandidates = 64;
    private const int MaximumDependencyRows = MaximumCandidates * 16 + 1;

    public async Task StageAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await StageCoreAsync(batch, receipts, operationId, cancellationToken);
        }
        catch (TriggerSchedulingContractException exception)
        {
            throw new ApplicationEcsTransactionParticipantException(
                $"Conditional trigger evaluation was rejected: {exception.Code}.");
        }
    }

    private async Task StageCoreAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        string operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(receipts);
        var componentKeys = batch.Effects.Where(value => value.Type is
                ApplicationEcsEffectType.ComponentAdd or ApplicationEcsEffectType.ComponentSet or
                ApplicationEcsEffectType.ComponentMerge or ApplicationEcsEffectType.ComponentRemove)
            .Select(value => (value.EntityId, value.ComponentType!.QualifiedTypeId)).ToHashSet();
        var deletedEntities = batch.Effects.Where(value => value.Type == ApplicationEcsEffectType.EntityDelete)
            .Select(value => value.EntityId).ToHashSet(StringComparer.Ordinal);
        var entities = componentKeys.Select(value => value.EntityId).Concat(deletedEntities).Distinct().ToArray();
        if (entities.Length == 0) return;

        var dependencyRows = await (from dependency in db.ConditionalTriggerDependencies.AsNoTracking()
            join current in db.ConditionalTriggerCurrent.AsNoTracking()
                on new { dependency.ApplicationId, Id = dependency.TriggerId }
                equals new { current.ApplicationId, current.Id }
            join definition in db.ConditionalTriggers.AsNoTracking()
                on new { dependency.ApplicationId, Id = dependency.TriggerId, Version = dependency.TriggerVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where dependency.StateSpaceId == batch.StateSpaceId &&
                dependency.TriggerVersion == current.CurrentVersion && definition.Lifecycle == "active" &&
                entities.Contains(dependency.EntityId)
            select dependency).Take(MaximumDependencyRows).ToListAsync(cancellationToken);
        if (dependencyRows.Count == MaximumDependencyRows)
            throw new TriggerSchedulingContractException("CONDITIONAL_DEPENDENCY_FANOUT",
                "The changed dependency set exceeds its bounded evaluation fan-out.");
        var candidates = dependencyRows.Where(value => deletedEntities.Contains(value.EntityId) ||
                componentKeys.Contains((value.EntityId, value.QualifiedTypeId)))
            .Select(value => (value.ApplicationId, value.TriggerId, value.TriggerVersion)).Distinct().ToArray();
        if (candidates.Length > MaximumCandidates)
            throw new TriggerSchedulingContractException("CONDITIONAL_CANDIDATE_LIMIT",
                "At most 64 conditional triggers may evaluate in one ECS transaction.");

        var newWork = new List<ConditionalTriggerFireWorkRecord>();
        foreach (var candidate in candidates)
        {
            var state = await db.ConditionalTriggerState.SingleAsync(value =>
                value.ApplicationId == candidate.ApplicationId && value.TriggerId == candidate.TriggerId &&
                value.CurrentVersion == candidate.TriggerVersion, cancellationToken);
            if (state.LastOperationId == operationId) continue;
            var row = await db.ConditionalTriggers.AsNoTracking()
                .Include(value => value.Dependencies).Include(value => value.NotificationEntities)
                .SingleAsync(value => value.ApplicationId == candidate.ApplicationId &&
                    value.Id == candidate.TriggerId && value.Version == candidate.TriggerVersion,
                    cancellationToken);
            var definition = SqliteConditionalTriggerStore.Definition(row);
            var adapter = store.ResolveAdapter(definition.Adapter);
            var truth = adapter.Evaluate(definition, await store.SnapshotsAsync(definition, cancellationToken));
            var fire = state.Armed && truth && (definition.Activation == ConditionalTriggerActivation.Level ||
                state.CurrentTruth != true);
            var armed = state.Armed;
            if (fire) armed = false;
            else if (!truth && definition.Rearm == ConditionalTriggerRearm.OnFalse) armed = true;

            state.CurrentTruth = truth;
            state.Armed = armed;
            state.EvaluationRevision++;
            state.LastOperationId = operationId;
            state.UpdatedAtUtc = UtcNow().UtcDateTime;
            if (fire)
            {
                state.LastFiredOperationId = operationId;
                var fireId = SqliteConditionalTriggerStore.FireId(candidate.ApplicationId,
                    candidate.TriggerId, candidate.TriggerVersion, operationId);
                newWork.Add(new ConditionalTriggerFireWorkRecord
                {
                    FireId = fireId,
                    ApplicationId = candidate.ApplicationId,
                    TriggerId = candidate.TriggerId,
                    TriggerVersion = candidate.TriggerVersion,
                    ChangeOperationId = operationId,
                    State = "ready",
                    AttemptCount = 0,
                    Revision = 0,
                    CreatedAtUtc = state.UpdatedAtUtc,
                    UpdatedAtUtc = state.UpdatedAtUtc
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        if (newWork.Count > 0)
        {
            db.ConditionalTriggerFireWork.AddRange(newWork);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
    }
}
