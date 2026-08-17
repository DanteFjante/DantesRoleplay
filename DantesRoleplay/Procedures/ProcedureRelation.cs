namespace DantesRoleplay.Procedures;

public enum ProcedureRelationKind
{
    /// <summary>Child belongs under parent in the contract hierarchy.</summary>
    Parent,

    /// <summary>Worth reading alongside; no hierarchy implied.</summary>
    Related,

    /// <summary>Source replaces target. Used when a contract is split or renamed.</summary>
    Supersedes
}

/// <summary>
/// A directed edge between two contracts. Kept as its own table rather than a parent column so
/// that "related" and "supersedes" cost nothing extra to add later.
/// </summary>
public sealed class ProcedureRelation
{
    public long Id { get; set; }

    public required string FromContractId { get; set; }

    public ProcedureContract? FromContract { get; set; }

    public required string ToContractId { get; set; }

    public ProcedureContract? ToContract { get; set; }

    public ProcedureRelationKind Kind { get; set; }
}
