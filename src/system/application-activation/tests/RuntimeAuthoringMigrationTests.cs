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
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO system_task_root_budget (root_task_id, maximum_operations, consumed_operations)
            VALUES ('retained-budget', 16, 3);
            """);
        await Assert.ThrowsAsync<SqliteException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        var retained = await db.Set<SystemTaskRootBudgetRecord>().SingleAsync();
        Assert.Equal(3, retained.ConsumedOperations);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    private static DantesRoleplayDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options);
}
