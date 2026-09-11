using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>Trusted, single-invocation approval for writes already admitted by the durable lifecycle.</summary>
internal sealed class SystemInnerWorkerWriteApprovalGate(
    SystemTaskLease lease,
    SystemInnerWorkerResolvedProfile profile,
    SystemCapabilityInvocationContext toolContext,
    Func<CancellationToken, Task> reauthorize) : ISystemCapabilityAiWriteApprovalGate
{
    private readonly ConcurrentDictionary<string, AiToolDispatchDescriptor> admitted = new(StringComparer.Ordinal);

    internal void RecordAdmittedTool(AiToolDispatchDescriptor dispatch) =>
        admitted[ToolCorrelation(dispatch.Invocation.CallId)] = dispatch;

    public async Task<SystemCapabilityAiApprovalDecision> ConfirmAsync(
        SystemCapabilityAiApprovalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await reauthorize(cancellationToken);
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow workflow
            || request.Capability.Mode != SystemCapabilityMode.Write
            || !request.Capability.RequiresConfirmation || !request.Capability.RequiresIdempotencyKey
            || !request.Capability.ProcedureIds.Contains(workflow.ProcedureVersion.ExactDefinitionId,
                StringComparer.Ordinal)
            || !InvocationMatches(request.Invocation))
            return SystemCapabilityAiApprovalDecision.Denied();
        var binding = profile.ToolBindings.SingleOrDefault(value =>
            value.CapabilityVersion.ExactDefinitionId == request.Capability.Id);
        if (binding is null || binding.CapabilityVersion.Version != request.Capability.Version
            || binding.CapabilityVersion.Fingerprint != request.Capability.Fingerprint
            || !TryConsume(request, binding, out var dispatch))
            return SystemCapabilityAiApprovalDecision.Denied();
        var input = InteractionCanonicalJson.CanonicalizeObject(request.Arguments.GetRawText());
        var executionEvidence = InteractionCanonicalJson.CanonicalizeObject(
            request.Preflight.ExecutionEvidenceJson);
        var token = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "dantes-roleplay/inner-worker-write/v1\n" + lease.Request.Handle.TaskId + "\n"
            + lease.Request.Handle.CommandId + "\n" + lease.Attempt.AttemptId + "\n"
            + lease.Attempt.FencingCounter + "\n" + dispatch.DispatchOrdinal + "\n"
            + request.Capability.Id + "\n" + request.Capability.Fingerprint + "\n"
            + request.Preflight.PreconditionFingerprint + "\n" + executionEvidence + "\n" + input)))[..32];
        return new(true, token,
            $"Execute {request.Capability.Id} for retained worker {lease.Request.Handle.TaskId}.");
    }

    private bool InvocationMatches(SystemCapabilityInvocationContext actual) =>
        actual.Principal == toolContext.Principal
        && actual.Scope == toolContext.Scope
        && actual.ApplicationId == toolContext.ApplicationId
        && actual.StateSpaceId == toolContext.StateSpaceId
        && actual.ResolutionFingerprint == toolContext.ResolutionFingerprint
        && actual.CorrelationId.StartsWith("ai-tool:", StringComparison.Ordinal);

    private bool TryConsume(SystemCapabilityAiApprovalRequest request,
        SystemInnerWorkerToolBinding binding, out AiToolDispatchDescriptor dispatch)
    {
        var correlation = request.Invocation.CorrelationId;
        if (!admitted.TryGetValue(correlation, out dispatch!)) return false;
        var matches = dispatch.Definition == binding.Definition
            && dispatch.Invocation.Name == binding.Definition.Name
            && InteractionCanonicalJson.CanonicalizeObject(dispatch.Invocation.Arguments.GetRawText())
                == InteractionCanonicalJson.CanonicalizeObject(request.Arguments.GetRawText());
        return matches && admitted.TryRemove(
            new KeyValuePair<string, AiToolDispatchDescriptor>(correlation, dispatch));
    }

    private static string ToolCorrelation(string callId)
    {
        var value = $"ai-tool:{callId}";
        return value.Length <= 128 ? value : value[..128];
    }
}
