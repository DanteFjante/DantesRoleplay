using DantesRoleplay.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.Information.Tests;

public sealed class InformationMetadataSchemaMigrationTests
{
    private const string Previous = "20260911222713_DurableProcedureWorkflowTriggers";
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Migration_backfills_exact_source_revision_and_preserves_schema_binding_constraints()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DantesRoleplayDbContext(
            new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO information_source
                (Id, ScopeId, Name, Description, MetadataSchemaJson, ContentHash, Revision, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('source.migration', 'local.notes', 'Notes', '', '{{}}', '__HASH__', 3,
                '2026-09-12T00:00:00Z', '2026-09-12T00:00:00Z');
            INSERT INTO information_record
                (Id, SourceId, Title, Content, MetadataJson, ContentHash, Revision, CreatedAtUtc, UpdatedAtUtc)
            VALUES ('record.migration', 'source.migration', 'Record', 'Preserved', '{{}}', '__HASH__', 2,
                '2026-09-12T00:00:00Z', '2026-09-12T00:00:00Z');
            CREATE TABLE information_migration_trigger_probe (observed INTEGER NOT NULL);
            INSERT INTO information_migration_trigger_probe (observed) VALUES (0);
            CREATE TRIGGER preserve_information_migration_trigger
            AFTER UPDATE OF Content ON information_record
            BEGIN
                UPDATE information_migration_trigger_probe SET observed = observed + 1;
            END;
            """.Replace("__HASH__", Hash, StringComparison.Ordinal));

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(3L, await ScalarAsync(connection,
            "SELECT MetadataSchemaSourceRevision FROM information_record WHERE Id='record.migration'"));
        Assert.Equal(3L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM pragma_table_info('information_source')
            WHERE name IN ('MetadataSchemaQualifiedId','MetadataSchemaVersion','MetadataSchemaHash')
            """));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('information_record') WHERE \"table\"='information_source'"));
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE information_record SET Content=Content WHERE Id='record.migration'");
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT observed FROM information_migration_trigger_probe"));
        var partialReference = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE information_source
            SET MetadataSchemaQualifiedId='demo.schema.metadata'
            WHERE Id='source.migration'
            """));
        Assert.Equal(19, partialReference.SqliteErrorCode);
        var invalidPin = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
            UPDATE information_record SET MetadataSchemaSourceRevision=0 WHERE Id='record.migration'
            """));
        Assert.Equal(19, invalidPin.SqliteErrorCode);

        var downgrade = await Assert.ThrowsAsync<SqliteException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Contains("retained_information_metadata_schema_prevents_downgrade", downgrade.Message);
        Assert.Equal(3L, await ScalarAsync(connection,
            "SELECT MetadataSchemaSourceRevision FROM information_record WHERE Id='record.migration'"));
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='preserve_information_migration_trigger'"));
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
