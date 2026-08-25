using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class InteractionReceiptStore(
    DantesRoleplayDbContext db,
    IInteractionAuthorizationPolicy authorizationPolicy) : IInteractionReceiptStore, IInteractionExecutionAuthorityStore
{
    public async Task<InteractionReceiptWriteResult> AppendResolutionAsync(InteractionResolutionReceiptDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var envelope = draft.Envelope;
        var existing = await db.InteractionResolutionReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.PrincipalReference == envelope.Host.Principal.PrincipalId &&
            row.ApplicationId == envelope.Host.ApplicationRevision.ApplicationId.Value &&
            row.StateSpaceId == envelope.Host.StateSpaceId &&
            row.IdempotencyKey == envelope.Intent.IdempotencyKey, cancellationToken);
        if (existing is not null) return ResolutionReplay(existing, envelope.Fingerprint);

        var row = Resolution(draft);
        db.InteractionResolutionReceipts.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return InteractionReceiptWriteResult.Appended(Projection(row));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.InteractionResolutionReceipts.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.PrincipalReference == envelope.Host.Principal.PrincipalId &&
                candidate.ApplicationId == envelope.Host.ApplicationRevision.ApplicationId.Value &&
                candidate.StateSpaceId == envelope.Host.StateSpaceId &&
                candidate.IdempotencyKey == envelope.Intent.IdempotencyKey, cancellationToken);
            if (existing is not null) return ResolutionReplay(existing, envelope.Fingerprint);
            throw;
        }
    }

    public async Task<InteractionReceiptWriteResult> AppendExecutionAsync(InteractionExecutionReceiptDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var consent = draft.Consent;
        var parent = await db.InteractionResolutionReceipts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == consent.ResolutionReceiptId, cancellationToken)
            ?? throw new InteractionContractException("RESOLUTION_RECEIPT_NOT_FOUND", "The resolution receipt does not exist.");
        if (parent.PrincipalReference != consent.PrincipalReference || parent.ApplicationId != consent.ApplicationId.Value ||
            parent.StateSpaceId != consent.StateSpaceId || parent.ProposalFingerprint != consent.ProposalFingerprint)
            throw new InteractionContractException("RESOLUTION_RECEIPT_SCOPE_MISMATCH", "The execution consent does not match the resolution receipt.");

        var existing = await db.InteractionExecutionReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.PrincipalReference == consent.PrincipalReference && row.ApplicationId == consent.ApplicationId.Value &&
            row.StateSpaceId == consent.StateSpaceId && row.ResolutionReceiptId == consent.ResolutionReceiptId &&
            row.IdempotencyKey == consent.IdempotencyKey, cancellationToken);
        if (existing is not null) return ExecutionReplay(existing, draft);

        if (draft.Steps.Where(step => step.OperationId is not null).Any())
        {
            var ids = draft.Steps.Where(step => step.OperationId is not null).Select(step => step.OperationId!).ToArray();
            var found = await db.Operations.AsNoTracking().Where(operation => ids.Contains(operation.Id)).Select(operation => operation.Id).ToArrayAsync(cancellationToken);
            if (found.Length != ids.Length) throw new InteractionContractException("EXECUTION_OPERATION_NOT_FOUND", "An execution receipt references an unknown operation.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = new InteractionExecutionReceipt
            {
                Id = InteractionReceiptIds.New(), ResolutionReceiptId = parent.Id,
                PrincipalReference = consent.PrincipalReference, ApplicationId = consent.ApplicationId.Value,
                StateSpaceId = consent.StateSpaceId, IdempotencyKey = consent.IdempotencyKey,
                ExecutionRequestFingerprint = draft.ExecutionRequestFingerprint, ProposalFingerprint = consent.ProposalFingerprint,
                Disposition = ExecutionDisposition(draft.Disposition), SafeSummary = draft.SafeSummary,
                EvidenceJson = InteractionReceiptSafety.SerializeEvidence(draft.Evidence), CreatedAtUtc = DateTime.UtcNow
            };
            foreach (var step in draft.Steps)
                row.Steps.Add(new InteractionExecutionReceiptStep { ExecutionReceiptId = row.Id, Ordinal = step.Ordinal, ProposalStepId = step.ProposalStepId, Disposition = StepDisposition(step.Disposition), OperationId = step.OperationId });
            db.InteractionExecutionReceipts.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return InteractionReceiptWriteResult.Appended(Projection(row));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var concurrent = await db.InteractionExecutionReceipts.AsNoTracking().Include(row => row.Steps).SingleOrDefaultAsync(row =>
                row.PrincipalReference == consent.PrincipalReference && row.ApplicationId == consent.ApplicationId.Value &&
                row.StateSpaceId == consent.StateSpaceId && row.ResolutionReceiptId == consent.ResolutionReceiptId &&
                row.IdempotencyKey == consent.IdempotencyKey, cancellationToken);
            if (concurrent is not null) return ExecutionReplay(concurrent, draft);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<InteractionReceiptProjection?> GetAsync(InteractionAuthorizationRequest authorizationRequest, string receiptId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizationRequest);
        receiptId = InteractionReceiptIds.Require(receiptId, nameof(receiptId));
        var authorization = authorizationPolicy.Evaluate(authorizationRequest);
        if (!authorization.Allowed || authorization.Capability != InteractionCapability.ReadReceipt ||
            authorizationRequest.Capability != InteractionCapability.ReadReceipt ||
            authorization.PrincipalReference != authorizationRequest.Principal.PrincipalId ||
            authorization.ApplicationId != authorizationRequest.ApplicationId ||
            authorization.StateSpaceId != authorizationRequest.StateSpaceId)
            return null;
        var resolution = await db.InteractionResolutionReceipts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == receiptId &&
            row.PrincipalReference == authorization.PrincipalReference && row.ApplicationId == authorization.ApplicationId.Value &&
            row.StateSpaceId == authorization.StateSpaceId, cancellationToken);
        if (resolution is not null) return Projection(resolution);
        var execution = await db.InteractionExecutionReceipts.AsNoTracking().Include(row => row.Steps).SingleOrDefaultAsync(row => row.Id == receiptId &&
            row.PrincipalReference == authorization.PrincipalReference && row.ApplicationId == authorization.ApplicationId.Value &&
            row.StateSpaceId == authorization.StateSpaceId, cancellationToken);
        return execution is null ? null : Projection(execution);
    }

    async Task<InteractionResolutionExecutionAuthority?> IInteractionExecutionAuthorityStore.GetAsync(
        InteractionAuthorizationRequest authorizationRequest,
        string resolutionReceiptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationRequest);
        resolutionReceiptId = InteractionReceiptIds.Require(resolutionReceiptId, nameof(resolutionReceiptId));
        var authorization = authorizationPolicy.Evaluate(authorizationRequest);
        if (!authorization.Allowed || authorization.Capability != InteractionCapability.Execute
            || authorizationRequest.Capability != InteractionCapability.Execute
            || authorization.PrincipalReference != authorizationRequest.Principal.PrincipalId
            || authorization.ApplicationId != authorizationRequest.ApplicationId
            || authorization.StateSpaceId != authorizationRequest.StateSpaceId)
            return null;
        var row = await db.InteractionResolutionReceipts.AsNoTracking().SingleOrDefaultAsync(value =>
            value.Id == resolutionReceiptId
            && value.PrincipalReference == authorization.PrincipalReference
            && value.ApplicationId == authorization.ApplicationId.Value
            && value.StateSpaceId == authorization.StateSpaceId, cancellationToken);
        if (row is null || row.Status != "resolved" || string.IsNullOrWhiteSpace(row.ProposalFingerprint))
            return null;
        return new(row.Id, row.PrincipalReference, Applications.ApplicationIdentifier.Parse(row.ApplicationId),
            row.ApplicationRevision, row.ApplicationFingerprint, row.StateSpaceId, row.SessionContextId,
            row.StateRevision, row.EffectiveSetFingerprint, row.RoleProfile, row.ConversationId,
            row.ParentDelegationId, row.AuthorizationEvidenceReference, row.IdempotencyKey,
            row.EnvelopeFingerprint, row.Status, row.ProposalFingerprint,
            row.RecipeId is null ? null : new(row.RecipeId, row.RecipeVersion!.Value, row.RecipeTemplateFingerprint!));
    }

    private static InteractionResolutionReceipt Resolution(InteractionResolutionReceiptDraft draft)
    {
        var host = draft.Envelope.Host;
        return new()
        {
            Id = InteractionReceiptIds.New(), PrincipalReference = host.Principal.PrincipalId,
            ApplicationId = host.ApplicationRevision.ApplicationId.Value, ApplicationRevision = host.ApplicationRevision.Revision,
            ApplicationFingerprint = host.ApplicationRevision.Fingerprint, StateSpaceId = host.StateSpaceId,
            SessionContextId = host.SessionContextId, StateRevision = host.StateRevision,
            EffectiveSetFingerprint = host.EffectiveSetFingerprint, RoleProfile = host.RoleProfile.StableKey,
            ConversationId = host.ConversationId, ParentDelegationId = host.ParentDelegationId,
            AuthorizationEvidenceReference = host.Authorization.EvidenceReference, IdempotencyKey = draft.Envelope.Intent.IdempotencyKey,
            EnvelopeFingerprint = draft.Envelope.Fingerprint, QueryFingerprint = draft.QueryFingerprint,
            Status = InteractionResolutionStatusNames.Get(draft.Result.Status), Code = draft.Result.Code,
            ProposalFingerprint = draft.Result.Proposal?.Fingerprint, SafeSummary = draft.Result.SafeSummary,
            EvidenceJson = InteractionReceiptSafety.SerializeEvidence(draft.Result.Evidence), CreatedAtUtc = DateTime.UtcNow,
            RecipeId = draft.Result.RecipeReference?.Id,
            RecipeVersion = draft.Result.RecipeReference?.Version,
            RecipeTemplateFingerprint = draft.Result.RecipeReference?.TemplateFingerprint
        };
    }

    private static InteractionReceiptWriteResult ResolutionReplay(InteractionResolutionReceipt row, string fingerprint) =>
        InteractionReplay.Decide(row.IdempotencyKey, row.EnvelopeFingerprint, row.IdempotencyKey, fingerprint) == InteractionReplayDisposition.Replay
            ? InteractionReceiptWriteResult.Replay(Projection(row)) : InteractionReceiptWriteResult.Conflict();

    private static InteractionReceiptWriteResult ExecutionReplay(InteractionExecutionReceipt row, InteractionExecutionReceiptDraft draft) =>
        row.ExecutionRequestFingerprint == draft.ExecutionRequestFingerprint && row.ProposalFingerprint == draft.Consent.ProposalFingerprint
            ? InteractionReceiptWriteResult.Replay(Projection(row)) : InteractionReceiptWriteResult.Conflict();

    private static InteractionReceiptProjection Projection(InteractionResolutionReceipt row) => new(
        row.Id, "resolution", row.PrincipalReference, Applications.ApplicationIdentifier.Parse(row.ApplicationId), row.StateSpaceId,
        row.IdempotencyKey, row.EnvelopeFingerprint, row.Status, row.Code, row.ProposalFingerprint, row.SafeSummary,
        InteractionReceiptSafety.DeserializeEvidence(row.EvidenceJson), row.CreatedAtUtc,
        RecipeReference: row.RecipeId is null ? null : new(row.RecipeId, row.RecipeVersion!.Value, row.RecipeTemplateFingerprint!));

    private static InteractionReceiptProjection Projection(InteractionExecutionReceipt row) => new(
        row.Id, "execution", row.PrincipalReference, Applications.ApplicationIdentifier.Parse(row.ApplicationId), row.StateSpaceId,
        row.IdempotencyKey, row.ExecutionRequestFingerprint, row.Disposition, "INTERACTION_EXECUTION_" + row.Disposition.ToUpperInvariant(),
        row.ProposalFingerprint, row.SafeSummary, InteractionReceiptSafety.DeserializeEvidence(row.EvidenceJson), row.CreatedAtUtc,
        row.ResolutionReceiptId, row.Steps.OrderBy(step => step.Ordinal).Select(step => new InteractionExecutionStepReceiptProjection(step.Ordinal, step.ProposalStepId, step.Disposition, step.OperationId)).ToArray());

    private static string ExecutionDisposition(InteractionExecutionReceiptDisposition value) => value switch
    {
        InteractionExecutionReceiptDisposition.Succeeded => "succeeded", InteractionExecutionReceiptDisposition.Failed => "failed",
        InteractionExecutionReceiptDisposition.Partial => "partial", InteractionExecutionReceiptDisposition.Skipped => "skipped",
        InteractionExecutionReceiptDisposition.Stale => "stale", InteractionExecutionReceiptDisposition.Unauthorized => "unauthorized",
        InteractionExecutionReceiptDisposition.Cancelled => "cancelled", InteractionExecutionReceiptDisposition.TimedOut => "timed-out",
        _ => throw new InteractionContractException("INVALID_EXECUTION_RECEIPT_DISPOSITION", "The execution receipt disposition is not supported.")
    };

    private static string StepDisposition(InteractionExecutionStepDisposition value) => value switch
    {
        InteractionExecutionStepDisposition.Succeeded => "succeeded", InteractionExecutionStepDisposition.Replayed => "succeeded",
        InteractionExecutionStepDisposition.Failed => "failed",
        InteractionExecutionStepDisposition.Skipped => "skipped",
        _ => throw new InteractionContractException("INVALID_EXECUTION_STEP_DISPOSITION", "The execution step disposition is not supported.")
    };
}
