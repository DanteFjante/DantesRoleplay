namespace DantesRoleplay.Procedures;

/// <summary>
/// Reads and writes procedure contracts. Declared in the core project so nothing above the
/// data layer needs to know that Entity Framework or SQLite exist.
/// </summary>
public interface IProcedureStore
{
    /// <summary>
    /// List or search. A null or empty <paramref name="query"/> returns everything, which is the
    /// common case: for a few dozen contracts, a list is a better answer than a search.
    /// </summary>
    Task<IReadOnlyList<ProcedureSummary>> FindAsync(
        string? query = null,
        string? category = null,
        bool includeInactive = false,
        int limit = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one contract. A null <paramref name="version"/> means the live one; a number pins a
    /// historical revision, which is how an operation recorded months ago stays legible.
    /// </summary>
    Task<ProcedureDetail?> GetAsync(
        string id,
        int? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>Create a contract, or append a revision. Content is never mutated in place.</summary>
    Task<WriteProcedureResult> WriteAsync(
        WriteProcedureRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProcedureSummary>> GetVersionsAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exact category-path counts for navigation. Archived contracts are omitted by default,
    /// matching <see cref="FindAsync"/>; callers that need to describe all authored content,
    /// such as orientation and write guidance, opt in explicitly.
    /// </summary>
    Task<IReadOnlyList<ProcedureCategoryCount>> GetCategoriesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the same validations a write would, without writing. Returns one entry per named
    /// check so a caller can see exactly what was and was not verified.
    /// </summary>
    Task<IReadOnlyList<WriteCheck>> CheckAsync(
        WriteProcedureRequest request,
        CancellationToken cancellationToken = default);
}
