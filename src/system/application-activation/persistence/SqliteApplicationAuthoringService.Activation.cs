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
            var compatibleUpdate = await new ApplicationCandidateCompatibleUpdateReader(db, applications, activations, evidence,
                targets, new(new BoundedJsonSchemaValidator())).ReadAsync(host, candidate, token);
            var intentMatchUpdate = compatibleUpdate is null
                ? await new ApplicationCandidateIntentMatchUpdateReader(db, applications, activations, evidence)
                    .ReadAsync(candidate, token)
                : null;
            var reviewed = compatibleUpdate is null && intentMatchUpdate is null && reviewedPureUpdates is not null
                ? await reviewedPureUpdates.ReadAsync(host, candidate, token) : null;
            var statefulUpdate = compatibleUpdate is null && intentMatchUpdate is null && reviewed is null
                && statefulRuntime is not null
                ? await new ApplicationCandidateStatefulUpdateReader(db, applications, activations, evidence, targets)
                    .ReadAsync(host, candidate, token) : null;
            if (compatibleUpdate is null && intentMatchUpdate is null && reviewed is null && statefulUpdate is null)
                return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_COMPATIBILITY_UNAVAILABLE",
                    "This publication path requires a compatible body update, exact intent metadata update, independently reviewed closed pure mechanic candidate, or validated existing atomic body update.");
            var definitions = compatibleUpdate is not null
                ? compatibleUpdate.Closure.Definitions.Select(value => value.Plan.Definition).ToArray()
                : intentMatchUpdate is not null ? new[] { intentMatchUpdate.Successor }
                : reviewed is not null ? reviewed.Closure.Definitions.Select(value => value.Plan.Definition).ToArray()
                : new[] { statefulUpdate!.Definition };
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
            if (validation is null || checkedOperation is null)
                return Failed("APPLICATION_CANDIDATE_VALIDATION_REQUIRED");
            ApplicationCandidateStatefulRuntimeReport? statefulReport = null;
            if (intentMatchUpdate is not null)
            {
                if (!ApplicationCandidateOperationProof.ValidationMatches(checkedOperation, validation, candidate, definitions)
                    || !await ApplicationCandidateIntentMatchUpdateValidation.VerifyAsync(db, intentMatchUpdate, validation, token))
                    return Failed("APPLICATION_CANDIDATE_VALIDATION_REQUIRED");
            }
            else if (statefulUpdate is not null)
            {
                if (!ApplicationCandidateOperationProof.TryReadStatefulRuntimeReport(checkedOperation, validation,
                        candidate, definitions, out statefulReport) || statefulReport is null
                    || !await ApplicationCandidateStatefulUpdateValidation.VerifyAsync(
                        db, statefulUpdate, statefulReport, validation, token)
                    || statefulRuntime is null
                    || !await statefulRuntime.CurrentAsync(statefulUpdate, statefulReport, host, token))
                    return Failed("APPLICATION_CANDIDATE_VALIDATION_REQUIRED");
                var activationAuthority = await grants.EvaluateAsync(host,
                    new(StandingGrantCapability.Activate, StandingGrantScope.Application,
                        selected, statefulReport.EffectKinds), token);
                if (!activationAuthority.Allowed) return Failed(activationAuthority.Code);
            }
            else
            {
                var closure = compatibleUpdate?.Closure ?? reviewed!.Closure;
                if (!ApplicationCandidateOperationProof.TryReadRuntimeReport(checkedOperation, validation,
                        candidate, definitions, out var report)
                    || report?.Status != ApplicationCandidateRuntimeStatus.Completed
                    || report.SelectionEvidenceFingerprint != closure.EvidenceFingerprint
                    || report.RuntimePolicyVersion != ApplicationCandidateRuntimeValidator.RuntimePolicyVersion
                    || report.RuntimePolicyFingerprint != ApplicationCandidateRuntimeValidator.RuntimePolicyFingerprint
                    || compatibleUpdate is not null && !await ApplicationCandidateCompatibleUpdateValidation.VerifyAsync(
                        db, compatibleUpdate, validation, token)
                    || reviewed is not null && !ApplicationCandidateReviewedPureUpdateValidation.Matches(report, validation, reviewed))
                    return Failed("APPLICATION_CANDIDATE_VALIDATION_REQUIRED");
            }
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
            var basis = compatibleUpdate?.Basis ?? intentMatchUpdate?.Basis ?? reviewed?.Basis ?? statefulUpdate!.Basis;
            if (activations.Current(candidate.ApplicationId)?.ActivationFingerprint != basis.ActivationFingerprint)
                return Failed("APPLICATION_CANDIDATE_ACTIVE_STALE");
            var operation = await operations.RecordAsync("application-candidate-activation",
                compatibleUpdate is not null
                    ? "Published validated runtime mechanic body updates."
                    : intentMatchUpdate is not null ? "Published a validated intent match metadata update."
                    : reviewed is not null ? "Published a runtime-tested and independently reviewed pure mechanic candidate."
                    : "Published a dry-run-validated existing atomic mechanic body update.",
                true, subject: candidate.ApplicationId.Value, projectionJson: canonical, guardEvidenceJson: "{}", id: operationId,
                cancellationToken: token);
            var activation = compatibleUpdate is not null
                ? await publisher.StageCompatibleUpdateAsync(compatibleUpdate, validation, operation.Id, token)
                : intentMatchUpdate is not null
                    ? await publisher.StageIntentMatchUpdateAsync(intentMatchUpdate, validation, operation.Id, token)
                    : reviewed is not null
                        ? await publisher.StageReviewedPureUpdateAsync(reviewed, validation, operation.Id, token)
                        : await publisher.StageStatefulUpdateAsync(statefulUpdate!, statefulReport!, validation,
                            operation.Id, token);
            if (compatibleUpdate is not null)
                _ = await stateSpaceRebinder.StageAsync(compatibleUpdate, activation, token);
            else if (intentMatchUpdate is not null)
                _ = await stateSpaceRebinder.StageAsync(intentMatchUpdate, activation, token);
            else if (reviewed is not null)
                _ = await stateSpaceRebinder.StageAsync(reviewed!, activation, token);
            else
                _ = await stateSpaceRebinder.StageAsync(statefulUpdate!, activation, token);
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
