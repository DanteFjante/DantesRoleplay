using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Ecs;

/// <summary>
/// Interprets only the generic role-policy annotations carried by registered component schemas.
/// SQLite transaction ownership stays with the caller so validation observes the staged write.
/// </summary>
public sealed class SqliteEcsRoleConstraintValidator(DantesRoleplayDbContext db)
    : IEcsRoleConstraintValidator
{
    public async Task ValidateStateSpaceAsync(
        string stateSpaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateSpaceId);
        var stateSpace = await db.Set<ApplicationStateSpaceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == stateSpaceId, cancellationToken)
            ?? throw Failure("STATE_SPACE_UNKNOWN", "The constraint scope does not exist.");
        var scope = EcsComponentRolePolicyParser.ParseScope(stateSpace.Scope);
        var owners = await EligibleOwnersAsync(stateSpace, cancellationToken);
        var typeRows = await db.Set<ComponentTypeRecord>().AsNoTracking()
            .Where(value => owners.Contains(value.ApplicationId))
            .Select(value => value.QualifiedId)
            .ToArrayAsync(cancellationToken);
        if (typeRows.Length == 0) return;

        var versions = await db.Set<ComponentTypeVersionRecord>().AsNoTracking()
            .Where(value => typeRows.Contains(value.QualifiedId))
            .OrderBy(value => value.QualifiedId).ThenBy(value => value.Version)
            .ToArrayAsync(cancellationToken);
        var policies = versions.ToDictionary(
            value => (value.QualifiedId, value.Version),
            value => EcsComponentRolePolicyParser.Parse(value.SchemaJson));
        var constraints = EffectiveConstraints(versions, policies)
            .Where(value => value.Scope == scope).ToArray();
        if (constraints.Length == 0) return;

        var entities = await db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId && value.DeletedAtUtc == null)
            .OrderBy(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var components = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == stateSpaceId)
            .OrderBy(value => value.EntityId).ThenBy(value => value.QualifiedTypeId)
            .ToArrayAsync(cancellationToken);
        var componentsByEntity = components.GroupBy(value => value.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ApplicationEcsComponentRecord>)group.ToArray(),
                StringComparer.Ordinal);

        foreach (var constraint in constraints.OrderBy(value => value.Id, StringComparer.Ordinal))
            Validate(constraint, entities, componentsByEntity, policies);
    }

    private async Task<HashSet<string>> EligibleOwnersAsync(
        ApplicationStateSpaceRecord stateSpace,
        CancellationToken cancellationToken)
    {
        var baseOwners = await db.Set<ApplicationRevisionBaseRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == stateSpace.ApplicationId
                && value.Revision == stateSpace.ApplicationRevision)
            .Select(value => value.BaseApplicationId)
            .ToArrayAsync(cancellationToken);
        var owners = baseOwners.ToHashSet(StringComparer.Ordinal);
        owners.Add(stateSpace.ApplicationId);
        owners.Add(ApplicationIdentifier.System.Value);
        return owners;
    }

    private static IReadOnlyList<EcsEntityRoleConstraint> EffectiveConstraints(
        IReadOnlyList<ComponentTypeVersionRecord> versions,
        IReadOnlyDictionary<(string Id, int Version), EcsComponentRolePolicy> policies)
    {
        var latest = versions.GroupBy(value => value.QualifiedId, StringComparer.Ordinal)
            .Select(group => group.MaxBy(value => value.Version)!).ToArray();
        var result = new Dictionary<string, (EcsEntityRoleConstraint Value, string Canonical)>(StringComparer.Ordinal);
        foreach (var version in latest)
        {
            foreach (var constraint in policies[(version.QualifiedId, version.Version)].Constraints)
            {
                var canonical = JsonSerializer.Serialize(constraint);
                if (result.TryGetValue(constraint.Id, out var existing))
                {
                    if (existing.Canonical != canonical)
                        throw Failure("ROLE_CONSTRAINT_ID_CONFLICT",
                            $"Constraint '{constraint.Id}' has conflicting immutable declarations.");
                    continue;
                }
                result.Add(constraint.Id, (constraint, canonical));
            }
        }
        return result.Values.Select(value => value.Value).ToArray();
    }

    private static void Validate(
        EcsEntityRoleConstraint constraint,
        IReadOnlyList<ApplicationEcsEntityRecord> entities,
        IReadOnlyDictionary<string, IReadOnlyList<ApplicationEcsComponentRecord>> componentsByEntity,
        IReadOnlyDictionary<(string Id, int Version), EcsComponentRolePolicy> policies)
    {
        var selected = entities.Where(entity => Matches(
            constraint.Selector, Components(entity.Id, componentsByEntity), policies)).ToArray();
        if (selected.Length < constraint.MinimumEnabled
            || constraint.MaximumEnabled is { } maximum && selected.Length > maximum)
            throw Failure("ROLE_CARDINALITY_VIOLATION",
                $"Constraint '{constraint.Id}' permits {constraint.MinimumEnabled} through "
                + $"{constraint.MaximumEnabled?.ToString() ?? "unbounded"} enabled entities, but found {selected.Length}.");

        foreach (var entity in selected)
        {
            var entityComponents = Components(entity.Id, componentsByEntity);
            foreach (var required in constraint.Requires)
            {
                if (!Matches(required, entityComponents, policies))
                    throw Failure("ROLE_REQUIREMENT_VIOLATION",
                        $"Entity '{entity.Id}' selected by '{constraint.Id}' is missing a required role or component.");
            }
        }

        if (constraint.UniqueKeys.Count == 0) return;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in selected)
        {
            var entityComponents = Components(entity.Id, componentsByEntity);
            var composite = string.Join('\u001f', constraint.UniqueKeys.Select(key =>
                UniqueValue(constraint.Id, entity.Id, key, entityComponents, policies)));
            if (values.TryGetValue(composite, out var previous))
                throw Failure("ROLE_UNIQUENESS_VIOLATION",
                    $"Entities '{previous}' and '{entity.Id}' collide on the uniqueness keys of '{constraint.Id}'.");
            values.Add(composite, entity.Id);
        }
    }

    private static IReadOnlyList<ApplicationEcsComponentRecord> Components(
        string entityId,
        IReadOnlyDictionary<string, IReadOnlyList<ApplicationEcsComponentRecord>> values) =>
        values.TryGetValue(entityId, out var result) ? result : [];

    private static bool Matches(
        EcsEntitySelector selector,
        IReadOnlyList<ApplicationEcsComponentRecord> components,
        IReadOnlyDictionary<(string Id, int Version), EcsComponentRolePolicy> policies) =>
        selector.Kind switch
        {
            EcsEntitySelectorKind.Component => components.Any(value => value.QualifiedTypeId == selector.Value),
            EcsEntitySelectorKind.SemanticRole => components.Any(value =>
                policies.TryGetValue((value.QualifiedTypeId, value.TypeVersion), out var policy)
                && policy.SemanticRoles.Contains(selector.Value, StringComparer.Ordinal)),
            _ => false
        };

    private static string UniqueValue(
        string constraintId,
        string entityId,
        EcsEntityUniquenessKey key,
        IReadOnlyList<ApplicationEcsComponentRecord> components,
        IReadOnlyDictionary<(string Id, int Version), EcsComponentRolePolicy> policies)
    {
        var sources = components.Where(value => ComponentMatches(key.Source, value, policies)).ToArray();
        if (sources.Length != 1)
            throw Failure("ROLE_UNIQUENESS_SOURCE_INVALID",
                $"Entity '{entityId}' must have exactly one source for key '{key.Name}' in '{constraintId}'.");
        using var document = JsonDocument.Parse(sources[0].Data, new JsonDocumentOptions { MaxDepth = 32 });
        if (!TryPointer(document.RootElement, key.JsonPointer, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                or JsonValueKind.Object or JsonValueKind.Array)
            throw Failure("ROLE_UNIQUENESS_VALUE_INVALID",
                $"Entity '{entityId}' has no scalar value for key '{key.Name}' in '{constraintId}'.");
        return key.Name + "=" + value.ValueKind + ":" + value.GetRawText();
    }

    private static bool ComponentMatches(
        EcsEntitySelector selector,
        ApplicationEcsComponentRecord component,
        IReadOnlyDictionary<(string Id, int Version), EcsComponentRolePolicy> policies) =>
        selector.Kind switch
        {
            EcsEntitySelectorKind.Component => component.QualifiedTypeId == selector.Value,
            EcsEntitySelectorKind.SemanticRole =>
                policies.TryGetValue((component.QualifiedTypeId, component.TypeVersion), out var policy)
                && policy.SemanticRoles.Contains(selector.Value, StringComparer.Ordinal),
            _ => false
        };

    private static bool TryPointer(JsonElement root, string pointer, out JsonElement value)
    {
        value = root;
        if (pointer.Length == 0) return true;
        foreach (var encoded in pointer.Split('/').Skip(1))
        {
            var segment = encoded.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!value.TryGetProperty(segment, out value)) return false;
                continue;
            }
            if (value.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index)
                && index >= 0 && index < value.GetArrayLength())
            {
                value = value[index];
                continue;
            }
            return false;
        }
        return true;
    }

    private static EcsRoleConstraintException Failure(string code, string message) => new(code, message);
}
