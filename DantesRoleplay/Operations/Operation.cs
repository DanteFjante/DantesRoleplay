namespace DantesRoleplay.Operations;

/// <summary>
/// One recorded AI operation. Deliberately NOT chain-of-thought — this records observable
/// decisions: what was asked, which contracts were consulted, which tool ran, what changed.
///
/// Written for every tool call, including reads. Reads are cheap to store and are half of the
/// story when reconstructing why the agent did something — and, since 2026-08-16, they are also
/// how <see cref="ProceduresRead"/> gets populated without any session state.
/// </summary>
public sealed class Operation
{
    public required string Id { get; set; }

    /// <summary>
    /// UTC. Plain DateTime rather than DateTimeOffset on purpose: SQLite cannot ORDER BY a
    /// DateTimeOffset, and this column is ordered on every read.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>The MCP tool that ran, e.g. "write_procedure".</summary>
    public required string Tool { get; set; }

    /// <summary>
    /// The primary thing this operation acted on — a contract id, an entity id. Empty when the
    /// operation had no single subject (a list, an orientation).
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// What the caller said it was trying to achieve, in its own words. Supplied by the LLM;
    /// the kernel never invents it. Empty when the caller did not say.
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// Procedures the caller SAYS it consulted. Self-reported and unverified — this is the
    /// agent's account of its own reasoning, which is worth having but is not evidence.
    /// Compare against <see cref="ProceduresRead"/>.
    /// </summary>
    public string ProceduresCited { get; set; } = string.Empty;

    /// <summary>
    /// Procedures the agent DEMONSTRABLY read shortly before this operation, derived from the
    /// log's own get_procedure entries rather than from anything the caller said.
    ///
    /// A citation that does not appear here means the agent claimed a procedure it never opened.
    /// A read that is not cited is usually harmless. The gap between the two columns is the
    /// interesting part, and it is why they are stored separately rather than merged.
    /// </summary>
    public string ProceduresRead { get; set; } = string.Empty;

    /// <summary>
    /// Whether this operation was one that CONSUMES read evidence — a real write, as opposed to a
    /// dry run or a read-only call.
    ///
    /// Stored rather than inferred, because "no procedures read" and "did not look" are different
    /// facts that an empty <see cref="ProceduresRead"/> cannot tell apart. Without it, a dry run
    /// that cited a procedure was reported as an unbacked citation — the audit accusing an agent
    /// of skipping the manual at the exact moment it was following it.
    /// </summary>
    public bool ConsumedReadEvidence { get; set; }

    /// <summary>One line describing the outcome, e.g. "created procedure.system.modify v1".</summary>
    public string Summary { get; set; } = string.Empty;

    public bool Success { get; set; }

    /// <summary>Error code when <see cref="Success"/> is false. Empty otherwise.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Mechanic identity when this operation executed an action.</summary>
    public string MechanicId { get; set; } = string.Empty;

    /// <summary>Mechanic revision that produced the action result.</summary>
    public int? MechanicVersion { get; set; }

    /// <summary>Replay seed supplied to the sandbox.</summary>
    public long? Seed { get; set; }

    /// <summary>Frozen projection handed to the mechanic, serialized as JSON.</summary>
    public string ProjectionJson { get; set; } = string.Empty;
}
