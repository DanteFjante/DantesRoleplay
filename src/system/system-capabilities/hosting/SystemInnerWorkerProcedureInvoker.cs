using DantesRoleplay.AI;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>One bounded AI computation. The caller owns durable lease and publication.</summary>
internal sealed class SystemInnerWorkerProcedureInvoker(
    ISystemAiAgentService ai,
    SystemInnerWorkerApplicationToolFactory? applicationTools = null)
{
    internal Task<AiResponse> InvokeAsync(
        SystemInnerWorkerResolvedProfile profile,
        SystemInnerWorkerPreparedRequest prepared,
        SystemCapabilityInvocationContext context,
        IAiInvocationLifecycle lifecycle,
        ISystemCapabilityAiWriteApprovalGate writeApproval,
        IReadOnlyList<SystemInnerWorkerApplicationToolSelection>? selectedApplicationTools = null,
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
        var selections = selectedApplicationTools?.ToArray() ?? [];
        if (selections.Length == 0)
            return ai.SendAsync(profile.Profile, prepared.Request, context, lifecycle,
                writeApprovalGate: writeApproval, toolApprovalGate: null, cancellationToken);
        if (ai is not SystemAiAgentService direct || applicationTools is null
            || !selections.Select(value => value.Binding).SequenceEqual(
                profile.ToolBindings.Where(value => value.Kind != SystemInnerWorkerToolKind.SystemCapability)))
            throw new AiLifecycleException("INNER_AI_APPLICATION_TOOLS_UNAVAILABLE",
                "The exact application tool runtime is unavailable.");
        return direct.SendWithToolsAsync(profile.Profile, prepared.Request, context, lifecycle,
            applicationTools.Create(profile, selections), writeApproval, cancellationToken);
    }
}
