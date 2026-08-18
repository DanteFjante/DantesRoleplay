namespace DantesRoleplay.Mechanics;

/// <summary>
/// Turns "this mechanic needs these roles with these components" plus "these entities are filling
/// them" into the single frozen object the sandbox receives (ARCHITECTURE.md §3.6a).
///
/// This is the other half of the purity guarantee. The sandbox makes a mechanic UNABLE to query;
/// the resolver is what makes that survivable, by fetching everything it declared up front. The
/// two together are why the same inputs give the same outputs, which is the only reason a rule an
/// LLM wrote at midnight can be reviewed in the morning.
///
/// It also enforces the other direction, which is easy to miss: a mechanic gets ONLY what it
/// declared. Not the entity's other components, not its relationships, not the rest of the world.
/// A rule that reads something it never declared would be a rule whose stated requirements are a
/// lie, and the requirements are what the supervision view shows instead of the source.
/// </summary>
public interface IProjectionResolver
{
    /// <param name="requirements">The mechanic's declared projection spec.</param>
    /// <param name="roleAssignments">Role name to entity id, supplied by the caller.</param>
    /// <param name="input">A valid JSON object for the specifics of this action.</param>
    /// <param name="seed">Recorded with the operation so the run can be replayed exactly.</param>
    Task<ProjectionResult> ResolveAsync(
        MechanicRequirements requirements,
        IReadOnlyDictionary<string, string> roleAssignments,
        string input = "{}",
        long seed = 0,
        CancellationToken cancellationToken = default);
}

/// <param name="Projection">Null when <paramref name="Problems"/> is non-empty.</param>
/// <param name="Problems">
/// Everything wrong at once — invalid action input, a missing role, an unknown entity, a role the
/// mechanic does not declare — each phrased as what to do about it. One round trip, not one per
/// mistake.
/// </param>
public sealed record ProjectionResult(MechanicProjection? Projection, IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0 && Projection is not null;

    public static ProjectionResult Failed(params string[] problems) => new(null, problems);
}
