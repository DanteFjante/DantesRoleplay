using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

public sealed partial class ApplicationActivationService
{
    internal async Task<ActiveApplicationManifest> StageReviewedPureUpdateAsync(
        ApplicationCandidateReviewedPureUpdateEvidence update, ApplicationCandidateValidationRecord validation,
        string operationId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || validation.Outcome != "valid"
            || validation.CandidateFingerprint != update.Closure.Candidate.ContentFingerprint
            || validation.DependencyFingerprint != update.Fingerprint)
            throw Invalid("APPLICATION_CANDIDATE_PUBLICATION_INVALID", "A reviewed pure candidate and the owning transaction are required.");
        RequireExpectation(update.Basis.ActivationFingerprint, Current(update.Basis.ApplicationId)?.ActivationFingerprint);
        var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/reviewed-pure-candidate-activation/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                update.Closure.Candidate, basis = update.Basis.ActivationFingerprint,
                update.Fingerprint, validation.PreparationVersion, validation.PreparedEvidenceReference,
                validation.ReuseEvidenceReference
            })));
        var activation = update.Basis with
        {
            ActivationRevision = NextRevision(update.Basis.ApplicationId),
            PreviewFingerprint = validation.CanonicalCommandFingerprint,
            ScannedDocumentsFingerprint = update.Closure.Candidate.ContentFingerprint,
            CandidateManifestFingerprint = update.Closure.Candidate.ContentFingerprint,
            ActivationFingerprint = fingerprint,
            ResolutionFingerprint = fingerprint,
            DependencyGraphFingerprint = update.Fingerprint,
            Winners = update.Retained.Documents,
            ActivatedByOperationId = operationId,
            ActivatedAtUtc = DateTime.UtcNow,
            PreparationVersion = CurrentPreparationVersion
        };
        await PersistAsync(activation, null, cancellationToken);
        db.Add(new ApplicationActivationReceiptRecord
        {
            OperationId = operationId, RequestFingerprint = fingerprint,
            ApplicationId = activation.ApplicationId.Value,
            ActivationRevision = activation.ActivationRevision, Outcome = "activated"
        });
        db.Add(new ApplicationCandidatePublicationRecord
        {
            ActivationOperationId = operationId, ValidationOperationId = validation.OperationId,
            ApplicationId = activation.ApplicationId.Value,
            CandidateId = update.Closure.Candidate.CandidateId, Revision = update.Closure.Candidate.Revision
        });
        await db.SaveChangesAsync(cancellationToken);
        return activation;
    }

    /// <summary>Stages the existing activation owner's generation inside the author's writer transaction.</summary>
    internal async Task<ActiveApplicationManifest> StageCompatibleUpdateAsync(
        ApplicationCandidateCompatibleUpdateEvidence update, ApplicationCandidateValidationRecord validation,
        string operationId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || validation.Outcome != "valid"
            || validation.CandidateFingerprint != update.Closure.Candidate.ContentFingerprint
            || validation.DependencyFingerprint != update.Fingerprint)
            throw Invalid("APPLICATION_CANDIDATE_PUBLICATION_INVALID", "A validated candidate and the owning transaction are required.");
        RequireExpectation(update.Basis.ActivationFingerprint, Current(update.Basis.ApplicationId)?.ActivationFingerprint);
        var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/runtime-candidate-activation/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                update.Closure.Candidate, basis = update.Basis.ActivationFingerprint,
                update.Fingerprint, validation.PreparationVersion, validation.PreparedEvidenceReference,
                validation.ReuseEvidenceReference
            })));
        var activation = update.Basis with
        {
            ActivationRevision = NextRevision(update.Basis.ApplicationId),
            PreviewFingerprint = validation.CanonicalCommandFingerprint,
            ScannedDocumentsFingerprint = update.Closure.Candidate.ContentFingerprint,
            CandidateManifestFingerprint = update.Closure.Candidate.ContentFingerprint,
            ActivationFingerprint = fingerprint,
            ResolutionFingerprint = fingerprint,
            DependencyGraphFingerprint = update.Fingerprint,
            // The unchanged base retains its original coverage claim. The update's complete,
            // narrower compatibility proof is retained separately in the candidate validation.
            Winners = update.Retained.Documents,
            ActivatedByOperationId = operationId,
            ActivatedAtUtc = DateTime.UtcNow,
            PreparationVersion = CurrentPreparationVersion
        };
        await PersistAsync(activation, null, cancellationToken);
        db.Add(new ApplicationActivationReceiptRecord
        {
            OperationId = operationId, RequestFingerprint = fingerprint,
            ApplicationId = activation.ApplicationId.Value,
            ActivationRevision = activation.ActivationRevision, Outcome = "activated"
        });
        db.Add(new ApplicationCandidatePublicationRecord
        {
            ActivationOperationId = operationId, ValidationOperationId = validation.OperationId,
            ApplicationId = activation.ApplicationId.Value,
            CandidateId = update.Closure.Candidate.CandidateId, Revision = update.Closure.Candidate.Revision
        });
        await db.SaveChangesAsync(cancellationToken);
        return activation;
    }

    internal async Task<ActiveApplicationManifest> StageStatefulUpdateAsync(
        ApplicationCandidateStatefulUpdateEvidence update,
        ApplicationExecution.ApplicationCandidateStatefulRuntimeReport report,
        ApplicationCandidateValidationRecord validation, string operationId,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || validation.Outcome != "valid"
            || validation.CandidateFingerprint != update.Candidate.ContentFingerprint
            || validation.DependencyFingerprint != update.Fingerprint
            || report.Candidate != update.Candidate || report.UpdateFingerprint != update.Fingerprint)
            throw Invalid("APPLICATION_CANDIDATE_PUBLICATION_INVALID",
                "A validated stateful candidate and the owning transaction are required.");
        RequireExpectation(update.Basis.ActivationFingerprint,
            Current(update.Basis.ApplicationId)?.ActivationFingerprint);
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/stateful-runtime-candidate-activation/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                update.Candidate, basis = update.Basis.ActivationFingerprint, update.Fingerprint,
                validation.PreparationVersion, validation.PreparedEvidenceReference,
                validation.ReuseEvidenceReference, report.StateGrantReference,
                report.StateGrantFingerprint, report.EffectKinds
            })));
        var activation = update.Basis with
        {
            ActivationRevision = NextRevision(update.Basis.ApplicationId),
            PreviewFingerprint = validation.CanonicalCommandFingerprint,
            ScannedDocumentsFingerprint = update.Candidate.ContentFingerprint,
            CandidateManifestFingerprint = update.Candidate.ContentFingerprint,
            ActivationFingerprint = fingerprint,
            ResolutionFingerprint = fingerprint,
            DependencyGraphFingerprint = update.Fingerprint,
            Winners = update.Retained.Documents,
            ActivatedByOperationId = operationId,
            ActivatedAtUtc = DateTime.UtcNow,
            PreparationVersion = CurrentPreparationVersion
        };
        await PersistAsync(activation, null, cancellationToken);
        db.Add(new ApplicationActivationReceiptRecord
        {
            OperationId = operationId, RequestFingerprint = fingerprint,
            ApplicationId = activation.ApplicationId.Value,
            ActivationRevision = activation.ActivationRevision, Outcome = "activated"
        });
        db.Add(new ApplicationCandidatePublicationRecord
        {
            ActivationOperationId = operationId, ValidationOperationId = validation.OperationId,
            ApplicationId = activation.ApplicationId.Value,
            CandidateId = update.Candidate.CandidateId, Revision = update.Candidate.Revision
        });
        await db.SaveChangesAsync(cancellationToken);
        return activation;
    }

    internal async Task<ActiveApplicationManifest> StageReviewedStatefulUpdateAsync(
        ApplicationCandidateReviewedStatefulUpdateEvidence update,
        ApplicationExecution.ApplicationCandidateStatefulRuntimeReport report,
        ApplicationCandidateValidationRecord validation, string operationId,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || validation.Outcome != "valid"
            || validation.CandidateFingerprint != update.Closure.Candidate.ContentFingerprint
            || validation.DependencyFingerprint != update.Fingerprint
            || report.Candidate != update.Closure.Candidate
            || report.UpdateFingerprint != update.Closure.EvidenceFingerprint)
            throw Invalid("APPLICATION_CANDIDATE_PUBLICATION_INVALID",
                "A reviewed stateful candidate and the owning transaction are required.");
        RequireExpectation(update.Closure.Basis.ActivationFingerprint,
            Current(update.Closure.Basis.ApplicationId)?.ActivationFingerprint);
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/reviewed-stateful-runtime-candidate-activation/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                update.Closure.Candidate, basis = update.Closure.Basis.ActivationFingerprint,
                update.Fingerprint, update.Closure.EvidenceFingerprint,
                validation.PreparationVersion, validation.PreparedEvidenceReference,
                validation.ReuseEvidenceReference, report.StateGrantReference,
                report.StateGrantFingerprint, report.EffectKinds
            })));
        var activation = update.Closure.Basis with
        {
            ActivationRevision = NextRevision(update.Closure.Basis.ApplicationId),
            PreviewFingerprint = validation.CanonicalCommandFingerprint,
            ScannedDocumentsFingerprint = update.Closure.Candidate.ContentFingerprint,
            CandidateManifestFingerprint = update.Closure.Candidate.ContentFingerprint,
            ActivationFingerprint = fingerprint,
            ResolutionFingerprint = fingerprint,
            DependencyGraphFingerprint = update.Fingerprint,
            Winners = update.Closure.Retained.Documents,
            ActivatedByOperationId = operationId,
            ActivatedAtUtc = DateTime.UtcNow,
            PreparationVersion = CurrentPreparationVersion
        };
        await PersistAsync(activation, null, cancellationToken);
        db.Add(new ApplicationActivationReceiptRecord
        {
            OperationId = operationId, RequestFingerprint = fingerprint,
            ApplicationId = activation.ApplicationId.Value,
            ActivationRevision = activation.ActivationRevision, Outcome = "activated"
        });
        db.Add(new ApplicationCandidatePublicationRecord
        {
            ActivationOperationId = operationId, ValidationOperationId = validation.OperationId,
            ApplicationId = activation.ApplicationId.Value,
            CandidateId = update.Closure.Candidate.CandidateId, Revision = update.Closure.Candidate.Revision
        });
        await db.SaveChangesAsync(cancellationToken);
        return activation;
    }

    internal async Task<ActiveApplicationManifest> StageReviewedWorkflowUpdateAsync(
        ApplicationCandidateReviewedWorkflowUpdateEvidence update,
        ApplicationCandidateRuntimeReport report,
        ApplicationCandidateValidationRecord validation,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || validation.Outcome != "valid"
            || validation.CandidateFingerprint != update.Closure.Candidate.ContentFingerprint
            || validation.DependencyFingerprint != update.Fingerprint
            || report.Candidate != update.Closure.Candidate
            || report.SelectionEvidenceFingerprint != update.Closure.EvidenceFingerprint)
            throw Invalid("APPLICATION_CANDIDATE_PUBLICATION_INVALID",
                "A reviewed and runtime-validated workflow candidate is required.");
        RequireExpectation(update.Basis.ActivationFingerprint,
            Current(update.Basis.ApplicationId)?.ActivationFingerprint);
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/reviewed-workflow-candidate-activation/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                update.Closure.Candidate, basis = update.Basis.ActivationFingerprint,
                update.Fingerprint, update.Closure.EvidenceFingerprint,
                validation.PreparationVersion, validation.PreparedEvidenceReference,
                validation.ReuseEvidenceReference,
                runtimeReportFingerprint = ApplicationExecution.ApplicationCandidateWorkflowRuntimeValidator
                    .ReportFingerprint(report)
            })));
        var activation = update.Basis with
        {
            ActivationRevision = NextRevision(update.Basis.ApplicationId),
            PreviewFingerprint = validation.CanonicalCommandFingerprint,
            ScannedDocumentsFingerprint = update.Closure.Candidate.ContentFingerprint,
            CandidateManifestFingerprint = update.Closure.Candidate.ContentFingerprint,
            ActivationFingerprint = fingerprint,
            ResolutionFingerprint = fingerprint,
            DependencyGraphFingerprint = update.Fingerprint,
            Winners = update.Retained.Documents,
            ActivatedByOperationId = operationId,
            ActivatedAtUtc = DateTime.UtcNow,
            PreparationVersion = CurrentPreparationVersion
        };
        await PersistAsync(activation, null, cancellationToken);
        db.Add(new ApplicationActivationReceiptRecord
        {
            OperationId = operationId,
            RequestFingerprint = fingerprint,
            ApplicationId = activation.ApplicationId.Value,
            ActivationRevision = activation.ActivationRevision,
            Outcome = "activated"
        });
        db.Add(new ApplicationCandidatePublicationRecord
        {
            ActivationOperationId = operationId,
            ValidationOperationId = validation.OperationId,
            ApplicationId = activation.ApplicationId.Value,
            CandidateId = update.Closure.Candidate.CandidateId,
            Revision = update.Closure.Candidate.Revision
        });
        await db.SaveChangesAsync(cancellationToken);
        return activation;
    }
}
