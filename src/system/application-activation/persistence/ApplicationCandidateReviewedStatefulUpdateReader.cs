using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.ApplicationActivation;

internal sealed class ApplicationCandidateReviewedStatefulUpdateReader(
    ApplicationCandidateReviewedClosureReceiptReader receipts)
{
    internal async Task<ApplicationCandidateReviewedStatefulUpdateEvidence?> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var receipt in await receipts.ReadAsync(host, candidate, cancellationToken))
            {
                if (receipt.Authority.ReviewClosure is not ApplicationCandidateStatefulReviewClosureEvidence closure
                    || !ApplicationCandidateReviewedPureUpdateReader.JudgmentSupports(
                        closure.HasNew, receipt.Judgment)) continue;
                return ApplicationCandidateReviewedStatefulUpdateEvidence.Create(
                    closure, receipt.Task, receipt.Judgment, receipt.Input, receipt.CausalCommandId);
            }
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or JsonException or InteractionContractException or ApplicationActivationException
            or SystemTaskException or CryptographicException or InvalidDataException)
        { return null; }
    }
}

internal sealed class ApplicationCandidateReviewedStatefulUpdateEvidence
{
    private ApplicationCandidateReviewedStatefulUpdateEvidence(
        ApplicationCandidateStatefulReviewClosureEvidence closure,
        SystemTaskCompletedValidationProof task, ApplicationCandidateReuseJudgmentOutputV2 judgment,
        ApplicationCandidateReuseInputV2 input, string causalCommandId)
    {
        Closure = closure; Task = task; Judgment = judgment; Input = input; CausalCommandId = causalCommandId;
        Fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/reviewed-stateful-atomic-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Closure.Candidate, basis = Closure.Basis.ActivationFingerprint, Closure.EvidenceFingerprint,
                task = Task.Snapshot.Request.Handle, Task.Snapshot.CompletionEvidenceReference,
                input.InputFingerprint, judgment.SelectionFingerprint, judgment.ManualResultFingerprint,
                judgment.Judgment, causalCommandId
            })));
    }
    internal ApplicationCandidateStatefulReviewClosureEvidence Closure { get; }
    internal SystemTaskCompletedValidationProof Task { get; }
    internal ApplicationCandidateReuseJudgmentOutputV2 Judgment { get; }
    internal ApplicationCandidateReuseInputV2 Input { get; }
    internal string CausalCommandId { get; }
    internal string Fingerprint { get; }
    internal static ApplicationCandidateReviewedStatefulUpdateEvidence Create(
        ApplicationCandidateStatefulReviewClosureEvidence closure, SystemTaskCompletedValidationProof task,
        ApplicationCandidateReuseJudgmentOutputV2 judgment, ApplicationCandidateReuseInputV2 input,
        string causalCommandId) => new(closure, task, judgment, input, causalCommandId);
}

