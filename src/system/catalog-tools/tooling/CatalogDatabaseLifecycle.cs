using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tools;

/// <summary>
/// Reconstructs the runtime database from reviewed catalog files. This is orchestration only:
/// migrations own schema meaning and CatalogImporter owns record semantics and transactions.
/// </summary>
public static class CatalogDatabaseLifecycle
{
    public static async Task<CatalogDatabaseLifecycleResult> SetupAsync(
        string catalogRoot,
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var root = RequireCatalog(catalogRoot);
        var target = Path.GetFullPath(databasePath);

        if (File.Exists(target))
        {
            throw new InvalidOperationException(
                $"A database already exists at '{target}'. Run `roleplay upgrade` instead.");
        }

        await RequireValidCatalogAsync(root, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException($"Database path '{target}' has no parent directory."));

        try
        {
            var applied = await MigrateImportAndVerifyAsync(root, target, cancellationToken);
            return new CatalogDatabaseLifecycleResult(target, null, applied.Created, applied.Updated);
        }
        catch
        {
            DeleteDatabaseArtifacts(target);
            throw;
        }
    }

    public static async Task<CatalogDatabaseLifecycleResult> UpgradeAsync(
        string catalogRoot,
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var root = RequireCatalog(catalogRoot);
        var target = Path.GetFullPath(databasePath);

        if (!File.Exists(target))
        {
            throw new FileNotFoundException(
                $"No database exists at '{target}'. Run `roleplay setup` instead.", target);
        }

        await RequireValidCatalogAsync(root, cancellationToken);
        var backup = BackupPath(target);
        CreateConsistentBackup(target, backup);

        try
        {
            var applied = await MigrateImportAndVerifyAsync(root, target, cancellationToken);
            return new CatalogDatabaseLifecycleResult(target, backup, applied.Created, applied.Updated);
        }
        catch
        {
            RestoreBackup(backup, target);
            throw;
        }
    }

    private static async Task<CatalogImportResult> MigrateImportAndVerifyAsync(
        string catalogRoot,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);

        var importer = Importer(db);
        var result = await importer.ApplyAsync(
            catalogRoot,
            new CatalogImportOptions(Force: CatalogForce.Files),
            cancellationToken);

        if (result.Aborted)
        {
            throw new InvalidOperationException(
                $"Catalog import aborted with {result.Plan.Conflicts.Count()} conflict(s).");
        }

        var finalPlan = await importer.PlanAsync(catalogRoot, cancellationToken);

        if (!finalPlan.IsClean)
        {
            var differences = finalPlan.Entries.Count(entry =>
                entry.Change is not CatalogChange.Unchanged and not CatalogChange.GoneFromBoth);
            throw new InvalidOperationException(
                $"Database upgrade left {differences} catalog difference(s). "
                + "Export live-only records before upgrading.");
        }

        return result;
    }

    private static CatalogImporter Importer(DantesRoleplayDbContext db) =>
        new(
            db,
            new MechanicStore(db),
            new ProcedureStore(db),
            new WorldStore(db),
            new EventTypeStore(db),
            new SubscriptionStore(db));

    private static async Task RequireValidCatalogAsync(string root, CancellationToken cancellationToken)
    {
        var validation = await CatalogValidator.ValidateAsync(root, cancellationToken);

        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Catalog validation failed with {validation.Errors} error(s). No database was changed.");
        }
    }

    private static string RequireCatalog(string catalogRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRoot);
        var root = Path.GetFullPath(catalogRoot);

        return Directory.Exists(root)
            ? root
            : throw new DirectoryNotFoundException($"No catalog at '{root}'.");
    }

    private static string BackupPath(string databasePath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        var candidate = $"{databasePath}.backup-{timestamp}";
        return File.Exists(candidate) ? $"{candidate}-{Guid.NewGuid():n}" : candidate;
    }

    private static void CreateConsistentBackup(string sourcePath, string backupPath)
    {
        using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Pooling=False");
        using var destination = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate;Pooling=False");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void RestoreBackup(string backupPath, string databasePath)
    {
        DeleteDatabaseArtifacts(databasePath);
        File.Copy(backupPath, databasePath, overwrite: false);
    }

    private static void DeleteDatabaseArtifacts(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

public sealed record CatalogDatabaseLifecycleResult(
    string DatabasePath,
    string? BackupPath,
    int Created,
    int Updated);
