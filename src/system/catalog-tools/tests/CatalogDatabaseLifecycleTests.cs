using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Tools;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CatalogDatabaseLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"dantesroleplay-lifecycle-{Guid.NewGuid():n}");

    private string CatalogPath => Path.Combine(_root, "catalog");
    private string SourcePath => Path.Combine(_root, "source.db");
    private string TargetPath => Path.Combine(_root, "runtime", "dantesroleplay.db");

    public CatalogDatabaseLifecycleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Setup_creates_a_fresh_database_and_refuses_to_overwrite_it()
    {
        await ExportCatalogAsync("Initial description.");

        var result = await CatalogDatabaseLifecycle.SetupAsync(CatalogPath, TargetPath);

        Assert.Equal(Path.GetFullPath(TargetPath), result.DatabasePath);
        Assert.Null(result.BackupPath);
        Assert.True(File.Exists(TargetPath));
        Assert.Equal("Initial description.", await DescriptionAsync(TargetPath));

        var before = await File.ReadAllBytesAsync(TargetPath);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CatalogDatabaseLifecycle.SetupAsync(CatalogPath, TargetPath));

        Assert.Contains("upgrade", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await File.ReadAllBytesAsync(TargetPath));
    }

    [Fact]
    public async Task Upgrade_backs_up_and_applies_the_new_filesystem_catalog()
    {
        await ExportCatalogAsync("Initial description.");
        await CatalogDatabaseLifecycle.SetupAsync(CatalogPath, TargetPath);
        await ExportCatalogAsync("Description from the upgraded filesystem catalog.");

        var result = await CatalogDatabaseLifecycle.UpgradeAsync(CatalogPath, TargetPath);

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.True(result.Updated > 0);
        Assert.Equal(
            "Description from the upgraded filesystem catalog.",
            await DescriptionAsync(TargetPath));
        Assert.Equal("Initial description.", await DescriptionAsync(result.BackupPath));

        var replay = await CatalogDatabaseLifecycle.UpgradeAsync(CatalogPath, TargetPath);
        Assert.Equal(0, replay.Created);
        Assert.Equal(0, replay.Updated);
        Assert.True(File.Exists(replay.BackupPath));
    }

    [Fact]
    public async Task Upgrade_refuses_to_create_a_missing_database()
    {
        await ExportCatalogAsync("Initial description.");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => CatalogDatabaseLifecycle.UpgradeAsync(CatalogPath, TargetPath));

        Assert.False(File.Exists(TargetPath));
    }

    private async Task ExportCatalogAsync(string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SourcePath)!);

        await using var db = Context(SourcePath);
        await db.Database.MigrateAsync();
        await new MechanicStore(db).WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.lifecycle",
            Category = "test",
            Name = "Lifecycle test",
            Description = description,
            Matches = "lifecycle test",
            Requirements = "{}",
            Source = "return { narration: 'ok', effects: [] };",
            Status = MechanicStatus.Active,
            CreatedBy = "catalog lifecycle test",
            ChangeNote = description
        });

        await new CatalogExporter(db).ExportAsync(CatalogPath);
    }

    private static async Task<string> DescriptionAsync(string path)
    {
        await using var db = Context(path);
        var mechanic = await new MechanicStore(db).GetAsync("mechanic.test.lifecycle");
        return mechanic?.Description ?? throw new InvalidOperationException("Test mechanic was not imported.");
    }

    private static DantesRoleplayDbContext Context(string path)
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;
        return new DantesRoleplayDbContext(options);
    }
}
