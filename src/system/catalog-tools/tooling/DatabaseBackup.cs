using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tools;

public static class DatabaseBackup
{
    public static string Create(string databasePath, string? outputPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var sourcePath = Path.GetFullPath(databasePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"No database at '{sourcePath}'.", sourcePath);

        var backupPath = outputPath is null
            ? TimestampedPath(sourcePath)
            : Path.GetFullPath(outputPath);
        if (string.Equals(sourcePath, backupPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The backup path must differ from the source database.");
        if (File.Exists(backupPath))
            throw new IOException($"The backup already exists at '{backupPath}'.");

        var directory = Path.GetDirectoryName(backupPath)
            ?? throw new InvalidOperationException("The backup path has no parent directory.");
        Directory.CreateDirectory(directory);
        var ownsBackup = false;
        try
        {
            // Claim the output atomically so a concurrent process cannot appear after the
            // existence check and have its file replaced by SQLite's backup operation.
            using (new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            ownsBackup = true;
            using (var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False"))
            using (var destination = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate;Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            using var verification = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
            verification.Open();
            using var command = verification.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The completed backup did not pass SQLite integrity_check.");
            return backupPath;
        }
        catch
        {
            if (ownsBackup && File.Exists(backupPath)) File.Delete(backupPath);
            throw;
        }
    }

    private static string TimestampedPath(string databasePath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        var candidate = $"{databasePath}.backup-{timestamp}";
        return File.Exists(candidate) ? $"{candidate}-{Guid.NewGuid():n}" : candidate;
    }
}
