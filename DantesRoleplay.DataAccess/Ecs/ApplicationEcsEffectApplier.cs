using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SqliteInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Cryptography;
using System.Text;

namespace DantesRoleplay.EcsEffects;

/// <summary>Atomic, ruleset-neutral mutation boundary for one application state space.</summary>
public sealed class ApplicationEcsEffectApplier(
    DantesRoleplayDbContext db,
    IEntityComponentStore store,
    IStateSpaceRegistry stateSpaces,
    IOperationLog operations,
    IStateSpaceEdgeStore? edges = null,
    IEnumerable<IApplicationEcsTransactionParticipant>? transactionParticipants = null,
    IEcsRoleConstraintValidator? roleConstraints = null,
    IEnumerable<IApplicationEcsEventSourceParticipant>? eventSources = null,
    IApplicationEcsReactionRouter? reactionRouter = null) : IApplicationEcsEffectApplier,
    IApplicationEcsGuardedEffectApplier
{
    private const string AuditIdentity = ApplicationEcsExecutionIdentity.AuditTool;

    public Task<ApplicationEcsEffectResult> ApplyAsync(
        ApplicationEcsEffectBatch batch,
        bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(batch, dryRun, commitGuard: null, cancellationToken);

    Task<ApplicationEcsEffectResult> IApplicationEcsGuardedEffectApplier.ApplyAuthorizedAsync(
        ApplicationEcsEffectBatch batch,
        IApplicationEcsCommitGuard commitGuard,
        CancellationToken cancellationToken) =>
        ApplyAsync(batch, dryRun: false, commitGuard, cancellationToken);

    private async Task<ApplicationEcsEffectResult> ApplyAsync(
        ApplicationEcsEffectBatch batch,
        bool dryRun,
        IApplicationEcsCommitGuard? commitGuard,
        CancellationToken cancellationToken)
    {
        if (batch is null)
            return await FailedSafelyAsync(null, dryRun, Operation.NewId(),
                ApplicationEcsEffectValidation.Validate(null), CancellationToken.None);
        var shapeProblems = ApplicationEcsEffectValidation.Validate(batch);
        var operationId = shapeProblems.Any(problem => problem.Code == "INVALID_EXECUTION_IDENTITY")
            ? Operation.NewId()
            : batch.ExecutionIdentity?.OperationId ?? Operation.NewId();
        if (shapeProblems.Count > 0)
            return await FailedSafelyAsync(batch, dryRun, operationId, shapeProblems, CancellationToken.None);

        if (batch.ExecutionIdentity is not null)
        {
            var replay = await ReplayAsync(batch, dryRun, cancellationToken);
            if (replay is not null) return replay;
        }

        if (stateSpaces.Get(batch.StateSpaceId) is null)
            return await FailedSafelyAsync(batch, dryRun, operationId,
                [new(-1, "STATE_SPACE_UNKNOWN", "The state space is unknown.")], CancellationToken.None);

        IDbContextTransaction? transaction = null;
        var receipts = new List<ApplicationEcsEffectReceipt>(batch.Effects.Count);
        var currentIndex = -1;
        try
        {
            transaction = await SqliteEcsConstraintTransaction.BeginIfNeededAsync(db, cancellationToken)
                ?? throw new InvalidOperationException("Application ECS effects require their own write transaction.");
            var recoveryBefore = await SqliteChangeRecovery.ReadAsync(db.Database.GetDbConnection(),
                transaction.GetDbTransaction(), cancellationToken);
            var appliedBatches = new List<ApplicationEcsEffectBatch> { batch };
            currentIndex = await ApplyBatchAsync(batch, receipts, currentIndex, cancellationToken);

            if (dryRun)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                transaction = null;
                db.ChangeTracker.Clear();
                await RecordAsync(batch, operationId, success: true, dryRun: true, receipts.Count, "", CancellationToken.None);
                return new(false, true, operationId, receipts.AsReadOnly(), []);
            }

            var sources = (eventSources ?? []).ToArray();
            var rootSource = Source(batch.StateSpaceId);
            var budget = new Events.ChainBudget();
            var accepted = await StageSourcesAsync(sources, batch, receipts, new(
                operationId, 0, string.Empty, "application-action:" + operationId), cancellationToken);
            CountEvents(budget, accepted.Count);
            var queue = new Queue<Events.EventDetail>(accepted);
            while (queue.Count > 0 && reactionRouter is not null)
            {
                var pending = queue.Dequeue();
                if (pending.Source != rootSource)
                    throw new ApplicationEcsTransactionParticipantException(
                        "An application event source does not match the root state space.");
                await foreach (var routed in reactionRouter.RouteAsync(new(rootSource, [pending], operationId,
                    RootSeed(batch, operationId), budget), cancellationToken).WithCancellation(cancellationToken))
                {
                    if (!routed.Ok)
                        throw new ApplicationEcsTransactionParticipantException(
                            $"{routed.Code}: {routed.Reason}");
                    if (routed.Batches.Count > 1)
                        throw new ApplicationEcsTransactionParticipantException(
                            "An application reaction route returned more than one batch.");
                    foreach (var reaction in routed.Batches)
                    {
                    if (reaction.ParentEventId != pending.Id || reaction.Depth != pending.Depth + 1
                        || reaction.Batch.StateSpaceId != batch.StateSpaceId
                        || reaction.Batch.ExecutionIdentity is not null
                        || string.IsNullOrWhiteSpace(reaction.ProducerExecutionId))
                        throw new ApplicationEcsTransactionParticipantException(
                            "A reaction batch did not preserve its accepted event and state-space provenance.");
                    var depthCode = budget.CheckDepth(reaction.Depth);
                    if (depthCode is not null)
                        throw new ApplicationEcsTransactionParticipantException(Events.ChainBudget.Explain(depthCode));
                    var reactionProblems = ApplicationEcsEffectValidation.Validate(reaction.Batch, trustedReaction: true);
                    if (reactionProblems.Count > 0)
                        throw new ApplicationEcsTransactionParticipantException(
                            "A reaction batch has an invalid typed effect envelope.");
                    var start = receipts.Count;
                    currentIndex = await ApplyBatchAsync(reaction.Batch, receipts, currentIndex, cancellationToken,
                        trustedReaction: true);
                    appliedBatches.Add(reaction.Batch);
                    var emitted = await StageSourcesAsync(sources, reaction.Batch,
                        receipts.Skip(start).ToArray(), new(operationId, reaction.Depth, pending.Id,
                            reaction.ProducerExecutionId), cancellationToken);
                    CountEvents(budget, emitted.Count);
                    foreach (var @event in emitted) queue.Enqueue(@event);
                    }
                }
            }

            if (roleConstraints is not null)
                await roleConstraints.ValidateStateSpaceAsync(batch.StateSpaceId, cancellationToken);

            var committed = Aggregate(batch, appliedBatches);
            // Cover the root and every typed reaction effect, but not arbitrary
            // ECS writes by terminal audit/invalidation participants. Those
            // untracked writes must retain the conservative recovery signal.
            var recoveryAfter = await SqliteChangeRecovery.ReadAsync(db.Database.GetDbConnection(),
                transaction.GetDbTransaction(), cancellationToken);
            foreach (var participant in transactionParticipants ?? [])
                await participant.StageAsync(committed, receipts.AsReadOnly(), operationId, cancellationToken);
            if (commitGuard is not null)
            {
                var authority = await commitGuard.EvaluateAsync(committed, cancellationToken);
                if (!authority.Allowed)
                    throw new ApplicationEcsCommitGuardException(authority.Code);
            }
            await RecordAsync(committed, operationId, success: true, dryRun: false, receipts.Count, "", cancellationToken);
            await SqliteChangeRecovery.AcknowledgeAsync(db.Database.GetDbConnection(),
                transaction.GetDbTransaction(), recoveryBefore, recoveryAfter, operationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, false, operationId, receipts.AsReadOnly(), []);
        }
        catch (ApplicationEcsCommitGuardException exception)
        {
            await RollbackAndClearAsync(transaction);
            transaction = null;
            return new(false, dryRun, operationId, [],
                [new(-1, exception.Code, "Current action authority rejected the typed effect transaction.")]);
        }
        catch (OperationCanceledException)
        {
            await RollbackAndClearAsync(transaction);
            transaction = null;
            return await FailedSafelyAsync(batch, dryRun, operationId,
                [new(currentIndex, "CANCELLED", "The ECS effect batch was cancelled and rolled back.")], CancellationToken.None);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or DbUpdateException)
        {
            await RollbackAndClearAsync(transaction);
            transaction = null;
            if (batch.ExecutionIdentity is not null)
            {
                var replay = await ReplayAsync(batch, dryRun, CancellationToken.None);
                if (replay is not null) return replay;
            }
            var problemException = exception is EffectApplicationException applied
                ? applied.InnerException ?? applied
                : exception;
            var failureIndex = exception is EffectApplicationException failed
                ? failed.Index
                : receipts.Count > 0 ? receipts[^1].Index : currentIndex;
            return await FailedSafelyAsync(batch, dryRun, operationId,
                [new(failureIndex, Code(problemException), problemException.Message)], CancellationToken.None);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            transaction = null;
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task<int> ApplyBatchAsync(
        ApplicationEcsEffectBatch batch,
        List<ApplicationEcsEffectReceipt> receipts,
        int currentIndex,
        CancellationToken cancellationToken,
        bool trustedReaction = false)
    {
        await VerifyComponentsAsync(batch, cancellationToken);
        await VerifyEntitiesAsync(batch, cancellationToken);
        await VerifyRelationshipsAsync(batch, cancellationToken);
        await VerifyContainmentsAsync(batch, cancellationToken);
        for (var localIndex = 0; localIndex < batch.Effects.Count; localIndex++)
        {
            currentIndex++;
            try
            {
                receipts.Add(await ApplyOneAsync(batch.StateSpaceId, batch.Effects[localIndex], currentIndex,
                    localIndex, cancellationToken));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                throw new EffectApplicationException(currentIndex, exception);
            }
        }
        if (roleConstraints is not null)
            await roleConstraints.ValidateStateSpaceAsync(batch.StateSpaceId, cancellationToken);
        return currentIndex;
    }

    private async Task<IReadOnlyList<Events.EventDetail>> StageSourcesAsync(
        IReadOnlyList<IApplicationEcsEventSourceParticipant> sources,
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        ApplicationEcsEventEmissionContext emission,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0) return [];
        var source = Source(batch.StateSpaceId);
        var written = new List<Events.EventDetail>();
        foreach (var participant in sources)
        {
            var rows = await participant.StageEventsAsync(batch, receipts, emission, cancellationToken);
            foreach (var row in rows)
            {
                if (row.Source != source || row.RootOperationId != emission.RootOperationId
                    || row.Depth != emission.Depth || row.CausationId != emission.CausationEventId)
                    throw new ApplicationEcsTransactionParticipantException(
                        "An application event source returned rows outside the current transaction provenance.");
                written.Add(row);
            }
        }
        return written;
    }

    private Events.EventSourceContext Source(string stateSpaceId)
    {
        var stateSpace = stateSpaces.Get(stateSpaceId)
            ?? throw new ApplicationEcsTransactionParticipantException(
                "The application event state space is unknown.");
        return new(stateSpace.ApplicationRevision.ApplicationId.Value, stateSpace.StateSpaceId);
    }

    private static void CountEvents(Events.ChainBudget budget, int count)
    {
        var code = budget.CountEvents(count);
        if (code is not null)
            throw new ApplicationEcsTransactionParticipantException(Events.ChainBudget.Explain(code));
    }

    private static long RootSeed(ApplicationEcsEffectBatch batch, string operationId)
    {
        if (batch.Seed is { } seed) return seed;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(operationId));
        return BitConverter.ToInt64(hash, 0);
    }

    private static ApplicationEcsEffectBatch Aggregate(
        ApplicationEcsEffectBatch root,
        IReadOnlyList<ApplicationEcsEffectBatch> batches) => root with
    {
        Effects = batches.SelectMany(value => value.Effects).ToArray()
    };

    private async Task<ApplicationEcsEffectReceipt> ApplyOneAsync(
        string stateSpaceId,
        ApplicationEcsEffect effect,
        int index,
        int batchEffectIndex,
        CancellationToken cancellationToken)
    {
        switch (effect.Type)
        {
            case ApplicationEcsEffectType.EntityCreate:
            {
                var entity = await store.CreateEntityAsync(stateSpaceId, effect.EntityId, effect.Name, cancellationToken);
                return new(index, effect.Type, entity.EntityId, "", entity.Revision,
                    BatchEffectIndex: batchEffectIndex);
            }
            case ApplicationEcsEffectType.EntityDelete:
                if (!await store.DeleteEntityAsync(stateSpaceId, effect.EntityId, effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The entity is unknown or already deleted.");
                return new(index, effect.Type, effect.EntityId, "", effect.ExpectedRevision + 1,
                    BatchEffectIndex: batchEffectIndex);
            case ApplicationEcsEffectType.ComponentAdd:
            {
                var before = await ComponentBeforeAsync(stateSpaceId, effect, cancellationToken);
                var after = await store.AddComponentAsync(Write(stateSpaceId, effect), cancellationToken);
                return Receipt(index, batchEffectIndex, effect, before, after);
            }
            case ApplicationEcsEffectType.ComponentSet:
            case ApplicationEcsEffectType.ClockAdvance:
            {
                var before = await ComponentBeforeAsync(stateSpaceId, effect, cancellationToken);
                var after = await store.SetComponentAsync(Write(stateSpaceId, effect), cancellationToken);
                return Receipt(index, batchEffectIndex, effect, before, after);
            }
            case ApplicationEcsEffectType.ComponentMerge:
            {
                var before = await ComponentBeforeAsync(stateSpaceId, effect, cancellationToken);
                var after = await store.MergeComponentAsync(Write(stateSpaceId, effect), cancellationToken);
                return Receipt(index, batchEffectIndex, effect, before, after);
            }
            case ApplicationEcsEffectType.ComponentRemove:
            {
                var before = await ComponentBeforeAsync(stateSpaceId, effect, cancellationToken);
                if (!await store.RemoveComponentAsync(stateSpaceId, effect.EntityId, effect.ComponentType!, effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The component is absent.");
                return new(index, effect.Type, effect.EntityId, effect.ComponentType!.QualifiedTypeId, null,
                    effect.ExpectedRevision, BeforeJson: before?.ValueJson, BeforeRevision: before?.Revision,
                    ComponentTypeVersion: before?.Type.TypeVersion ?? effect.ComponentType.TypeVersion,
                    BatchEffectIndex: batchEffectIndex);
            }
            case ApplicationEcsEffectType.ContainmentMove:
            {
                var value = await RequireEdges().MoveContainmentAsync(stateSpaceId, effect.EntityId,
                    effect.TargetEntityId, effect.Slot, effect.ExpectedRevision, cancellationToken);
                return new(index, effect.Type, effect.EntityId, "", value.Revision,
                    TargetEntityId: value.ContainerEntityId, BatchEffectIndex: batchEffectIndex);
            }
            case ApplicationEcsEffectType.ContainmentRemove:
                if (!await RequireEdges().RemoveContainmentAsync(stateSpaceId, effect.EntityId,
                        effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The containment is absent.");
                return new(index, effect.Type, effect.EntityId, "", null, effect.ExpectedRevision,
                    BatchEffectIndex: batchEffectIndex);
            case ApplicationEcsEffectType.RelationshipSet:
            {
                var value = await RequireEdges().SetRelationshipAsync(stateSpaceId, effect.EntityId,
                    effect.TargetEntityId, effect.QualifiedRelationshipKind, effect.DataJson,
                    effect.ExpectedRevision, cancellationToken);
                return new(index, effect.Type, effect.EntityId, "", value.Revision,
                    TargetEntityId: value.ToEntityId,
                    QualifiedRelationshipKind: value.QualifiedKind, BatchEffectIndex: batchEffectIndex);
            }
            case ApplicationEcsEffectType.RelationshipRemove:
                if (!await RequireEdges().RemoveRelationshipAsync(stateSpaceId, effect.EntityId,
                        effect.TargetEntityId, effect.QualifiedRelationshipKind,
                        effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The relationship is absent.");
                return new(index, effect.Type, effect.EntityId, "", null, effect.ExpectedRevision,
                    effect.TargetEntityId, effect.QualifiedRelationshipKind,
                    BatchEffectIndex: batchEffectIndex);
            default:
                throw new InvalidOperationException("The effect type was not validated.");
        }
    }

    private async Task VerifyComponentsAsync(
        ApplicationEcsEffectBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.ComponentExpectations.Count == 0) return;
        var rows = await store.GetComponentsAsync(batch.StateSpaceId,
            batch.ComponentExpectations.Select(value => new EcsComponentLocator(
                value.EntityId, value.ComponentType.QualifiedTypeId)).ToArray(), cancellationToken);
        var current = rows.ToDictionary(value => (value.EntityId, value.Type.QualifiedTypeId));
        foreach (var expected in batch.ComponentExpectations)
        {
            current.TryGetValue((expected.EntityId, expected.ComponentType.QualifiedTypeId), out var actual);
            if (expected.Revision == 0 ? actual is not null
                : actual is null || actual.Type != expected.ComponentType || actual.Revision != expected.Revision)
                throw new InvalidOperationException("Component source is stale.");
        }
    }

    private async Task VerifyEntitiesAsync(ApplicationEcsEffectBatch batch, CancellationToken cancellationToken)
    {
        if (batch.EntityExpectations.Count == 0) return;
        var ids = batch.EntityExpectations.Select(value => value.EntityId).ToArray();
        var current = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == batch.StateSpaceId && ids.Contains(value.Id) && value.DeletedAtUtc == null)
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        foreach (var expected in batch.EntityExpectations)
            if (!current.TryGetValue(expected.EntityId, out var actual) || actual.Revision != expected.Revision)
                throw new InvalidOperationException("Entity source is stale.");
    }

    private async Task VerifyRelationshipsAsync(ApplicationEcsEffectBatch batch, CancellationToken cancellationToken)
    {
        if (batch.RelationshipExpectations.Count == 0) return;
        var reader = RequireEdges() as IRelationshipCollectionReader
            ?? throw new InvalidOperationException("The relationship snapshot reader is unavailable.");
        foreach (var group in batch.RelationshipExpectations.GroupBy(value => (value.QualifiedKind, value.Incoming)))
        {
            // Bound each read to exactly the observed size (+ the reader's overflow sentinel).
            // Grouping by kind and direction avoids per-edge reads and unrelated graph scans.
            var expectedCount = group.Sum(value => value.Relationships.Count);
            if (expectedCount > 10_000)
                throw new InvalidOperationException("Relationship snapshot read bound exceeded.");
            IReadOnlyList<EcsRelationshipView> rows;
            try
            {
                rows = await reader.ReadCollectionsAsync(batch.StateSpaceId,
                    group.Select(value => value.AnchorEntityId).ToArray(), [group.Key.QualifiedKind],
                    Math.Max(1, expectedCount), group.Key.Incoming, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException("Relationship collection source is stale.");
            }
            var current = rows.ToDictionary(value => (value.FromEntityId, value.ToEntityId));
            if (rows.Count != expectedCount || group.SelectMany(value => value.Relationships).Any(expected =>
                    !current.TryGetValue((expected.FromEntityId, expected.ToEntityId), out var actual)
                    || actual.Revision != expected.Revision))
                throw new InvalidOperationException("Relationship collection source is stale.");
        }
    }

    private async Task VerifyContainmentsAsync(ApplicationEcsEffectBatch batch, CancellationToken cancellationToken)
    {
        foreach (var expected in batch.ContainmentEdgeExpectations)
        {
            var actual = await RequireEdges().GetContainmentAsync(
                batch.StateSpaceId, expected.ContainedEntityId, cancellationToken);
            if (actual is null
                || actual.ContainerEntityId != expected.ContainerEntityId
                || actual.Slot != expected.Slot
                || actual.Revision != expected.Revision)
                throw new InvalidOperationException("Containment edge is stale.");
        }

        if (batch.ContainmentExpectations.Count > 0)
        {
            var current = await RequireEdges().ListContainmentsAsync(batch.StateSpaceId, cancellationToken);
            foreach (var expected in batch.ContainmentExpectations)
            {
                var actual = current.Where(value => value.ContainerEntityId == expected.ContainerEntityId)
                    .OrderBy(value => value.ContainedEntityId, StringComparer.Ordinal).ToArray();
                if (actual.Length != expected.Contents.Count
                    || actual.Where((value, index) => value.ContainedEntityId != expected.Contents[index].EntityId
                        || value.Slot != expected.Contents[index].Slot || value.Revision != expected.Contents[index].Revision).Any())
                    throw new InvalidOperationException("Containment roster is stale.");
            }
        }
    }

    private static EcsComponentWrite Write(string stateSpaceId, ApplicationEcsEffect effect) =>
        new(stateSpaceId, effect.EntityId, effect.ComponentType!, effect.DataJson, effect.ExpectedRevision);

    private async Task<EcsComponentView?> ComponentBeforeAsync(
        string stateSpaceId,
        ApplicationEcsEffect effect,
        CancellationToken cancellationToken) =>
        await store.GetComponentAsync(stateSpaceId, effect.EntityId,
            effect.ComponentType!.QualifiedTypeId, cancellationToken);

    private static ApplicationEcsEffectReceipt Receipt(
        int index,
        int batchEffectIndex,
        ApplicationEcsEffect effect,
        EcsComponentView? before,
        EcsComponentView after) =>
        new(index, effect.Type, effect.EntityId, after.Type.QualifiedTypeId, after.Revision,
            BeforeJson: before?.ValueJson,
            BeforeRevision: before?.Revision,
            AfterJson: after.ValueJson,
            AfterRevision: after.Revision,
            ComponentTypeVersion: after.Type.TypeVersion,
            BatchEffectIndex: batchEffectIndex);

    private IStateSpaceEdgeStore RequireEdges() =>
        edges ?? throw new InvalidOperationException("The application state-space edge store is unavailable.");

    private sealed class EffectApplicationException(int index, Exception innerException)
        : InvalidOperationException(innerException.Message, innerException)
    {
        public int Index { get; } = index;
    }

    private sealed class ApplicationEcsCommitGuardException(string code) : Exception
    {
        internal string Code { get; } = code;
    }

    private async Task<ApplicationEcsEffectResult> FailedAsync(
        ApplicationEcsEffectBatch? batch,
        bool dryRun,
        string operationId,
        IReadOnlyList<ApplicationEcsEffectProblem> problems,
        CancellationToken cancellationToken)
    {
        var safeBatch = batch ?? new ApplicationEcsEffectBatch { StateSpaceId = "", Effects = [] };
        await RecordAsync(safeBatch, operationId, success: false, dryRun, 0,
            string.Join(" ", problems.Select(x => x.Code)), cancellationToken);
        return new(false, dryRun, operationId, [], problems);
    }

    private async Task<ApplicationEcsEffectResult> FailedSafelyAsync(
        ApplicationEcsEffectBatch? batch,
        bool dryRun,
        string operationId,
        IReadOnlyList<ApplicationEcsEffectProblem> problems,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FailedAsync(batch, dryRun, operationId, problems, cancellationToken);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task RecordAsync(
        ApplicationEcsEffectBatch batch,
        string operationId,
        bool success,
        bool dryRun,
        int count,
        string error,
        CancellationToken cancellationToken)
    {
        var effects = batch.Effects ?? [];
        var subjects = effects.Where(x => x is not null).Select(x => x.EntityId).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var subject = batch.ExecutionIdentity is null
            ? string.Join(',', subjects)
            : batch.ExecutionIdentity.AuditSubject;
        await operations.RecordAsync(
            AuditIdentity,
            success
                ? $"{(dryRun ? "Validated" : "Applied")} {count} application ECS effect(s) in '{batch.StateSpaceId}'."
                : $"Rejected application ECS effects in '{batch.StateSpaceId}'.",
            success,
            intent: batch.Intent ?? string.Empty,
            subject: subject,
            proceduresCited: batch.ProceduresUsed ?? [],
            error: error,
            consumesReadEvidence: success && !dryRun,
            cancellationToken: cancellationToken,
            mechanicId: batch.MechanicId ?? string.Empty,
            mechanicVersion: batch.MechanicVersion,
            seed: batch.Seed,
            projectionJson: batch.ProjectionJson ?? string.Empty,
            id: operationId);
    }

    private async Task<ApplicationEcsEffectResult?> ReplayAsync(
        ApplicationEcsEffectBatch batch,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var identity = batch.ExecutionIdentity!;
        var existing = await operations.GetAsync(identity.OperationId, cancellationToken);
        if (existing is null) return null;
        if (existing.Tool != AuditIdentity || existing.Subject != identity.AuditSubject)
            return new(false, dryRun, identity.OperationId, [],
                [new(-1, "OPERATION_ID_CONFLICT", "The execution operation ID is already bound to another request.")]);
        return existing.Success
            ? new(false, dryRun, identity.OperationId, [], [], Replayed: true)
            : new(false, dryRun, identity.OperationId, [],
                [new(-1, string.IsNullOrWhiteSpace(existing.Error) ? "REPLAYED_FAILURE" : existing.Error,
                    "The same application effect request previously failed.")], Replayed: true);
    }


    private static async Task RollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null) return;
        try { await transaction.RollbackAsync(CancellationToken.None); }
        finally { await transaction.DisposeAsync(); }
    }

    private async Task RollbackAndClearAsync(IDbContextTransaction? transaction)
    {
        try { await RollbackAsync(transaction); }
        finally { db.ChangeTracker.Clear(); }
    }

    private static string Code(Exception exception) => exception switch
    {
        EcsRoleConstraintException constraint => constraint.Code,
        ArgumentException => "VALIDATION_FAILED",
        DbUpdateException => "PERSISTENCE_REJECTED",
        _ when exception.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) => "REFERENCE_UNKNOWN",
        _ when exception.Message.Contains("stale", StringComparison.OrdinalIgnoreCase) => "REVISION_STALE",
        _ => "EFFECT_REJECTED"
    };
}

internal interface IApplicationEcsCommitGuard
{
    Task<ApplicationEcsCommitGuardDecision> EvaluateAsync(
        ApplicationEcsEffectBatch committedBatch,
        CancellationToken cancellationToken = default);
}

internal sealed record ApplicationEcsCommitGuardDecision(bool Allowed, string Code);

internal interface IApplicationEcsGuardedEffectApplier
{
    Task<ApplicationEcsEffectResult> ApplyAuthorizedAsync(
        ApplicationEcsEffectBatch batch,
        IApplicationEcsCommitGuard commitGuard,
        CancellationToken cancellationToken = default);
}
