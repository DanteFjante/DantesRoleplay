using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Ephemeral computation only, never a task state or validation attestation. Response preserves the
/// actual runner result even when its structured judgment is rejected. A caller must check Judgment
/// and FailureCode, not Response.Ok alone. Plan 04 owns fencing, persistence and publication.
/// </summary>
internal sealed record SystemInnerWorkerValidationComputation(
    AiResponse Response, ApplicationCandidateReuseJudgmentOutputV2? Judgment, string FailureCode);

/// <summary>
/// Uses only the required-lifecycle AI path. The owning caller obtains the lifecycle from the actual
/// lease/profile factory after owner coverage and authority checks; passing an interface or DTO here
/// establishes neither. No provider is enabled or registered by this adapter, and it never retries.
/// </summary>
internal sealed class SystemInnerWorkerValidationInvoker(IAiService ai, TimeProvider time)
{
    internal async Task<SystemInnerWorkerValidationComputation> InvokeAsync(SystemInnerWorkerResolvedProfile profile,
        ApplicationCandidateReuseInputV2 input, AiRequest hostConfiguration, IAiInvocationLifecycle lifecycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        cancellationToken.ThrowIfCancellationRequested();
        var request = SystemInnerWorkerValidationRequestBuilder.Build(profile, input, hostConfiguration,
            time.GetUtcNow().UtcDateTime);
        var response = await ai.SendAgentRequestAsync(profile.Profile, request, [], lifecycle, cancellationToken);
        if (!response.Ok)
            return new(response, null, string.IsNullOrWhiteSpace(response.ErrorCode) ? "WORKER_VALIDATION_RUN_FAILED" : response.ErrorCode);
        if (response.StructuredData is null)
            return new(response, null, "WORKER_VALIDATION_OUTPUT_INVALID");
        try
        {
            return new(response, ApplicationCandidateReuseJudgmentOutputV2.Parse(
                response.StructuredData.Value.GetRawText(), input), "");
        }
        catch (Exception exception) when (exception is JsonException or InteractionContractException or InvalidOperationException)
        {
            return new(response, null, "WORKER_VALIDATION_OUTPUT_INVALID");
        }
    }
}
