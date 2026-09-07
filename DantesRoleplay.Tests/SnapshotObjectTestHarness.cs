using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

// Existing rule fixtures authorize data using fixture-local storage identities. Assemble that
// data through the real structural engine and exact catalog definitions before running their JS.
internal static class SnapshotObjectTestHarness
{
    private static readonly Lazy<Definitions> Catalog = new(Build);
    internal static async Task<MechanicProjection> AssembleAsync(MechanicFile mechanic, MechanicProjection snapshot)
    {
        var requirements = MechanicRequirements.Parse(mechanic.Requirements);
        if (requirements.SnapshotObjects.Count == 0) return snapshot;
        var definitions = Catalog.Value;
        var mapping = new ApplicationMechanicProjectionMapping(definitions.Values.Values.SelectMany(value => value.ComponentInputs)
            .GroupBy(value => value.Type.QualifiedTypeId).ToDictionary(group => group.Key, group => group.First().Type),
            new Dictionary<string, string>());
        snapshot = snapshot with { StateSpaceId = string.IsNullOrEmpty(snapshot.StateSpaceId) ? "snapshot-fixture" : snapshot.StateSpaceId,
            Audience = snapshot.Audience ?? new("dm") };
        var materializer = new ProjectionMaterializer(definitions, null!, null!, new BoundedJsonSchemaValidator());
        var resolver = new ApplicationMechanicObjectProjectionResolver(definitions, materializer, null!, null!);
        var result = await resolver.ResolveSnapshotAsync(new(snapshot.StateSpaceId, ApplicationIdentifier.Parse("dnd2024"),
            mechanic.Id, new string('A', 64), mapping, snapshot.Roles.ToDictionary(pair => pair.Key, pair => pair.Value.Id),
            snapshot.Input, snapshot.Seed, Audience: snapshot.Audience), requirements, snapshot);
        Assert.True(result.Ok, string.Join(';', result.Problems));
        return result.Projection!;
    }

    private static Definitions Build()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DantesRoleplay.slnx"))) root = root.Parent;
        var catalog = Path.Combine(root!.FullName, "catalog/applications/dnd2024");
        var owner = ApplicationIdentifier.Parse("dnd2024");
        var paths = new[] { "character/dnd2024.object.character-dossier-records.json",
            "item/dnd2024.object.inventory-item-definition-records.v1.json", "item/dnd2024.object.inventory-item-definition-records.json",
            "item/dnd2024.object.inventory-item-instance-records.v1.json", "item/dnd2024.object.inventory-item-instance-records.json",
            "item/dnd2024.object.inventory-item-recipe-record.json", "item/dnd2024.object.inventory-item-activity-record.json" };
        var requests = paths.Select(path => ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(catalog, "objects", path)), owner)).ToArray();
        using var database = new SqliteFixture();
        using var db = database.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(owner, "Snapshot fixture", "", []));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        foreach (var type in requests.SelectMany(value => value.ComponentInputs).Select(value => value.Type).Distinct())
        {
            for (var version = 1; version < type.TypeVersion; version++)
                types.Define(new(owner, type.QualifiedTypeId, "{\"type\":\"object\",\"properties\":{\"prior" + version + "\":{\"type\":\"string\"}}}"));
            var registered = types.Define(new(owner, type.QualifiedTypeId,
                File.ReadAllText(Path.Combine(catalog, "components", type.QualifiedTypeId + ".schema.json"))));
            Assert.Equal(type.SchemaHash, registered.SchemaHash);
        }
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
        var values = requests.Select(registry.Define).ToDictionary(value => (value.QualifiedId, value.Version));
        return new(values);
    }

    private sealed class Definitions(Dictionary<(string, int), RegisteredProjectionDefinition> values) : IProjectionDefinitionRegistry
    {
        internal Dictionary<(string, int), RegisteredProjectionDefinition> Values => values;
        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) => values.GetValueOrDefault((qualifiedId, version));
        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition) => throw new NotSupportedException();
        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) => throw new NotSupportedException();
    }
}
