using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Actions;

/// <summary>
/// One complete action: find a reusable mechanic, materialise its declared projection, run it in
/// the sandbox, and apply the returned effects atomically.
///
/// The interface lives in the kernel, but its implementation belongs in DataAccess. That keeps
/// the MCP surface thin while making the transaction boundary explicit and testable without an
/// MCP transport.
/// </summary>
public interface IActionRunner
{
    Task<ActionRunResult> RunAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Arguments supplied by the action caller.</summary>
public sealed record ActionRequest
{
    public required string Intent { get; init; }

    /// <summary>Author-defined role names mapped explicitly to permanent entity ids.</summary>
    public IReadOnlyDictionary<string, string> RoleEntityIds { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Free-form JSON passed to the mechanic as ctx.input.</summary>
    public string Input { get; init; } = "{}";

    /// <summary>Ruleset scope to prefer; shared mechanics remain eligible.</summary>
    public string? Scope { get; init; }

    /// <summary>Optional replay seed. The runner generates one when omitted.</summary>
    public long? Seed { get; init; }

    /// <summary>Procedure ids the caller says it consulted for this action.</summary>
    public IReadOnlyList<string> ProceduresUsed { get; init; } = [];
}

/// <summary>Stable failure information suitable for the MCP error envelope.</summary>
public sealed record ActionRunError(string Code, string Why, string Fix);

/// <summary>The observable result of an action attempt.</summary>
public sealed record ActionRunResult
{
    public bool Ok { get; init; }

    public ActionRunError? Error { get; init; }

    public string OperationId { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<MechanicSummary> Candidates { get; init; } = [];

    public MechanicSummary? Mechanic { get; init; }

    public MechanicProjection? Projection { get; init; }

    public MechanicOutput Output { get; init; } = new();

    public long? Seed { get; init; }

    public int AppliedCount { get; init; }

    public IReadOnlyList<string> AffectedEntityIds { get; init; } = [];

    public IReadOnlyList<string> Log { get; init; } = [];

    public string LimitHit { get; init; } = string.Empty;

    public int ElapsedMilliseconds { get; init; }

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public static ActionRunResult Failed(
        string code,
        string why,
        string fix,
        string summary,
        IReadOnlyList<MechanicSummary>? candidates = null,
        string operationId = "") =>
        new()
        {
            Error = new ActionRunError(code, why, fix),
            Summary = summary,
            Candidates = candidates ?? [],
            OperationId = operationId,
            NextSteps = [fix]
        };
}
