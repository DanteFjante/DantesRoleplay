namespace DantesRoleplay.Mechanics;

/// <summary>
/// Runs one mechanic's source against one projection and returns what it proposed.
///
/// The interface is this small on purpose. Everything the engine may do is in the signature: it
/// gets source, it gets data, it returns a result. It is handed no store, no DbContext and no
/// connection, so "a mechanic wrote to the database" is not a bug that can be introduced later —
/// there is nothing here to write with.
///
/// Implemented in <c>DantesRoleplay.RuleAccess</c> so the JavaScript engine stays out of the
/// kernel, exactly as Entity Framework does.
/// </summary>
public interface IMechanicEngine
{
    /// <summary>
    /// Execute <paramref name="source"/> with <paramref name="projection"/> in scope.
    ///
    /// Never throws for anything the mechanic did — a syntax error, a thrown value or an exceeded
    /// limit all come back as <see cref="MechanicRunResult.Ok"/> false with an explanation. Author
    /// error is an expected outcome here, not an exceptional one: the author is an LLM writing
    /// code mid-session, and the message it gets back is how it fixes the rule.
    /// </summary>
    Task<MechanicRunResult> RunAsync(
        string source,
        MechanicProjection projection,
        ExecutionLimits limits,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The sandbox's boundaries, set on the very first mechanic that ever runs rather than added after
/// something hangs.
///
/// ARCHITECTURE.md §2 lists arbitrary AI-written JavaScript as the major risk of this design, and
/// these values are most of the answer to it. The other part is not expressible as a number and is
/// stated here so it is never quietly dropped: the engine must be constructed WITHOUT CLR access.
/// A JavaScript interpreter that can reach .NET types is not a sandbox — it is a second way to
/// call anything the process can call, including the file system and the database.
/// </summary>
public sealed record ExecutionLimits
{
    /// <summary>
    /// Statement budget. Stops a runaway loop that a wall-clock timeout would only catch after the
    /// caller has already waited.
    /// </summary>
    public int MaxStatements { get; init; } = 100_000;

    /// <summary>Wall clock. Catches what a statement count cannot — a single pathological call.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Memory ceiling. A loop appending to an array exhausts this long before the process.</summary>
    public long MemoryBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Recursion depth, so runaway recursion fails as a limit rather than a stack overflow.</summary>
    public int MaxRecursionDepth { get; init; } = 64;

    /// <summary>How many effects one run may propose. A rule that returns ten thousand is broken.</summary>
    public int MaxEffects { get; init; } = 200;

    /// <summary>Log lines kept. Beyond this the run is failing anyway and the tail is not the interesting part.</summary>
    public int MaxLogLines { get; init; } = 100;

    /// <summary>
    /// Deliberately generous rather than tight. These exist to stop runaway and hostile code, not
    /// to make authors think about performance — a limit that a legitimate rule trips is a limit
    /// that teaches the author to work around the sandbox.
    /// </summary>
    public static ExecutionLimits Default { get; } = new();
}
