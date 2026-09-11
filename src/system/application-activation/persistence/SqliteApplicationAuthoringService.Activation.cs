using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

public sealed partial class SqliteApplicationAuthoringService
{
    private async Task<InteractionInvocationResult> ActivateCompatibleUpdateAsync(InteractionInvocationHost host,
        ApplicationCandidateActivationRequest request, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Failed("APPLICATION_CANDIDATE_CONTEXT_HAS_PENDING_WRITES");
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        var candidate = request?.Candidate;
        if (candidate is null || candidate.ApplicationId != host.ApplicationRevision.ApplicationId
            || !CandidateId(candidate.CandidateId) || candidate.Revision < 1 || !Hash(candidate.ContentFingerprint)
            || request!.ValidationOperationId is null || !CandidateId(request.ValidationOperationId)) return Failed("INVALID_PAYLOAD");
        if (activations is not ApplicationActivationService publisher)
            return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_ACTIVATE_UNAVAILABLE", "The activation owner is unavailable.");
        var opened = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
        if (remaining.TotalMilliseconds <= int.MaxValue) deadline.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        var token = deadline.Token;
        try
        {
            var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            { principal = host.Principal.PrincipalId, host.Principal.AuthenticationMethod, host.CommandId,
                host.GrantReference, candidate, request.ValidationOperationId }));
            var commandFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-activate-command/v1", canonical);
            var operationId = Id(host, candidate.ApplicationId, "activate");
            await db.Database.OpenConnectionAsync(token); opened = true;
            await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, token);
            var registered = applications.Get(candidate.ApplicationId);
            if (registered is null || registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Failed("APPLICATION_CANDIDATE_APPLICATION_STALE");
            var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == operationId, token);
            if (prior is not null && (prior.Tool != "application-candidate-activation" || !prior.Success
                    || prior.Subject != candidate.ApplicationId.Value || prior.ProjectionJson != canonical))
                return Failed("APPLICATION_CANDIDATE_COMMAND_CONFLICT");
            var update = await new ApplicationCandidateCompatibleUpdateReader(db, applications, activations, evidence,
                targets, new(new BoundedJsonSchemaValidator())).ReadAsync(host, candidate, token);
            if (update is null)
                return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_COMPATIBILITY_UNAVAILABLE",
                    "This publication path requires existing pure mechanic bodies with unchanged contracts.");
            var definitions = update.Closure.Definitions.Select(value => value.Plan.Definition).ToArray();
            var selected = new List<StandingGrantDefinitionTarget>();
            foreach (var definition in definitions)
            {
                var target = await targets.ResolveCandidateReferenceAsync(host, candidate, definition, token);
                if (target.Status != StandingGrantTargetResolutionStatus.Available || target.Target is null) return Failed(target.Code);
                selected.Add(target.Target);
            }
            foreach (var capability in new[] { StandingGrantCapability.Read, StandingGrantCapability.Activate })
            {
                var permission = await grants.EvaluateAsync(host, new(capability, StandingGrantScope.Application, selected, []), token);
                if (!permission.Allowed) return Failed(permission.Code);
            }
            var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.OperationId == request.ValidationOperationId, token);
            var checkedOperation = await db.Operations.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == request.ValidationOperationId, token);
            if (validation is null || checkedOperation is null
                || !ApplicationCandidateOperationProof.TryReadRuntimeReport(checkedOperation, validation, candidate, definitions, out var report)
                || report?.Status != ApplicationCandidateRuntimeStatus.Completed
                || report.SelectionEvidenceFingerprint != update.Closure.EvidenceFingerprint
                || report.RuntimePolicyVersion != ApplicationCandidateRuntimeValidator.RuntimePolicyVersion
                || report.RuntimePolicyFingerprint != ApplicationCandidateRuntimeValidator.RuntimePolicyFingerprint
                || !await ApplicationCandidateCompatibleUpdateValidation.VerifyAsync(db, update, validation, token))
                return Failed("APPLICATION_CANDIDATE_VALIDATION_REQUIRED");
            if (prior is not null)
            {
                var publication = await db.Set<ApplicationCandidatePublicationRecord>().AsNoTracking()
                    .SingleOrDefaultAsync(value => value.ActivationOperationId == operationId, token);
                var receipt = await db.Set<ApplicationActivationReceiptRecord>().AsNoTracking()
                    .SingleOrDefaultAsync(value => value.OperationId == operationId, token);
                var activated = receipt is null ? null : activations.ReadRevision(candidate.ApplicationId, receipt.ActivationRevision);
                if (publication is null || publication.ApplicationId != candidate.ApplicationId.Value
                    || publication.CandidateId != candidate.CandidateId || publication.Revision != candidate.Revision
                    || publication.ValidationOperationId != validation.OperationId || receipt?.Outcome != "activated"
                    || receipt.ApplicationId != candidate.ApplicationId.Value
                    || activated is null || activated.ActivatedByOperationId != operationId
                    || receipt.RequestFingerprint != activated.ActivationFingerprint
                    || prior.GuardEvidenceJson != ActivationGuard(candidate, validation.OperationId, activated.ActivationFingerprint))
                    return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", "Publication evidence cannot be reconciled.");
                await transaction.CommitAsync(token);
                return Receipt(operationId, commandFingerprint);
            }
            if (activations.Current(candidate.ApplicationId)?.ActivationFingerprint != update.Basis.ActivationFingerprint)
                return Failed("APPLICATION_CANDIDATE_ACTIVE_STALE");
            var operation = await operations.RecordAsync("application-candidate-activation", "Published validated runtime mechanic body updates.",
                true, subject: candidate.ApplicationId.Value, projectionJson: canonical, guardEvidenceJson: "{}", id: operationId,
                cancellationToken: token);
            var activation = await publisher.StageCompatibleUpdateAsync(update, validation, operation.Id, token);
            operation.GuardEvidenceJson = ActivationGuard(candidate, validation.OperationId, activation.ActivationFingerprint);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return Receipt(operationId, commandFingerprint);
        }
        catch (ApplicationActivationException exception) { return Failed(exception.Code); }
        catch (InteractionContractException exception) { return Failed(exception.Code); }
        catch (OperationCanceledException)
        { return InteractionInvocationResult.Cancelled("APPLICATION_CANDIDATE_ACTIVATE_CANCELLED", "Publication was cancelled; reconcile the command before retrying."); }
        catch (Exception)
        { return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_ACTIVATE_UNAVAILABLE", "Candidate publication is unavailable."); }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray()) entry.State = EntityState.Detached;
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    private static string ActivationGuard(ApplicationCandidateReference candidate, string validationOperationId,
        string activationFingerprint) => InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        { candidate, validationOperationId, activationFingerprint }));
}
