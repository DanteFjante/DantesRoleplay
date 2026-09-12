using System.Data;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskLifecycleReadTransactionTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Read_uses_exact_handle_and_never_commits_the_caller_transaction()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var staged = await store.StageEnqueueAsync(Request(fixture, "command.uncommitted"), false,
            connection, transaction);

        Assert.NotNull(await store.ReadInTransactionAsync(staged.Handle!, connection, transaction));
        Assert.Null(await store.ReadInTransactionAsync(new(staged.Handle!.TaskId, "command.wrong"), connection, transaction));
        await transaction.RollbackAsync();
        Assert.Null(await store.ReadAsync(staged.Handle));
    }

    [Fact]
    public async Task Cancellation_targets_follow_only_declared_parent_edges_and_exclude_dependencies()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var dependency = (await store.EnqueueAsync(Request(fixture, "command.dependency"))).Handle!;
        var root = (await store.EnqueueAsync(Request(fixture, "command.root"))).Handle!;
        var propagating = (await store.EnqueueAsync(Request(fixture, "command.propagating", root.CommandId,
            [dependency]), propagateCancellation: true)).Handle!;
        var stopped = (await store.EnqueueAsync(Request(fixture, "command.stopped", root.CommandId),
            propagateCancellation: false)).Handle!;
        var belowStopped = (await store.EnqueueAsync(Request(fixture, "command.below-stopped", stopped.CommandId),
            propagateCancellation: true)).Handle!;
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);

        var targets = await store.ReadCancellationTargetsAsync(root, true, connection, transaction);
        Assert.Equal(new[] { root.TaskId, propagating.TaskId }.OrderBy(value => value, StringComparer.Ordinal),
            targets.Select(value => value.Request.Handle.TaskId));
        Assert.DoesNotContain(targets, value => value.Request.Handle == dependency);
        Assert.DoesNotContain(targets, value => value.Request.Handle == stopped);
        Assert.DoesNotContain(targets, value => value.Request.Handle == belowStopped);
        Assert.Equal([root.TaskId], (await store.ReadCancellationTargetsAsync(root, false, connection, transaction))
            .Select(value => value.Request.Handle.TaskId));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Cancellation_target_read_rejects_scope_crossing_and_oversize_retained_graphs()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var root = (await store.EnqueueAsync(Request(fixture, "command.root"))).Handle!;
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await CloneChildrenAsync(connection, transaction, root, count: 65);
        await ExecuteAsync(connection, transaction,
            "UPDATE system_task_lifecycle SET state_space_id = 'state.crossed' WHERE command_id = 'command.clone.00'");

        var scope = await Assert.ThrowsAsync<SystemTaskException>(() =>
            store.ReadCancellationTargetsAsync(root, true, connection, transaction));
        Assert.Equal("SYSTEM_TASK_CANCELLATION_SCOPE_MISMATCH", scope.Code);
        await ExecuteAsync(connection, transaction,
            "UPDATE system_task_lifecycle SET state_space_id = 'state.1' WHERE command_id = 'command.clone.00'");
        var bound = await Assert.ThrowsAsync<SystemTaskException>(() =>
            store.ReadCancellationTargetsAsync(root, true, connection, transaction));
        Assert.Equal("SYSTEM_TASK_DESCENDANT_LIMIT", bound.Code);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Accessors_require_the_supplied_transaction_to_belong_to_the_open_connection()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var handle = (await store.EnqueueAsync(Request(fixture, "command.validation"))).Handle!;
        await using var first = new SqliteConnection(fixture.ConnectionString);
        await using var second = new SqliteConnection(fixture.ConnectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var transaction = (SqliteTransaction)await first.BeginTransactionAsync(IsolationLevel.Serializable);

        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadInTransactionAsync(handle, second, transaction));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCancellationTargetsAsync(handle, true, second, transaction));
        await transaction.RollbackAsync();
    }

    private static async Task CloneChildrenAsync(SqliteConnection connection, SqliteTransaction transaction,
        SystemTaskDurableHandle root, int count)
    {
        for (var index = 0; index < count; index++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO system_task_lifecycle(
                    task_id, command_id, payload_fingerprint, parent_task_id, parent_command_id,
                    root_task_id, parent_depth, propagate_cancellation, state, principal_reference,
                    authentication_method, application_id, application_revision, application_fingerprint,
                    base_applications_json, state_space_id, grant_reference, state_revision,
                    execution_profile, admitted_operations, deadline_utc, definition_id,
                    definition_version, definition_fingerprint, input_json, checkpoint_name,
                    completion_handler, correlation_id, checkpoint_state_json, wake_json,
                    attempt_count, consecutive_failures, consumed_operations, fencing_counter,
                    lease_owner, lease_token, lease_expires_at_utc, next_attempt_at_utc,
                    cancel_requested, cancel_acknowledged, result_json,
                    completion_evidence_reference, evidence_json, error_code, safe_message,
                    created_at_utc, updated_at_utc, completed_at_utc)
                SELECT $task, $command, payload_fingerprint, task_id, command_id, root_task_id, 1, 1,
                    state, principal_reference, authentication_method, application_id,
                    application_revision, application_fingerprint, base_applications_json,
                    state_space_id, grant_reference, state_revision, execution_profile,
                    admitted_operations, deadline_utc, definition_id, definition_version,
                    definition_fingerprint, input_json, checkpoint_name, completion_handler,
                    correlation_id, checkpoint_state_json, wake_json, attempt_count,
                    consecutive_failures, consumed_operations, fencing_counter, lease_owner,
                    lease_token, lease_expires_at_utc, next_attempt_at_utc, cancel_requested,
                    cancel_acknowledged, result_json, completion_evidence_reference, evidence_json,
                    error_code, safe_message, created_at_utc, updated_at_utc, completed_at_utc
                FROM system_task_lifecycle WHERE task_id = $root
                """;
            command.Parameters.AddWithValue("$task", $"task.clone.{index:00}");
            command.Parameters.AddWithValue("$command", $"command.clone.{index:00}");
            command.Parameters.AddWithValue("$root", root.TaskId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static SystemTaskDurableSubmissionRequest Request(SystemTaskLifecycleSchemaFixture fixture,
        string command, string? parentCommand = null,
        IReadOnlyList<SystemTaskDurableHandle>? dependencies = null) => new(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", "grant.1", command, "revision.1", InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(16, fixture.TimeProvider.GetUtcNow().AddHours(1).UtcDateTime), parentCommand),
        new("procedure.fixture", 1, Hash), "{}", dependencyHandles: dependencies);
}
