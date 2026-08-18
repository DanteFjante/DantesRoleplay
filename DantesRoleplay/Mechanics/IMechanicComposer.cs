namespace DantesRoleplay.Mechanics;

/// <summary>Runs a declared child mechanic without applying its proposed effects.</summary>
public interface IMechanicComposer
{
    /// <summary>
    /// Materialises every child declared by a parent before that parent's JavaScript runs.
    /// </summary>
    Task<CompositionResult> ComposeAsync(
        string parentMechanicId,
        MechanicRequirements requirements,
        MechanicProjection projection,
        int depth = 0,
        IReadOnlySet<string>? ancestors = null,
        CancellationToken cancellationToken = default);

    Task<ChildMechanicRun> RunChildAsync(
        ChildMechanicInvocation invocation,
        CancellationToken cancellationToken = default);
}

public sealed record ChildMechanicInvocation(
    string MechanicId,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    string Input,
    long Seed,
    int Depth = 0,
    IReadOnlySet<string>? Ancestors = null);

public sealed record ChildMechanicRun(
    MechanicDetail? Mechanic,
    MechanicProjection? Projection,
    MechanicRunResult? Run,
    string Error = "")
{
    public bool Ok => string.IsNullOrEmpty(Error) && Mechanic is not null && Projection is not null && Run?.Ok == true;
}

/// <summary>Either the parent projection enriched with child results, or an actionable failure.</summary>
public sealed record CompositionResult(MechanicProjection? Projection, string Error = "")
{
    public bool Ok => Projection is not null && string.IsNullOrEmpty(Error);

    public static CompositionResult Failed(string error) => new(null, error);
}
