using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tests;

/// <summary>One reviewed catalog import that gameplay tests clone into private writable databases.</summary>
internal static class CatalogTestTemplate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static SqliteFixture? _template;

    public static async Task<SqliteFixture> CloneImportedAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _template ??= await BuildAsync();
            return SqliteFixture.CloneOf(_template.Connection);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static CatalogTestCopy CopyRepositoryCatalog(string name) =>
        new(RepositoryCatalog(), name);

    private static async Task<SqliteFixture> BuildAsync()
    {
        var fixture = new SqliteFixture();
        using var catalog = CopyRepositoryCatalog("catalog-test-template");
        try
        {
            await using var db = fixture.CreateContext();
            var imported = await new CatalogImporter(
                    db,
                    new MechanicStore(db),
                    new ProcedureStore(db),
                    new WorldStore(db),
                    new EventTypeStore(db),
                    new SubscriptionStore(db))
                .ApplyAsync(catalog.Root, new CatalogImportOptions());
            if (imported.Aborted)
                throw new InvalidOperationException("The shared catalog test template could not be imported.");
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
        }

        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }
}

internal sealed class CatalogTestCopy : IDisposable
{
    public CatalogTestCopy(string source, string name)
    {
        Root = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():n}");
        Directory.CreateDirectory(Root);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(Root, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(Root, Path.GetRelativePath(source, file)));
        WorldFeatureFixture.RestoreRelationships(source, Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(Root);
        if (!root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A catalog test copy must remain inside the temporary directory.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
