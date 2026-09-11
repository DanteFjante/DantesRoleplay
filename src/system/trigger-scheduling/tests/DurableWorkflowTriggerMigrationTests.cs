using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class DurableWorkflowTriggerMigrationTests
{
    private const string Previous = "20260911195508_SystemTaskLifecycleOrigins";
    private const string Current = "20260911222713_DurableProcedureWorkflowTriggers";
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Upgrade_and_empty_downgrade_preserve_trigger_rows_foreign_keys_and_custom_triggers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        SeedApplication(db);
        await SeedNotificationTriggerAsync(db);

        await db.Database.MigrateAsync();

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_one_time_definition"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_fire_work"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_fire_receipt"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_workflow_trigger_parent'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));

        await db.GetService<IMigrator>().MigrateAsync(Previous);

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_one_time_definition"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_fire_work"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_fire_receipt"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_workflow_trigger_parent'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
        await db.Database.MigrateAsync();
        Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Downgrade_refuses_retained_workflow_binding_without_removing_schema_or_history()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.MigrateAsync();
        SeedApplication(db);
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO trigger_one_time_definition
                (ApplicationId, Id, Version, DueAtUtc, MisfirePolicy, Target, Lifecycle,
                 NotificationTopic, NotificationSubject, NotificationBody, RecordedAtUtc)
            VALUES ('jobs', 'jobs.workflow', 1, '2026-09-12T12:00:00Z', 'fire-once',
                'procedure-workflow', 'active', 'scheduled.reminder', 'Workflow', '',
                '2026-09-12T11:00:00Z');
            INSERT INTO trigger_one_time_workflow_binding
                (ApplicationId, TriggerId, TriggerVersion, PrincipalReference, AuthenticationMethod,
                 ApplicationRevision, ApplicationFingerprint, BaseApplicationsJson, StateSpaceId,
                 GrantReference, StateRevision, DefinitionId, DefinitionVersion, DefinitionFingerprint,
                 ExecutionRequestJson, MaximumOperations, RuntimeWindowSeconds, BindingFingerprint)
            VALUES ('jobs', 'jobs.workflow', 1, 'principal.' || lower('{Hash}'), 'fixture', 1,
                '{Hash}', '[]', 'state.jobs', 'grant.jobs', 'state.revision', 'jobs.procedure', 1,
                '{Hash}', char(123) || char(125), 4, 60, '{Hash}');
            """);

        var rejected = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(Previous));

        Assert.Contains("retained_durable_workflow_triggers_prevent_downgrade", rejected.Message);
        Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_one_time_workflow_binding"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_one_time_definition WHERE Target='procedure-workflow'"));
    }

    [Fact]
    public async Task Late_upgrade_guard_failure_rolls_back_schema_and_history_then_allows_retry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        SeedApplication(db);
        await SeedNotificationTriggerAsync(db);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO trigger_one_time_current (ApplicationId,Id,CurrentVersion) VALUES ('jobs','jobs.missing',1)");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON");

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.MigrateAsync());

        Assert.DoesNotContain(Current, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name='trigger_one_time_workflow_binding'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT instr(sql, '\"Target\" IN (''notification-only'', ''procedure-workflow'')') FROM sqlite_schema WHERE type='table' AND name='trigger_one_time_definition'"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM trigger_one_time_definition"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_workflow_trigger_parent'"));

        await db.Database.ExecuteSqlRawAsync("DELETE FROM trigger_one_time_current WHERE Id='jobs.missing'");
        await db.Database.MigrateAsync();
        Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }

    private static async Task SeedNotificationTriggerAsync(DantesRoleplayDbContext db) =>
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_one_time_definition
                (ApplicationId, Id, Version, DueAtUtc, MisfirePolicy, Target, Lifecycle,
                 NotificationTopic, NotificationSubject, NotificationBody, RecordedAtUtc)
            VALUES ('jobs', 'jobs.notification', 1, '2026-09-12T12:00:00Z', 'fire-once',
                'notification-only', 'active', 'jobs.notification', 'Notification', '',
                '2026-09-12T11:00:00Z');
            INSERT INTO trigger_one_time_current (ApplicationId, Id, CurrentVersion)
            VALUES ('jobs', 'jobs.notification', 1);
            INSERT INTO trigger_fire_work
                (FireId, ApplicationId, TriggerId, TriggerVersion, OccurrenceAtUtc, State,
                 AttemptCount, FailureKind, Revision, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('trigger-fire.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'jobs', 'jobs.notification', 1,
                '2026-09-12T12:00:00Z', 'completed', 1, NULL, 1,
                '2026-09-12T12:00:00Z', '2026-09-12T12:00:00Z');
            INSERT INTO trigger_fire_receipt
                (Id, ApplicationId, TriggerId, TriggerVersion, OccurrenceAtUtc, Disposition, RecordedAtUtc)
            VALUES ('trigger-fire.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'jobs', 'jobs.notification', 1,
                '2026-09-12T12:00:00Z', 'due', '2026-09-12T12:00:00Z');
            CREATE TABLE durable_trigger_migration_probe (observed INTEGER NOT NULL);
            INSERT INTO durable_trigger_migration_probe (observed) VALUES (0);
            CREATE TRIGGER preserve_workflow_trigger_parent
            AFTER UPDATE OF NotificationBody ON trigger_one_time_definition
            BEGIN UPDATE durable_trigger_migration_probe SET observed=observed+1; END;
            """);

    private static void SeedApplication(DantesRoleplayDbContext db) =>
        new SqliteApplicationRegistry(db).Register(new(ApplicationIdentifier.Parse("jobs"),
            "Jobs", "Durable workflow migration fixture.", []));

    private static DantesRoleplayDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options);

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
