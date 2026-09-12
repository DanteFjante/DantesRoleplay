using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Applies procedure structure and a positive reuse judgment to the common retained reviewer
/// receipt. Candidate content remains inert until activation rechecks this proof.
/// </summary>
internal sealed class ApplicationCandidateReviewedProcedureUpdateReader(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IStandingGrantTargetResolver targets,
    SystemTaskApplicationValidationGate validationGate)
{
    internal async Task<ApplicationCandidateReviewedProcedureUpdateEvidence?> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receipts = await new ApplicationCandidateReviewedClosureReceiptReader(
                db, applications, validationGate).ReadAsync(host, candidate, cancellationToken);
            foreach (var receipt in receipts)
            {
                if (receipt.Authority.ReviewClosure
                        is not ApplicationCandidateProcedureReviewClosureEvidence closure)
                    continue;
                var basis = await ApplicationCandidateDocumentSelection.ReadBaseAsync(
                    db, activations, candidate.ApplicationId,
                    receipt.Retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
                if (basis is null || closure.BaseOrigin.ActivationFingerprint != basis.ActivationFingerprint)
                    continue;
                var active = activations.Current(candidate.ApplicationId);
                var basisIsCurrent = active?.ActivationFingerprint == basis.ActivationFingerprint;
                var candidateIsCurrent = active?.CandidateManifestFingerprint == candidate.ContentFingerprint
                    && active.Winners.SequenceEqual(receipt.Retained.Documents);
                if (!basisIsCurrent && !candidateIsCurrent) continue;
                if (basisIsCurrent && !await PredecessorIsCurrentAsync(host, closure, cancellationToken))
                    continue;
                if (!JudgmentSupports(closure, receipt.Judgment)) continue;
                return ApplicationCandidateReviewedProcedureUpdateEvidence.FromVerified(
                    receipt.Retained, basis, closure, receipt.Task, receipt.Judgment,
                    receipt.Input, receipt.CausalCommandId);
            }
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or JsonException or InteractionContractException or ApplicationActivationException)
        { return null; }
    }

    private async Task<bool> PredecessorIsCurrentAsync(InteractionInvocationHost host,
        ApplicationCandidateProcedureReviewClosureEvidence closure, CancellationToken cancellationToken)
    {
        if (closure.Predecessor is null)
        {
            var absent = await targets.ResolveCurrentAsync(host, closure.Successor.DefinitionId,
                closure.Successor.Kind, cancellationToken);
            return absent.Code == "STANDING_GRANT_DEFINITION_UNAVAILABLE";
        }
        var resolved = await targets.ResolveAsync(host, closure.Predecessor, cancellationToken);
        return resolved.Status == StandingGrantTargetResolutionStatus.Available
            && resolved.Target?.OwnerApplicationId == closure.Candidate.ApplicationId;
    }

    private static bool JudgmentSupports(ApplicationCandidateProcedureReviewClosureEvidence closure,
        ApplicationCandidateReuseJudgmentOutputV2 judgment)
    {
        var expected = closure.Predecessor is null
            ? ApplicationCandidateReuseJudgment.JustifiedNew
            : ApplicationCandidateReuseJudgment.ExtendExisting;
        return judgment.Judgment == expected
            && judgment.Assessments.All(value => value.Judgment == expected);
    }
}

internal sealed class ApplicationCandidateReviewedProcedureUpdateEvidence
{
    private ApplicationCandidateReviewedProcedureUpdateEvidence(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateProcedureReviewClosureEvidence closure,
        SystemTaskCompletedValidationProof task,
        ApplicationCandidateReuseJudgmentOutputV2 judgment,
        ApplicationCandidateReuseInputV2 input, string causalCommandId)
    {
        Retained = retained; Basis = basis; Closure = closure; Task = task;
        Judgment = judgment; Input = input; CausalCommandId = causalCommandId;
        Fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/reviewed-procedure-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Closure.Candidate, basis = Basis.ActivationFingerprint, Closure.EvidenceFingerprint,
                Closure.Predecessor, Closure.Dependencies,
                task = Task.Snapshot.Request.Handle, Task.Snapshot.CompletionEvidenceReference,
                input.InputFingerprint, judgment.SelectionFingerprint,
                judgment.ManualResultFingerprint, judgment.Judgment, causalCommandId
            })));
    }

    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal ApplicationCandidateProcedureReviewClosureEvidence Closure { get; }
    internal SystemTaskCompletedValidationProof Task { get; }
    internal ApplicationCandidateReuseJudgmentOutputV2 Judgment { get; }
    internal ApplicationCandidateReuseInputV2 Input { get; }
    internal string CausalCommandId { get; }
    internal string Fingerprint { get; }

    internal static ApplicationCandidateReviewedProcedureUpdateEvidence FromVerified(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateProcedureReviewClosureEvidence closure,
        SystemTaskCompletedValidationProof task,
        ApplicationCandidateReuseJudgmentOutputV2 judgment,
        ApplicationCandidateReuseInputV2 input, string causalCommandId) =>
        new(retained, basis, closure, task, judgment, input, causalCommandId);
}
