namespace DantesRoleplay.Procedures;

/// <summary>
/// One immutable revision of a contract's content. Never updated in place — every edit appends
/// a new row. This is what makes "which instructions was the agent following in session 4?"
/// answerable.
/// </summary>
public sealed class ProcedureContractVersion
{
    public long Id { get; set; }

    public required string ContractId { get; set; }

    public ProcedureContract? Contract { get; set; }

    /// <summary>1-based, incrementing per contract.</summary>
    public int Version { get; set; }

    /// <summary>Short human title, e.g. "Modify the application".</summary>
    public required string Name { get; set; }

    /// <summary>One or two sentences. This is what the LLM sees in a list, so keep it tight.</summary>
    public required string Description { get; set; }

    /// <summary>
    /// Which operations this contract governs — comma-separated tool names or operation phrases,
    /// e.g. "write_procedure" or "adding a component definition".
    ///
    /// Added 2026-08-16 after a cold-model test. The system's one rule is "retrieve and follow the
    /// relevant procedure contracts", and without this field "relevant" was a guess: the model had
    /// to infer from titles which of three system.* contracts covered its task, and hedged by
    /// reading all of them. This turns that guess into a lookup.
    /// </summary>
    public string Governs { get; set; } = string.Empty;

    /// <summary>The procedure itself — usually numbered steps. Markdown.</summary>
    public required string Instructions { get; set; }

    /// <summary>
    /// Hard rules, separated from <see cref="Instructions"/> on purpose: instructions are
    /// guidance, constraints are things that must not happen. Keeping them apart is what later
    /// allows a constraint to be promoted from prose into an enforced check.
    /// </summary>
    public string Constraints { get; set; } = string.Empty;

    /// <summary>
    /// Fingerprint of the bootstrap file this revision came from, when it came from one.
    ///
    /// Stored rather than recomputed. Re-deriving a hash from round-tripped content makes the
    /// comparison depend on every field surviving storage byte-for-byte — line endings, enum
    /// formatting, trimming — and any drift there silently reseeds everything on every start.
    /// Empty for revisions written through MCP rather than seeded.
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>Who wrote this revision — "seed", an operation id, or a caller-supplied name.</summary>
    public required string CreatedBy { get; set; }

    /// <summary>Why this revision exists. Empty for version 1.</summary>
    public string ChangeNote { get; set; } = string.Empty;

    /// <summary>UTC — see <see cref="ProcedureContract.CreatedAt"/> for why this is not a DateTimeOffset.</summary>
    public DateTime CreatedAt { get; set; }
}
