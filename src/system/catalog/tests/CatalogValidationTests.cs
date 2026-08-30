using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;

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

    [Fact]
    public void Ratified_dnd2024_action_records_have_authored_navigation_metadata_and_lossless_paths()
    {
        var catalog = RepositoryCatalog();
        var procedureRoots = new[]
        {
            "procedures/game/core",
            "procedures/campaign",
            "procedures/quest",
            "procedures/play"
        };
        var mechanicRoots = new[]
        {
            "mechanics/game/core",
            "mechanics/check",
            "mechanics/change"
        };

        var procedures = procedureRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(catalog, root.Replace('/', Path.DirectorySeparatorChar)),
                "*.md",
                SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path => ProcedureFile.Parse(File.ReadAllText(path), Relative(catalog, path)))
            .ToArray();
        var mechanics = mechanicRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(catalog, root.Replace('/', Path.DirectorySeparatorChar)),
                "*.md",
                SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path => MechanicFile.Parse(
                File.ReadAllText(path),
                Relative(catalog, path),
                File.ReadAllText(Path.ChangeExtension(path, ".js"))))
            .ToArray();

        Assert.Equal(20, procedures.Length);
        Assert.Equal(14, mechanics.Length);
        var records = procedures.Select(value => (value.Id, value.Category, value.Name, value.Description))
            .Concat(mechanics.Select(value => (value.Id, value.Category, value.Name, value.Description)))
            .ToArray();
        Assert.Equal(records.Length, records.Select(record => record.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(records, record =>
        {
            Assert.False(string.IsNullOrWhiteSpace(record.Name), record.Id);
            Assert.False(string.IsNullOrWhiteSpace(record.Description), record.Id);
            var logicalPath = CatalogLayout.CategoryDirectory(record.Category);
            Assert.Equal(record.Category, logicalPath.Replace('/', '.'));
            Assert.DoesNotContain('\\', logicalPath);
        });
    }

    [Fact]
    public void Ratified_dnd2024_mechanics_need_no_structural_compatibility_projections()
    {
        var catalog = RepositoryCatalog();
        var mechanicRoots = new[]
        {
            "mechanics/game/core",
            "mechanics/check",
            "mechanics/change"
        };
        var mechanics = mechanicRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(catalog, root.Replace('/', Path.DirectorySeparatorChar)),
                "*.md",
                SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path => MechanicFile.Parse(
                File.ReadAllText(path),
                Relative(catalog, path),
                File.ReadAllText(Path.ChangeExtension(path, ".js"))))
            .ToArray();

        Assert.Equal(14, mechanics.Length);
        var requirements = mechanics
            .Select(mechanic => (mechanic.Id, Parsed: MechanicRequirements.Parse(mechanic.Requirements)))
            .ToArray();
        var supportedRequirementProperties = new HashSet<string>(["roles", "event", "children"], StringComparer.Ordinal);
        foreach (var mechanic in mechanics)
        {
            using var document = JsonDocument.Parse(mechanic.Requirements);
            Assert.All(document.RootElement.EnumerateObject(), property =>
                Assert.Contains(property.Name, supportedRequirementProperties));
        }

        var componentDirectory = Path.Combine(catalog, "components");
        var adoptedComponentIds = Directory.EnumerateFiles(componentDirectory, "*.schema.json")
            .Select(Path.GetFileName)
            .Where(name => name is not null &&
                (name.StartsWith("game.core.", StringComparison.Ordinal) ||
                 string.Equals(name, "stats.schema.json", StringComparison.Ordinal)))
            .Select(name => name![..^".schema.json".Length])
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(34, adoptedComponentIds.Count);

        var requiredComponentIds = requirements
            .SelectMany(requirement => requirement.Parsed.AllComponentIds())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(requiredComponentIds);
        Assert.All(requiredComponentIds, componentId =>
            Assert.Contains(componentId, adoptedComponentIds));

        var projectionDirectory = Path.Combine(catalog, "projections");
        Assert.False(Directory.Exists(projectionDirectory) &&
            Directory.EnumerateFiles(projectionDirectory, "*", SearchOption.AllDirectories).Any());
    }

    private static IReadOnlyDictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

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
