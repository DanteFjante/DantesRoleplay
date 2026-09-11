using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

internal sealed class ApplicationCandidateReviewedPureUpdateValidation(
    ApplicationCandidateReviewedPureUpdateReader reviews)
{
    internal async Task<bool> CompleteAsync(InteractionInvocationHost host, ApplicationCandidateRuntimeReport report,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        var reviewed = await reviews.ReadAsync(host, report.Candidate, cancellationToken);
        if (reviewed is null || !RuntimeCovers(report, reviewed)) return false;
        validation.DependenciesJson = Dependencies(reviewed);
        validation.DependencyFingerprint = reviewed.Fingerprint;
        validation.DependenciesComplete = true;
        validation.DependencyEvidenceReference = reviewed.Task.Snapshot.CompletionEvidenceReference;
        validation.ReuseEvidenceReference = reviewed.Task.Snapshot.CompletionEvidenceReference;
        validation.ManualPacketResultFingerprint = reviewed.Judgment.ManualResultFingerprint;
        validation.AlternativesJson = Alternatives(reviewed);
        validation.Outcome = "valid";
        validation.DiagnosticsJson = "[]";
        return true;
    }

    internal async Task<ApplicationCandidateReviewedPureUpdateEvidence?> VerifyAsync(InteractionInvocationHost host,
        ApplicationCandidateRuntimeReport report, ApplicationCandidateValidationRecord validation,
        CancellationToken cancellationToken)
    {
        var reviewed = await reviews.ReadAsync(host, report.Candidate, cancellationToken);
        return reviewed is not null && Matches(report, validation, reviewed) ? reviewed : null;
    }

    internal static bool Matches(ApplicationCandidateRuntimeReport report,
        ApplicationCandidateValidationRecord validation, ApplicationCandidateReviewedPureUpdateEvidence reviewed) =>
        RuntimeCovers(report, reviewed)
            && validation.Outcome == "valid" && validation.DependenciesComplete
            && validation.DependencyFingerprint == reviewed.Fingerprint
            && validation.DependenciesJson == Dependencies(reviewed)
            && validation.DependencyEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
            && validation.ReuseEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
            && validation.ManualPacketResultFingerprint == reviewed.Judgment.ManualResultFingerprint
            && validation.AlternativesJson == Alternatives(reviewed);

    private static bool RuntimeCovers(ApplicationCandidateRuntimeReport report,
        ApplicationCandidateReviewedPureUpdateEvidence reviewed) =>
        report.Status == ApplicationCandidateRuntimeStatus.Completed
        && report.SelectionEvidenceFingerprint == reviewed.Closure.EvidenceFingerprint
        && report.RuntimePolicyVersion == ApplicationCandidateRuntimeValidator.RuntimePolicyVersion
        && report.RuntimePolicyFingerprint == ApplicationCandidateRuntimeValidator.RuntimePolicyFingerprint
        && reviewed.Closure.Definitions.All(definition => report.Samples.Any(sample =>
            sample.Definition == definition.Plan.Definition && sample.Attempted
            && sample.Outcome == ApplicationCandidateRuntimeStatus.Completed
            && sample.ActualDataFingerprint == sample.ExpectedDataFingerprint));

    private static string Dependencies(ApplicationCandidateReviewedPureUpdateEvidence reviewed) =>
        InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(reviewed.Closure.Definitions
            .Select(value => value.Plan.Definition)
            .Select(value => new ApplicationCandidateDependency(value.DefinitionId, value.Revision, value.ContentFingerprint))));

    private static string Alternatives(ApplicationCandidateReviewedPureUpdateEvidence reviewed)
    {
        var reasons = reviewed.Judgment.Assessments.ToDictionary(value => value.Target, value => value.Reason);
        return InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(reviewed.Input.Alternatives.Select(value =>
            new ApplicationDefinitionAlternative(value.DefinitionId, value.Revision, value.ContentFingerprint,
                reasons.TryGetValue(value, out var reason) ? reason : reviewed.Judgment.Reason))));
    }
}
