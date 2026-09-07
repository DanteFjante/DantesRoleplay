using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SqliteInfrastructure;

// Shared infrastructure source for the two SQLite-owning assemblies, not the domain kernel.
internal static class SqliteChangeRecovery
{
    internal const string CreateSql = """
        CREATE TABLE system_change_recovery (
            Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
            StateVersion INTEGER NOT NULL CHECK (typeof(StateVersion) = 'integer' AND StateVersion >= 0),
            OtherVersion INTEGER NOT NULL CHECK (typeof(OtherVersion) = 'integer' AND OtherVersion >= 0),
            SchemaVersion INTEGER NOT NULL);
        INSERT INTO system_change_recovery VALUES (1, 0, 0, -1);
        """;

    internal readonly record struct Stamp(long StateVersion, long OtherVersion, long SchemaVersion);

    // Called only at explicit schema-initialization boundaries. Schema drift makes readers fail closed
    // until all real tables (including the separately migrated web schema) have coverage again.
    internal static async Task InstallAsync(DbConnection connection, CancellationToken ct = default)
    {
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(ct);
        try
        {
            using var transaction = ((SqliteConnection)connection).BeginTransaction(deferred: false);
            if (await ScalarAsync(connection, transaction,
                    "SELECT count(*) FROM sqlite_schema WHERE name='system_change_recovery' AND type='table'", ct) == 0)
                return; // Never silently migrate an older database.
            var tables = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' ORDER BY name";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
            }
            foreach (var table in tables.Where(name => !name.StartsWith("sqlite_", StringComparison.Ordinal)
                         && !name.StartsWith("__", StringComparison.Ordinal)
                         && name is not ("system_change_recovery" or "system_application_object_change" or "operation")))
            {
                var counter = table is "system_ecs_entity" or "system_ecs_component"
                    or "system_ecs_containment" or "system_ecs_relationship" ? "StateVersion" : "OtherVersion";
                foreach (var action in new[] { "INSERT", "UPDATE", "DELETE" })
                {
                    var trigger = Quote($"system_change_recovery_{table}_{action}");
                    await ExecuteAsync(connection, transaction,
                        $"DROP TRIGGER IF EXISTS {trigger}; CREATE TRIGGER {trigger} AFTER {action} ON {Quote(table)} "
                        + $"BEGIN UPDATE system_change_recovery SET {counter}={counter}+1 WHERE Id=1; END;", ct);
                }
            }
            var schema = await ScalarAsync(connection, transaction, "PRAGMA schema_version", ct);
            await ExecuteAsync(connection, transaction,
                $"UPDATE system_change_recovery SET SchemaVersion={schema}, OtherVersion=OtherVersion+1 WHERE Id=1", ct);
            await transaction.CommitAsync(ct);
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    internal static async Task<Stamp?> ReadAsync(DbConnection connection, DbTransaction? transaction,
        CancellationToken ct = default)
    {
        if (await ScalarAsync(connection, transaction,
                "SELECT count(*) FROM sqlite_schema WHERE name='system_change_recovery' AND type='table'", ct) == 0)
            return null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT StateVersion, OtherVersion, SchemaVersion FROM system_change_recovery
            WHERE Id=1 AND SchemaVersion=(SELECT schema_version FROM pragma_schema_version)
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)) : null;
    }

    // The caller holds SQLite's writer reservation. Only its own ECS delta is covered, never
    // earlier commits, non-ECS writes, or mutations made by subsequent transaction participants.
    internal static async Task AcknowledgeAsync(DbConnection connection, DbTransaction transaction,
        Stamp? before, Stamp? after, string operationId, CancellationToken ct)
    {
        if (before is null || after is null || before.Value.SchemaVersion != after.Value.SchemaVersion) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE system_change_recovery SET StateVersion=StateVersion-$delta
            WHERE Id=1 AND SchemaVersion=$schema AND SchemaVersion=(SELECT schema_version FROM pragma_schema_version)
              AND EXISTS(SELECT 1 FROM system_application_object_change WHERE OperationId=$operation)
            """;
        Add(command, "$delta", after.Value.StateVersion - before.Value.StateVersion);
        Add(command, "$schema", before.Value.SchemaVersion);
        Add(command, "$operation", operationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
    private static async Task<long> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }
    private static async Task ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
