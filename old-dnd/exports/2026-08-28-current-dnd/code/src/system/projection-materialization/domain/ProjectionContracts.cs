using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using System.Collections.ObjectModel;

namespace DantesRoleplay.Projections;

public sealed record ProjectionSource(string Role, ComponentTypeVersion ComponentType, string Pointer);
public sealed record ProjectionDependency(string ProjectionId, int Version);
public sealed record ProjectionMapping(string Role, string SourcePointer, string TargetPointer, string Operation = "copy");
public sealed record ProjectionDefinition(
    ApplicationIdentifier Owner,
    string QualifiedId,
    int Version,
    IReadOnlyList<ProjectionSource> Sources,
    IReadOnlyList<ProjectionDependency> Dependencies,
    IReadOnlyList<ProjectionMapping> Mappings);

public sealed record ProjectionGraph(IReadOnlyDictionary<string, IReadOnlyList<string>> Forward, IReadOnlyDictionary<string, IReadOnlyList<string>> Reverse);

public static class ProjectionValidator
{
    public static ProjectionGraph Validate(IReadOnlyList<ProjectionDefinition> definitions, IReadOnlyCollection<ApplicationIdentifier>? permittedBaseApplications = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var byKey = definitions.ToDictionary(Key, StringComparer.Ordinal);
        if (byKey.Count != definitions.Count) throw new ArgumentException("Projection IDs and versions must be unique.");
        var forward = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var reverse = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            try { ComponentTypeIdentifier.Validate(definition.Owner, definition.QualifiedId); }
            catch (ArgumentException exception) { throw new ArgumentException("Projection IDs must be qualified by their owner and versioned.", nameof(definitions), exception); }
            if (definition.Version < 1)
                throw new ArgumentException("Projection IDs must be qualified by their owner and versioned.");
            if (definition.Sources.Select(x => x.Role).Distinct(StringComparer.Ordinal).Count() != definition.Sources.Count
                || definition.Mappings.Select(x => x.TargetPointer).Distinct(StringComparer.Ordinal).Count() != definition.Mappings.Count)
                throw new ArgumentException("Projection source roles and output targets must be unique.");
            if (definition.Sources.Any(x => !ValidType(x.ComponentType) || !Pointer(x.Pointer)
                    || (x.ComponentType.Owner != definition.Owner && !(permittedBaseApplications?.Contains(x.ComponentType.Owner) ?? false))))
                throw new ArgumentException("Projection sources require valid pointers and explicit application ownership.");
            if (definition.Mappings.Any(x => x.Operation != "copy" || !Pointer(x.SourcePointer) || !Pointer(x.TargetPointer) || !definition.Sources.Any(s => s.Role == x.Role)))
                throw new ArgumentException("Mappings may only copy declared source paths to unique valid output paths.");

            var key = Key(definition);
            var edges = new List<string>();
            foreach (var dependency in definition.Dependencies)
            {
                var dependencyKey = dependency.ProjectionId + "@" + dependency.Version;
                if (!byKey.TryGetValue(dependencyKey, out var dependencyDefinition))
                    throw new ArgumentException("Projection dependencies must name an exact available version.");
                if (dependencyDefinition.Owner != definition.Owner
                    && !(permittedBaseApplications?.Contains(dependencyDefinition.Owner) ?? false))
                    throw new ArgumentException("Projection dependencies cannot hide a cross-application edge.");
                edges.Add(dependencyKey);
                if (!reverse.TryGetValue(dependencyKey, out var consumers)) reverse[dependencyKey] = consumers = [];
                consumers.Add(key);
            }
            forward[key] = edges.Order(StringComparer.Ordinal).ToArray();
        }

        foreach (var key in forward.Keys) Visit(key, forward, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        var immutableForward = forward.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()),
            StringComparer.Ordinal);
        var immutableReverse = reverse.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.Order(StringComparer.Ordinal).ToArray()),
            StringComparer.Ordinal);
        return new ProjectionGraph(
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(immutableForward),
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(immutableReverse));
    }

    private static void Visit(string key, IReadOnlyDictionary<string, IReadOnlyList<string>> graph, HashSet<string> visiting, HashSet<string> complete)
    {
        if (complete.Contains(key)) return;
        if (!visiting.Add(key)) throw new ArgumentException("Projection dependencies must be acyclic.");
        foreach (var next in graph[key]) Visit(next, graph, visiting, complete);
        visiting.Remove(key); complete.Add(key);
    }

    private static string Key(ProjectionDefinition value) => value.QualifiedId + "@" + value.Version;
    private static bool ValidType(ComponentTypeVersion value)
    {
        try { ComponentTypeIdentifier.Validate(value.Owner, value.QualifiedId); }
        catch (ArgumentException) { return false; }
        return value.Version > 0 && value.SchemaHash is { Length: 64 } && value.SchemaHash.All(Uri.IsHexDigit);
    }
    private static bool Pointer(string value) => value == "" || (value.StartsWith("/", StringComparison.Ordinal) && !value.Split('/').Skip(1).Any(x => x.Contains('~') && x.Replace("~0", "").Replace("~1", "").Contains('~')));
}
