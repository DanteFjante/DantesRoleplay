using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemTasks;

/// <summary>Opaque identity for durable task readback and cancellation.</summary>
public sealed record SystemTaskDurableHandle
{
    [JsonConstructor]
    public SystemTaskDurableHandle(string taskId, string commandId)
    {
        TaskId = InteractionGuard.Identifier(taskId, nameof(taskId));
        CommandId = InteractionGuard.IdempotencyKey(commandId);
    }

    public string TaskId { get; }
    public string CommandId { get; }
}

/// <summary>Serializable continuation data; this deliberately contains no executable runtime state.</summary>
public sealed record SystemTaskCheckpoint
{
    [JsonConstructor]
    public SystemTaskCheckpoint(string checkpoint, string completionHandler, string correlationId, string stateJson)
    {
        Checkpoint = InteractionGuard.Identifier(checkpoint, nameof(checkpoint));
        CompletionHandler = InteractionGuard.Identifier(completionHandler, nameof(completionHandler));
        CorrelationId = InteractionGuard.Identifier(correlationId, nameof(correlationId));
        StateJson = InteractionCanonicalJson.CanonicalizeObject(stateJson);
    }

    public string Checkpoint { get; }
    public string CompletionHandler { get; }
    public string CorrelationId { get; }
    public string StateJson { get; }
}

/// <summary>A stable command and one fenced lease attempt. Retrying never changes CommandId.</summary>
public sealed record SystemTaskAttemptIdentity
{
    [JsonConstructor]
    public SystemTaskAttemptIdentity(string stableCommandId, string attemptId, string leaseToken, long fencingCounter,
        DateTime leaseExpiresAtUtc)
    {
        StableCommandId = InteractionGuard.IdempotencyKey(stableCommandId);
        AttemptId = InteractionGuard.IdempotencyKey(attemptId);
        if (StringComparer.Ordinal.Equals(StableCommandId, AttemptId))
            throw new InteractionContractException("INVALID_ATTEMPT_IDENTITY", "The attempt ID must differ from the stable command ID.");
        LeaseToken = InteractionGuard.Bounded(leaseToken, InteractionContractLimits.IdempotencyKey,
            "INVALID_LEASE_TOKEN", nameof(leaseToken));
        if (fencingCounter < 1)
            throw new InteractionContractException("INVALID_FENCING_COUNTER", "The fencing counter must be positive.", nameof(fencingCounter));
        if (leaseExpiresAtUtc.Kind != DateTimeKind.Utc)
            throw new InteractionContractException("INVALID_LEASE_EXPIRY", "The lease expiry must be UTC.", nameof(leaseExpiresAtUtc));
        FencingCounter = fencingCounter;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public string StableCommandId { get; }
    public string AttemptId { get; }
    public string LeaseToken { get; }
    public long FencingCounter { get; }
    public DateTime LeaseExpiresAtUtc { get; }
}

/// <summary>The host-selected definition retained by a task; authored input cannot choose it.</summary>
public sealed record SystemTaskSelectedDefinition
{
    [JsonConstructor]
    public SystemTaskSelectedDefinition(string exactDefinitionId, int version, string fingerprint)
    {
        ExactDefinitionId = InteractionGuard.Identifier(exactDefinitionId, nameof(exactDefinitionId));
        if (version < 1)
            throw new InteractionContractException("INVALID_DEFINITION_VERSION", "The selected definition version must be positive.", nameof(version));
        Version = version;
        Fingerprint = InteractionGuard.UpperSha256(fingerprint, nameof(fingerprint));
    }

    public string ExactDefinitionId { get; }
    public int Version { get; }
    public string Fingerprint { get; }
}

/// <summary>
/// A generic durable submission. InvocationHost is host-only authority and its JSON converter rejects
/// deserialization, while the remaining values are safe to retain as a durable request record.
/// </summary>
public sealed record SystemTaskDurableSubmissionRequest
{
    public SystemTaskDurableSubmissionRequest(InteractionInvocationHost invocationHost,
        SystemTaskSelectedDefinition selectedDefinition, string inputJson, SystemTaskCheckpoint? checkpoint = null,
        IReadOnlyList<SystemTaskDurableHandle>? dependencyHandles = null)
    {
        InvocationHost = invocationHost ?? throw new ArgumentNullException(nameof(invocationHost));
        SelectedDefinition = selectedDefinition ?? throw new ArgumentNullException(nameof(selectedDefinition));
        InputJson = InteractionCanonicalJson.CanonicalizeObject(inputJson);
        Checkpoint = checkpoint;
        var dependencies = dependencyHandles?.ToArray() ?? [];
        if (dependencies.Length > InteractionContractLimits.DependenciesPerStep)
            throw new InteractionContractException("TOO_MANY_TASK_DEPENDENCIES", "The task dependency collection exceeds its bound.");
        if (dependencies.Any(handle => handle is null) || dependencies.Select(handle => handle.TaskId).Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
            throw new InteractionContractException("INVALID_TASK_DEPENDENCIES", "Task dependencies must be non-null and have distinct task IDs.");
        DependencyHandles = Array.AsReadOnly(dependencies);
    }

    public InteractionInvocationHost InvocationHost { get; }
    public SystemTaskSelectedDefinition SelectedDefinition { get; }
    public string InputJson { get; }
    public SystemTaskCheckpoint? Checkpoint { get; }
    public IReadOnlyList<SystemTaskDurableHandle> DependencyHandles { get; }
}

public interface ISystemTaskDurableService
{
    Task<InteractionInvocationResult> SubmitAsync(SystemTaskDurableSubmissionRequest request,
        CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> GetAsync(InteractionInvocationHost invocationHost, SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default);

    Task<InteractionInvocationResult> CancelAsync(InteractionInvocationHost invocationHost, SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default);
}
