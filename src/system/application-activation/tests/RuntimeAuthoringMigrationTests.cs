using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.ApplicationActivation.Tests;

public sealed class RuntimeAuthoringMigrationTests
{
    private const string Previous = "20260911162616_RetainedApplicationActivationEvidence";
    private const string AiAccountingPrevious = "20260911180721_RuntimeAuthoringAndDurableTasks";
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Upgrade_and_empty_downgrade_preserve_existing_application_revisions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        var app = ApplicationIdentifier.Parse("migration-platform");
        var registry = new SqliteApplicationRegistry(db);
        registry.Register(new(app, "Original platform", "Preserve exact registered application data.", []));
        var original = registry.Get(app)!;

        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(original.Fingerprint, registry.Get(app)!.Fingerprint);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        Assert.Empty(await db.Set<InformationContentRevisionRecord>().ToArrayAsync());
        Assert.Empty(await db.Set<SystemTaskLifecycleRecord>().ToArrayAsync());

        await db.GetService<IMigrator>().MigrateAsync(Previous);
        Assert.Equal(original.Fingerprint, registry.Get(app)!.Fingerprint);
        await db.Database.MigrateAsync();
        Assert.Equal(original.Fingerprint, registry.Get(app)!.Fingerprint);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Migrated_lifecycle_runs_real_SQL_and_retained_work_prevents_destructive_downgrade()
    {
        var connectionString = $"Data Source=platform-migration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(AiAccountingPrevious);
        var store = new SqliteSystemTaskLifecycleStore(connectionString, TimeProvider.System);
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("migration-platform"), 1, Hash, []),
            "state.fixture", "grant.fixture", "command.migrated", "revision.fixture",
            InteractionExecutionProfile.Workflow, new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(5)));
        var request = new SystemTaskDurableSubmissionRequest(host, new("migration-platform.work", 1, Hash), "{\"value\":1}");
        var submitted = await store.EnqueueAsync(request);
        Assert.NotNull(submitted.Handle);
        var lease = await store.ClaimNextAsync("worker.migration", TimeSpan.FromSeconds(30));
        Assert.NotNull(lease);
        Assert.True(await store.CompleteAsync(lease!, new SystemTaskTerminalOutcome(
            "{\"value\":2}", "evidence.migration.fixture", [])));
        var result = await store.ReadAsync(submitted.Handle!);
        Assert.Equal(SystemTaskLifecycleState.Completed, result!.State);
        var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

        var rejected = await Assert.ThrowsAsync<SqliteException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Contains("retained_runtime_state_prevents_downgrade", rejected.Message);
        Assert.Equal(before, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(SystemTaskLifecycleState.Completed, (await store.ReadAsync(submitted.Handle!))!.State);
        Assert.Equal(submitted.Handle, (await store.EnqueueAsync(request)).Handle);
    }

    [Fact]
    public async Task Root_budget_evidence_alone_also_prevents_downgrade()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(AiAccountingPrevious);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO system_task_root_budget (root_task_id, maximum_operations, consumed_operations)
            VALUES ('retained-budget', 16, 3);
            """);
        await Assert.ThrowsAsync<SqliteException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        var retained = await db.Set<SystemTaskRootBudgetRecord>().SingleAsync();
        Assert.Equal(3, retained.ConsumedOperations);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Ai_accounting_migration_creates_restricted_evidence_tables_and_preserves_retained_rows()
    {
        var connectionString = $"Data Source=platform-ai-accounting-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(AiAccountingPrevious);
        await db.Database.MigrateAsync();

        Assert.Equal(new[]
        {
            "system_task_ai_ceiling", "system_task_ai_dispatch_evidence",
            "system_task_ai_reservation", "system_task_ai_reservation_ancestor"
        }, await StringsAsync(connection, """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name LIKE 'system_task_ai_%'
            ORDER BY name
            """));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('system_task_ai_ceiling') WHERE \"table\" = 'system_task_lifecycle'"));
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('system_task_ai_reservation')"));
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('system_task_ai_reservation_ancestor')"));

        var rejectedForeignKey = await Assert.ThrowsAsync<SqliteException>(() => InsertAiCeilingAsync(connection, "task.missing"));
        Assert.Equal(19, rejectedForeignKey.SqliteErrorCode);

        var store = new SqliteSystemTaskLifecycleStore(connectionString, TimeProvider.System);
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("migration-platform"), 1, Hash, []),
            "state.fixture", "grant.fixture", "command.ai-accounting", "revision.fixture",
            InteractionExecutionProfile.Workflow, new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(5)));
        var handle = (await store.EnqueueAsync(new SystemTaskDurableSubmissionRequest(host,
            new("migration-platform.work", 1, Hash), "{\"value\":1}"))).Handle!;
        await InsertAiCeilingAsync(connection, handle.TaskId);
        var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

        var rejectedDowngrade = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(AiAccountingPrevious));
        Assert.Contains("retained_task_ai_accounting_prevents_downgrade", rejectedDowngrade.Message);
        Assert.Equal(before, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM system_task_ai_ceiling"));
    }

    private static async Task InsertAiCeilingAsync(SqliteConnection connection, string taskId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO system_task_ai_ceiling(
                task_id, enrollment_fingerprint, profile_id, profile_version, profile_fingerprint,
                grant_reference, grant_revision, grant_fingerprint, definition_id, definition_version,
                definition_fingerprint, output_schema_fingerprint, mode, maximum_provider_tokens,
                maximum_tool_calls, maximum_concurrent_provider_requests, deadline_utc, created_at_utc)
            VALUES ($task, $hash, 'profile.migration', 1, $hash, 'grant.migration', 'revision.migration',
                $hash, 'migration-platform.work', 1, $hash, $hash, 'hard-cap', 1, 0, 1, $now, $now)
            """;
        command.Parameters.AddWithValue("$task", taskId);
        command.Parameters.AddWithValue("$hash", Hash);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> StringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static DantesRoleplayDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options);
}
