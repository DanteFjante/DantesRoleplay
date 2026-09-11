using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskDurableService : ISystemTaskDurableReadbackService
{
    private static readonly JsonSerializerOptions ReadbackJson = new(JsonSerializerDefaults.Web);

    public Task<InteractionInvocationResult> ReadAsync(InteractionInvocationHost invocationHost,
        SystemTaskDurableHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return InOwnedTransactionAsync(invocationHost, write: false, async (store, connection, transaction) =>
        {
            var snapshot = await store.ReadInTransactionAsync(handle, connection, transaction, cancellationToken);
            if (snapshot is null || !TaskScopeMatches(invocationHost, snapshot.Request)) return NotAuthorized();
            var denied = await AuthorizeAsync(invocationHost, snapshot.Request.WorkflowDefinition,
                StandingGrantCapability.ReadTask, TaskTarget(snapshot.Request), cancellationToken, snapshot.Request.ActivationOrigin);
            if (denied is not null) return denied;
            var status = await ToReadbackAsync(snapshot, connection, transaction, cancellationToken);
            return ReadbackResult(invocationHost, status);
        }, cancellationToken);
    }

    public Task<InteractionInvocationResult> ListChildrenAsync(InteractionInvocationHost invocationHost,
        SystemTaskDurableHandle parent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return InOwnedTransactionAsync(invocationHost, write: false, async (store, connection, transaction) =>
        {
            var root = await store.ReadInTransactionAsync(parent, connection, transaction, cancellationToken);
            if (root is null || !TaskScopeMatches(invocationHost, root.Request)) return NotAuthorized();
            var denied = await AuthorizeAsync(invocationHost, root.Request.WorkflowDefinition,
                StandingGrantCapability.ReadTask, TaskTarget(root.Request), cancellationToken, root.Request.ActivationOrigin);
            if (denied is not null) return denied;
            var handles = new List<SystemTaskDurableHandle>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT task_id, command_id FROM system_task_lifecycle
                    WHERE parent_task_id = $parent ORDER BY task_id LIMIT 17
                    """;
                command.Parameters.AddWithValue("$parent", parent.TaskId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) handles.Add(new(reader.GetString(0), reader.GetString(1)));
            }
            if (handles.Count > SystemTaskLifecycleLimits.MaximumChildrenPerTask)
                return InteractionInvocationResult.Unavailable("SYSTEM_TASK_GRAPH_UNAVAILABLE", "The task graph exceeds its supported bound.");
            var children = new List<SystemTaskDurableReadback>();
            foreach (var handle in handles)
            {
                var child = await store.ReadInTransactionAsync(handle, connection, transaction, cancellationToken);
                if (child is null)
                    return InteractionInvocationResult.Unavailable("SYSTEM_TASK_GRAPH_UNAVAILABLE", "The task graph cannot currently be resolved.");
                if (!TaskScopeMatches(invocationHost, child.Request)) continue;
                denied = await AuthorizeAsync(invocationHost, child.Request.WorkflowDefinition,
                    StandingGrantCapability.ReadTask, TaskTarget(child.Request), cancellationToken, child.Request.ActivationOrigin);
                // A denied child's metadata is omitted. Missing owner support is explicit rather
                // than silently claiming that a historical child no longer exists.
                if (denied?.Tag == InteractionInvocationResultTag.Unavailable) return denied;
                if (denied is not null) continue;
                children.Add(await ToReadbackAsync(child, connection, transaction, cancellationToken));
            }
            return ReadbackResult(invocationHost, new SystemTaskDurableChildrenReadback(parent, children.AsReadOnly()));
        }, cancellationToken);
    }

    private static async Task<SystemTaskDurableReadback> ToReadbackAsync(SystemTaskLifecycleSnapshot snapshot,
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var hasCalls = await HasHostCallsAsync(snapshot.Request.Handle, connection, transaction, cancellationToken);
        var pendingAi = await SqliteSystemTaskLifecycleStore.HasUnresolvedAiAccountingAsync(connection, transaction,
            snapshot.Request.Handle.TaskId, cancellationToken);
        // Reuse actual result construction/validation, not just the lifecycle's state flag.
        var completionAvailable = !hasCalls && !pendingAi && PollResult(snapshot).Tag == InteractionInvocationResultTag.Completed;
        return new(snapshot.Request.Handle, snapshot.State.ToString().ToLowerInvariant(), snapshot.AttemptCount,
            snapshot.CancellationRequested, snapshot.CancellationAcknowledged, snapshot.Request.WorkflowDefinition,
            snapshot.CreatedAtUtc, snapshot.UpdatedAtUtc, snapshot.CompletedAtUtc, snapshot.Checkpoint?.Checkpoint,
            completionAvailable, hasCalls ? "SYSTEM_TASK_COMMIT_EVIDENCE_UNAVAILABLE" : pendingAi ? "INNER_AI_RECONCILIATION_REQUIRED" : snapshot.ErrorCode,
            hasCalls || pendingAi || snapshot.State == SystemTaskLifecycleState.Indeterminate,
            completionAvailable ? Array.AsReadOnly(new[] { snapshot.CompletionEvidenceReference! }) : Array.Empty<string>());
    }

    private static InteractionInvocationResult ReadbackResult<T>(InteractionInvocationHost host, T data)
    {
        var json = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(data, ReadbackJson));
        var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/system-task-readback/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                principal = host.Principal.PrincipalId,
                applicationId = host.ApplicationRevision.ApplicationId.Value,
                applicationRevision = host.ApplicationRevision.Revision,
                applicationFingerprint = host.ApplicationRevision.Fingerprint,
                host.StateSpaceId,
                host.StateRevision,
                host.GrantReference,
                data = json
            })));
        // This reference identifies the status computation, never a job completion or commit.
        return InteractionInvocationResult.CompletedComputation(json, "task-readback." + fingerprint.ToLowerInvariant());
    }
}
