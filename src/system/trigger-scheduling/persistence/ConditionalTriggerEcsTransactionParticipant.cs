using DantesRoleplay.DataAccess;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

internal sealed class ConditionalTriggerEcsTransactionParticipant(
    DantesRoleplayDbContext db,
    SqliteConditionalTriggerStore store,
    ITriggerClock clock,
    IApplicationObserverPredicateInputCapture? predicateCapture = null) : IApplicationEcsTransactionParticipant
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
        var relationshipKeys = batch.Effects.Where(value => value.Type is
                ApplicationEcsEffectType.RelationshipSet or ApplicationEcsEffectType.RelationshipRemove)
            .SelectMany(value => new[]
            {
                (value.QualifiedRelationshipKind, AnchorEntityId: value.EntityId, Incoming: false),
                (value.QualifiedRelationshipKind, AnchorEntityId: value.TargetEntityId, Incoming: true)
            }).ToHashSet();
        var entities = componentKeys.Select(value => value.EntityId).Concat(deletedEntities).Distinct().ToArray();
        if (entities.Length == 0 && relationshipKeys.Count == 0) return;

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
        var relationshipRows = relationshipKeys.Count == 0 ? [] : await (
            from dependency in db.ConditionalTriggerRelationshipDependencies.AsNoTracking()
            join current in db.ConditionalTriggerCurrent.AsNoTracking()
                on new { dependency.ApplicationId, Id = dependency.TriggerId }
                equals new { current.ApplicationId, current.Id }
            join definition in db.ConditionalTriggers.AsNoTracking()
                on new { dependency.ApplicationId, Id = dependency.TriggerId, Version = dependency.TriggerVersion }
                equals new { definition.ApplicationId, definition.Id, definition.Version }
            where dependency.StateSpaceId == batch.StateSpaceId &&
                  dependency.TriggerVersion == current.CurrentVersion && definition.Lifecycle == "active"
            select dependency).Take(MaximumDependencyRows).ToListAsync(cancellationToken);
        if (relationshipRows.Count == MaximumDependencyRows)
            throw new TriggerSchedulingContractException("CONDITIONAL_DEPENDENCY_FANOUT",
                "The changed relationship set exceeds its bounded evaluation fan-out.");
        var componentCandidates = dependencyRows.Where(value => deletedEntities.Contains(value.EntityId) ||
                componentKeys.Contains((value.EntityId, value.QualifiedTypeId)))
            .Select(value => (value.ApplicationId, value.TriggerId, value.TriggerVersion));
        var relationshipCandidates = relationshipRows.Where(value =>
                relationshipKeys.Contains((value.QualifiedKind, value.AnchorEntityId, value.Incoming)))
            .Select(value => (value.ApplicationId, value.TriggerId, value.TriggerVersion));
        var candidates = componentCandidates.Concat(relationshipCandidates).Distinct().ToArray();
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
                .Include(value => value.Dependencies).Include(value => value.RelationshipDependencies)
                .Include(value => value.NotificationEntities).Include(value => value.WorkflowBinding)
                .Include(value => value.PredicateBinding)
                .SingleAsync(value => value.ApplicationId == candidate.ApplicationId &&
                    value.Id == candidate.TriggerId && value.Version == candidate.TriggerVersion,
                    cancellationToken);
            var definition = SqliteConditionalTriggerStore.Definition(row);
            if (definition.Predicate is not null)
            {
                if (predicateCapture is null || row.WorkflowBinding is null)
                    continue;
                var fireId = SqliteConditionalTriggerStore.FireId(candidate.ApplicationId,
                    candidate.TriggerId, candidate.TriggerVersion, operationId);
                var admittedAt = UtcNow();
                var target = TriggerProcedureWorkflowBindingPersistence.Materialize(row.Id, row.Version,
                    fireId, admittedAt, row.WorkflowBinding);
                if (target is null) continue;
                var workflowHost = target.Submission.InvocationHost;
                var readHost = new InteractionInvocationHost(workflowHost.Principal,
                    workflowHost.ApplicationRevision, workflowHost.StateSpaceId!, workflowHost.GrantReference,
                    "predicate." + fireId[13..], workflowHost.StateRevision!,
                    InteractionExecutionProfile.ReadOnly,
                    new InteractionInvocationBudget(1, workflowHost.Budget.DeadlineUtc));
                var eventJson = InteractionCanonicalJson.CanonicalizeObject(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        source = "ecs-operation", operationId, batch.StateSpaceId,
                        effects = receipts.OrderBy(value => value.BatchEffectIndex).Select(value => new
                        {
                            value.BatchEffectIndex, value.Type, value.EntityId, value.QualifiedTypeId,
                            value.TargetEntityId, value.QualifiedRelationshipKind,
                            value.BeforeJson, value.BeforeRevision, value.AfterJson, value.AfterRevision,
                            value.RemovedRevision, value.Revision
                        })
                    }));
                ApplicationObserverPredicateCapturedInput captured;
                try
                {
                    captured = await predicateCapture.CaptureAsync(readHost, definition.Predicate.Selection,
                        "{}", eventJson, StableSeed(operationId, fireId), cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch { continue; }
                var captureJson = ConditionalTriggerPredicatePersistence.SerializeCapture(captured);
                var causalId = await EnsureCausalAllowanceAsync(operationId, admittedAt, cancellationToken);
                newWork.Add(new ConditionalTriggerFireWorkRecord
                {
                    FireId = fireId,
                    ApplicationId = candidate.ApplicationId,
                    TriggerId = candidate.TriggerId,
                    TriggerVersion = candidate.TriggerVersion,
                    ChangeOperationId = operationId,
                    CausalAllowanceId = causalId,
                    PredicateCaptureJson = captureJson,
                    PredicateCaptureFingerprint = ConditionalTriggerPredicatePersistence.EnvelopeFingerprint(captureJson),
                    PredicatePriorTruth = state.CurrentTruth,
                    PredicatePriorArmed = state.Armed,
                    State = "ready", AttemptCount = 0, Revision = 0,
                    CreatedAtUtc = admittedAt.UtcDateTime, UpdatedAtUtc = admittedAt.UtcDateTime
                });
                state.LastOperationId = operationId;
                state.EvaluationRevision++;
                state.UpdatedAtUtc = admittedAt.UtcDateTime;
                continue;
            }
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

    private async Task<string> EnsureCausalAllowanceAsync(string operationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var identity = InteractionCanonicalJson.CanonicalizeObject(
            System.Text.Json.JsonSerializer.Serialize(new { sourceKind = "ecs-operation", sourceId = operationId }));
        var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/trigger-causal-allowance/v1", identity);
        var id = "causal." + fingerprint.ToLowerInvariant()[..32];
        var existing = db.ChangeTracker.Entries<TriggerCausalAllowanceRecord>()
            .Select(value => value.Entity).SingleOrDefault(value => value.Id == id)
            ?? await db.TriggerCausalAllowances.SingleOrDefaultAsync(value => value.Id == id,
                cancellationToken);
        if (existing is null)
            db.TriggerCausalAllowances.Add(new TriggerCausalAllowanceRecord
            {
                Id = id, SourceKind = "ecs-operation", SourceId = operationId,
                MaximumOperations = 64, ReservedOperations = 0, IdentityFingerprint = fingerprint,
                CreatedAtUtc = now.UtcDateTime, UpdatedAtUtc = now.UtcDateTime
            });
        else if (existing.SourceKind != "ecs-operation" || existing.SourceId != operationId ||
                 existing.MaximumOperations != 64 || existing.IdentityFingerprint != fingerprint)
            throw new TriggerSchedulingContractException("TRIGGER_CAUSAL_IDENTITY_CONFLICT",
                "The source operation has conflicting causal allowance evidence.");
        return id;
    }

    private static long StableSeed(string operationId, string fireId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(operationId + "\n" + fireId));
        return System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
    }
}
