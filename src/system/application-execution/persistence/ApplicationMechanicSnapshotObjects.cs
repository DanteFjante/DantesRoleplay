using System.Text;
using System.Text.Json;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;

namespace DantesRoleplay.ApplicationExecution;

public sealed partial class ApplicationMechanicObjectProjectionResolver
{
    // This path has no entity/component/relationship reader. Its only data input is the exact
    // view the existing projection owner already authorized; absent fields remain absent.
    public async Task<ProjectionResult> ResolveSnapshotAsync(ApplicationMechanicEvaluationRequest request,
        MechanicRequirements requirements, MechanicProjection snapshot, CancellationToken cancellationToken = default)
    {
        try
        {
            if (requirements.ProjectionProblems().Count > 0 || snapshot.StateSpaceId != request.StateSpaceId ||
                request.Audience is not { IsValid: true }) return ProjectionResult.Failed("OBJECT_SNAPSHOT_INVALID");
            using var input = JsonDocument.Parse(snapshot.Input);
            var nodes = new Dictionary<string, SnapshotNode>(StringComparer.Ordinal);
            foreach (var role in snapshot.Roles.Values)
            {
                Add(new(role.Id, role.Name, role.Components));
                Contents(role.Contains, 0);
            }
            foreach (var reference in snapshot.References.Values)
                Add(new(reference.Id, reference.Name ?? "", reference.Components));
            var objects = new Dictionary<string, MechanicObjectProjection>(StringComparer.Ordinal);
            var consumed = new HashSet<(string EntityId, string ComponentId)>();
            var bytes = 0;
            var materializedItems = 0;
            foreach (var (key, declaration) in requirements.SnapshotObjects.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var definition = RequireSnapshotDefinition(new(declaration.QualifiedId, declaration.Version,
                    declaration.ContentFingerprint), new HashSet<string>(StringComparer.Ordinal));
                var candidates = declaration.ReferenceComponentIds.Count == 0 ? new string?[] { null }
                    : snapshot.References.Values.Where(value => declaration.ReferenceComponentIds.Any(value.Components.ContainsKey))
                        .Select(value => (string?)value.Id).Order(StringComparer.Ordinal).ToArray();
                if (candidates.Length > declaration.MaximumItems)
                    throw new InvalidOperationException("Snapshot object collection exceeds its declared bound.");
                var records = new List<object>();
                MechanicObjectProjection? singleton = null;
                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++materializedItems > 10000)
                        throw new InvalidOperationException("Snapshot materialization item bound exceeded.");
                    var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
                    var pending = declaration.RoleBindings.ToDictionary();
                    var resolved = new HashSet<string>(StringComparer.Ordinal);
                    while (pending.Count > 0)
                    {
                        var ready = pending.Where(pair => pair.Value.FromRole is null || resolved.Contains(pair.Value.FromRole)).ToArray();
                        if (ready.Length == 0) throw new InvalidOperationException("Snapshot bindings contain a cycle.");
                        foreach (var (role, binding) in ready)
                        {
                            string? id = null;
                            if (binding.Role is not null) id = snapshot.Roles.GetValueOrDefault(binding.Role)?.Id;
                            else if (binding.Reference) id = candidate;
                            else if (binding.InputEntityId is not null)
                                id = input.RootElement.GetProperty(binding.InputEntityId).GetString();
                            else if (bindings.TryGetValue(binding.FromRole!, out var sourceId) &&
                                     nodes[sourceId].Components.TryGetValue(binding.ComponentId!, out var source))
                            {
                                using var data = JsonDocument.Parse(source);
                                var value = data.RootElement;
                                foreach (var part in binding.Field!.Split('.'))
                                {
                                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
                                        break;
                                }
                                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("entityId", out var target))
                                    id = target.GetString();
                            }
                            if (id is not null)
                            {
                                if (!nodes.ContainsKey(id)) throw new InvalidOperationException("A snapshot binding targets an unauthorized entity.");
                                bindings.Add(role, id);
                            }
                            resolved.Add(role);
                            pending.Remove(role);
                        }
                    }
                    var values = bindings.Values.Distinct(StringComparer.Ordinal).SelectMany(id =>
                        nodes[id].Components.Where(pair => request.Mapping.Components.ContainsKey(pair.Key)).Select(pair =>
                            new EcsComponentView(request.StateSpaceId, id, request.Mapping.Components[pair.Key], pair.Value,
                                snapshot.ComponentRevisions.GetValueOrDefault(id)?.GetValueOrDefault(pair.Key) ?? 0,
                                default, default))).ToArray();
                    var result = await materializer.MaterializeSnapshotAsync(new(request.StateSpaceId,
                        definition.Reference, bindings), request.ApplicationId, values, cancellationToken);
                    foreach (var source in result.SourceRevisions)
                        foreach (var local in request.Mapping.Components.Where(pair => pair.Value == source.Type))
                            consumed.Add((source.EntityId, local.Key));
                    bytes += Encoding.UTF8.GetByteCount(result.OutputJson);
                    if (bytes > 1_048_576) throw new InvalidOperationException("Snapshot objects exceed their byte bound.");
                    var identities = bindings.ToDictionary(pair => pair.Key,
                        pair => new MechanicObjectEntity(pair.Value, nodes[pair.Value].Name), StringComparer.Ordinal);
                    using var output = JsonDocument.Parse(result.OutputJson);
                    singleton = new(definition.QualifiedId, definition.Version, definition.ContentHash, identities, output.RootElement.Clone());
                    if (candidate is not null) records.Add(new { id = candidate, name = nodes[candidate].Name, value = singleton.Value });
                }
                objects[key] = declaration.ReferenceComponentIds.Count == 0 ? singleton!
                    : new(definition.QualifiedId, definition.Version, definition.ContentHash,
                        new Dictionary<string, MechanicObjectEntity>(), JsonSerializer.SerializeToElement(records));
            }
            var assembled = snapshot with
            {
                Objects = objects,
                Roles = snapshot.Roles.ToDictionary(pair => pair.Key, pair => pair.Value with
                    { Components = Remaining(pair.Value.Id, pair.Value.Components), Contains = RemainingContents(pair.Value.Contains) }),
                References = snapshot.References.ToDictionary(pair => pair.Key, pair => pair.Value with
                    { Components = Remaining(pair.Value.Id, pair.Value.Components) })
            };
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(assembled)) > 1_048_576)
                throw new InvalidOperationException("Assembled snapshot exceeds its byte bound.");
            return new(assembled, []);

            IReadOnlyDictionary<string, string> Remaining(string id, IReadOnlyDictionary<string, string> values) =>
                values.Where(pair => !consumed.Contains((id, pair.Key))).ToDictionary();
            IReadOnlyList<ContainedProjection>? RemainingContents(IReadOnlyList<ContainedProjection>? values) =>
                values?.Select(value => value with
                {
                    Components = value.Components is null ? null : Remaining(value.Id, value.Components),
                    Contains = RemainingContents(value.Contains)
                }).ToArray();

            RegisteredProjectionDefinition RequireSnapshotDefinition(ProjectionReference reference, HashSet<string> visited)
            {
                var value = definitions.Get(reference.QualifiedId, reference.Version);
                if (value is null || value.ContentHash != reference.ContentHash || value.Owner != request.ApplicationId ||
                    value.ObjectContract is null || !value.ObjectContract.Access.ReadPerspectives.Contains(request.Audience.Perspective) ||
                    value.ObjectContract.Relationships.Count > 0 || value.ObjectContract.Collections.Count > 0)
                    throw new InvalidOperationException("The exact snapshot object is unavailable.");
                if (visited.Add(reference.QualifiedId + "/" + reference.Version))
                    foreach (var dependency in value.DependencyInputs) RequireSnapshotDefinition(dependency.Projection, visited);
                return value;
            }
            void Add(SnapshotNode node)
            {
                if (!nodes.TryGetValue(node.Id, out var prior))
                {
                    if (nodes.Count >= 10000) throw new InvalidOperationException("Snapshot node bound exceeded.");
                    nodes.Add(node.Id, node);
                    return;
                }
                var values = prior.Components.ToDictionary();
                foreach (var pair in node.Components)
                {
                    if (values.TryGetValue(pair.Key, out var previous) && previous != pair.Value)
                        throw new InvalidOperationException("Snapshot components conflict.");
                    values[pair.Key] = pair.Value;
                }
                nodes[node.Id] = new(node.Id, prior.Name.Length > 0 ? prior.Name : node.Name, values);
            }
            void Contents(IReadOnlyList<ContainedProjection>? contents, int depth)
            {
                if (depth > 16 || nodes.Count > 10000) throw new InvalidOperationException("Snapshot traversal exceeds its bound.");
                foreach (var item in contents ?? [])
                {
                    Add(new(item.Id, item.Name, item.Components ?? new Dictionary<string, string>()));
                    Contents(item.Contains, depth + 1);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException or NotSupportedException)
        {
            return ProjectionResult.Failed("OBJECT_SNAPSHOT_UNAVAILABLE");
        }
    }

    private sealed record SnapshotNode(string Id, string Name, IReadOnlyDictionary<string, string> Components);
}
