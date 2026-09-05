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
    public void Dnd_application_capabilities_have_complete_authored_contracts_and_exact_child_pins()
    {
        var catalog = RepositoryCatalog();
        var mechanicsRoot = Path.Combine(catalog, "applications", "dnd2024", "mechanics");
        var queryRoot = Path.Combine(catalog, "applications", "dnd2024", "queries");
        var mechanicPaths = Directory.EnumerateFiles(mechanicsRoot, "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        var queryPaths = Directory.EnumerateFiles(queryRoot, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(152, mechanicPaths.Length);
        Assert.Equal(18, queryPaths.Length);
        foreach (var path in mechanicPaths)
        {
            var file = MechanicFile.Parse(File.ReadAllText(path), path,
                File.ReadAllText(Path.ChangeExtension(path, ".js")));
            var requirements = MechanicRequirements.Parse(file.Requirements);
            Assert.NotNull(requirements.InputSchema);
            Assert.All(requirements.Children, child =>
            {
                Assert.Equal(1, child.Value.MechanicVersion);
                Assert.Matches("^[0-9A-F]{64}$", child.Value.ContentFingerprint);
            });
        }

        var errors = ApplicationCapabilityCatalogValidator.Validate(catalog)
            .Where(issue => !issue.Warning && issue.Id.StartsWith("dnd2024.", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Every_repository_identity_has_a_reviewed_conforming_namespace()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryCatalog(), CatalogCompatibilityRetention.FileName)),
            "The repository's closed compatibility inventory cannot be silently removed.");
        var result = await CatalogValidator.ValidateAsync(RepositoryCatalog());
        var findings = result.Issues.Where(issue => issue.Check == "namespace-review").ToArray();

        Assert.Empty(findings);
        // Other warning families (such as unchanged applications without authored input schemas)
        // have their own contracts; they are not namespace-review failures.
    }

    [Fact]
    public void Every_authored_dnd_feature_has_an_enabled_reviewed_discovery_namespace()
    {
        var catalog = RepositoryCatalog();
        foreach (var (folder, extension, kind) in new[] {
            ("mechanics", "*.md", "mechanic"), ("procedures", "*.md", "procedure"), ("queries", "*.json", "document") })
        foreach (var path in Directory.EnumerateFiles(Path.Combine(catalog, "applications", "dnd2024", folder), extension, SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            using var query = extension == "*.json" ? JsonDocument.Parse(text) : null;
            var id = query is not null
                ? query.RootElement.GetProperty("id").GetString()!
                : text.Split('\n').Single(line => line.StartsWith("id: ", StringComparison.Ordinal))[4..].Trim();
            var namespaceId = id[..id.LastIndexOf('.')];
            var namespacePath = Path.Combine(catalog, "namespaces", namespaceId.Replace('.', Path.DirectorySeparatorChar), "_namespace.json");
            Assert.True(File.Exists(namespacePath), $"{id} is hidden from exact discovery: namespace {namespaceId} is missing.");
            using var registration = JsonDocument.Parse(File.ReadAllText(namespacePath));
            var value = registration.RootElement;
            Assert.Equal(namespaceId, value.GetProperty("id").GetString());
            Assert.Equal("dnd2024", value.GetProperty("owner").GetString());
            Assert.True(value.GetProperty("enabled").GetBoolean());
            Assert.Equal("reviewed", value.GetProperty("reviewStatus").GetString());
            Assert.Contains(kind, value.GetProperty("allowedKinds").EnumerateArray().Select(item => item.GetString()));
        }
    }

    [Fact]
    public async Task Retained_compatibility_procedures_are_not_callable_or_kernel_bootstrap_content()
    {
        var catalog = RepositoryCatalog();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(catalog, CatalogCompatibilityRetention.FileName)));
        var ids = document.RootElement.GetProperty("records").EnumerateArray()
            .Where(record => record.GetProperty("kind").GetString() == "procedure")
            .Select(record => record.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(36, ids.Count);
        var contents = await CatalogReader.ReadAsync(catalog);
        Assert.All(contents.Procedures.Where(record => ids.Contains(record.Id)), record =>
        {
            Assert.Equal(DantesRoleplay.Procedures.ProcedureStatus.Deprecated, record.Status);
            Assert.Empty(record.Matches);
            Assert.Contains("not an executable route", record.Description, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(ProcedureSeeder.Load(), record => ids.Contains(record.Id));
    }

    [Fact]
    public void Kernel_bootstrap_does_not_embed_ruleset_content_from_legacy_namespace_paths()
    {
        Assert.DoesNotContain(ProcedureSeeder.Load(), file =>
            file.Category.StartsWith("ruleset.", StringComparison.Ordinal));
        Assert.DoesNotContain(MechanicSeeder.Load(), file =>
            file.Category.StartsWith("ruleset.", StringComparison.Ordinal));
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
            .Where(file => file.Id != "mechanic.lock.pick") // Export-only compatibility record, not startup content.
            .ToDictionary(file => file.Id, StringComparer.Ordinal);
        var embeddedMechanics = MechanicSeeder.Load().ToDictionary(file => file.Id, StringComparer.Ordinal);

        Assert.Equal(
            catalogMechanics.Keys.OrderBy(id => id, StringComparer.Ordinal),
            embeddedMechanics.Keys.OrderBy(id => id, StringComparer.Ordinal));

        foreach (var (id, file) in catalogMechanics)
        {
            Assert.Equal(file.ContentHash, embeddedMechanics[id].ContentHash);
        }

        var catalogEvents = contents.EventTypes
            .Where(file => !file.Id.StartsWith("dnd2024.", StringComparison.Ordinal))
            .ToDictionary(file => file.Id, StringComparer.Ordinal);
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
    public async Task Deprecated_rehearsal_remains_exportable_but_is_not_a_bootstrap_resource()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var retained = Assert.Single(contents.Mechanics, file => file.Id == "mechanic.lock.pick");
        Assert.Equal(MechanicStatus.Deprecated, retained.Status);
        Assert.NotEmpty(retained.Source);
        Assert.DoesNotContain(MechanicSeeder.Load(), file => file.Id == retained.Id);
        Assert.DoesNotContain(typeof(Mechanic).Assembly.GetManifestResourceNames(), name =>
            name.Contains(".Rules.mechanic.lock.pick.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ratified_action_records_have_authored_navigation_metadata_and_lossless_paths()
    {
        var catalog = RepositoryCatalog();
        var contents = await CatalogReader.ReadAsync(catalog);
        var procedures = contents.Procedures.Where(value => InCategory(value.Category,
            "game.core", "campaign", "quest", "play")).ToArray();
        var mechanics = contents.Mechanics.Where(value => InCategory(value.Category,
            "game.core", "check", "change")).ToArray();

        Assert.Equal(26, procedures.Length);
        Assert.Equal(24, mechanics.Length);
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
        Assert.All(procedures, procedure => Assert.True(File.Exists(CatalogLayout.ToFileSystemPath(
            catalog, CatalogLayout.ProcedureMarkdown(procedure.Category, procedure.Id)))));
        Assert.All(mechanics, mechanic => Assert.True(File.Exists(CatalogLayout.ToFileSystemPath(
            catalog, CatalogLayout.MechanicMarkdown(mechanic.Category, mechanic.Id)))));
    }

    [Fact]
    public async Task Ratified_mechanics_need_no_structural_compatibility_projections()
    {
        var catalog = RepositoryCatalog();
        var contents = await CatalogReader.ReadAsync(catalog);
        var mechanics = contents.Mechanics.Where(value => value.Status == MechanicStatus.Active && InCategory(value.Category,
            "game.core", "check", "change")).ToArray();

        Assert.Equal(22, mechanics.Length);
        Assert.DoesNotContain(mechanics, mechanic => mechanic.Id is "mechanic.lock.pick" or "mechanic.game.core.world.location.register");
        var requirements = mechanics
            .Select(mechanic => (mechanic.Id, Parsed: MechanicRequirements.Parse(mechanic.Requirements)))
            .ToArray();
        var supportedRequirementProperties = new HashSet<string>(
            ["roles", "event", "children", "effectComponentIds", "inputSchema", "elapsedTime"],
            StringComparer.Ordinal);
        foreach (var mechanic in mechanics)
        {
            using var document = JsonDocument.Parse(mechanic.Requirements);
            Assert.All(document.RootElement.EnumerateObject(), property =>
                Assert.Contains(property.Name, supportedRequirementProperties));
        }

        var adoptedComponentIds = contents.Components
            .Where(value => value.Id.StartsWith("game.core.", StringComparison.Ordinal)
                || value.Id == "fixture.legacy.stats")
            .Where(value => value.Schema is not null)
            .Select(value => value.Id)
            .ToHashSet(StringComparer.Ordinal);
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

    private static bool InCategory(string category, params string[] roots) => roots.Any(root =>
        category == root || category.StartsWith(root + ".", StringComparison.Ordinal));

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
