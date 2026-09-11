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
    public Task<InteractionInvocationResult> ValidateAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken = default) =>
        ValidateAsync(new ApplicationCandidateValidationRequest(candidate, []), host, cancellationToken);

    public async Task<InteractionInvocationResult> ValidateAsync(ApplicationCandidateValidationRequest request,
        InteractionInvocationHost host, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Failed("APPLICATION_CANDIDATE_CONTEXT_HAS_PENDING_WRITES");
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        var candidate = request?.Candidate;
        if (request?.Samples is null || candidate is null || candidate.ApplicationId is null || candidate.ApplicationId != host.ApplicationRevision.ApplicationId
            || candidate.Revision < 1 || candidate.CandidateId is not { } candidateId || !CandidateId(candidateId)
            || candidate.ContentFingerprint is not { } candidateFingerprint || !Hash(candidateFingerprint))
            return Failed("INVALID_PAYLOAD");
        var opened = false;
        try
        {
            var samples = NormalizeSamples(request.Samples);
            var canonical = ApplicationCandidateOperationProof.CanonicalValidation(host, candidate, samples);
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
            var definitions = Definitions(candidate.ApplicationId, changedDocuments, changed);
            if (samples.Any(sample => !definitions.Contains(sample.Definition)))
                return Failed("APPLICATION_CANDIDATE_SAMPLE_TARGET_MISMATCH");
            foreach (var definition in definitions)
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
                if (validation is null || !ApplicationCandidateOperationProof.ValidationMatches(prior, validation, candidate, definitions))
                    return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", "The stored validation receipt cannot be reconciled.");
                if (validation.Outcome == "valid"
                    && validation.DependencyEvidenceReference is { } durableReview
                    && durableReview.StartsWith("validation.result.", StringComparison.Ordinal)
                    && validation.ReuseEvidenceReference == durableReview
                    && (reviewedPureUpdates is null
                        || !ApplicationCandidateOperationProof.TryReadRuntimeReport(
                            prior, validation, candidate, definitions, out var retainedReport)
                        || retainedReport is null
                        || await new ApplicationCandidateReviewedPureUpdateValidation(reviewedPureUpdates)
                            .VerifyAsync(host, retainedReport, validation, cancellationToken) is null))
                    return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_VALIDATION_EVIDENCE_INCONSISTENT",
                        "The retained candidate review is no longer current or cannot be reconciled.");
                await transaction.CommitAsync(cancellationToken);
                return Receipt(operationId, commandFingerprint);
            }
            var intentMatchUpdate = samples.Count == 0
                ? await new ApplicationCandidateIntentMatchUpdateReader(db, applications, activations, evidence)
                    .ReadAsync(candidate, cancellationToken)
                : null;
            ApplicationCandidateRuntimeReport? runtimeReport = null;
            if (intentMatchUpdate is null && preparation is not null)
            {
                var prepared = await preparation.ValidateAsync(
                    new ApplicationCandidateValidationRequest(candidate, samples), host, cancellationToken);
                if (ApplicationCandidateOperationProof.TryValidateRuntimeReport(
                        prepared, new ApplicationCandidateValidationRequest(candidate, samples), out _, out _))
                    runtimeReport = prepared;
            }
            var operation = await operations.RecordAsync("application-candidate-validation",
                "Candidate validation retained bounded runtime evidence; dependency and reuse review remain required.",
                true, subject: candidate.ApplicationId.Value, projectionJson: canonical,
                guardEvidenceJson: "{}", id: operationId,
                cancellationToken: cancellationToken);
            var diagnostics = new List<ApplicationCandidateDiagnostic>
            {
                new ApplicationCandidateDiagnostic("DEPENDENCY_EXTRACTION_UNAVAILABLE", candidate.CandidateId, "Exact dependency extraction is unavailable."),
                new ApplicationCandidateDiagnostic("REUSE_REVIEW_UNAVAILABLE", candidate.CandidateId, "Reuse review is unavailable.")
            };
            if (runtimeReport is null)
            {
                diagnostics.Add(new("RUNTIME_PREPARATION_UNAVAILABLE", candidate.CandidateId,
                    "Runtime preparation and sample validation are unavailable."));
                if (samples.Count == 0)
                    diagnostics.Add(new("RUNTIME_SAMPLES_UNAVAILABLE", candidate.CandidateId,
                        "Retained execution samples are missing."));
            }
            else
            {
                diagnostics.AddRange(runtimeReport.Diagnostics);
            }
            var runtimeFingerprint = runtimeReport is null ? null
                : ApplicationCandidateOperationProof.RuntimeReportFingerprint(runtimeReport);
            var validationRow = new ApplicationCandidateValidationRecord
            {
                OperationId = operation.Id, ApplicationId = candidate.ApplicationId.Value, CandidateId = candidate.CandidateId,
                Revision = candidate.Revision, CandidateFingerprint = candidate.ContentFingerprint, GrantReference = host.GrantReference,
                ExpectedActiveFingerprint = readback.RevisionRow.ExpectedActiveFingerprint,
                DependencyFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-dependencies-incomplete/v1",
                    InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { candidate.ApplicationId, candidate.CandidateId, candidate.Revision, candidate.ContentFingerprint }))),
                PreparationVersion = runtimeReport is { RuntimePolicyVersion: not null, RuntimePolicyFingerprint: not null }
                    ? runtimeReport.RuntimePolicyVersion + "@" + runtimeReport.RuntimePolicyFingerprint : null,
                ManualPacketResultFingerprint = null, CanonicalCommandFingerprint = commandFingerprint,
                DependenciesJson = "[]", DependenciesComplete = false, DependencyEvidenceReference = null,
                PreparedEvidenceReference = runtimeFingerprint is null ? null
                    : ApplicationCandidateOperationProof.RuntimeEvidenceReference(operation.Id, runtimeFingerprint),
                ReuseEvidenceReference = null,
                Outcome = runtimeReport?.Status == ApplicationCandidateRuntimeStatus.Invalid ? "invalid" : "unavailable",
                DiagnosticsJson = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(diagnostics)), AlternativesJson = "[]"
            };
            if (intentMatchUpdate is not null && manuals is not null)
            {
                var exactMatch = new ApplicationCandidateIntentMatchUpdateValidation(
                    db, targets, grants, manuals, operations);
                await exactMatch.CompleteAsync(host, intentMatchUpdate, validationRow, cancellationToken);
            }
            else if (runtimeReport is not null && manuals is not null)
            {
                var compatible = new ApplicationCandidateCompatibleUpdateValidation(db,
                    new(db, applications, activations, evidence, targets,
                        new(new SchemaValidation.BoundedJsonSchemaValidator())), targets, grants, manuals, operations);
                await compatible.CompleteAsync(host, runtimeReport, validationRow, cancellationToken);
            }
            if (validationRow.Outcome != "valid" && runtimeReport is not null && reviewedPureUpdates is not null)
                await new ApplicationCandidateReviewedPureUpdateValidation(reviewedPureUpdates)
                    .CompleteAsync(host, runtimeReport, validationRow, cancellationToken);
            db.Add(validationRow);
            operation.GuardEvidenceJson = ApplicationCandidateOperationProof.ValidationGuard(host, candidate, validationRow,
                definitions, commandFingerprint, runtimeReport);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Receipt(operationId, commandFingerprint);
        }
        catch (ApplicationActivationException exception) when (exception.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal))
        { return InteractionInvocationResult.Unavailable(exception.Code, "Candidate validation is unavailable."); }
        catch (ApplicationActivationException exception) { return Failed(exception.Code); }
        catch (InteractionContractException exception) { return Failed(exception.Code); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_VALIDATE_UNAVAILABLE", "Candidate validation is unavailable."); }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray()) entry.State = EntityState.Detached;
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    internal static IReadOnlyList<ApplicationCandidateValidationSample> NormalizeSamples(IReadOnlyList<ApplicationCandidateValidationSample> samples)
    {
        if (samples is null || samples.Count > ApplicationAuthoringLimits.SamplesPerValidation
            || samples.Any(sample => sample is null || sample.Definition is null
                || string.IsNullOrWhiteSpace(sample.Definition.DefinitionId) || string.IsNullOrWhiteSpace(sample.Definition.Kind)
                || sample.Definition.Revision < 1 || sample.Definition.ContentFingerprint is null || !Hash(sample.Definition.ContentFingerprint)
                || sample.InputJson is null || sample.ExpectedDataJson is null)
            || samples.GroupBy(sample => sample.Definition.DefinitionId, StringComparer.Ordinal)
                .Any(group => group.Count() > ApplicationAuthoringLimits.SamplesPerDefinition))
            throw new ApplicationActivationException("APPLICATION_CANDIDATE_SAMPLES_INVALID", "Validation samples exceed their bounds or lack an exact definition.");
        return Array.AsReadOnly(samples.Select(sample => sample with
        {
            InputJson = InteractionCanonicalJson.CanonicalizeObject(sample.InputJson),
            ExpectedDataJson = InteractionCanonicalJson.CanonicalizeObject(sample.ExpectedDataJson)
        }).ToArray());
    }

}
