using DantesRoleplay.AI;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>One bounded AI computation. The caller owns durable lease and publication.</summary>
internal sealed class SystemInnerWorkerProcedureInvoker(ISystemAiAgentService ai)
{
    internal Task<AiResponse> InvokeAsync(
        SystemInnerWorkerResolvedProfile profile,
        SystemInnerWorkerPreparedRequest prepared,
        SystemCapabilityInvocationContext context,
        IAiInvocationLifecycle lifecycle,
        ISystemCapabilityAiWriteApprovalGate writeApproval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(writeApproval);
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow
            || prepared.OutputSchemaFingerprint != profile.OutputSchemaFingerprint
            || prepared.Profile != profile.Profile
            || !prepared.SelectedContextReferences.SequenceEqual(profile.RequiredContextReferences)
            || !prepared.Request.AllowedTools!.SequenceEqual(
                profile.ToolBindings.Select(value => value.Definition.Name).Order(StringComparer.Ordinal)))
            throw new AiLifecycleException("INNER_AI_INVOCATION_SCOPE_MISMATCH",
                "The focused worker preparation no longer matches its enrolled profile.");
        return ai.SendAsync(profile.Profile, prepared.Request, context, lifecycle,
            writeApprovalGate: writeApproval, toolApprovalGate: null, cancellationToken);
    }
}
