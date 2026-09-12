using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

public sealed partial class ApplicationActivationService
{
    internal async Task<ActiveApplicationManifest> StageReviewedQueryUpdateAsync(
        ApplicationCandidateReviewedQueryUpdateEvidence update,
        ApplicationCandidateValidationRecord validation, string operationId,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || validation.Outcome != "valid"
            || validation.CandidateFingerprint != update.Closure.Candidate.ContentFingerprint
            || validation.DependencyFingerprint != update.Fingerprint
            || !ApplicationCandidateReviewedQueryUpdateValidation.Matches(validation, update))
            throw Invalid("APPLICATION_CANDIDATE_PUBLICATION_INVALID",
                "A validated reviewed query candidate and the owning transaction are required.");
        RequireExpectation(update.Basis.ActivationFingerprint,
            Current(update.Basis.ApplicationId)?.ActivationFingerprint);
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/reviewed-query-candidate-activation/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                update.Closure.Candidate, basis = update.Basis.ActivationFingerprint,
                update.Fingerprint, validation.PreparationVersion,
                validation.PreparedEvidenceReference, validation.ReuseEvidenceReference
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
