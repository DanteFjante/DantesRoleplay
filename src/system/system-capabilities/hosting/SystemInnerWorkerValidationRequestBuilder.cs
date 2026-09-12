using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Pure request shaping from already selected host inputs, not a completeness or authorization check.
/// The lifecycle owner must validate real selection evidence and current authority before dispatch.
/// Its admission callback bounds the actual serialized provider request, including the AI runner's
/// system prompt, schema, and escaping, to 64 KiB. This builder neither duplicates that prompt nor
/// establishes that final byte bound. Input is never truncated and no provider is invoked here.
/// </summary>
internal static class SystemInnerWorkerValidationRequestBuilder
{
    internal static AiRequest Build(SystemInnerWorkerResolvedProfile profile, ApplicationCandidateReuseInputV2 input,
        AiRequest hostConfiguration, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hostConfiguration);
        if (nowUtc.Kind != DateTimeKind.Utc || profile.Worker.InvocationHost.Budget.DeadlineUtc <= nowUtc)
            throw new InteractionContractException("WORKER_VALIDATION_DEADLINE_EXPIRED", "Candidate validation has no remaining deadline.");
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ApplicationCandidateValidation)
            throw new InteractionContractException("WORKER_VALIDATION_SUBJECT_INVALID", "Candidate validation requires a candidate subject.");
        if (profile.Worker.InputJson != InteractionCanonicalJson.CanonicalizeObject(input.ModelInputJson)
            || profile.Worker.ResultSchemaJson != InteractionCanonicalJson.CanonicalizeObject(ApplicationCandidateReuseJudgmentOutputV2.OutputSchema(input))
            || profile.ManualContext.Fingerprint != input.ManualResultFingerprint)
            throw new InteractionContractException("WORKER_VALIDATION_INPUT_MISMATCH", "Candidate validation evidence does not match the selected worker.");
        if (string.IsNullOrWhiteSpace(hostConfiguration.Provider) || string.IsNullOrWhiteSpace(hostConfiguration.Model)
            || !Enum.IsDefined(hostConfiguration.Reasoning) || hostConfiguration.MaximumOutputTokens < 1
            || hostConfiguration.MaximumToolRounds < 0 || hostConfiguration.MaximumToolCalls is < 0 or > 16
            || hostConfiguration.MaximumResponseBytes is < 1 or > 1_048_576
            || hostConfiguration.MaximumDuration is { } d && (d <= TimeSpan.Zero || d > TimeSpan.FromMinutes(10)))
            throw new InteractionContractException("WORKER_HOST_CONFIGURATION_INVALID", "The host AI configuration is invalid.");
        var duration = hostConfiguration.MaximumDuration ?? TimeSpan.FromMinutes(10);
        duration = TimeSpan.FromTicks(Math.Min(duration.Ticks, (profile.Worker.InvocationHost.Budget.DeadlineUtc - nowUtc).Ticks));
        return hostConfiguration with { Messages = [new(AiMessageRole.User, input.ModelInputJson)], Kind = AiRequestKind.Task,
            ResponseSchemaJson = ApplicationCandidateReuseJudgmentOutputV2.OutputSchema(input), AllowedTools = [], MaximumToolCalls = 0,
            MaximumToolRounds = 0, MaximumResponseBytes = Math.Min(hostConfiguration.MaximumResponseBytes, ApplicationCandidateReuseJudgmentLimits.OutputUtf8Bytes),
            MaximumDuration = duration, MaximumOutputTokens = Math.Min(hostConfiguration.MaximumOutputTokens, 131_072) };
    }
}
