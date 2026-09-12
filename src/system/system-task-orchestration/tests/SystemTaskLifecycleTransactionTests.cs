using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

public sealed class SystemTaskLifecycleTransactionTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enqueue_follows_callers_commit_or_rollback_without_orphan_budget(bool commit)
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        await using var connection = await OpenAsync(fixture);
        await using var transaction = connection.BeginTransaction();
        await MarkerAsync(connection, transaction);
        var result = await fixture.CreateStore().StageEnqueueAsync(Request(fixture), false, connection, transaction);
        Assert.Equal(SystemTaskEnqueueDisposition.Created, result.Disposition);
        Assert.Equal(1, await CountAsync(connection, transaction, "system_task_lifecycle"));
        if (commit) await transaction.CommitAsync();
        else await transaction.RollbackAsync();
        Assert.Equal(commit ? 1 : 0, await CountAsync(connection, null, "system_task_lifecycle"));
        Assert.Equal(commit ? 1 : 0, await CountAsync(connection, null, "system_task_root_budget"));
        Assert.Equal(commit ? 1 : 0, await CountAsync(connection, null, "caller_marker"));
        Assert.Equal(commit, await fixture.CreateStore().ReadAsync(result.Handle!) is not null);
    }

    [Fact]
    public async Task Rejected_and_conflicting_enqueue_preserve_callers_work_and_transaction()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var original = (await store.EnqueueAsync(Request(fixture))).Handle!;
        await using var connection = await OpenAsync(fixture);
        await using var transaction = connection.BeginTransaction();
        await MarkerAsync(connection, transaction);
        var conflict = await store.StageEnqueueAsync(Request(fixture, input: "{\"changed\":true}"), false, connection, transaction);
        Assert.Equal(SystemTaskEnqueueDisposition.Conflict, conflict.Disposition);
        var rejected = await store.StageEnqueueAsync(Request(fixture, "command.rejected", profile: InteractionExecutionProfile.Atomic), false, connection, transaction);
        Assert.Equal(SystemTaskEnqueueDisposition.Rejected, rejected.Disposition);
        Assert.Equal(1, await CountAsync(connection, transaction, "caller_marker"));
        await transaction.CommitAsync();
        Assert.Equal(1, await CountAsync(connection, null, "system_task_root_budget"));
        Assert.Equal("{}", (await store.ReadAsync(original))!.Request.InputJson);
    }

    [Fact]
    public async Task Insert_failure_rolls_back_staged_budget_but_keeps_callers_work()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        await using var connection = await OpenAsync(fixture);
        await ExecuteAsync(connection, null, "CREATE TRIGGER reject_task BEFORE INSERT ON system_task_lifecycle BEGIN SELECT RAISE(ABORT, 'injected failure'); END;");
        await using var transaction = connection.BeginTransaction();
        await MarkerAsync(connection, transaction);
        await Assert.ThrowsAsync<SqliteException>(() => fixture.CreateStore().StageEnqueueAsync(Request(fixture), false, connection, transaction));
        Assert.Equal(0, await CountAsync(connection, transaction, "system_task_root_budget"));
        Assert.Equal(0, await CountAsync(connection, transaction, "system_task_lifecycle"));
        Assert.Equal(1, await CountAsync(connection, transaction, "caller_marker"));
        await transaction.CommitAsync();
        Assert.Equal(1, await CountAsync(connection, null, "caller_marker"));
    }

    [Fact]
    public async Task Different_connections_transaction_is_rejected_without_touching_caller()
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        await using var connection = await OpenAsync(fixture);
        await using var other = new SqliteConnection(fixture.ConnectionString);
        await other.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        await MarkerAsync(connection, transaction);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CreateStore().StageEnqueueAsync(Request(fixture), false, other, transaction));
        Assert.Equal(System.Data.ConnectionState.Open, other.State);
        Assert.Equal(1, await CountAsync(connection, transaction, "caller_marker"));
        await transaction.CommitAsync();
    }

    [Theory]
    [InlineData("rollback")]
    [InlineData("partial-failure")]
    [InlineData("foreign-scope")]
    public async Task Cancellation_is_atomic_and_preserves_caller_transaction(string scenario)
    {
        await using var fixture = await SystemTaskLifecycleSchemaFixture.CreateAsync();
        var store = fixture.CreateStore();
        var parent = (await store.EnqueueAsync(Request(fixture))).Handle!;
        var child = (await store.EnqueueAsync(Request(fixture, "command.child", parentCommand: parent.CommandId), true)).Handle!;
        await using var connection = await OpenAsync(fixture);
        if (scenario == "partial-failure")
            await ExecuteAsync(connection, null, "CREATE TRIGGER reject_cancel BEFORE UPDATE OF state ON system_task_lifecycle WHEN NEW.state = 'cancelled' AND NEW.parent_task_id IS NOT NULL BEGIN SELECT RAISE(ABORT, 'injected cancellation failure'); END;");
        if (scenario == "foreign-scope")
            await ExecuteAsync(connection, null, "UPDATE system_task_lifecycle SET state_space_id = 'state.foreign' WHERE parent_task_id IS NOT NULL;");
        await using var transaction = connection.BeginTransaction();
        await MarkerAsync(connection, transaction);
        if (scenario == "rollback")
        {
            Assert.True(await store.StageCancellationAsync(parent, true, connection, transaction));
            Assert.Equal(2, await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM system_task_lifecycle WHERE cancel_acknowledged = 1"));
            await transaction.RollbackAsync();
        }
        else
        {
            if (scenario == "partial-failure")
                await Assert.ThrowsAsync<SqliteException>(() => store.StageCancellationAsync(parent, true, connection, transaction));
            else
                await Assert.ThrowsAsync<SystemTaskException>(() => store.StageCancellationAsync(parent, true, connection, transaction));
            Assert.Equal(0, await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM system_task_lifecycle WHERE cancel_requested = 1 OR cancel_acknowledged = 1"));
            Assert.Equal(1, await CountAsync(connection, transaction, "caller_marker"));
            await transaction.CommitAsync();
        }
        foreach (var handle in new[] { parent, child })
        {
            var snapshot = (await store.ReadAsync(handle))!;
            Assert.Equal(SystemTaskLifecycleState.Queued, snapshot.State);
            Assert.False(snapshot.CancellationRequested);
            Assert.False(snapshot.CancellationAcknowledged);
        }
    }

    private static async Task<SqliteConnection> OpenAsync(SystemTaskLifecycleSchemaFixture fixture)
    {
        var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, "CREATE TABLE caller_marker(value INTEGER NOT NULL);");
        return connection;
    }

    private static Task MarkerAsync(SqliteConnection connection, SqliteTransaction transaction) =>
        ExecuteAsync(connection, transaction, "INSERT INTO caller_marker VALUES (1);");

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CountAsync(SqliteConnection connection, SqliteTransaction? transaction, string table) =>
        ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM " + table);

    private static async Task<long> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static SystemTaskDurableSubmissionRequest Request(SystemTaskLifecycleSchemaFixture fixture,
        string command = "command.staged", string input = "{}", string? parentCommand = null,
        InteractionExecutionProfile profile = InteractionExecutionProfile.Workflow) => new(new(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
            "state.1", "grant.1", command, "revision.1", profile,
            new InteractionInvocationBudget(16, fixture.TimeProvider.GetUtcNow().AddHours(1).UtcDateTime), parentCommand),
        new("procedure.fixture", 1, Hash), input);
}
