using DantesRoleplay.Tools;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dantesroleplay-backup-{Guid.NewGuid():n}");

    public DatabaseBackupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Backup_is_consistent_and_does_not_checkpoint_or_change_the_source()
    {
        var sourcePath = Path.Combine(_root, "source.db");
        var backupPath = Path.Combine(_root, "copies", "snapshot.db");
        using (var source = Open(sourcePath))
        {
            Execute(source, "PRAGMA journal_mode=WAL;");
            Execute(source, "CREATE TABLE records (value TEXT NOT NULL);");
            Execute(source, "INSERT INTO records VALUES ('preserved');");
            Assert.Equal(backupPath, DatabaseBackup.Create(sourcePath, backupPath));
            using var backup = Open(backupPath);
            Assert.Equal("preserved", Scalar(backup, "SELECT value FROM records;"));
            Assert.Equal("preserved", Scalar(source, "SELECT value FROM records;"));
        }
    }

    [Fact]
    public void Backup_refuses_to_overwrite_or_replace_the_source()
    {
        var sourcePath = Path.Combine(_root, "source.db");
        using (var source = Open(sourcePath)) Execute(source, "CREATE TABLE records (value TEXT);");
        Assert.Throws<InvalidOperationException>(() => DatabaseBackup.Create(sourcePath, sourcePath));
        var occupied = Path.Combine(_root, "occupied.db");
        File.WriteAllText(occupied, "keep");
        Assert.Throws<IOException>(() => DatabaseBackup.Create(sourcePath, occupied));
        Assert.Equal("keep", File.ReadAllText(occupied));
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }
}
