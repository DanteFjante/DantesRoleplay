using DantesRoleplay.DataAccess;
using DantesRoleplay.SqliteInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.Projections.Tests;

public sealed class SqliteChangeRecoveryTests
{
    [Fact]
    public async Task Repeated_installation_preserves_schema_recovery_stamp_and_data_without_writes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SqliteChangeRecovery.CreateSql + "CREATE TABLE fixture (value TEXT); INSERT INTO fixture VALUES ('retained');";
        await command.ExecuteNonQueryAsync();
        await SqliteChangeRecovery.InstallAsync(connection);
        var before = await SqliteChangeRecovery.ReadAsync(connection, null);
        Assert.NotNull(before);
        command.CommandText = "SELECT total_changes()";
        var writes = await command.ExecuteScalarAsync();
        await SqliteChangeRecovery.InstallAsync(connection);
        await SqliteChangeRecovery.InstallAsync(connection);
        Assert.Equal(writes, await command.ExecuteScalarAsync());
        Assert.Equal(before, await SqliteChangeRecovery.ReadAsync(connection, null));
        command.CommandText = "SELECT value FROM fixture";
        Assert.Equal("retained", await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Installation_repairs_missing_or_modified_triggers(bool replace)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SqliteChangeRecovery.CreateSql + "CREATE TABLE fixture (value TEXT);";
        await command.ExecuteNonQueryAsync();
        await SqliteChangeRecovery.InstallAsync(connection);
        var before = (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value;
        command.CommandText = "DROP TRIGGER system_change_recovery_fixture_INSERT;" + (replace
            ? "CREATE TRIGGER system_change_recovery_fixture_INSERT AFTER INSERT ON fixture BEGIN SELECT 1; END;"
            : "");
        await command.ExecuteNonQueryAsync();
        Assert.Null(await SqliteChangeRecovery.ReadAsync(connection, null));
        await SqliteChangeRecovery.InstallAsync(connection);
        var repaired = (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value;
        Assert.True(repaired.OtherVersion > before.OtherVersion);
        foreach (var sql in new[] { "INSERT INTO fixture VALUES ('first')", "UPDATE fixture SET value='second'", "DELETE FROM fixture" })
        {
            var previous = (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
            Assert.Equal(previous.OtherVersion + 1,
                (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value.OtherVersion);
        }
    }

    [Fact]
    public async Task Derived_fts5_index_is_not_instrumented_as_canonical_object_state()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SqliteChangeRecovery.CreateSql + "CREATE VIRTUAL TABLE fixture USING fts5(value);";
        await command.ExecuteNonQueryAsync();
        await SqliteChangeRecovery.InstallAsync(connection);
        foreach (var sql in new[] { "INSERT INTO fixture VALUES ('first')", "UPDATE fixture SET value='second'", "DELETE FROM fixture" })
        {
            var before = (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
            Assert.Equal(before, (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value);
        }
        var committed = await SqliteChangeRecovery.ReadAsync(connection, null);
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT INTO fixture VALUES ('rollback')";
            await command.ExecuteNonQueryAsync();
            await transaction.RollbackAsync();
        }
        Assert.Equal(committed, await SqliteChangeRecovery.ReadAsync(connection, null));
    }

    [Fact]
    public async Task Migration_preserves_existing_data_and_installs_transactional_coverage_on_actual_schema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260907032619_ScheduledAiTaskWorkQueue");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE retained_fixture (value TEXT); INSERT INTO retained_fixture VALUES ('preserved')");
        await db.Database.MigrateAsync();
        Assert.Null(await SqliteChangeRecovery.ReadAsync(connection, null));
        await SqliteChangeRecovery.InstallAsync(connection);
        var before = (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM retained_fixture";
        Assert.Equal("preserved", await command.ExecuteScalarAsync());
        foreach (var sql in new[] { "INSERT INTO retained_fixture VALUES ('new')", "UPDATE retained_fixture SET value='changed'", "DELETE FROM retained_fixture" })
        {
            var previous = (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value;
            await db.Database.ExecuteSqlRawAsync(sql);
            Assert.True((await SqliteChangeRecovery.ReadAsync(connection, null))!.Value.OtherVersion > previous.OtherVersion);
        }
        var committed = await SqliteChangeRecovery.ReadAsync(connection, null);
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT INTO retained_fixture VALUES ('rollback')";
            await command.ExecuteNonQueryAsync();
            await transaction.RollbackAsync();
        }
        Assert.Equal(committed, await SqliteChangeRecovery.ReadAsync(connection, null));
        Assert.True(committed!.Value.OtherVersion > before.OtherVersion);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE later_web_fixture (value TEXT)");
        Assert.Null(await SqliteChangeRecovery.ReadAsync(connection, null));
        await SqliteChangeRecovery.InstallAsync(connection);
        var reinstalled = (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value;
        await db.Database.ExecuteSqlRawAsync("INSERT INTO later_web_fixture VALUES ('covered')");
        Assert.Equal(reinstalled.OtherVersion + 1, (await SqliteChangeRecovery.ReadAsync(connection, null))!.Value.OtherVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>()
            .MigrateAsync("20260907032619_ScheduledAiTaskWorkQueue"));
    }
}
