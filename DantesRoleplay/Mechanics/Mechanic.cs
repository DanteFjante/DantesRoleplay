namespace DantesRoleplay.Mechanics;

/// <summary>
/// The lifecycle state of a mechanic. Mirrors <see cref="Procedures.ProcedureStatus"/> because a
/// mechanic and a contract are the same kind of thing from the system's point of view: authored
/// content, versioned, never deleted.
/// </summary>
public enum MechanicStatus
{
    Draft,
    Active,
    Deprecated,
    Archived
}

/// <summary>
/// A game rule, written in JavaScript, added while playing.
///
/// This is the object the whole architecture exists to hold. The kernel has no idea what any
/// mechanic does — it stores the source, materialises the data the mechanic declares it needs,
/// runs it in a sandbox, and applies the effects it returns. Everything that makes this a
/// roleplaying game rather than a database lives in <see cref="MechanicVersion.Source"/>, and none
/// of it is C#.
///
/// Identity only, exactly as with procedure contracts: content lives on the version rows, so
/// there is one source of truth for what a mechanic currently does, and revising it is append-only.
/// A rule that silently changed under a campaign would make every past operation unexplainable.
/// </summary>
public sealed class Mechanic
{
    /// <summary>Dotted identifier, e.g. "mechanic.check.ability". Chosen by the author, stable forever.</summary>
    public required string Id { get; set; }

    /// <summary>Coarse grouping for browsing, e.g. "check", "movement", "effect".</summary>
    public required string Category { get; set; }

    public MechanicStatus Status { get; set; } = MechanicStatus.Draft;

    /// <summary>
    /// Which ruleset this belongs to. Empty means shared by everything.
    ///
    /// A scope column rather than an inheritance chain: the open question was whether mechanics
    /// belong to a campaign or to a shared ruleset, and a nullable scope answers both cheaply
    /// while a real chain cannot be removed once anything depends on it. Retrieval prefers an
    /// exact scope match and falls back to shared, which is the whole of the inheritance the MVP
    /// needs. If that stops being enough, the upgrade is a table, not a rewrite.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    public int CurrentVersion { get; set; }

    /// <summary>UTC, plain DateTime — see the note on ProcedureContract.CreatedAt for why.</summary>
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<MechanicVersion> Versions { get; set; } = new List<MechanicVersion>();
}

/// <summary>
/// One revision of a mechanic. Append-only: the source that ran last week is still readable,
/// which is what makes a past operation explainable at all.
/// </summary>
public sealed class MechanicVersion
{
    public int Id { get; set; }

    public required string MechanicId { get; set; }

    public int Version { get; set; }

    public required string Name { get; set; }

    /// <summary>One or two sentences. What appears in listings.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Phrases a player might use for this, one per line. How <c>run_action</c> narrows a free-text
    /// intent down to a candidate mechanic before the LLM picks between them.
    ///
    /// Text matching, not embeddings: at MVP scale there are a few dozen mechanics, and the
    /// retrieval that matters is the LLM reading a short candidate list. ARCHITECTURE.md §8.3 names
    /// the population at which that stops being true.
    /// </summary>
    public string Matches { get; set; } = string.Empty;

    /// <summary>
    /// JSON projection spec: which roles this mechanic needs and which components of each to
    /// materialise. See <see cref="MechanicRequirements"/> — this is the string form of it.
    ///
    /// Declaring the data up front is what lets the mechanic be a pure function (§3.6): everything
    /// it may read is fetched in one query before it starts, so it never reaches back into the
    /// database mid-run and never depends on when it ran.
    /// </summary>
    public string Requirements { get; set; } = "{}";

    /// <summary>The JavaScript. Never executed outside the sandbox, never with CLR access.</summary>
    public required string Source { get; set; }

    /// <summary>Why this revision exists. Expected when revising.</summary>
    public string ChangeNote { get; set; } = string.Empty;

    /// <summary>"llm" or "bootstrap". Who wrote it, for the supervision view.</summary>
    public string CreatedBy { get; set; } = "llm";

    /// <summary>Fingerprint of the bootstrap file this came from; empty when written through MCP.</summary>
    public string SourceHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Mechanic? Mechanic { get; set; }
}
