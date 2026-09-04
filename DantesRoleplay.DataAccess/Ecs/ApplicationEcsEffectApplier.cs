using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.EcsEffects;

/// <summary>Atomic, ruleset-neutral mutation boundary for one application state space.</summary>
public sealed class ApplicationEcsEffectApplier(
    DantesRoleplayDbContext db,
    IEntityComponentStore store,
    IStateSpaceRegistry stateSpaces,
    IOperationLog operations,
    IStateSpaceEdgeStore? edges = null,
    IEnumerable<IApplicationEcsTransactionParticipant>? transactionParticipants = null,
    IEcsRoleConstraintValidator? roleConstraints = null) : IApplicationEcsEffectApplier
{
    private const string AuditIdentity = ApplicationEcsExecutionIdentity.AuditTool;

    public async Task<ApplicationEcsEffectResult> ApplyAsync(
        ApplicationEcsEffectBatch batch,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
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
            await VerifyContainmentsAsync(batch, cancellationToken);
            for (var index = 0; index < batch.Effects.Count; index++)
            {
                currentIndex = index;
                receipts.Add(await ApplyOneAsync(batch.StateSpaceId, batch.Effects[index], index, cancellationToken));
            }

            if (roleConstraints is not null)
                await roleConstraints.ValidateStateSpaceAsync(batch.StateSpaceId, cancellationToken);

            if (dryRun)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                transaction = null;
                db.ChangeTracker.Clear();
                await RecordAsync(batch, operationId, success: true, dryRun: true, receipts.Count, "", CancellationToken.None);
                return new(false, true, operationId, receipts.AsReadOnly(), []);
            }

            foreach (var participant in transactionParticipants ?? [])
                await participant.StageAsync(batch, receipts.AsReadOnly(), operationId, cancellationToken);

            await RecordAsync(batch, operationId, success: true, dryRun: false, receipts.Count, "", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, false, operationId, receipts.AsReadOnly(), []);
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
            return await FailedSafelyAsync(batch, dryRun, operationId,
                [new(currentIndex, Code(exception), exception.Message)], CancellationToken.None);
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

    private async Task<ApplicationEcsEffectReceipt> ApplyOneAsync(
        string stateSpaceId,
        ApplicationEcsEffect effect,
        int index,
        CancellationToken cancellationToken)
    {
        switch (effect.Type)
        {
            case ApplicationEcsEffectType.EntityCreate:
            {
                var entity = await store.CreateEntityAsync(stateSpaceId, effect.EntityId, effect.Name, cancellationToken);
                return new(index, effect.Type, entity.EntityId, "", entity.Revision);
            }
            case ApplicationEcsEffectType.EntityDelete:
                if (!await store.DeleteEntityAsync(stateSpaceId, effect.EntityId, effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The entity is unknown or already deleted.");
                return new(index, effect.Type, effect.EntityId, "", effect.ExpectedRevision + 1);
            case ApplicationEcsEffectType.ComponentAdd:
                return Receipt(index, effect, await store.AddComponentAsync(Write(stateSpaceId, effect), cancellationToken));
            case ApplicationEcsEffectType.ComponentSet:
                return Receipt(index, effect, await store.SetComponentAsync(Write(stateSpaceId, effect), cancellationToken));
            case ApplicationEcsEffectType.ClockAdvance:
                return Receipt(index, effect, await store.SetComponentAsync(Write(stateSpaceId, effect), cancellationToken));
            case ApplicationEcsEffectType.ComponentMerge:
                return Receipt(index, effect, await store.MergeComponentAsync(Write(stateSpaceId, effect), cancellationToken));
            case ApplicationEcsEffectType.ComponentRemove:
                if (!await store.RemoveComponentAsync(stateSpaceId, effect.EntityId, effect.ComponentType!, effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The component is absent.");
                return new(index, effect.Type, effect.EntityId, effect.ComponentType!.QualifiedTypeId, null, effect.ExpectedRevision);
            case ApplicationEcsEffectType.ContainmentMove:
            {
                var value = await RequireEdges().MoveContainmentAsync(stateSpaceId, effect.EntityId,
                    effect.TargetEntityId, effect.Slot, effect.ExpectedRevision, cancellationToken);
                return new(index, effect.Type, effect.EntityId, "", value.Revision,
                    TargetEntityId: value.ContainerEntityId);
            }
            case ApplicationEcsEffectType.ContainmentRemove:
                if (!await RequireEdges().RemoveContainmentAsync(stateSpaceId, effect.EntityId,
                        effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The containment is absent.");
                return new(index, effect.Type, effect.EntityId, "", null, effect.ExpectedRevision);
            case ApplicationEcsEffectType.RelationshipSet:
            {
                var value = await RequireEdges().SetRelationshipAsync(stateSpaceId, effect.EntityId,
                    effect.TargetEntityId, effect.QualifiedRelationshipKind, effect.DataJson,
                    effect.ExpectedRevision, cancellationToken);
                return new(index, effect.Type, effect.EntityId, "", value.Revision,
                    TargetEntityId: value.ToEntityId,
                    QualifiedRelationshipKind: value.QualifiedKind);
            }
            case ApplicationEcsEffectType.RelationshipRemove:
                if (!await RequireEdges().RemoveRelationshipAsync(stateSpaceId, effect.EntityId,
                        effect.TargetEntityId, effect.QualifiedRelationshipKind,
                        effect.ExpectedRevision, cancellationToken))
                    throw new InvalidOperationException("The relationship is absent.");
                return new(index, effect.Type, effect.EntityId, "", null, effect.ExpectedRevision,
                    effect.TargetEntityId, effect.QualifiedRelationshipKind);
            default:
                throw new InvalidOperationException("The effect type was not validated.");
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

    private static ApplicationEcsEffectReceipt Receipt(int index, ApplicationEcsEffect effect, EcsComponentView value) =>
        new(index, effect.Type, effect.EntityId, value.Type.QualifiedTypeId, value.Revision);

    private IStateSpaceEdgeStore RequireEdges() =>
        edges ?? throw new InvalidOperationException("The application state-space edge store is unavailable.");

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
