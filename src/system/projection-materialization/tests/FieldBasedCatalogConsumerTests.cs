using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class FieldBasedCatalogConsumerTests
{
    [Fact]
    public void Every_current_structural_object_and_consumer_uses_the_exact_registered_field_based_definition()
    {
        var catalog = Catalog();
        var root = Path.Combine(catalog, "applications", "dnd2024");
        var owner = ApplicationIdentifier.Parse("dnd2024");
        var baseOwner = ApplicationIdentifier.Parse("game");
        var requests = Directory.EnumerateFiles(Path.Combine(root, "objects"), "*.json", SearchOption.AllDirectories)
            .Select(path => ApplicationObjectDocument.Parse(File.ReadAllText(path), owner)).ToArray();
        var current = requests.GroupBy(value => value.QualifiedId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.MaxBy(value => value.DeclaredVersion)!, StringComparer.Ordinal);
        Assert.Equal(13, current.Count);
        Assert.All(current.Values, value => Assert.Equal(
            RegisteredApplicationObjectContract.FieldBasedContractProfileId, value.ObjectContract!.ProfileId));

        using var fixture = new SqliteFixture();
        using var db = fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(baseOwner, "Base fixture", "", []));
        applications.Register(new(owner, "Object consumer fixture", "", [baseOwner]));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var referencedTypes = requests.SelectMany(value => value.ComponentInputs.Select(input => input.Type)
            .Concat(value.ObjectContract!.Relationships.SelectMany(relationship =>
                relationship.RequiredEndpointComponents.Concat(relationship.OptionalEndpointComponents)
                    .Select(endpoint => endpoint.Type)))).Distinct().ToArray();
        foreach (var group in referencedTypes.GroupBy(value => value.QualifiedTypeId, StringComparer.Ordinal))
        {
            // Historical versions not referenced by any object need only reserve their ordinal in
            // this disposable registry. Every actually referenced schema must match the catalog.
            var type = Assert.Single(group);
            var shared = type.QualifiedTypeId.StartsWith("game.", StringComparison.Ordinal);
            var typeOwner = shared ? baseOwner : owner;
            for (var version = 1; version < type.TypeVersion; version++)
                types.Define(new(typeOwner, type.QualifiedTypeId,
                    $$"""{"type":"object","title":"unreferenced-fixture-version-{{version}}"}"""));
            var schema = shared
                ? CatalogLayout.ToFileSystemPath(catalog, CatalogLayout.ComponentSchema(type.QualifiedTypeId))
                : Path.Combine(root, "components", type.QualifiedTypeId + ".schema.json");
            var registered = types.Define(new(typeOwner, type.QualifiedTypeId, File.ReadAllText(schema)));
            Assert.Equal(type.TypeVersion, registered.Version);
            Assert.Equal(type.SchemaHash, registered.SchemaHash);
        }

        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
        var pending = requests.OrderBy(value => value.QualifiedId, StringComparer.Ordinal)
            .ThenBy(value => value.DeclaredVersion).ToList();
        while (pending.Count > 0)
        {
            var ready = pending.Where(value => (value.DeclaredVersion == 1 ||
                registry.Get(value.QualifiedId, value.DeclaredVersion!.Value - 1) is not null) &&
                value.DependencyInputs.All(input => registry.Get(input.Projection.QualifiedId,
                    input.Projection.Version) is not null)).ToArray();
            Assert.NotEmpty(ready);
            foreach (var request in ready) { registry.Define(request); pending.Remove(request); }
        }

        var consumers = new Dictionary<string, int>(StringComparer.Ordinal);
        void Check(string consumer, string id, int version, string hash)
        {
            Assert.True(current.TryGetValue(id, out var latest), $"{consumer} references missing object {id}.");
            Assert.True(version == latest.DeclaredVersion, $"{consumer} references old {id}@{version}.");
            var definition = registry.Get(id, version);
            Assert.NotNull(definition);
            Assert.True(hash == definition.ContentHash, $"{consumer} has stale {id}@{version} fingerprint.");
            Assert.True(definition.ObjectContract!.IsFieldBased);
            consumers[id] = consumers.GetValueOrDefault(id) + 1;
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "queries"), "*.json", SearchOption.AllDirectories))
        {
            var query = ApplicationQueryContract.Parse(File.ReadAllText(path), owner);
            if (query.Status != "active" || !query.IsObjectProjection) continue;
            Check(query.Id, query.ProjectionQualifiedId, query.ProjectionVersion, query.ProjectionContentHash);
        }
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "mechanics"), "*.md", SearchOption.AllDirectories))
        {
            var mechanic = MechanicFile.Parse(File.ReadAllText(path), path, File.ReadAllText(Path.ChangeExtension(path, ".js")));
            if (mechanic.Status != MechanicStatus.Active) continue;
            var requirements = MechanicRequirements.Parse(mechanic.Requirements);
            foreach (var role in requirements.ObjectRoles)
                Check(mechanic.Id + "/" + role.Key, role.Value.QualifiedId, role.Value.Version, role.Value.ContentFingerprint);
            foreach (var snapshot in requirements.SnapshotObjects)
                Check(mechanic.Id + "/" + snapshot.Key, snapshot.Value.QualifiedId,
                    snapshot.Value.Version, snapshot.Value.ContentFingerprint);
        }
        foreach (var definition in current.Values)
            foreach (var input in definition.DependencyInputs)
                Check(definition.QualifiedId + "/" + input.InputId, input.Projection.QualifiedId,
                    input.Projection.Version, input.Projection.ContentHash);
        Assert.Equal(current.Keys.Order(StringComparer.Ordinal), consumers.Keys.Order(StringComparer.Ordinal));
    }

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }
}
