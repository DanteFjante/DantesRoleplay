using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

internal sealed class ApplicationCandidateReviewedWorkflowUpdateValidation(
    ApplicationCandidateReviewedWorkflowUpdateReader reviews,
    ApplicationCandidateWorkflowRuntimeValidator runtime)
{
    internal async Task<bool> CompleteAsync(InteractionInvocationHost host,
        ApplicationCandidateValidationRequest request, ApplicationCandidateRuntimeReport report,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        var update = await reviews.ReadAsync(host, request.Candidate, cancellationToken);
        var reportFingerprint = ApplicationCandidateWorkflowRuntimeValidator.ReportFingerprint(report);
        if (update is null || !RuntimeCovers(request, report, update)
            || !await runtime.CurrentAsync(request, report, reportFingerprint, host, cancellationToken)) return false;
        validation.DependenciesJson = Dependencies(update);
        validation.DependencyFingerprint = update.Fingerprint;
        validation.DependenciesComplete = true;
        validation.DependencyEvidenceReference = update.Review.Task.Snapshot.CompletionEvidenceReference;
        validation.ReuseEvidenceReference = update.Review.Task.Snapshot.CompletionEvidenceReference;
        validation.ManualPacketResultFingerprint = update.Review.Judgment.ManualResultFingerprint;
        validation.AlternativesJson = Alternatives(update);
        validation.Outcome = "valid";
        validation.DiagnosticsJson = "[]";
        return true;
    }

    internal async Task<ApplicationCandidateReviewedWorkflowUpdateEvidence?> VerifyAsync(
        InteractionInvocationHost host, ApplicationCandidateValidationRequest request,
        ApplicationCandidateRuntimeReport report, ApplicationCandidateValidationRecord validation,
        CancellationToken cancellationToken)
    {
        var update = await reviews.ReadAsync(host, request.Candidate, cancellationToken);
        return update is not null && Matches(request, report, validation, update)
            && await runtime.CurrentAsync(request, report,
                ApplicationCandidateWorkflowRuntimeValidator.ReportFingerprint(report), host, cancellationToken)
            ? update : null;
    }

    internal static bool Matches(ApplicationCandidateValidationRequest request,
        ApplicationCandidateRuntimeReport report, ApplicationCandidateValidationRecord validation,
        ApplicationCandidateReviewedWorkflowUpdateEvidence update) =>
        RuntimeCovers(request, report, update)
        && validation.Outcome == "valid" && validation.DependenciesComplete
        && validation.DependencyFingerprint == update.Fingerprint
        && validation.DependenciesJson == Dependencies(update)
        && validation.DependencyEvidenceReference == update.Review.Task.Snapshot.CompletionEvidenceReference
        && validation.ReuseEvidenceReference == update.Review.Task.Snapshot.CompletionEvidenceReference
        && validation.ManualPacketResultFingerprint == update.Review.Judgment.ManualResultFingerprint
        && validation.AlternativesJson == Alternatives(update);

    private static bool RuntimeCovers(ApplicationCandidateValidationRequest request,
        ApplicationCandidateRuntimeReport report, ApplicationCandidateReviewedWorkflowUpdateEvidence update)
    {
        var dependencies = update.Closure.Dependencies.Select(value =>
            new ApplicationCandidateDependency(value.DefinitionId, value.Revision, value.ContentFingerprint));
        return report.Status == ApplicationCandidateRuntimeStatus.Completed
            && report.Candidate == update.Closure.Candidate
            && report.SelectionEvidenceFingerprint == update.Closure.EvidenceFingerprint
            && report.RuntimePolicyVersion == ApplicationCandidateWorkflowRuntimeValidator.PolicyVersion
            && report.RuntimePolicyFingerprint == ApplicationCandidateWorkflowRuntimeValidator.PolicyFingerprint
            && report.Diagnostics.Count == 0 && report.Dependencies is not null
            && report.Dependencies.SequenceEqual(dependencies)
            && report.Samples.Count == request.Samples.Count
            && report.Samples.Select((sample, index) => (sample, index)).All(value =>
                value.sample.SampleIndex == value.index
                && value.sample.Definition == update.Closure.Definition
                && value.sample.Outcome == ApplicationCandidateRuntimeStatus.Completed
                && value.sample.Attempted && value.sample.CompletionBoundary is not null);
    }

    private static string Dependencies(ApplicationCandidateReviewedWorkflowUpdateEvidence update) =>
        InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(update.Closure.Dependencies.Select(value =>
            new ApplicationCandidateDependency(value.DefinitionId, value.Revision, value.ContentFingerprint))));

    private static string Alternatives(ApplicationCandidateReviewedWorkflowUpdateEvidence update)
    {
        var reasons = update.Review.Judgment.Assessments.ToDictionary(value => value.Target,
            value => value.Reason);
        return InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
            update.Review.Input.Alternatives.Select(value => new ApplicationDefinitionAlternative(
                value.DefinitionId, value.Revision, value.ContentFingerprint,
                reasons.TryGetValue(value, out var reason) ? reason : update.Review.Judgment.Reason))));
    }
}
