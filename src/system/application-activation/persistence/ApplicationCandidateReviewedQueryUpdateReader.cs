using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Applies an ExtendExisting judgment to the retained existing-query review closure.</summary>
internal sealed class ApplicationCandidateReviewedQueryUpdateReader(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IStandingGrantTargetResolver targets,
    SystemTaskApplicationValidationGate validationGate)
{
    internal async Task<ApplicationCandidateReviewedQueryUpdateEvidence?> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receipts = await new ApplicationCandidateReviewedClosureReceiptReader(
                db, applications, validationGate).ReadAsync(host, candidate, cancellationToken);
            foreach (var receipt in receipts)
            {
                if (receipt.Authority.ReviewClosure is not ApplicationCandidateQueryReviewClosureEvidence closure)
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
                if (receipt.Judgment.Judgment != ApplicationCandidateReuseJudgment.ExtendExisting
                    || receipt.Judgment.Assessments.Any(value =>
                        value.Judgment != ApplicationCandidateReuseJudgment.ExtendExisting))
                    continue;
                return ApplicationCandidateReviewedQueryUpdateEvidence.FromVerified(
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
        ApplicationCandidateQueryReviewClosureEvidence closure, CancellationToken cancellationToken)
    {
        var resolved = await targets.ResolveAsync(host, closure.Predecessor, cancellationToken);
        return resolved.Status == StandingGrantTargetResolutionStatus.Available
            && resolved.Target?.OwnerApplicationId == closure.Candidate.ApplicationId;
    }
}

internal sealed class ApplicationCandidateReviewedQueryUpdateEvidence
{
    private ApplicationCandidateReviewedQueryUpdateEvidence(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateQueryReviewClosureEvidence closure,
        SystemTaskCompletedValidationProof task,
        ApplicationCandidateReuseJudgmentOutputV2 judgment,
        ApplicationCandidateReuseInputV2 input, string causalCommandId)
    {
        Retained = retained;
        Basis = basis;
        Closure = closure;
        Task = task;
        Judgment = judgment;
        Input = input;
        CausalCommandId = causalCommandId;
        Fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/reviewed-query-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Closure.Candidate, basis = Basis.ActivationFingerprint, Closure.EvidenceFingerprint,
                Closure.Predecessor, Closure.Projection,
                task = Task.Snapshot.Request.Handle, Task.Snapshot.CompletionEvidenceReference,
                input.InputFingerprint, judgment.SelectionFingerprint,
                judgment.ManualResultFingerprint, judgment.Judgment, causalCommandId
            })));
    }

    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal ApplicationCandidateQueryReviewClosureEvidence Closure { get; }
    internal SystemTaskCompletedValidationProof Task { get; }
    internal ApplicationCandidateReuseJudgmentOutputV2 Judgment { get; }
    internal ApplicationCandidateReuseInputV2 Input { get; }
    internal string CausalCommandId { get; }
    internal string Fingerprint { get; }

    internal static ApplicationCandidateReviewedQueryUpdateEvidence FromVerified(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateQueryReviewClosureEvidence closure,
        SystemTaskCompletedValidationProof task,
        ApplicationCandidateReuseJudgmentOutputV2 judgment,
        ApplicationCandidateReuseInputV2 input, string causalCommandId) =>
        new(retained, basis, closure, task, judgment, input, causalCommandId);
}