internal sealed class ApplicationCandidateReviewedStatefulUpdateValidation(
    ApplicationCandidateReviewedStatefulUpdateReader reviews,
    ApplicationCandidateStatefulRuntimeValidator runtime)
{
    internal async Task<bool> CompleteAsync(InteractionInvocationHost host,
        ApplicationCandidateStatefulRuntimeReport report, ApplicationCandidateValidationRecord validation,
        CancellationToken cancellationToken)
    {
        var reviewed = await reviews.ReadAsync(host, report.Candidate, cancellationToken);
        if (reviewed is null || !await RuntimeCoversAsync(host, report, reviewed, cancellationToken)) return false;
        Populate(validation, report, reviewed);
        return true;
    }

    internal async Task<ApplicationCandidateReviewedStatefulUpdateEvidence?> VerifyAsync(
        InteractionInvocationHost host, ApplicationCandidateStatefulRuntimeReport report,
        ApplicationCandidateValidationRecord validation, CancellationToken cancellationToken)
    {
        var reviewed = await reviews.ReadAsync(host, report.Candidate, cancellationToken);
        return reviewed is not null && Matches(report, validation, reviewed)
            && await runtime.CurrentAsync(reviewed.Closure, report, host, cancellationToken) ? reviewed : null;
    }

    internal static bool Matches(ApplicationCandidateStatefulRuntimeReport report,
        ApplicationCandidateValidationRecord validation, ApplicationCandidateReviewedStatefulUpdateEvidence reviewed) =>
        report.Candidate == reviewed.Closure.Candidate
        && report.UpdateFingerprint == reviewed.Closure.EvidenceFingerprint
        && report.PolicyVersion == ApplicationCandidateStatefulRuntimeValidator.PolicyVersion
        && report.PolicyFingerprint == ApplicationCandidateStatefulRuntimeValidator.PolicyFingerprint
        && report.RootPredecessor == reviewed.Closure.PredecessorDefinition
        && report.Dependencies.SequenceEqual(Dependencies(reviewed.Closure))
        && validation.Outcome == "valid" && validation.DependenciesComplete
        && validation.DependencyFingerprint == reviewed.Fingerprint
        && validation.DependenciesJson == DependenciesJson(reviewed.Closure)
        && validation.DependencyEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.ReuseEvidenceReference == reviewed.Task.Snapshot.CompletionEvidenceReference
        && validation.ManualPacketResultFingerprint == reviewed.Judgment.ManualResultFingerprint
        && validation.AlternativesJson == Alternatives(reviewed);

    private async Task<bool> RuntimeCoversAsync(InteractionInvocationHost host,
        ApplicationCandidateStatefulRuntimeReport report, ApplicationCandidateReviewedStatefulUpdateEvidence reviewed,
        CancellationToken cancellationToken) => MatchesRuntime(report, reviewed)
        && await runtime.CurrentAsync(reviewed.Closure, report, host, cancellationToken);

    private static bool MatchesRuntime(ApplicationCandidateStatefulRuntimeReport report,
        ApplicationCandidateReviewedStatefulUpdateEvidence reviewed) =>
        report.Candidate == reviewed.Closure.Candidate
        && report.UpdateFingerprint == reviewed.Closure.EvidenceFingerprint
        && report.PolicyVersion == ApplicationCandidateStatefulRuntimeValidator.PolicyVersion
        && report.PolicyFingerprint == ApplicationCandidateStatefulRuntimeValidator.PolicyFingerprint
        && report.RootPredecessor == reviewed.Closure.PredecessorDefinition
        && report.Dependencies.SequenceEqual(Dependencies(reviewed.Closure))
        && report.Samples.Count > 0 && report.Samples.All(value => value.Definition == reviewed.Closure.Definition
            && value.ActualDataFingerprint == value.ExpectedDataFingerprint
            && value.ActualEffectsFingerprint == value.ExpectedEffectsFingerprint);

    private static void Populate(ApplicationCandidateValidationRecord validation,
        ApplicationCandidateStatefulRuntimeReport report, ApplicationCandidateReviewedStatefulUpdateEvidence reviewed)
    {
        validation.DependenciesJson = DependenciesJson(reviewed.Closure);
        validation.DependencyFingerprint = reviewed.Fingerprint;
        validation.DependenciesComplete = true;
        validation.DependencyEvidenceReference = reviewed.Task.Snapshot.CompletionEvidenceReference;
        validation.ReuseEvidenceReference = reviewed.Task.Snapshot.CompletionEvidenceReference;
        validation.ManualPacketResultFingerprint = reviewed.Judgment.ManualResultFingerprint;
        validation.AlternativesJson = Alternatives(reviewed);
        validation.Outcome = "valid";
        validation.DiagnosticsJson = "[]";
    }

    private static IReadOnlyList<StandingGrantDefinitionReference> Dependencies(
        ApplicationCandidateStatefulReviewClosureEvidence closure) => closure.PredecessorDefinition is { } predecessor
        ? closure.Dependencies.Append(predecessor).OrderBy(value => value.DefinitionId, StringComparer.Ordinal).ToArray()
        : closure.Dependencies;
    private static string DependenciesJson(ApplicationCandidateStatefulReviewClosureEvidence closure) =>
        InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(Dependencies(closure)
            .Select(value => new ApplicationCandidateDependency(value.DefinitionId, value.Revision, value.ContentFingerprint))));
    private static string Alternatives(ApplicationCandidateReviewedStatefulUpdateEvidence reviewed)
    {
        var reasons = reviewed.Judgment.Assessments.ToDictionary(value => value.Target, value => value.Reason);
        return InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(reviewed.Input.Alternatives.Select(value =>
            new ApplicationDefinitionAlternative(value.DefinitionId, value.Revision, value.ContentFingerprint,
                reasons.TryGetValue(value, out var reason) ? reason : reviewed.Judgment.Reason))));
    }
}
