using DantesRoleplay.Actions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Materialises a mechanic's declared requirements in one pass.
///
/// "One query" in §3.6a is the intent rather than a literal count — it is two here, and both are
/// batched across every role at once. What matters is that the number of round trips does not grow
/// with the number of participants, because the alternative is the N+1 pattern that made
/// TravelRoleplay's rules slow in exactly the situations that had the most going on.
/// </summary>
public sealed class ProjectionResolver(DantesRoleplayDbContext db) : IProjectionResolver
{
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<ProjectionResult> ResolveAsync(
        MechanicRequirements requirements,
        IReadOnlyDictionary<string, string> roleAssignments,
        string input = "{}",
        long seed = 0,
        CancellationToken cancellationToken = default)
    {
        requirements ??= new MechanicRequirements();
        roleAssignments ??= new Dictionary<string, string>();

        var problems = new List<string>();

        if (!ActionInput.TryValidateObject(input, out var inputProblem))
        {
            problems.Add($"INVALID_INPUT: {inputProblem}");
        }

        // A role the mechanic does not declare is a caller misunderstanding, not a harmless extra.
        // Passing "target" to a rule that never mentions one usually means the wrong mechanic was
        // chosen, and silently dropping it would turn that into a puzzling result instead.
        foreach (var supplied in roleAssignments.Keys)
        {
            if (!requirements.Roles.ContainsKey(supplied))
            {
                problems.Add(
                    $"UNKNOWN_ROLE: This mechanic does not have a role called '{supplied}'. It takes: " +
                    $"{(requirements.Roles.Count == 0 ? "(none)" : string.Join(", ", requirements.Roles.Keys))}.");
            }
        }

        var needed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (role, requirement) in requirements.Roles)
        {
            if (roleAssignments.TryGetValue(role, out var entityId) && !string.IsNullOrWhiteSpace(entityId))
            {
                needed[role] = entityId.Trim();
                continue;
            }

            if (!requirement.Optional)
            {
                var wants = requirement.Components.Count == 0
                    ? "no components"
                    : string.Join(", ", requirement.Components);

                problems.Add(
                    $"MISSING_REQUIRED_ROLE: Role '{role}' is required and was not supplied. " +
                    $"{(requirement.Description.Length > 0 ? requirement.Description + " " : "")}" +
                    $"It reads: {wants}. Pass roles: {{\"{role}\": \"<entityId>\"}}.");
            }
        }

        if (problems.Count > 0)
        {
            return new ProjectionResult(null, problems);
        }

        if (needed.Count == 0)
        {
            return new ProjectionResult(
                new MechanicProjection { Input = input, Seed = seed },
                []);
        }

        var wantedIds = needed.Values.Distinct(StringComparer.Ordinal).ToList();

        // Every component of every wanted entity comes back, and the filtering to what each ROLE
        // declared happens below. Filtering in SQL would need one query per role, and the whole
        // point of a declared projection is that materialising it is a fixed cost.
        var entities = await _db.Entities
            .AsNoTracking()
            .Where(e => wantedIds.Contains(e.Id) && e.DeletedAt == null)
            .Select(e => new
            {
                e.Id,
                e.Name,
                Components = e.Components.Select(c => new { c.DefinitionId, c.Data }).ToList()
            })
            .ToListAsync(cancellationToken);

        var byId = entities.ToDictionary(e => e.Id, StringComparer.Ordinal);

        foreach (var (role, entityId) in needed)
        {
            if (!byId.ContainsKey(entityId))
            {
                problems.Add(
                    $"UNKNOWN_ENTITY: Role '{role}' names entity '{entityId}', which does not exist or was deleted. " +
                    "Check it with get_entities.");
            }
        }

        if (problems.Count > 0)
        {
            return new ProjectionResult(null, problems);
        }

        var containers = await _db.Containments
            .AsNoTracking()
            .Where(c => wantedIds.Contains(c.ContainedId))
            .Select(c => new { c.ContainedId, c.ContainerId, c.Slot })
            .ToListAsync(cancellationToken);

        var containerOf = containers.ToDictionary(c => c.ContainedId, StringComparer.Ordinal);

        var contentsWanted = needed
            .Where(pair => requirements.Roles[pair.Key].IncludeContents)
            .Select(pair => pair.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var contents = contentsWanted.Count == 0
            ? []
            : await _db.Containments
                .AsNoTracking()
                .Where(c => contentsWanted.Contains(c.ContainerId))
                .Join(
                    _db.Entities.Where(e => e.DeletedAt == null),
                    c => c.ContainedId,
                    e => e.Id,
                    (c, e) => new { c.ContainerId, e.Id, e.Name, c.Slot })
                .ToListAsync(cancellationToken);

        var projection = new MechanicProjection
        {
            Input = input,
            Seed = seed
        };

        foreach (var (role, entityId) in needed)
        {
            var requirement = requirements.Roles[role];
            var entity = byId[entityId];

            // THE filter. A mechanic sees the components it declared and nothing else — including
            // when the entity happens to carry a dozen others. Requirements that understate what a
            // rule reads would make the supervision view a lie, so they are also what is enforced.
            var declared = entity.Components
                .Where(c => requirement.Components.Contains(c.DefinitionId, StringComparer.Ordinal))
                .ToDictionary(c => c.DefinitionId, c => c.Data, StringComparer.Ordinal);

            containerOf.TryGetValue(entityId, out var containment);

            projection.Roles[role] = new EntityProjection(
                entity.Id,
                entity.Name,
                declared,
                containment?.ContainerId,
                containment?.Slot ?? string.Empty,
                requirement.IncludeContents
                    ? contents
                        .Where(c => c.ContainerId == entityId)
                        .OrderBy(c => c.Name, StringComparer.Ordinal)
                        .Select(c => new ContainedProjection(c.Id, c.Name, c.Slot))
                        .ToList()
                    : null);
        }

        return new ProjectionResult(projection, []);
    }
}
