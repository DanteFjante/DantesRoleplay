namespace DantesRoleplay.Procedures;

/// <summary>
/// What the LLM sees in a list. Deliberately small: an agent browsing forty contracts should
/// spend a few hundred tokens, not a few thousand.
///
/// <paramref name="Governs"/> is included even though it costs tokens, because it is what turns
/// "which contract is relevant to what I am about to do?" from inference into a lookup — and the
/// list is exactly where that question gets asked.
/// </summary>
public sealed record ProcedureSummary(
    string Id,
    string Category,
    string Name,
    string Description,
    string Governs,
    string Matches,
    ProcedureStatus Status,
    int Version);

/// <summary>The full contract at one version.</summary>
public sealed record ProcedureDetail(
    string Id,
    string Category,
    string Name,
    string Description,
    string Governs,
    string Matches,
    string Instructions,
    string Constraints,
    ProcedureStatus Status,
    int Version,
    int LatestVersion,
    string CreatedBy,
    string ChangeNote,
    DateTime CreatedAt)
{
    /// <summary>
    /// Fingerprint of this revision's authored content, computed by the store on every write.
    /// See <see cref="DantesRoleplay.Content.ContentHash"/>. Not part of the positional record
    /// because it is derived from the content rather than authored alongside it.
    /// </summary>
    public string SourceHash { get; init; } = string.Empty;
}

/// <summary>Used by the orientation view: how many contracts exist per category.</summary>
public sealed record ProcedureCategoryCount(string Category, int Count);

/// <summary>
/// A create-or-revise request. There is no separate "update" path on purpose: writing an
/// existing id appends a version, which is the only way content ever changes.
/// </summary>
public sealed record WriteProcedureRequest
{
    public required string Id { get; init; }

    public required string Category { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Instructions { get; init; }

    /// <summary>Which operations this contract governs. See <see cref="ProcedureContractVersion.Governs"/>.</summary>
    public string Governs { get; init; } = string.Empty;

    /// <summary>Phrases that should find this contract. See <see cref="ProcedureContractVersion.Matches"/>.</summary>
    public string Matches { get; init; } = string.Empty;

    public string Constraints { get; init; } = string.Empty;

    public ProcedureStatus? Status { get; init; }

    public string CreatedBy { get; init; } = "llm";

    public string ChangeNote { get; init; } = string.Empty;
}

/// <summary>Outcome of a write, so callers can tell a create from a revision without re-reading.</summary>
public sealed record WriteProcedureResult(ProcedureDetail Procedure, bool Created);

/// <summary>
/// One named validation performed by a dry run.
///
/// A dry run that reports only "ok" is worthless — the caller cannot tell whether it validated
/// the schema or the contract's own constraints. Naming each check is what makes a clean dry run
/// mean something.
/// </summary>
/// <param name="Name">Short identifier, e.g. "category-known".</param>
/// <param name="Passed">False marks a problem worth acting on; a warning still passes.</param>
/// <param name="Detail">What was checked and what was found.</param>
public sealed record WriteCheck(string Name, bool Passed, string Detail);
