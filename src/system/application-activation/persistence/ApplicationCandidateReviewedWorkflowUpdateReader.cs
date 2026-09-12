using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Converts the common retained reviewer receipt into the workflow publication proof only after
/// the judgment names the exact predecessor semantics. Runtime evidence remains a separate gate.
/// </summary>
internal sealed class ApplicationCandidateReviewedWorkflowUpdateReader(
    ApplicationCandidateReviewedClosureReceiptReader reviews)
{
    internal async Task<ApplicationCandidateReviewedWorkflowUpdateEvidence?> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        foreach (var receipt in await reviews.ReadAsync(host, candidate, cancellationToken))
        {
            if (receipt.Authority.ReviewClosure is not ApplicationCandidateWorkflowReviewClosureEvidence closure
                || closure.Grammar != ApplicationCandidateWorkflowReviewClosureReader.GrammarVersion
                || closure.Candidate != candidate || !SameRetained(closure.Retained, receipt.Retained)
                || !JudgmentSupports(closure, receipt.Judgment, receipt.Input)) continue;
            return ApplicationCandidateReviewedWorkflowUpdateEvidence.Create(closure, receipt);
        }
        return null;
    }

    private static bool SameRetained(ApplicationCandidateRetainedMetadata left,
        ApplicationCandidateRetainedMetadata right) =>
        left.RevisionRow.ApplicationId == right.RevisionRow.ApplicationId
        && left.RevisionRow.CandidateId == right.RevisionRow.CandidateId
        && left.RevisionRow.Revision == right.RevisionRow.Revision
        && left.RevisionRow.ContentFingerprint == right.RevisionRow.ContentFingerprint
        && left.RevisionRow.SourceOperationId == right.RevisionRow.SourceOperationId
        && left.RevisionRow.ExpectedActiveFingerprint == right.RevisionRow.ExpectedActiveFingerprint
        && left.ApplicationRevision == right.ApplicationRevision
        && left.Documents.SequenceEqual(right.Documents);

    private static bool JudgmentSupports(ApplicationCandidateWorkflowReviewClosureEvidence closure,
        ApplicationCandidateReuseJudgmentOutputV2 judgment, ApplicationCandidateReuseInputV2 input)
    {
        if (judgment.SelectionFingerprint != input.SelectionFingerprint
            || judgment.ManualResultFingerprint != input.ManualResultFingerprint) return false;
        if (closure.Predecessor is null)
            return judgment.Judgment == ApplicationCandidateReuseJudgment.JustifiedNew
                && judgment.Assessments.All(value => value.Judgment == ApplicationCandidateReuseJudgment.JustifiedNew);
        return judgment.Judgment == ApplicationCandidateReuseJudgment.ExtendExisting
            && input.Alternatives.Contains(closure.Predecessor)
            && judgment.Assessments.Any(value => value.Target == closure.Predecessor
                && value.Judgment == ApplicationCandidateReuseJudgment.ExtendExisting)
            && judgment.Assessments.All(value => value.Judgment == ApplicationCandidateReuseJudgment.ExtendExisting);
    }
}

internal sealed class ApplicationCandidateReviewedWorkflowUpdateEvidence
{
    private ApplicationCandidateReviewedWorkflowUpdateEvidence(
        ApplicationCandidateWorkflowReviewClosureEvidence closure,
        ApplicationCandidateReviewedClosureReceipt receipt)
    {
        Closure = closure;
        Review = receipt;
        Fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/reviewed-workflow-service-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Closure.Candidate, basis = Closure.Basis.ActivationFingerprint,
                Closure.EvidenceFingerprint, Closure.Definition, Closure.Predecessor,
                dependencies = Closure.Dependencies,
                task = Review.Task.Snapshot.Request.Handle,
                Review.Task.Snapshot.CompletionEvidenceReference,
                Review.Input.InputFingerprint, Review.Judgment.SelectionFingerprint,
                Review.Judgment.ManualResultFingerprint, Review.Judgment.Judgment,
                Review.CausalCommandId
            })));
    }

    internal ApplicationCandidateWorkflowReviewClosureEvidence Closure { get; }
    internal ApplicationCandidateReviewedClosureReceipt Review { get; }
    internal ApplicationCandidateRetainedMetadata Retained => Closure.Retained;
    internal ActiveApplicationManifest Basis => Closure.Basis;
    internal string Fingerprint { get; }

    internal static ApplicationCandidateReviewedWorkflowUpdateEvidence Create(
        ApplicationCandidateWorkflowReviewClosureEvidence closure,
        ApplicationCandidateReviewedClosureReceipt receipt) => new(closure, receipt);
}
