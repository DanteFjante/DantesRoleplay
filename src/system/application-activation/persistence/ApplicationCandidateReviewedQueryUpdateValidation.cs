using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Retains and rechecks the exact existing-query closure and independent review.</summary>
internal sealed class ApplicationCandidateReviewedQueryUpdateValidation(
    ApplicationCandidateReviewedQueryUpdateReader reviews)
{
    internal const string PreparationVersion = "reviewed-query-v1";

    internal async Task<bool> CompleteAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, ApplicationCandidateValidationRecord validation,
        CancellationToken cancellationToken)
    {
        var reviewed = await reviews.ReadAsync(host, candidate, cancellationToken);
        if (reviewed is null) return false;
        validation.DependenciesJson = Dependencies(reviewed);
        validation.DependencyFingerprint = reviewed.Fingerprint;
        validation.DependenciesComplete = true;
        validation.DependencyEvidenceReference = reviewed.Task.Snapshot.CompletionEvidenceReference;
        validation.PreparedEvidenceReference = reviewed.Task.Snapshot.CompletionEvidenceReference;
        validation.ReuseEvidenceReference = reviewed.Task.Snapshot.CompletionEvidenceReference;
        validation.PreparationVersion = PreparationVersion;
        validation.ManualPacketResultFingerprint = reviewed.Judgment.ManualResultFingerprint;
        validation.AlternativesJson = Alternatives(reviewed);
        validation.Outcome = "valid";
        validation.DiagnosticsJson = "[]";
        return true;
    }

    internal async Task<ApplicationCandidateReviewedQueryUpdateEvidence?> VerifyAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        var reviewed = await reviews.ReadAsync(host, candidate, cancellationToken);
        return reviewed is not null && Matches(validation, reviewed) ? reviewed : null;
    }

    internal static bool Matches(ApplicationCandidateValidationRecord validation,
        ApplicationCandidateReviewedQueryUpdateEvidence reviewed) =>
        validation.Outcome == "valid" && validation.DependenciesComplete
        && validation.PreparationVersion == PreparationVersion
        && validation.DependencyFingerprint == reviewed.Fingerprint
        && validation.DependenciesJson == Dependencies(reviewed)
        && validation.DependencyEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.PreparedEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.ReuseEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.ManualPacketResultFingerprint == reviewed.Judgment.ManualResultFingerprint
        && validation.AlternativesJson == Alternatives(reviewed);

    private static string Dependencies(ApplicationCandidateReviewedQueryUpdateEvidence reviewed) =>
        InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
            reviewed.Closure.Dependencies.Add(reviewed.Closure.Predecessor)
                .Distinct().OrderBy(value => value.DefinitionId, StringComparer.Ordinal)
                .Select(value => new ApplicationCandidateDependency(
                    value.DefinitionId, value.Revision, value.ContentFingerprint))));

    private static string Alternatives(ApplicationCandidateReviewedQueryUpdateEvidence reviewed)
    {
        var reasons = reviewed.Judgment.Assessments.ToDictionary(value => value.Target, value => value.Reason);
        return InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(reviewed.Input.Alternatives.Select(value =>
            new ApplicationDefinitionAlternative(value.DefinitionId, value.Revision, value.ContentFingerprint,
                reasons.TryGetValue(value, out var reason) ? reason : reviewed.Judgment.Reason))));
    }
}
