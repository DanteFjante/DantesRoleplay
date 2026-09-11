using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>Host-authorized, bounded request for a focused worker. It contains no provider or tool authority.</summary>
public sealed record SystemInnerWorkerRequest
{
    public SystemInnerWorkerRequest(InteractionInvocationHost invocationHost,
        SystemTaskSelectedDefinition procedureVersion, string inputJson, string resultSchemaJson,
        IReadOnlyList<SystemTaskDurableHandle>? dependencyHandles = null)
    {
        InvocationHost = invocationHost ?? throw new ArgumentNullException(nameof(invocationHost));
        ProcedureVersion = procedureVersion ?? throw new ArgumentNullException(nameof(procedureVersion));
        InputJson = InteractionCanonicalJson.CanonicalizeObject(inputJson);
        ResultSchemaJson = InteractionCanonicalJson.CanonicalizeObject(resultSchemaJson);
        var dependencies = dependencyHandles?.ToArray() ?? [];
        if (dependencies.Length > InteractionContractLimits.DependenciesPerStep)
            throw new InteractionContractException("TOO_MANY_WORKER_DEPENDENCIES", "The worker dependency collection exceeds its bound.");
        if (dependencies.Any(handle => handle is null) || dependencies.Select(handle => handle.TaskId).Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
            throw new InteractionContractException("INVALID_WORKER_DEPENDENCIES", "Worker dependencies must be non-null and have distinct task IDs.");
        DependencyHandles = Array.AsReadOnly(dependencies);
    }

    public InteractionInvocationHost InvocationHost { get; }
    public SystemTaskSelectedDefinition ProcedureVersion { get; }
    public string InputJson { get; }
    public string ResultSchemaJson { get; }
    public IReadOnlyList<SystemTaskDurableHandle> DependencyHandles { get; }
}

public interface ISystemInnerWorkerService
{
    Task<InteractionInvocationResult> SubmitAsync(SystemInnerWorkerRequest request,
        CancellationToken cancellationToken = default);
}
