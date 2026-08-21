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

    /// <summary>
    /// Proposals made by this child's nested children. They are carried upward only; no child
    /// applies them and they are never made available as another child's input.
    /// </summary>
    public CompositionProposal Proposal { get; init; } = CompositionProposal.Empty;
}

/// <summary>Either the parent projection enriched with child results, or an actionable failure.</summary>
public sealed record CompositionResult(MechanicProjection? Projection, string Error = "")
{
    public bool Ok => Projection is not null && string.IsNullOrEmpty(Error);

    /// <summary>Ordered child proposals that the top-level action must validate and apply atomically.</summary>
    public CompositionProposal Proposal { get; init; } = CompositionProposal.Empty;

    public static CompositionResult Failed(string error) => new(null, error);
}

/// <summary>
/// The ordered effect-bearing portion of a composed child tree. It deliberately excludes output
/// data, narration, logs, and role bindings: those remain frozen child-result metadata.
/// </summary>
public sealed record CompositionProposal(
    IReadOnlyList<Effects.Effect> Effects,
    IReadOnlyList<Events.DeclaredEvent> Events,
    IReadOnlyList<Notifications.DeclaredNotification> Notifications)
{
    public static CompositionProposal Empty { get; } = new([], [], []);

    public CompositionProposal Append(CompositionProposal other) => new(
        [.. Effects, .. other.Effects],
        [.. Events, .. other.Events],
        [.. Notifications, .. other.Notifications]);

    public CompositionProposal Append(MechanicOutput output) => new(
        [.. Effects, .. output.Effects],
        [.. Events, .. output.Events],
        [.. Notifications, .. output.Notifications]);
}
