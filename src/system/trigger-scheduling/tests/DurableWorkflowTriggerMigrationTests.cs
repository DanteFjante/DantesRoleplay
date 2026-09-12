using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class DurableWorkflowTriggerMigrationTests
{
    private const string Previous = "20260911195508_SystemTaskLifecycleOrigins";
    private const string Current = "20260911222713_DurableProcedureWorkflowTriggers";
    private const string ResultSchemas = "20260911231305_DurableProcedureWorkflowResultSchemas";
    private const string RecurringWorkflows = "20260911234047_DurableRecurringProcedureWorkflowTriggers";
    private const string ConditionalWorkflowObservers = "20260912081912_DurableConditionalWorkflowObservers";
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
    public async Task Result_schema_upgrade_preserves_legacy_rows_and_downgrade_refuses_new_schema_data()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(Current);
        SeedApplication(db);
        await SeedLegacyWorkflowBindingAsync(db);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE workflow_schema_probe (observed INTEGER NOT NULL);
            INSERT INTO workflow_schema_probe (observed) VALUES (0);
            CREATE TRIGGER preserve_workflow_binding_trigger
            AFTER UPDATE OF RuntimeWindowSeconds ON trigger_one_time_workflow_binding
            BEGIN UPDATE workflow_schema_probe SET observed=observed+1; END;
            """);

        await db.Database.MigrateAsync();

        Assert.Contains(ResultSchemas, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM trigger_one_time_workflow_binding WHERE ResultSchemaJson IS NULL AND ResultSchemaFingerprint IS NULL"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_workflow_binding_trigger'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT instr(sql, 'CK_trigger_one_time_workflow_binding_result_schema') > 0 FROM sqlite_schema WHERE type='table' AND name='trigger_one_time_workflow_binding'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE trigger_one_time_workflow_binding
            SET ResultSchemaJson = '{{"type":"object"}}' WHERE TriggerId = 'jobs.workflow';
            """));

        const string resultSchema = "{\"type\":\"object\"}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trigger_one_time_workflow_binding
            SET ResultSchemaJson = {resultSchema}, ResultSchemaFingerprint = {Hash}
            WHERE TriggerId = 'jobs.workflow';
            """);
        var rejected = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(Current));

        Assert.Contains("retained_workflow_result_schema_prevents_downgrade", rejected.Message);
        Assert.Contains(ResultSchemas, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('trigger_one_time_workflow_binding') WHERE name='ResultSchemaJson'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_workflow_binding_trigger'"));
    }

    [Fact]
    public async Task Recurring_workflow_upgrade_preserves_rows_and_custom_triggers_and_refuses_unsafe_downgrade()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(ResultSchemas);
        SeedApplication(db);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_recurring_definition
                (ApplicationId, Id, Version, Lifecycle, Kind, Interval, LocalTimeSeconds, TimeZoneId,
                 StartDate, EndDate, WeekdaysMask, DayOfMonth, GapPolicy, OverlapPolicy, MisfirePolicy,
                 Target, NotificationTopic, NotificationSubject, NotificationBody,
                 NotificationStateSpaceId, RecordedAtUtc)
            VALUES ('jobs', 'jobs.recurring.legacy', 1, 'active', 'daily', 1, 43200, 'Etc/UTC',
                NULL, NULL, 0, NULL, 'skip', 'earlier', 'fire-once', 'notification-only',
                'scheduled.reminder', 'Legacy recurring', '', NULL, '2026-09-12T11:00:00Z');
            CREATE TABLE recurring_workflow_probe (observed INTEGER NOT NULL);
            INSERT INTO recurring_workflow_probe (observed) VALUES (0);
            CREATE TRIGGER preserve_recurring_definition_trigger
            AFTER UPDATE OF NotificationSubject ON trigger_recurring_definition
            BEGIN UPDATE recurring_workflow_probe SET observed=observed+1; END;
            """);

        await db.Database.MigrateAsync();

        Assert.Contains(RecurringWorkflows, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM trigger_recurring_definition WHERE Id='jobs.recurring.legacy'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_recurring_definition_trigger'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT instr(sql, '\"Target\" IN (''notification-only'', ''procedure-workflow'')') > 0 FROM sqlite_schema WHERE type='table' AND name='trigger_recurring_definition'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));

        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO trigger_recurring_definition
                (ApplicationId, Id, Version, Lifecycle, Kind, Interval, LocalTimeSeconds, TimeZoneId,
                 StartDate, EndDate, WeekdaysMask, DayOfMonth, GapPolicy, OverlapPolicy, MisfirePolicy,
                 Target, NotificationTopic, NotificationSubject, NotificationBody,
                 NotificationStateSpaceId, RecordedAtUtc)
            VALUES ('jobs', 'jobs.recurring.workflow', 1, 'active', 'daily', 1, 43200, 'Etc/UTC',
                NULL, NULL, 0, NULL, 'skip', 'earlier', 'fire-once', 'procedure-workflow',
                'scheduled.reminder', 'Workflow recurring', '', NULL, '2026-09-12T11:00:00Z');
            INSERT INTO trigger_recurring_workflow_binding
                (ApplicationId, TriggerId, TriggerVersion, PrincipalReference, AuthenticationMethod,
                 ApplicationRevision, ApplicationFingerprint, BaseApplicationsJson, StateSpaceId,
                 GrantReference, StateRevision, DefinitionId, DefinitionVersion, DefinitionFingerprint,
                 ExecutionRequestJson, ResultSchemaJson, ResultSchemaFingerprint, MaximumOperations,
                 RuntimeWindowSeconds, BindingFingerprint)
            VALUES ('jobs', 'jobs.recurring.workflow', 1, 'principal.' || lower('{Hash}'), 'fixture', 1,
                '{Hash}', '[]', 'state.jobs', 'grant.jobs', 'state.revision', 'jobs.procedure', 1,
                '{Hash}', char(123) || char(125), char(123) || char(125), '{Hash}', 4, 60, '{Hash}');
            """);

        var rejected = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(ResultSchemas));

        Assert.Contains("retained_recurring_workflow_prevents_downgrade", rejected.Message);
        Assert.Contains(RecurringWorkflows, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM trigger_recurring_workflow_binding"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_recurring_definition_trigger'"));

    }

    [Fact]
    public async Task Empty_recurring_workflow_downgrade_restores_constraint_without_rebuilding_definition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(ResultSchemas);
        SeedApplication(db);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO trigger_recurring_definition
                (ApplicationId, Id, Version, Lifecycle, Kind, Interval, LocalTimeSeconds, TimeZoneId,
                 StartDate, EndDate, WeekdaysMask, DayOfMonth, GapPolicy, OverlapPolicy, MisfirePolicy,
                 Target, NotificationTopic, NotificationSubject, NotificationBody,
                 NotificationStateSpaceId, RecordedAtUtc)
            VALUES ('jobs', 'jobs.recurring.legacy', 1, 'active', 'daily', 1, 43200, 'Etc/UTC',
                NULL, NULL, 0, NULL, 'skip', 'earlier', 'fire-once', 'notification-only',
                'scheduled.reminder', 'Legacy recurring', '', NULL, '2026-09-12T11:00:00Z');
            CREATE TABLE recurring_downgrade_probe (observed INTEGER NOT NULL);
            INSERT INTO recurring_downgrade_probe (observed) VALUES (0);
            CREATE TRIGGER preserve_recurring_downgrade_trigger
            AFTER UPDATE OF NotificationSubject ON trigger_recurring_definition
            BEGIN UPDATE recurring_downgrade_probe SET observed=observed+1; END;
            """);
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(ResultSchemas);

        Assert.DoesNotContain(RecurringWorkflows, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name='trigger_recurring_workflow_binding'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT instr(sql, '\"Target\" = ''notification-only''') > 0 FROM sqlite_schema WHERE type='table' AND name='trigger_recurring_definition'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM trigger_recurring_definition WHERE Id='jobs.recurring.legacy'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='preserve_recurring_downgrade_trigger'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
        await db.Database.MigrateAsync();
        Assert.Contains(RecurringWorkflows, await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Conditional_workflow_upgrade_and_empty_downgrade_preserve_legacy_rows_indexes_foreign_keys_and_triggers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(RecurringWorkflows);
        await SeedLegacyConditionalFireAsync(db);

        await db.Database.MigrateAsync();

        Assert.Contains(ConditionalWorkflowObservers, await db.Database.GetAppliedMigrationsAsync());
        await AssertLegacyConditionalFireAsync(connection, expectObserverColumns: true);
        Assert.Equal(5L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name IN
                ('trigger_conditional_relationship_dependency', 'trigger_conditional_workflow_binding',
                 'trigger_conditional_predicate_binding', 'trigger_causal_allowance',
                 'trigger_causal_reservation')
            """));
        Assert.Equal(1L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM pragma_foreign_key_list('trigger_conditional_fire_work')
            WHERE "table"='trigger_causal_allowance' AND "from"='CausalAllowanceId'
            """));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));

        await db.GetService<IMigrator>().MigrateAsync(RecurringWorkflows);

        Assert.DoesNotContain(ConditionalWorkflowObservers, await db.Database.GetAppliedMigrationsAsync());
        await AssertLegacyConditionalFireAsync(connection, expectObserverColumns: false);
        Assert.Equal(0L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name IN
                ('trigger_conditional_relationship_dependency', 'trigger_conditional_workflow_binding',
                 'trigger_conditional_predicate_binding', 'trigger_causal_allowance',
                 'trigger_causal_reservation')
            """));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check"));

        await db.Database.MigrateAsync();
        Assert.Contains(ConditionalWorkflowObservers, await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Conditional_workflow_downgrade_refuses_procedure_target_without_sidecars()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.MigrateAsync();
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(ApplicationIdentifier.Parse("jobs"),
            "Jobs", "Conditional workflow downgrade fixture.", []));
        new SqliteStateSpaceRegistry(db, applications).Create(new("jobs-space", revision, Hash));
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO trigger_conditional_definition
                (ApplicationId, Id, Version, Lifecycle, Kind, Activation, Rearm, StateSpaceId,
                 AdapterId, AdapterVersion, AdapterConfigurationJson, AdapterConfigurationHash,
                 Target, NotificationTopic, NotificationSubject, NotificationBody,
                 NotificationStateSpaceId, RecordedAtUtc)
            VALUES ('jobs', 'jobs.conditional.workflow', 1, 'active', 'state-condition',
                'rising-edge', 'on-false', 'jobs-space', 'jobs.predicate', 1,
                char(123) || char(125), '{Hash}', 'procedure-workflow', 'jobs.workflow',
                'Workflow', '', NULL, '2026-09-12T11:00:00Z');
            """);

        var rejected = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(RecurringWorkflows));

        Assert.Contains("retained_conditional_workflow_observers_prevent_downgrade", rejected.Message);
        Assert.Contains(ConditionalWorkflowObservers, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM trigger_conditional_definition WHERE Target='procedure-workflow'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name='trigger_causal_allowance'"));
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

    private static async Task SeedLegacyConditionalFireAsync(DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(ApplicationIdentifier.Parse("jobs"),
            "Jobs", "Conditional workflow migration fixture.", []));
        new SqliteStateSpaceRegistry(db, applications).Create(new("jobs-space", revision, Hash));
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO trigger_conditional_definition
                (ApplicationId, Id, Version, Lifecycle, Kind, Activation, Rearm, StateSpaceId,
                 AdapterId, AdapterVersion, AdapterConfigurationJson, AdapterConfigurationHash,
                 Target, NotificationTopic, NotificationSubject, NotificationBody,
                 NotificationStateSpaceId, RecordedAtUtc)
            VALUES ('jobs', 'jobs.conditional.legacy', 1, 'active', 'state-condition',
                'rising-edge', 'on-false', 'jobs-space', 'jobs.predicate', 1,
                char(123) || char(125), '{Hash}', 'notification-only', 'jobs.conditional',
                'Legacy conditional', 'preserved body', NULL, '2026-09-12T11:00:00Z');
            INSERT INTO trigger_conditional_current (ApplicationId, Id, CurrentVersion)
            VALUES ('jobs', 'jobs.conditional.legacy', 1);
            INSERT INTO trigger_conditional_state
                (ApplicationId, TriggerId, CurrentVersion, CurrentTruth, Armed,
                 EvaluationRevision, LastOperationId, LastFiredOperationId, UpdatedAtUtc)
            VALUES ('jobs', 'jobs.conditional.legacy', 1, NULL, 1, 0, NULL, NULL,
                '2026-09-12T11:00:00Z');
            UPDATE trigger_conditional_state SET CurrentTruth=1, Armed=0, EvaluationRevision=1,
                LastOperationId='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                LastFiredOperationId='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                UpdatedAtUtc='2026-09-12T11:01:00Z'
            WHERE ApplicationId='jobs' AND TriggerId='jobs.conditional.legacy';
            INSERT INTO trigger_conditional_fire_work
                (FireId, ApplicationId, TriggerId, TriggerVersion, ChangeOperationId, State,
                 AttemptCount, NextAttemptAtUtc, LeaseOwner, LeaseToken, LeaseExpiresAtUtc,
                 FailureKind, Revision, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('trigger-fire.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'jobs',
                'jobs.conditional.legacy', 1, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'completed',
                1, NULL, NULL, NULL, NULL, NULL, 1,
                '2026-09-12T11:01:00Z', '2026-09-12T11:02:00Z');
            INSERT INTO trigger_conditional_fire_receipt
                (Id, ApplicationId, TriggerId, TriggerVersion, ChangeOperationId,
                 Disposition, RecordedAtUtc)
            VALUES ('trigger-fire.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'jobs',
                'jobs.conditional.legacy', 1, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'due',
                '2026-09-12T11:02:00Z');
            CREATE INDEX preserve_conditional_work_index
                ON trigger_conditional_fire_work(UpdatedAtUtc);
            CREATE TABLE conditional_migration_probe (observed INTEGER NOT NULL);
            INSERT INTO conditional_migration_probe (observed) VALUES (0);
            CREATE TRIGGER preserve_conditional_definition_trigger
            AFTER UPDATE OF NotificationSubject ON trigger_conditional_definition
            BEGIN UPDATE conditional_migration_probe SET observed=observed+1; END;
            CREATE TRIGGER preserve_conditional_work_trigger
            AFTER UPDATE OF UpdatedAtUtc ON trigger_conditional_fire_work
            BEGIN UPDATE conditional_migration_probe SET observed=observed+1; END;
            CREATE TRIGGER preserve_conditional_receipt_trigger
            AFTER UPDATE OF RecordedAtUtc ON trigger_conditional_fire_receipt
            BEGIN UPDATE conditional_migration_probe SET observed=observed+1; END;
            """);
    }

    private static async Task AssertLegacyConditionalFireAsync(
        SqliteConnection connection, bool expectObserverColumns)
    {
        Assert.Equal("notification-only|Legacy conditional|preserved body",
            await TextScalarAsync(connection, """
                SELECT Target || '|' || NotificationSubject || '|' || NotificationBody
                FROM trigger_conditional_definition WHERE Id='jobs.conditional.legacy'
                """));
        Assert.Equal("1|1|0|1|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            await TextScalarAsync(connection, """
                SELECT CurrentVersion || '|' || CurrentTruth || '|' || Armed || '|' || EvaluationRevision
                    || '|' || LastOperationId || '|' || LastFiredOperationId
                FROM trigger_conditional_state WHERE TriggerId='jobs.conditional.legacy'
                """));
        Assert.Equal("completed|1|1|2026-09-12T11:01:00Z|2026-09-12T11:02:00Z",
            await TextScalarAsync(connection, """
                SELECT State || '|' || AttemptCount || '|' || Revision || '|' || CreatedAtUtc || '|' || UpdatedAtUtc
                FROM trigger_conditional_fire_work WHERE TriggerId='jobs.conditional.legacy'
                """));
        Assert.Equal("due|2026-09-12T11:02:00Z", await TextScalarAsync(connection, """
            SELECT Disposition || '|' || RecordedAtUtc FROM trigger_conditional_fire_receipt
            WHERE TriggerId='jobs.conditional.legacy'
            """));
        Assert.Equal(expectObserverColumns ? 5L : 0L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM pragma_table_info('trigger_conditional_fire_work')
            WHERE name IN ('CausalAllowanceId', 'PredicateCaptureJson',
                'PredicateCaptureFingerprint', 'PredicatePriorTruth', 'PredicatePriorArmed')
            """));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='index' AND name='preserve_conditional_work_index'"));
        Assert.Equal(3L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name IN
                ('preserve_conditional_definition_trigger', 'preserve_conditional_work_trigger',
                 'preserve_conditional_receipt_trigger')
            """));
        Assert.Equal(expectObserverColumns ? 1L : 0L, await ScalarAsync(connection, """
            SELECT instr(sql, 'PredicateCaptureJson') > 0 FROM sqlite_schema
            WHERE type='trigger' AND name='trigger_conditional_work_insert_guard'
            """));
    }

    private static async Task SeedLegacyWorkflowBindingAsync(DantesRoleplayDbContext db) =>
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO trigger_one_time_definition
                (ApplicationId, Id, Version, DueAtUtc, MisfirePolicy, Target, Lifecycle,
                 NotificationTopic, NotificationSubject, NotificationBody, RecordedAtUtc)
            VALUES ('jobs', 'jobs.workflow', 1, '2026-09-12T12:00:00Z', 'fire-once',
                'procedure-workflow', 'active', 'scheduled.workflow', 'Workflow', '',
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

    private static async Task<string> TextScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
