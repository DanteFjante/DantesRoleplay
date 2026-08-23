using System.Security.Cryptography;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tests;

public sealed class CatalogValidationTests
{
    [Fact]
    public async Task Repository_catalog_validates_without_changing_its_files()
    {
        var catalog = RepositoryCatalog();
        var before = Snapshot(catalog);

        var result = await CatalogValidator.ValidateAsync(catalog);

        Assert.True(
            result.IsValid,
            string.Join(Environment.NewLine, result.Issues
                .Where(issue => !issue.Warning)
                .Select(issue => $"{issue.Kind} {issue.Id} [{issue.Check}]: {issue.Detail}")));
        Assert.Equal(before, Snapshot(catalog));
    }

    [Fact]
    public async Task Embedded_startup_content_is_the_canonical_catalog_content()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var catalogProcedures = contents.Procedures
            .Where(file => !file.Category.StartsWith("ruleset.", StringComparison.Ordinal))
            .ToDictionary(file => file.Id, StringComparer.Ordinal);
        var embeddedProcedures = ProcedureSeeder.Load().ToDictionary(file => file.Id, StringComparer.Ordinal);

        Assert.Equal(
            catalogProcedures.Keys.OrderBy(id => id, StringComparer.Ordinal),
            embeddedProcedures.Keys.OrderBy(id => id, StringComparer.Ordinal));

        foreach (var (id, file) in catalogProcedures)
        {
            Assert.Equal(file.ContentHash, embeddedProcedures[id].ContentHash);
        }

        var catalogMechanics = contents.Mechanics
            .Where(file => !file.Category.StartsWith("ruleset.", StringComparison.Ordinal))
            .ToDictionary(file => file.Id, StringComparer.Ordinal);
        var embeddedMechanics = MechanicSeeder.Load().ToDictionary(file => file.Id, StringComparer.Ordinal);

        Assert.Equal(
            catalogMechanics.Keys.OrderBy(id => id, StringComparer.Ordinal),
            embeddedMechanics.Keys.OrderBy(id => id, StringComparer.Ordinal));

        foreach (var (id, file) in catalogMechanics)
        {
            Assert.Equal(file.ContentHash, embeddedMechanics[id].ContentHash);
        }

        var catalogEvents = contents.EventTypes.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var embeddedEvents = EventTypeSeeder.Load().ToDictionary(file => file.Id, StringComparer.Ordinal);

        Assert.Equal(
            catalogEvents.Keys.OrderBy(id => id, StringComparer.Ordinal),
            embeddedEvents.Keys.OrderBy(id => id, StringComparer.Ordinal));

        foreach (var (id, file) in catalogEvents)
        {
            Assert.Equal(file.ContentHash, embeddedEvents[id].ContentHash);
        }

        foreach (var legacy in new[] { "Bootstrap", "Rules", "EventTypes" })
        {
            var directory = Path.Combine(RepositoryRoot(), "DantesRoleplay", legacy);
            Assert.False(Directory.Exists(directory) && Directory.EnumerateFiles(directory).Any());
        }
    }

    private static IReadOnlyDictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root from the test output directory.");
    }
}
