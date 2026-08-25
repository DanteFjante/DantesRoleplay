using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections;

public sealed class SqliteProjectionDefinitionRegistry(
    DantesRoleplayDbContext db,
    IApplicationComponentTypeRegistry componentTypes,
    IBoundedJsonSchemaValidator validator) : IProjectionDefinitionRegistry
{
    public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ComponentTypeIdentifier.Validate(definition.Owner, definition.QualifiedId);
        var compilation = validator.Compile(definition.OutputSchemaJson);
        if (!compilation.IsAccepted) throw new ArgumentException("The projection output schema is not accepted by the bounded profile.");
        ValidateInputs(definition);
        var contentHash = Hash(Canonical(definition, compilation.NormalizedSchema));

        using var transaction = db.Database.BeginTransaction();
        if (!db.Set<ApplicationRegistryRecord>().Any(x => x.Id == definition.Owner.Value))
            throw new ArgumentException("A projection belongs to a registered application.");
        var identity = db.Set<ProjectionDefinitionRecord>().SingleOrDefault(x => x.QualifiedId == definition.QualifiedId);
        if (identity is not null && identity.ApplicationId != definition.Owner.Value)
            throw new InvalidOperationException("A qualified projection belongs to a different application.");
        var replay = db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking()
            .Where(x => x.QualifiedId == definition.QualifiedId && x.ContentHash == contentHash).OrderBy(x => x.Version).FirstOrDefault();
        if (replay is not null) { transaction.Commit(); return Read(replay, definition.Owner); }
        var version = db.Set<ProjectionDefinitionVersionRecord>().Where(x => x.QualifiedId == definition.QualifiedId).Max(x => (int?)x.Version).GetValueOrDefault() + 1;
        var now = DateTime.UtcNow;
        if (identity is null) db.Add(new ProjectionDefinitionRecord { QualifiedId = definition.QualifiedId, ApplicationId = definition.Owner.Value, CreatedAtUtc = now });
        var row = new ProjectionDefinitionVersionRecord { QualifiedId = definition.QualifiedId, Version = version, ProfileId = compilation.ProfileId, OutputSchemaJson = compilation.NormalizedSchema, OutputSchemaHash = compilation.SchemaHash, ContentHash = contentHash, CreatedAtUtc = now };
        db.Add(row);
        foreach (var (input, ordinal) in definition.ComponentInputs.Select((x, i) => (x, i)))
            db.Add(new ProjectionComponentInputRecord { QualifiedId = definition.QualifiedId, Version = version, InputId = input.InputId, EntityRole = input.EntityRole, QualifiedTypeId = input.Type.QualifiedTypeId, TypeVersion = input.Type.TypeVersion, SchemaHash = input.Type.SchemaHash, Ordinal = ordinal });
        foreach (var (input, ordinal) in definition.DependencyInputs.Select((x, i) => (x, i)))
            db.Add(new ProjectionDependencyInputRecord { QualifiedId = definition.QualifiedId, Version = version, InputId = input.InputId, DependencyQualifiedId = input.Projection.QualifiedId, DependencyVersion = input.Projection.Version, DependencyContentHash = input.Projection.ContentHash, RoleBindingsJson = JsonSerializer.Serialize(input.RoleBindings.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value)), Ordinal = ordinal });
        foreach (var (mapping, ordinal) in definition.Mappings.Select((x, i) => (x, i)))
            db.Add(new ProjectionMappingRecord { QualifiedId = definition.QualifiedId, Version = version, TargetPointer = mapping.TargetPointer, InputId = mapping.InputId, SourcePointer = mapping.SourcePointer, Ordinal = ordinal });
        db.SaveChanges(); transaction.Commit(); return Read(row, definition.Owner);
    }

    public RegisteredProjectionDefinition? Get(string qualifiedId, int version)
    {
        if (string.IsNullOrWhiteSpace(qualifiedId) || version < 1) throw new ArgumentException("An exact projection ID and version are required.");
        var row = db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking().SingleOrDefault(x => x.QualifiedId == qualifiedId && x.Version == version);
        if (row is null) return null;
        var owner = db.Set<ProjectionDefinitionRecord>().AsNoTracking().Where(x => x.QualifiedId == qualifiedId).Select(x => x.ApplicationId).Single();
        return Read(row, ApplicationIdentifier.Parse(owner));
    }

    public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definitions = db.Set<ProjectionDefinitionRecord>().AsNoTracking().Where(x => x.ApplicationId == owner.Value).Select(x => x.QualifiedId).ToArray();
        var versions = db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking().Where(x => definitions.Contains(x.QualifiedId)).ToArray();
        var keys = versions.Select(Key).ToHashSet(StringComparer.Ordinal);
        var forward = keys.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
        var reverse = keys.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in db.Set<ProjectionDependencyInputRecord>().AsNoTracking().Where(x => definitions.Contains(x.QualifiedId)))
        {
            var from = edge.QualifiedId + "@" + edge.Version; var to = edge.DependencyQualifiedId + "@" + edge.DependencyVersion;
            if (keys.Contains(from) && keys.Contains(to)) { forward[from].Add(to); reverse[to].Add(from); }
        }
        return new ProjectionImpactGraph(Frozen(forward), Frozen(reverse));
    }

    private RegisteredProjectionDefinition Read(ProjectionDefinitionVersionRecord row, ApplicationIdentifier owner)
    {
        var components = db.Set<ProjectionComponentInputRecord>().AsNoTracking().Where(x => x.QualifiedId == row.QualifiedId && x.Version == row.Version).OrderBy(x => x.Ordinal)
            .Select(x => new ProjectionComponentInput(x.InputId, x.EntityRole, new EcsComponentReference(x.QualifiedTypeId, x.TypeVersion, x.SchemaHash))).ToArray();
        var dependencies = db.Set<ProjectionDependencyInputRecord>().AsNoTracking().Where(x => x.QualifiedId == row.QualifiedId && x.Version == row.Version).OrderBy(x => x.Ordinal).ToArray()
            .Select(x => new ProjectionDependencyInput(x.InputId, new ProjectionReference(x.DependencyQualifiedId, x.DependencyVersion, x.DependencyContentHash), JsonSerializer.Deserialize<Dictionary<string, string>>(x.RoleBindingsJson) ?? [])).ToArray();
        var mappings = db.Set<ProjectionMappingRecord>().AsNoTracking().Where(x => x.QualifiedId == row.QualifiedId && x.Version == row.Version).OrderBy(x => x.Ordinal)
            .Select(x => new StructuralProjectionMapping(x.InputId, x.SourcePointer, x.TargetPointer)).ToArray();
        return new(owner, row.QualifiedId, row.Version, row.ProfileId, row.OutputSchemaJson, row.OutputSchemaHash, row.ContentHash, Array.AsReadOnly(components), Array.AsReadOnly(dependencies), Array.AsReadOnly(mappings), row.CreatedAtUtc);
    }

    private void ValidateInputs(ProjectionDefinitionRequest definition)
    {
        if (definition.ComponentInputs is null || definition.DependencyInputs is null || definition.Mappings is null || definition.ComponentInputs.Count + definition.DependencyInputs.Count > 32 || definition.Mappings.Count is < 1 or > 128)
            throw new ArgumentException("Projection inputs and mappings exceed fixed bounds.");
        var ids = definition.ComponentInputs.Select(x => x.InputId).Concat(definition.DependencyInputs.Select(x => x.InputId)).ToArray();
        if (ids.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || definition.ComponentInputs.Any(x => string.IsNullOrWhiteSpace(x.EntityRole) || x.EntityRole.Length > 200))
            throw new ArgumentException("Projection input IDs and roles must be unique and bounded.");
        foreach (var input in definition.ComponentInputs)
        {
            input.Type.Validate(); var type = componentTypes.Get(input.Type.QualifiedTypeId, input.Type.TypeVersion);
            if (type is null || type.Owner != definition.Owner || type.SchemaHash != input.Type.SchemaHash) throw new ArgumentException("Projection component inputs require exact owner-local registered types.");
        }
        var sourceSchemas = definition.ComponentInputs.ToDictionary(x => x.InputId, x => componentTypes.Get(x.Type.QualifiedTypeId, x.Type.TypeVersion)!.SchemaJson, StringComparer.Ordinal);
        foreach (var input in definition.DependencyInputs)
        {
            input.Projection.Validate(); if (input.RoleBindings is null || input.RoleBindings.Count > 64
                || input.RoleBindings.Values.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200)) throw new ArgumentException("Projection role bindings exceed fixed bounds.");
            var dependency = Get(input.Projection.QualifiedId, input.Projection.Version);
            if (dependency is null || dependency.Owner != definition.Owner || dependency.ContentHash != input.Projection.ContentHash || !input.RoleBindings.Keys.Order().SequenceEqual(dependency.EntityRoles.Order(), StringComparer.Ordinal) || input.RoleBindings.Values.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Projection dependencies require exact local references and closed role bindings.");
            if (1 + DependencyDepth(dependency, new Dictionary<string, int>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)) > 16)
                throw new ArgumentException("Projection dependency depth exceeds the fixed bound.");
            sourceSchemas.Add(input.InputId, dependency.OutputSchemaJson);
        }
        if (definition.Mappings.Select(x => x.TargetPointer).Distinct(StringComparer.Ordinal).Count() != definition.Mappings.Count || definition.Mappings.Any(x => !ids.Contains(x.InputId, StringComparer.Ordinal)
                || x.SourcePointer.Length > 1000 || x.TargetPointer.Length > 1000
                || !Pointer(x.SourcePointer) || !Pointer(x.TargetPointer) || (!string.IsNullOrEmpty(x.TargetPointer) && x.TargetPointer.Split('/').Skip(1).Any(IsArrayIndex))))
            throw new ArgumentException("Mappings must structurally copy declared JSON pointers to unique object targets.");
        if (definition.Mappings.Any(x => x.TargetPointer == "") && definition.Mappings.Count != 1) throw new ArgumentException("A root mapping must be the only mapping.");
        if (definition.Mappings.Any(x => !ProjectionSchemaPath.Exists(sourceSchemas[x.InputId], x.SourcePointer)))
            throw new ArgumentException("Projection source pointers must exist in their exact declared schema.");
    }

    private static bool IsArrayIndex(string value) => int.TryParse(value, out _);
    private int DependencyDepth(RegisteredProjectionDefinition definition, Dictionary<string, int> memo, HashSet<string> visiting)
    {
        var key = definition.QualifiedId + "@" + definition.Version;
        if (memo.TryGetValue(key, out var known)) return known;
        if (!visiting.Add(key)) throw new ArgumentException("Projection dependencies must be acyclic.");
        var depth = 0;
        foreach (var input in definition.DependencyInputs)
        {
            var dependency = Get(input.Projection.QualifiedId, input.Projection.Version);
            if (dependency is null || dependency.ContentHash != input.Projection.ContentHash)
                throw new ArgumentException("Projection dependencies require exact available versions.");
            depth = Math.Max(depth, 1 + DependencyDepth(dependency, memo, visiting));
            if (depth > 16) break;
        }
        visiting.Remove(key);
        memo[key] = depth;
        return depth;
    }
    private static bool Pointer(string value) => value == "" || (value.StartsWith("/", StringComparison.Ordinal) && !value.Split('/').Skip(1).Any(x => x.Contains('~') && x.Replace("~0", "").Replace("~1", "").Contains('~')));
    private static string Canonical(ProjectionDefinitionRequest d, string schema) => JsonSerializer.Serialize(new { owner = d.Owner.Value, id = d.QualifiedId, schema, components = d.ComponentInputs, dependencies = d.DependencyInputs.Select(x => new { x.InputId, projection = x.Projection, roles = x.RoleBindings.OrderBy(p => p.Key) }), mappings = d.Mappings });
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Key(ProjectionDefinitionVersionRecord x) => x.QualifiedId + "@" + x.Version;
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Frozen(Dictionary<string, List<string>> graph) => new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(graph.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)Array.AsReadOnly(x.Value.Order(StringComparer.Ordinal).ToArray()), StringComparer.Ordinal));
}

internal sealed class ProjectionDefinitionRecord { public required string QualifiedId { get; set; } public required string ApplicationId { get; set; } public DateTime CreatedAtUtc { get; set; } }
internal sealed class ProjectionDefinitionVersionRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string ProfileId { get; set; } public required string OutputSchemaJson { get; set; } public required string OutputSchemaHash { get; set; } public required string ContentHash { get; set; } public DateTime CreatedAtUtc { get; set; } }
internal sealed class ProjectionComponentInputRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string InputId { get; set; } public required string EntityRole { get; set; } public required string QualifiedTypeId { get; set; } public int TypeVersion { get; set; } public required string SchemaHash { get; set; } public int Ordinal { get; set; } }
internal sealed class ProjectionDependencyInputRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string InputId { get; set; } public required string DependencyQualifiedId { get; set; } public int DependencyVersion { get; set; } public required string DependencyContentHash { get; set; } public required string RoleBindingsJson { get; set; } public int Ordinal { get; set; } }
internal sealed class ProjectionMappingRecord { public required string QualifiedId { get; set; } public int Version { get; set; } public required string TargetPointer { get; set; } public required string InputId { get; set; } public required string SourcePointer { get; set; } public int Ordinal { get; set; } }
