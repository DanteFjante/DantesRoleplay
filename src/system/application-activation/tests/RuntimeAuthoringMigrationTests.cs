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
    private const string InformationOwnershipPrevious = "20260911191531_SystemTaskAiAccounting";
    private const string LifecycleOriginsPrevious = "20260911193950_InformationSourceOwnership";
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
    public async Task Lifecycle_origin_migration_preserves_legacy_task_graph_and_safe_rollback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(LifecycleOriginsPrevious);
        const string input = "{\"value\":1,\"nested\":{\"stable\":true}}";
        const string request = "{\"messages\":[]}";

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO system_task_root_budget (root_task_id, maximum_operations, consumed_operations)
            VALUES ('task.migration.root', 16, 4);

            INSERT INTO system_task_lifecycle (
                task_id, command_id, payload_fingerprint, parent_task_id, parent_command_id,
                root_task_id, parent_depth, propagate_cancellation, state, principal_reference,
                authentication_method, application_id, application_revision, application_fingerprint,
                base_applications_json, state_space_id, grant_reference, state_revision,
                execution_profile, admitted_operations, deadline_utc, definition_id,
                definition_version, definition_fingerprint, input_json, created_at_utc, updated_at_utc)
            VALUES
                ('task.migration.root', 'command.migration.root', {Hash}, NULL, NULL,
                 'task.migration.root', 0, 1, 'queued', 'principal.migration', 'fixture',
                 'migration-platform', 3, {Hash}, '[]', 'state.migration', 'grant.migration',
                 'state.revision.migration', 'workflow', 16, '2026-09-12T00:00:00Z',
                 'migration-platform.root', 7, {Hash}, {input},
                 '2026-09-11T20:00:00Z', '2026-09-11T20:00:00Z'),
                ('task.migration.child', 'command.migration.child', {Hash}, 'task.migration.root',
                 'command.migration.root', 'task.migration.root', 1, 1, 'queued',
                 'principal.migration', 'fixture', 'migration-platform', 3, {Hash}, '[]',
                 'state.migration', 'grant.migration', 'state.revision.migration', 'workflow', 8,
                 '2026-09-12T00:00:00Z', 'migration-platform.child', 5, {Hash}, {input},
                 '2026-09-11T20:00:01Z', '2026-09-11T20:00:01Z');

            INSERT INTO system_task_dependency (task_id, dependency_task_id, dependency_command_id)
            VALUES ('task.migration.child', 'task.migration.root', 'command.migration.root');

            INSERT INTO system_task_attempt (
                task_id, ordinal, attempt_id, fencing_counter, lease_token, state, started_at_utc)
            VALUES ('task.migration.child', 1, 'attempt.migration.child', 1,
                    'lease.migration.child', 'waiting', '2026-09-11T20:00:02Z');

            INSERT INTO system_task_checkpoint (
                task_id, sequence, checkpoint_name, completion_handler, correlation_id,
                state_json, status, created_at_utc)
            VALUES ('task.migration.child', 1, 'checkpoint.migration', 'handler.migration',
                    'correlation.migration', char(123) || char(125), 'waiting', '2026-09-11T20:00:02Z');

            INSERT INTO system_task_host_call (
                task_id, operation_id, request_fingerprint, request_json, status,
                attempt_id, fencing_counter, started_at_utc)
            VALUES ('task.migration.child', 'operation.migration.host',
                    'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                    char(123) || char(125), 'pending', 'attempt.migration.child', 1, '2026-09-11T20:00:02Z');

            INSERT INTO system_task_ai_ceiling (
                task_id, enrollment_fingerprint, profile_id, profile_version, profile_fingerprint,
                grant_reference, grant_revision, grant_fingerprint, definition_id, definition_version,
                definition_fingerprint, output_schema_fingerprint, mode, maximum_provider_tokens,
                maximum_tool_calls, maximum_concurrent_provider_requests, deadline_utc, created_at_utc)
            VALUES
                ('task.migration.root', {Hash}, 'profile.migration', 2, {Hash}, 'grant.migration',
                 'revision.migration', {Hash}, 'migration-platform.root', 7, {Hash}, {Hash},
                 'hard-cap', 4096, 2, 1, '2026-09-12T00:00:00Z', '2026-09-11T20:00:00Z'),
                ('task.migration.child', {Hash}, 'profile.migration', 2, {Hash}, 'grant.migration',
                 'revision.migration', {Hash}, 'migration-platform.child', 5, {Hash}, {Hash},
                 'hard-cap', 2048, 1, 1, '2026-09-12T00:00:00Z', '2026-09-11T20:00:01Z');

            INSERT INTO system_task_ai_reservation (
                record_reference, task_id, reservation_id, request_fingerprint, attempt_id,
                fencing_counter, lease_token, lease_expires_at_utc, deadline_utc,
                requested_provider_tokens, reserved_provider_tokens, reserved_tool_calls,
                mode, status, created_at_utc, updated_at_utc)
            VALUES ('reservation.migration', 'task.migration.child', 'reservation.child', {Hash},
                    'attempt.migration.child', 1, 'lease.migration.child',
                    '2026-09-11T20:05:00Z', '2026-09-12T00:00:00Z', 512, 512, 0,
                    'hard-cap', 'reserved', '2026-09-11T20:00:02Z', '2026-09-11T20:00:02Z');

            INSERT INTO system_task_ai_reservation_ancestor (record_reference, ancestor_task_id)
            VALUES ('reservation.migration', 'task.migration.root');

            INSERT INTO system_task_ai_dispatch_evidence (
                record_reference, sequence, event_reference, payload_fingerprint, kind,
                dispatch_kind, request_fingerprint, provider_id, model_id, profile_fingerprint,
                schema_fingerprint, request_json, observed_at_utc)
            VALUES ('reservation.migration', 0, 'event.migration.dispatch', {Hash}, 'dispatch',
                    'provider', {Hash}, 'provider.migration', 'model.migration', {Hash}, {Hash},
                     {request}, '2026-09-11T20:00:03Z');

            CREATE TABLE lifecycle_migration_trigger_probe (observed INTEGER NOT NULL);
            INSERT INTO lifecycle_migration_trigger_probe (observed) VALUES (0);
            CREATE TRIGGER preserve_lifecycle_migration_trigger
            AFTER UPDATE OF input_json ON system_task_lifecycle
            BEGIN
                UPDATE lifecycle_migration_trigger_probe SET observed = observed + 1;
            END;
            """);
        Assert.Empty(await ForeignKeyViolationsAsync(connection));

        await db.Database.MigrateAsync();

        Assert.Equal(new[]
        {
            $"task.migration.child|procedure-workflow|migration-platform.child|5|{Hash}|{input}",
            $"task.migration.root|procedure-workflow|migration-platform.root|7|{Hash}|{input}"
        }, await StringsAsync(connection, """
            SELECT task_id || '|' || purpose || '|' || definition_id || '|' ||
                   definition_version || '|' || definition_fingerprint || '|' || input_json
            FROM system_task_lifecycle ORDER BY task_id
            """));
        Assert.Equal(new[]
        {
            $"task.migration.child|procedure-workflow|migration-platform.child|5|{Hash}",
            $"task.migration.root|procedure-workflow|migration-platform.root|7|{Hash}"
        }, await StringsAsync(connection, """
            SELECT task_id || '|' || task_purpose || '|' || definition_id || '|' ||
                   definition_version || '|' || definition_fingerprint
            FROM system_task_ai_ceiling ORDER BY task_id
            """));
        Assert.Equal(2L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM system_task_lifecycle
            WHERE activation_revision IS NULL AND activation_fingerprint IS NULL
              AND activation_application_revision IS NULL AND activation_application_fingerprint IS NULL
              AND admission_payload_json IS NULL AND candidate_id IS NULL
              AND candidate_revision IS NULL AND candidate_fingerprint IS NULL
              AND causation_operation_id IS NULL
            """));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_dependency WHERE task_id = 'task.migration.child' AND dependency_task_id = 'task.migration.root'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_ai_reservation WHERE task_id = 'task.migration.child' AND attempt_id = 'attempt.migration.child'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_ai_reservation_ancestor WHERE ancestor_task_id = 'task.migration.root'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_ai_dispatch_evidence WHERE request_json = '{\"messages\":[]}'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_checkpoint WHERE task_id = 'task.migration.child' AND status = 'waiting'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_host_call WHERE task_id = 'task.migration.child' AND status = 'pending'"));
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE system_task_lifecycle SET input_json = input_json WHERE task_id = 'task.migration.child'");
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT observed FROM lifecycle_migration_trigger_probe"));
        Assert.Empty(await ForeignKeyViolationsAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys"));

        await db.GetService<IMigrator>().MigrateAsync(LifecycleOriginsPrevious);

        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('system_task_lifecycle') WHERE name = 'purpose'"));
        Assert.Equal(5L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM pragma_table_info('system_task_lifecycle')
            WHERE name IN ('state_space_id','state_revision','definition_id','definition_version','definition_fingerprint')
              AND "notnull" = 1 AND dflt_value IS NULL
            """));
        Assert.Equal(3L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM pragma_table_info('system_task_ai_ceiling')
            WHERE name IN ('definition_id','definition_version','definition_fingerprint')
              AND "notnull" = 1 AND dflt_value IS NULL
            """));
        Assert.Equal(new[]
        {
            $"task.migration.child|migration-platform.child|5|{Hash}|{input}",
            $"task.migration.root|migration-platform.root|7|{Hash}|{input}"
        }, await StringsAsync(connection, """
            SELECT task_id || '|' || definition_id || '|' || definition_version || '|' ||
                   definition_fingerprint || '|' || input_json
            FROM system_task_lifecycle ORDER BY task_id
            """));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('system_task_ai_ceiling') WHERE \"table\" = 'system_task_lifecycle'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_checkpoint WHERE task_id = 'task.migration.child'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_host_call WHERE task_id = 'task.migration.child'"));
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE system_task_lifecycle SET input_json = input_json WHERE task_id = 'task.migration.child'");
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT observed FROM lifecycle_migration_trigger_probe"));
        Assert.Empty(await ForeignKeyViolationsAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task Lifecycle_origin_migration_failure_rolls_back_every_schema_change_and_can_retry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(LifecycleOriginsPrevious);
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO system_task_root_budget (root_task_id, maximum_operations, consumed_operations)
            VALUES ('task.rollback', 16, 0);
            INSERT INTO system_task_lifecycle (
                task_id, command_id, payload_fingerprint, root_task_id, parent_depth,
                propagate_cancellation, state, principal_reference, authentication_method,
                application_id, application_revision, application_fingerprint, base_applications_json,
                state_space_id, grant_reference, state_revision, execution_profile, admitted_operations,
                deadline_utc, definition_id, definition_version, definition_fingerprint, input_json,
                created_at_utc, updated_at_utc)
            VALUES ('task.rollback', 'command.rollback', '{Hash}', 'task.rollback', 0, 1, 'queued',
                    'principal.rollback', 'fixture', 'migration-platform', 1, '{Hash}', '[]',
                    'state.rollback', 'grant.rollback', 'revision.rollback', 'workflow', 16,
                    '2026-09-12T00:00:00Z', 'migration-platform.rollback', 1, '{Hash}',
                    char(123) || char(125),
                    '2026-09-11T20:00:00Z', '2026-09-11T20:00:00Z');
            CREATE INDEX "ix_system_task_lifecycle_causation_operation" ON operation ("Id");
            """);

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.MigrateAsync());

        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('system_task_lifecycle') WHERE name = 'purpose'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM system_task_lifecycle WHERE task_id = 'task.rollback'"));
        Assert.DoesNotContain("20260911195508_SystemTaskLifecycleOrigins",
            await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await ForeignKeyViolationsAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys"));

        await db.Database.ExecuteSqlRawAsync("DROP INDEX ix_system_task_lifecycle_causation_operation");
        await db.Database.MigrateAsync();

        Assert.Equal("procedure-workflow", (await StringsAsync(connection,
            "SELECT purpose FROM system_task_lifecycle WHERE task_id = 'task.rollback'")).Single());
        Assert.Contains("20260911195508_SystemTaskLifecycleOrigins",
            await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await ForeignKeyViolationsAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task Lifecycle_origin_schema_declares_purpose_and_provenance_relationships()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.MigrateAsync();

        Assert.Equal(new[]
        {
            "admission_payload_json|0|",
            "candidate_id|0|",
            "definition_id|0|",
            "purpose|1|'procedure-workflow'",
            "state_space_id|0|"
        }, await StringsAsync(connection, """
            SELECT name || '|' || "notnull" || '|' || COALESCE(dflt_value, '')
            FROM pragma_table_info('system_task_lifecycle')
            WHERE name IN ('purpose','admission_payload_json','candidate_id','definition_id','state_space_id')
            ORDER BY name
            """));
        Assert.Equal(new[]
        {
            "system_task_lifecycle|task_id|task_id",
            "system_task_lifecycle|task_purpose|purpose"
        }, await StringsAsync(connection, """
            SELECT "table" || '|' || "from" || '|' || "to"
            FROM pragma_foreign_key_list('system_task_ai_ceiling') ORDER BY id, seq
            """));
        Assert.Equal(new[] { "operation|causation_operation_id|Id" }, await StringsAsync(connection, """
            SELECT "table" || '|' || "from" || '|' || "to"
            FROM pragma_foreign_key_list('system_task_lifecycle')
            WHERE "from" = 'causation_operation_id'
            """));
        var tableSql = (await StringsAsync(connection, """
            SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'system_task_lifecycle'
            """)).Single();
        Assert.Contains("CK_system_task_lifecycle_purpose", tableSql, StringComparison.Ordinal);
        Assert.Contains("CK_system_task_lifecycle_purpose_shape", tableSql, StringComparison.Ordinal);
        Assert.Contains("CK_system_task_lifecycle_admission_payload_shape", tableSql, StringComparison.Ordinal);
        Assert.Equal(0L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = '__ux_system_task_lifecycle_task_id_purpose_migration'
            """));
        Assert.Empty(await ForeignKeyViolationsAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys"));
    }

    [Theory]
    [InlineData("activated-workflow")]
    [InlineData("application-validation")]
    [InlineData("validation-causation")]
    public async Task Lifecycle_origin_downgrade_guard_runs_before_ddl_for_new_evidence(string evidenceKind)
    {
        var connectionString = $"Data Source=platform-lifecycle-guard-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.MigrateAsync();
        var store = new SqliteSystemTaskLifecycleStore(connectionString, TimeProvider.System);
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "fixture"),
            new ApplicationRevision(ApplicationIdentifier.Parse("migration-platform"), 1, Hash, []),
            "state.fixture", "grant.fixture", "command.guard." + evidenceKind, "revision.fixture",
            InteractionExecutionProfile.Workflow, new InteractionInvocationBudget(16, DateTime.UtcNow.AddMinutes(5)));
        var workflow = (await store.EnqueueAsync(new SystemTaskDurableSubmissionRequest(host,
            new("migration-platform.work", 1, Hash), "{\"value\":1}"))).Handle!;

        if (evidenceKind == "activated-workflow")
        {
            const string proof = "{\"proof\":true}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE system_task_lifecycle
                SET activation_revision = 2,
                    activation_fingerprint = {Hash},
                    activation_application_revision = 1,
                    activation_application_fingerprint = {Hash},
                    admission_payload_json = {proof}
                WHERE task_id = {workflow.TaskId}
                """);
        }
        else
        {
            const string validationTask = "task.validation.migration";
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO system_task_root_budget(root_task_id, maximum_operations, consumed_operations)
                VALUES ('task.validation.migration', 16, 0)
                """);
            await CloneValidationTaskAsync(connection, workflow.TaskId, validationTask);
            await InsertAiCeilingAsync(connection, workflow.TaskId);
            await CloneValidationAiCeilingAsync(connection, workflow.TaskId, validationTask);

            if (evidenceKind == "validation-causation")
            {
                const string operationId = "0123456789abcdef0123456789abcdef";
                db.Operations.Add(new DantesRoleplay.Operations.Operation
                {
                    Id = operationId,
                    Timestamp = DateTime.UtcNow,
                    Tool = "fixture"
                });
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync($"""
                    UPDATE system_task_lifecycle
                    SET causation_operation_id = '{operationId}'
                    WHERE task_id = '{validationTask}'
                    """);
            }
        }

        var rejected = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(LifecycleOriginsPrevious));
        Assert.Contains("retained_task_lifecycle_origins_prevent_downgrade", rejected.Message);
        Assert.Contains("20260911195508_SystemTaskLifecycleOrigins",
            await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('system_task_lifecycle') WHERE name = 'purpose'"));
        Assert.Empty(await ForeignKeyViolationsAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task Migrated_lifecycle_runs_real_SQL_and_retained_work_prevents_destructive_downgrade()
    {
        var connectionString = $"Data Source=platform-migration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(AiAccountingPrevious);
        await db.Database.MigrateAsync();
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
        var retainedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(AiAccountingPrevious, retainedMigrations[^1]);
        Assert.Equal(1L, await ScalarAsync(connection,
            $"SELECT COUNT(*) FROM system_task_lifecycle WHERE task_id = '{submitted.Handle!.TaskId}' AND state = 'completed'"));
        await db.Database.MigrateAsync();
        Assert.Equal(before, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(SystemTaskLifecycleState.Completed, (await store.ReadAsync(submitted.Handle))!.State);
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
        Assert.Equal(2L, await ScalarAsync(connection,
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
        var retainedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(InformationOwnershipPrevious, retainedMigrations[^1]);
        Assert.Equal(before.Where(value => string.CompareOrdinal(value, InformationOwnershipPrevious) <= 0),
            retainedMigrations);
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM system_task_ai_ceiling"));
    }

    [Fact]
    public async Task Information_source_ownership_migration_enforces_foreign_keys_and_preserves_immutable_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync(InformationOwnershipPrevious);
        await db.Database.MigrateAsync();
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('system_information_source_target_identity')"));
        Assert.Equal(5L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('system_information_source_owner_revision')"));
        Assert.Equal(4L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('system_information_source_owner_current')"));

        var foreignKey = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO system_information_source_target_identity
                (QualifiedTargetId, SourceId, CreatedByOperationId)
            VALUES ('target.fixture', 'source.missing', 'operation.missing')
            """));
        Assert.Equal(19, foreignKey.SqliteErrorCode);
        var application = ApplicationIdentifier.Parse("fixtureapp");
        new SqliteApplicationRegistry(db).Register(new(application, "Fixture", "Ownership migration fixture.", []));
        db.Operations.Add(new DantesRoleplay.Operations.Operation
        {
            Id = new string('a', 32), Timestamp = DateTime.UtcNow, Tool = "fixture"
        });
        db.Set<DantesRoleplay.Information.InformationSource>().Add(new()
        {
            Id = "source.fixture", ScopeId = "fixture", Name = "Fixture", ContentHash = Hash,
            Revision = 1, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO system_information_source_target_identity
                (QualifiedTargetId, SourceId, CreatedByOperationId)
            VALUES ('target.fixture', 'source.fixture', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO system_information_source_owner_revision
                (SourceId, Revision, ApplicationId, QualifiedTargetId, ContentFingerprint, PreviousFingerprint, BoundByOperationId)
            VALUES ('source.fixture', 1, 'fixtureapp', 'target.fixture', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', NULL, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'),
                   ('source.fixture', 2, 'fixtureapp', 'target.fixture', 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
            INSERT INTO system_information_source_owner_current (SourceId, Revision, QualifiedTargetId)
            VALUES ('source.fixture', 1, 'target.fixture');
            UPDATE system_information_source_owner_current SET Revision = 2 WHERE SourceId = 'source.fixture';
            """);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE system_information_source_target_identity SET SourceId = 'other' WHERE QualifiedTargetId = 'target.fixture'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM system_information_source_target_identity WHERE QualifiedTargetId = 'target.fixture'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE system_information_source_owner_revision SET ApplicationId = 'other' WHERE SourceId = 'source.fixture' AND Revision = 1"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM system_information_source_owner_revision WHERE SourceId = 'source.fixture' AND Revision = 1"));
        var rejected = await Assert.ThrowsAsync<SqliteException>(() =>
            db.GetService<IMigrator>().MigrateAsync(InformationOwnershipPrevious));
        Assert.Contains("retained_information_source_ownership_prevents_downgrade", rejected.Message);
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT Revision FROM system_information_source_owner_current WHERE SourceId = 'source.fixture'"));
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

    private static async Task CloneValidationTaskAsync(
        SqliteConnection connection,
        string sourceTask,
        string targetTask)
    {
        var columns = await StringsAsync(connection,
            "SELECT name FROM pragma_table_info('system_task_lifecycle') ORDER BY cid");
        var selections = columns.Select(column => column switch
        {
            "task_id" or "root_task_id" => "$target",
            "command_id" => "'command.validation.migration'",
            "purpose" => "'application-validation'",
            "parent_task_id" or "parent_command_id" => "NULL",
            "state_space_id" or "state_revision" or "definition_id" or "definition_version"
                or "definition_fingerprint" or "activation_revision" or "activation_fingerprint"
                or "activation_application_revision" or "activation_application_fingerprint" => "NULL",
            "candidate_id" => "'0123456789abcdef0123456789abcdef'",
            "candidate_revision" => "4",
            "candidate_fingerprint" => "$hash",
            "causation_operation_id" => "NULL",
            "admission_payload_json" => "'{}'",
            _ => Quote(column)
        });
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO system_task_lifecycle ({string.Join(',', columns.Select(Quote))}) "
            + $"SELECT {string.Join(',', selections)} FROM system_task_lifecycle WHERE task_id=$source";
        command.Parameters.AddWithValue("$source", sourceTask);
        command.Parameters.AddWithValue("$target", targetTask);
        command.Parameters.AddWithValue("$hash", Hash);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CloneValidationAiCeilingAsync(
        SqliteConnection connection,
        string sourceTask,
        string targetTask)
    {
        var columns = await StringsAsync(connection,
            "SELECT name FROM pragma_table_info('system_task_ai_ceiling') ORDER BY cid");
        var selections = columns.Select(column => column switch
        {
            "task_id" => "$target",
            "task_purpose" => "'application-validation'",
            "definition_id" or "definition_version" or "definition_fingerprint" => "NULL",
            _ => Quote(column)
        });
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO system_task_ai_ceiling ({string.Join(',', columns.Select(Quote))}) "
            + $"SELECT {string.Join(',', selections)} FROM system_task_ai_ceiling WHERE task_id=$source";
        command.Parameters.AddWithValue("$source", sourceTask);
        command.Parameters.AddWithValue("$target", targetTask);
        await command.ExecuteNonQueryAsync();
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    private static async Task<IReadOnlyList<string>> StringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static Task<IReadOnlyList<string>> ForeignKeyViolationsAsync(SqliteConnection connection) =>
        StringsAsync(connection, """
            SELECT "table" || '|' || rowid || '|' || parent || '|' || fkid
            FROM pragma_foreign_key_check ORDER BY "table", rowid, fkid
            """);

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static DantesRoleplayDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options);
}
