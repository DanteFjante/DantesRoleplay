using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemTasks;

/// <summary>
/// Inert, bounded status data. A completed invocation containing this document means status was
/// computed, not that the job executed or effects committed. CompletionAvailable requires usable
/// result evidence; a stored completed phase alone is insufficient. Evidence references confer no
/// authority. Raw input, checkpoint state, grants, principal snapshots and lease tokens are omitted.
/// </summary>
public sealed record SystemTaskDurableReadback(
    SystemTaskDurableHandle Handle,
    string Phase,
    int AttemptCount,
    bool CancellationRequested,
    bool CancellationAcknowledged,
    SystemTaskSelectedDefinition SelectedDefinition,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    string? CheckpointName,
    bool CompletionAvailable,
    string? DiagnosticCode,
    bool RecoveryRequired,
    IReadOnlyList<string> EvidenceReferences);

/// <summary>At most sixteen authorized direct children; no hidden totals or denied metadata.</summary>
public sealed record SystemTaskDurableChildrenReadback(
    SystemTaskDurableHandle Parent,
    IReadOnlyList<SystemTaskDurableReadback> Children);

public interface ISystemTaskDurableReadbackService
{
    Task<InteractionInvocationResult> ReadAsync(InteractionInvocationHost invocationHost,
        SystemTaskDurableHandle handle, CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> ListChildrenAsync(InteractionInvocationHost invocationHost,
        SystemTaskDurableHandle parent, CancellationToken cancellationToken = default);
}
