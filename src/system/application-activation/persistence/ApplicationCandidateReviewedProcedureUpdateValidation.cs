using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Retains and rechecks the exact procedure closure and independent reuse review.</summary>
internal sealed class ApplicationCandidateReviewedProcedureUpdateValidation(
    ApplicationCandidateReviewedProcedureUpdateReader reviews)
{
    internal const string PreparationVersion = "reviewed-procedure-v1";

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

    internal async Task<ApplicationCandidateReviewedProcedureUpdateEvidence?> VerifyAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        var reviewed = await reviews.ReadAsync(host, candidate, cancellationToken);
        return reviewed is not null && Matches(validation, reviewed) ? reviewed : null;
    }

    internal static bool Matches(ApplicationCandidateValidationRecord validation,
        ApplicationCandidateReviewedProcedureUpdateEvidence reviewed) =>
        validation.Outcome == "valid" && validation.DependenciesComplete
        && validation.PreparationVersion == PreparationVersion
        && validation.DependencyFingerprint == reviewed.Fingerprint
        && validation.DependenciesJson == Dependencies(reviewed)
        && validation.DependencyEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.PreparedEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.ReuseEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.ManualPacketResultFingerprint == reviewed.Judgment.ManualResultFingerprint
        && validation.AlternativesJson == Alternatives(reviewed);

    private static string Dependencies(ApplicationCandidateReviewedProcedureUpdateEvidence reviewed)
    {
        var dependencies = reviewed.Closure.Dependencies;
        if (reviewed.Closure.Predecessor is { } predecessor)
            dependencies = dependencies.Add(predecessor);
        return InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(dependencies
            .Distinct().OrderBy(value => value.DefinitionId, StringComparer.Ordinal)
            .Select(value => new ApplicationCandidateDependency(
                value.DefinitionId, value.Revision, value.ContentFingerprint))));
    }

    private static string Alternatives(ApplicationCandidateReviewedProcedureUpdateEvidence reviewed)
    {
        var reasons = reviewed.Judgment.Assessments.ToDictionary(value => value.Target, value => value.Reason);
        return InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(reviewed.Input.Alternatives.Select(value =>
            new ApplicationDefinitionAlternative(value.DefinitionId, value.Revision, value.ContentFingerprint,
                reasons.TryGetValue(value, out var reason) ? reason : reviewed.Judgment.Reason))));
    }
}
