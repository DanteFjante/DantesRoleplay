namespace DantesRoleplay.Procedures;

/// <summary>
/// The lifecycle state of a procedure contract.
/// </summary>
public enum ProcedureStatus
{
    Draft,
    Active,
    Deprecated,
    Archived
}

/// <summary>
/// A procedure contract: an instruction the LLM retrieves before performing an operation.
///
/// This row holds IDENTITY only. All content (name, description, instructions, constraints)
/// lives on <see cref="ProcedureContractVersion"/>, so there is exactly one source of truth
/// for what a contract currently says. <see cref="CurrentVersion"/> names which version is live.
/// </summary>
public sealed class ProcedureContract
{
    /// <summary>Dotted identifier, e.g. "procedure.system.modify". Chosen by the author, stable forever.</summary>
    public required string Id { get; set; }

    /// <summary>Coarse grouping used for browsing, e.g. "system", "database", "mcp", "contracts".</summary>
    public required string Category { get; set; }

    public ProcedureStatus Status { get; set; } = ProcedureStatus.Draft;

    /// <summary>
    /// Version number of the live content. Not a foreign key on purpose: a FK here would create
    /// a cycle with <see cref="ProcedureContractVersion.ContractId"/> and complicate inserts.
    /// </summary>
    public int CurrentVersion { get; set; }

    /// <summary>
    /// UTC. Every timestamp in the kernel is a plain DateTime rather than a DateTimeOffset,
    /// because SQLite refuses to ORDER BY a DateTimeOffset ("SQLite does not support expressions
    /// of type 'DateTimeOffset' in ORDER BY clauses"). Everything here is written as UtcNow, so
    /// the offset carried no information anyway.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<ProcedureContractVersion> Versions { get; set; } = new List<ProcedureContractVersion>();
}
