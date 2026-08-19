namespace DantesRoleplay.Mechanics;

/// <summary>
/// Storage for mechanics. Deliberately shaped like <see cref="Procedures.IProcedureStore"/>:
/// authored content, append-only versioning, find-then-read, and a dry run that reports named
/// checks. Two subsystems that behave the same way are one thing to learn instead of two.
/// </summary>
public interface IMechanicStore
{
    /// <param name="query">Matched against id, name, description and the author's match phrases.</param>
    /// <param name="scope">
    /// Ruleset to prefer. Results in that scope rank above shared ones; shared mechanics are always
    /// included, because a campaign that silently lost the base rules would be a mystery to debug.
    /// </param>
    Task<IReadOnlyList<MechanicSummary>> FindAsync(
        string? query = null,
        string? category = null,
        string? scope = null,
        bool includeInactive = false,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <param name="version">Omit for the live version.</param>
    Task<MechanicDetail?> GetAsync(
        string id,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Create, or append a version to an existing id. There is no update path and no delete.</summary>
    Task<WriteMechanicResult> WriteAsync(
        WriteMechanicRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate without writing: id format, whether this would create a version or a mechanic,
    /// whether the requirements parse, whether the components named in them exist, and whether
    /// something near-identical is already here.
    ///
    /// The last one is the anti-sprawl guard (§P12). A system whose whole premise is that an LLM
    /// adds rules while playing accumulates six subtly different versions of the same rule unless
    /// something asks the question at the moment of writing.
    /// </summary>
    Task<IReadOnlyList<MechanicCheck>> CheckAsync(
        WriteMechanicRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MechanicCategoryCount>> GetCategoriesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the LLM sees in a list. No source: a candidate list of ten mechanics should cost a few
/// hundred tokens, and the source is what <see cref="IMechanicStore.GetAsync"/> is for.
/// </summary>
public sealed record MechanicSummary(
    string Id,
    string Category,
    string Name,
    string Description,
    string Matches,
    string Scope,
    MechanicStatus Status,
    int Version);

/// <summary>The full mechanic at one version, source included.</summary>
public sealed record MechanicDetail(
    string Id,
    string Category,
    string Name,
    string Description,
    string Matches,
    string Requirements,
    string Source,
    string Scope,
    MechanicStatus Status,
    int Version,
    int LatestVersion,
    string CreatedBy,
    string ChangeNote,
    DateTime CreatedAt)
{
    public string SourceHash { get; init; } = string.Empty;
}

public sealed record MechanicCategoryCount(string Category, int Count);

/// <param name="Passed">Whether this check passed.</param>
/// <param name="Detail">What was found, and what to do about it.</param>
/// <param name="Blocking">Whether a failed check prevents a commit. Near-duplicate detection is a warning.</param>
public sealed record MechanicCheck(string Name, bool Passed, string Detail, bool Blocking = true);

public sealed record WriteMechanicRequest
{
    public required string Id { get; init; }

    public required string Category { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Matches { get; init; } = string.Empty;

    /// <summary>JSON projection spec. See <see cref="MechanicRequirements"/>.</summary>
    public string Requirements { get; init; } = "{}";

    public required string Source { get; init; }

    public string Scope { get; init; } = string.Empty;

    public MechanicStatus? Status { get; init; }

    public string CreatedBy { get; init; } = "llm";

    public string ChangeNote { get; init; } = string.Empty;
}

public sealed record WriteMechanicResult(MechanicDetail Mechanic, bool Created);
