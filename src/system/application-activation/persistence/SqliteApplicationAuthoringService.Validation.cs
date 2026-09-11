using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

public sealed partial class SqliteApplicationAuthoringService
{
    public async Task<InteractionInvocationResult> ValidateAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Failed("APPLICATION_CANDIDATE_CONTEXT_HAS_PENDING_WRITES");
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        if (candidate is null || candidate.ApplicationId is null || candidate.ApplicationId != host.ApplicationRevision.ApplicationId
            || candidate.Revision < 1 || candidate.CandidateId is not { } candidateId || !CandidateId(candidateId)
            || candidate.ContentFingerprint is not { } candidateFingerprint || !Hash(candidateFingerprint))
            return Failed("INVALID_PAYLOAD");
        var opened = false;
        try
        {
            var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                principal = host.Principal.PrincipalId, applicationId = candidate.ApplicationId.Value, host.CommandId, candidate
            }));
            var commandFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-validation/v1", canonical);
            var operationId = Id(host, candidate.ApplicationId, "validation");
            await db.Database.OpenConnectionAsync(cancellationToken); opened = true;
            await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var registered = applications.Get(candidate.ApplicationId);
            if (registered is null || registered.Revision != host.ApplicationRevision.Revision || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications)) return Failed("APPLICATION_CANDIDATE_APPLICATION_STALE");
            var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == operationId, cancellationToken);
            if (prior is not null && (prior.Tool != "application-candidate-validation" || !prior.Success || prior.ProjectionJson != canonical))
                return Failed("APPLICATION_CANDIDATE_COMMAND_CONFLICT");
            var readback = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(candidate.ApplicationId,
                candidate.CandidateId, candidate.Revision, cancellationToken);
            if (readback is null || readback.RevisionRow.ContentFingerprint != candidate.ContentFingerprint)
                return Failed("APPLICATION_CANDIDATE_NOT_FOUND");
            var basis = await BaseAsync(candidate.ApplicationId, readback.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
            var current = activations.Current(candidate.ApplicationId);
            if (prior is null && !string.Equals(current?.ActivationFingerprint, readback.RevisionRow.ExpectedActiveFingerprint, StringComparison.Ordinal))
                return Failed("APPLICATION_CANDIDATE_ACTIVE_STALE");
            var changed = ApplicationCandidateDocumentSelection.ChangedPaths(readback.Documents, basis);
            var changedDocuments = await ReadChangedAsync(readback, changed, cancellationToken);
            var targetsToValidate = new List<StandingGrantDefinitionTarget>();
            foreach (var definition in Definitions(candidate.ApplicationId, changedDocuments, changed))
            {
                var resolved = await targets.ResolveCandidateReferenceAsync(host, candidate, definition, cancellationToken);
                if (resolved.Status == StandingGrantTargetResolutionStatus.Unavailable) return InteractionInvocationResult.Unavailable(resolved.Code, "Candidate definition ownership is unavailable.");
                if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is null) return Failed(resolved.Code);
                targetsToValidate.Add(resolved.Target);
            }
            var authority = await grants.EvaluateAsync(host, new(StandingGrantCapability.Validate, StandingGrantScope.Application, targetsToValidate, []), cancellationToken);
            if (!authority.Allowed) return authority.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal)
                ? InteractionInvocationResult.Unavailable(authority.Code, "Validation authority is unavailable.") : Failed(authority.Code);
            if (prior is not null)
            {
                var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
                    value.OperationId == operationId && value.ApplicationId == candidate.ApplicationId.Value
                    && value.CandidateId == candidate.CandidateId && value.Revision == candidate.Revision
                    && value.CandidateFingerprint == candidate.ContentFingerprint && value.CanonicalCommandFingerprint == commandFingerprint,
                    cancellationToken);
                if (validation is null || !ValidationEvidenceMatches(validation, prior.GuardEvidenceJson))
                    return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", "The stored validation receipt cannot be reconciled.");
                await transaction.CommitAsync(cancellationToken);
                return Receipt(operationId, commandFingerprint);
            }
            var operation = await operations.RecordAsync("application-candidate-validation", "Candidate validation is unavailable pending dependency preparation.",
                true, subject: candidate.ApplicationId.Value, projectionJson: canonical,
                guardEvidenceJson: InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(authority.Evidence)), id: operationId,
                cancellationToken: cancellationToken);
            var diagnostics = new[]
            {
                new ApplicationCandidateDiagnostic("DEPENDENCY_EXTRACTION_UNAVAILABLE", candidate.CandidateId, "Exact dependency extraction is unavailable."),
                new ApplicationCandidateDiagnostic("RUNTIME_PREPARATION_UNAVAILABLE", candidate.CandidateId, "Runtime preparation and sample validation are unavailable."),
                new ApplicationCandidateDiagnostic("REUSE_REVIEW_UNAVAILABLE", candidate.CandidateId, "Reuse review is unavailable.")
            };
            var validationRow = new ApplicationCandidateValidationRecord
            {
                OperationId = operation.Id, ApplicationId = candidate.ApplicationId.Value, CandidateId = candidate.CandidateId,
                Revision = candidate.Revision, CandidateFingerprint = candidate.ContentFingerprint, GrantReference = host.GrantReference,
                ExpectedActiveFingerprint = readback.RevisionRow.ExpectedActiveFingerprint,
                DependencyFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-dependencies-incomplete/v1",
                    InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { candidate.ApplicationId, candidate.CandidateId, candidate.Revision, candidate.ContentFingerprint }))),
                PreparationVersion = null, ManualPacketResultFingerprint = null, CanonicalCommandFingerprint = commandFingerprint,
                DependenciesJson = "[]", DependenciesComplete = false, DependencyEvidenceReference = null,
                PreparedEvidenceReference = null, ReuseEvidenceReference = null, Outcome = "unavailable",
                DiagnosticsJson = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(diagnostics)), AlternativesJson = "[]"
            };
            db.Add(validationRow);
            operation.GuardEvidenceJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                authorization = authority.Evidence,
                validationFingerprint = ValidationFingerprint(validationRow)
            }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Receipt(operationId, commandFingerprint);
        }
        catch (ApplicationActivationException exception) when (exception.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal))
        { return InteractionInvocationResult.Unavailable(exception.Code, "Candidate validation is unavailable."); }
        catch (ApplicationActivationException exception) { return Failed(exception.Code); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_VALIDATE_UNAVAILABLE", "Candidate validation is unavailable."); }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray()) entry.State = EntityState.Detached;
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    internal static bool ValidationEvidenceMatches(ApplicationCandidateValidationRecord row, string? operationGuardJson)
    {
        if (string.IsNullOrWhiteSpace(operationGuardJson)) return false;
        try
        {
            using var guard = JsonDocument.Parse(operationGuardJson);
            return guard.RootElement.ValueKind == JsonValueKind.Object
                && guard.RootElement.TryGetProperty("validationFingerprint", out var fingerprint)
                && fingerprint.ValueKind == JsonValueKind.String
                && string.Equals(fingerprint.GetString(), ValidationFingerprint(row), StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private static string ValidationFingerprint(ApplicationCandidateValidationRecord row) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-validation-outcome/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(row)));
}
