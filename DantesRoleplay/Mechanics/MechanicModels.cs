using System.Text.Json;
using DantesRoleplay.Effects;

namespace DantesRoleplay.Mechanics;

// ---- what a mechanic declares it needs ------------------------------------------------

/// <summary>
/// The projection spec (ARCHITECTURE.md §3.6a). A mechanic states which participants it operates
/// on and which components of each it wants, and the resolver turns that into one query.
///
/// The role names are the AUTHOR's words, never the kernel's. A mechanic may call them "actor" and
/// "target", or "speaker" and "listener", or anything else; the kernel only ever sees a dictionary
/// of names it does not interpret. That is §3.11 applied to the one place it is most tempting to
/// break — a kernel that knew what an "attacker" was would be a kernel with a combat system in it.
/// </summary>
public sealed record MechanicRequirements
{
    public Dictionary<string, RoleRequirement> Roles { get; init; } = [];

    /// <summary>
    /// Everything the mechanic may read, flattened. Used by the resolver to build one query, and
    /// by the supervision view to answer "what can this rule see?" without reading its source.
    /// </summary>
    public IReadOnlyList<string> AllComponentIds() =>
        Roles.Values.SelectMany(r => r.Components).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// How requirements are read, everywhere. The store validates them at write time and the
    /// resolver reads them at run time, and if those two disagreed about what a spec means, a
    /// mechanic could pass every check and then be handed different data than it declared.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Parse a stored spec. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static MechanicRequirements Parse(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new MechanicRequirements()
            : JsonSerializer.Deserialize<MechanicRequirements>(json, JsonOptions) ?? new MechanicRequirements();
}

/// <param name="Components">
/// Component definition ids to materialise for this role. Anything not listed is not visible to
/// the mechanic — a rule that wants to read something must say so, which makes the declaration
/// the honest answer to "what does this touch?".
/// </param>
/// <param name="Optional">
/// When false, running without this role is an error the caller can act on. Most roles are
/// required; an optional one is how a mechanic handles "with or without a second participant"
/// without needing two mechanics.
/// </param>
/// <param name="Description">What this role means, for whoever calls the mechanic.</param>
public sealed record RoleRequirement(
    IReadOnlyList<string> Components,
    bool Optional = false,
    string Description = "");

// ---- what the mechanic is handed ------------------------------------------------------

/// <summary>
/// The materialised world, fetched in full before the mechanic starts and frozen for its duration.
///
/// This is the object that makes §3.6 true. A mechanic cannot query, so it cannot see the world
/// change under it, so the same inputs give the same outputs — which is what makes a rule written
/// by an LLM reviewable at all. Everything unpredictable is either in here or in the seed.
/// </summary>
public sealed record MechanicProjection
{
    /// <summary>Role name to the entity filling it. A missing optional role is simply absent.</summary>
    public Dictionary<string, EntityProjection> Roles { get; init; } = [];

    /// <summary>Free-form JSON from the caller — the specifics of this particular action.</summary>
    public string Input { get; init; } = "{}";

    /// <summary>
    /// Seed for the sandbox's random source, recorded so a run can be replayed exactly.
    ///
    /// A rule that decides outcomes by chance is unreviewable unless the chance is reproducible.
    /// With the seed in the audit log, "why did that happen?" is answerable months later.
    /// </summary>
    public long Seed { get; init; }
}

/// <param name="Components">Definition id to that component's data as raw JSON.</param>
public sealed record EntityProjection(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Components,
    string? ContainerId = null,
    string ContainerSlot = "",
    IReadOnlyList<ContainedProjection>? Contains = null);

public sealed record ContainedProjection(string Id, string Name, string Slot);

// ---- what comes back ------------------------------------------------------------------

/// <summary>
/// What a mechanic returns. Proposed changes and something to say — never a write (§3.3).
/// </summary>
public sealed record MechanicOutput
{
    /// <summary>Proposed changes, validated and applied by the effect applier, never by the mechanic.</summary>
    public IReadOnlyList<Effect> Effects { get; init; } = [];

    /// <summary>Text describing what happened, for the LLM to relay to the player.</summary>
    public string Narration { get; init; } = string.Empty;

    /// <summary>Anything else the mechanic wants to hand back, as JSON. Not interpreted.</summary>
    public string Data { get; init; } = "{}";
}

/// <summary>
/// The full outcome of one run, including everything needed to supervise it.
///
/// The diagnostics are not debugging garnish. The premise of this system is a human approving code
/// an AI wrote (§3.12), and "it worked" is not reviewable — what it printed, how much it executed
/// and how long it took are the difference between approving a rule and trusting one.
/// </summary>
public sealed record MechanicRunResult
{
    public bool Ok { get; init; }

    public MechanicOutput Output { get; init; } = new();

    /// <summary>Why it failed: a syntax error, a thrown value, or a limit being hit.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Which limit stopped it, when one did. Empty otherwise.</summary>
    public string LimitHit { get; init; } = string.Empty;

    /// <summary>Whatever the mechanic logged, in order.</summary>
    public IReadOnlyList<string> Log { get; init; } = [];

    public long Seed { get; init; }

    public int ElapsedMilliseconds { get; init; }

    public static MechanicRunResult Failed(string error, string limitHit = "", IReadOnlyList<string>? log = null) =>
        new() { Ok = false, Error = error, LimitHit = limitHit, Log = log ?? [] };
}
