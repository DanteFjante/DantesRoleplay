using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// Present only when this mechanic is an event middleware target. The explicit mode prevents a
    /// guard (which may only decide allow/deny) from being registered as a reaction by accident.
    /// </summary>
    public EventMechanicRequirement? Event { get; init; }

    /// <summary>
    /// Child mechanics this mechanic may invoke. These declarations are interpreted by the host
    /// before the parent source runs; JavaScript receives only their serialised results. That
    /// keeps composition inside the same strict, string-only sandbox boundary as every other
    /// mechanic input.
    /// </summary>
    public Dictionary<string, ChildMechanicRequirement> Children { get; init; } = [];

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
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Parse a stored spec. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static MechanicRequirements Parse(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new MechanicRequirements()
            : JsonSerializer.Deserialize<MechanicRequirements>(json, JsonOptions) ?? new MechanicRequirements();

    /// <summary>Checks declarations that cannot be validated by deserialisation alone.</summary>
    public IReadOnlyList<string> CompositionProblems()
    {
        var problems = new List<string>();

        foreach (var (key, child) in Children)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace))
                problems.Add("A child result key must be non-empty and contain no whitespace.");

            if (child is null || string.IsNullOrWhiteSpace(child.MechanicId))
            {
                problems.Add($"Child '{key}' must name a mechanicId.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(child.ForEachContentsOf) &&
                !Roles.ContainsKey(child.ForEachContentsOf))
            {
                problems.Add(
                    $"Child '{key}' iterates contents of undeclared parent role '{child.ForEachContentsOf}'.");
            }

            foreach (var (childRole, source) in child.RoleBindings)
            {
                if (string.IsNullOrWhiteSpace(childRole) || string.IsNullOrWhiteSpace(source))
                {
                    problems.Add($"Child '{key}' has an empty role binding.");
                    continue;
                }

                if (source != "$item" && !Roles.ContainsKey(source))
                    problems.Add($"Child '{key}' binds '{childRole}' from undeclared parent role '{source}'.");

                if (source == "$item" && string.IsNullOrWhiteSpace(child.ForEachContentsOf))
                    problems.Add($"Child '{key}' uses '$item' but does not declare forEachContentsOf.");
            }

            if (!child.InheritInput && !IsJsonObject(child.Input))
                problems.Add($"Child '{key}' has input that is not a JSON object.");

            if (!string.IsNullOrWhiteSpace(child.InputFromParentProperty) && child.InheritInput)
                problems.Add($"Child '{key}' cannot inherit the full parent input and select an input property at once.");

            if (!string.IsNullOrWhiteSpace(child.InputFromParentProperty) &&
                child.InputFromParentProperty.Trim() != child.InputFromParentProperty)
            {
                problems.Add($"Child '{key}' has an untrimmed inputFromParentProperty.");
            }

            if (child.InputForEachItem && string.IsNullOrWhiteSpace(child.ForEachContentsOf))
                problems.Add($"Child '{key}' uses inputForEachItem but does not declare forEachContentsOf.");
        }

        return problems;
    }

    /// <summary>Closed event-target declaration checks shared by authoring and subscription validation.</summary>
    public IReadOnlyList<string> EventProblems()
    {
        if (Event is null) return [];
        var problems = new List<string>();
        if (Event.Types.Count == 0 || Event.Types.Any(string.IsNullOrWhiteSpace) || Event.Types.Distinct(StringComparer.Ordinal).Count() != Event.Types.Count)
            problems.Add("An event requirement needs distinct non-empty types.");
        if (Event.Components.Any(string.IsNullOrWhiteSpace) || Event.Components.Distinct(StringComparer.Ordinal).Count() != Event.Components.Count)
            problems.Add("Event component ids must be distinct and non-empty.");
        if (Children.Count > 0) problems.Add("An event mechanic cannot declare child mechanics.");
        return problems;
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The one event mode a mechanic declares. It must match its subscription.</summary>
public enum EventMechanicMode { Guard, Reaction }

public sealed record EventMechanicRequirement
{
    public required EventMechanicMode Mode { get; init; }
    public IReadOnlyList<string> Types { get; init; } = [];
    public IReadOnlyList<string> Components { get; init; } = [];
    public bool IncludeContents { get; init; }
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
/// <param name="IncludeContents">
/// Materialise what this entity contains. Declared rather than always-on for the same reason the
/// components are: a rule that looks in someone's possession should have to say so, and a
/// container holding forty things should not be fetched for a rule that never looks.
/// </param>
public sealed record RoleRequirement(
    IReadOnlyList<string> Components,
    bool Optional = false,
    string Description = "",
    bool IncludeContents = false);

/// <summary>
/// A host-side child invocation declaration. The dictionary key in
/// <see cref="MechanicRequirements.Children"/> is the result key exposed at <c>ctx.children</c>.
/// Role binding values refer to a parent role; <c>$item</c> means the current member of the
/// declared container role. With <see cref="ForEachContentsOf"/> present one result is produced
/// for every contained entity, in the stable projected order.
/// </summary>
public sealed record ChildMechanicRequirement
{
    public string MechanicId { get; init; } = string.Empty;

    public Dictionary<string, string> RoleBindings { get; init; } = [];

    public string ForEachContentsOf { get; init; } = string.Empty;

    /// <summary>
    /// Child input defaults to the parent's already-validated JSON object. A static object may
    /// be supplied when inheritance is disabled.
    /// </summary>
    public bool InheritInput { get; init; } = true;

    public string Input { get; init; } = "{}";

    /// <summary>
    /// Optional top-level key whose object value becomes the child input. This keeps child input
    /// closed even when the parent also needs sibling metadata such as a tie decision.
    /// </summary>
    public string InputFromParentProperty { get; init; } = string.Empty;

    /// <summary>
    /// Select an object from <see cref="InputFromParentProperty"/> by the current `$item` id.
    /// This is valid only with <see cref="ForEachContentsOf"/>.
    /// </summary>
    public bool InputForEachItem { get; init; }
}

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

    /// <summary>JSON-object text from the caller — the specifics of this particular action.</summary>
    public string Input { get; init; } = "{}";

    /// <summary>
    /// Seed for the sandbox's random source, recorded so a run can be replayed exactly.
    ///
    /// A rule that decides outcomes by chance is unreviewable unless the chance is reproducible.
    /// With the seed in the audit log, "why did that happen?" is answerable months later.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Results of host-orchestrated child mechanics. These are serialised into the sandbox and
    /// never carry a live CLR callback or database capability.
    /// </summary>
    public Dictionary<string, IReadOnlyList<ChildMechanicResult>> Children { get; init; } = [];

    /// <summary>Immutable event proposal supplied only when the mechanic runs as middleware.</summary>
    public string Event { get; init; } = "{}";

    /// <summary>
    /// The entities that proposal affects, keyed by id, carrying only the components the mechanic
    /// declared it needs.
    ///
    /// Shaped exactly like <see cref="Roles"/> on purpose: an author who has read one role has
    /// already learned this. A bare list of ids was the earlier form, and it made every reaction
    /// that wanted to know anything about the thing that changed unable to ask.
    ///
    /// A deleted entity is absent — there is nothing left to project. What it was is in the event
    /// payload, which was captured before the deletion.
    /// </summary>
    public Dictionary<string, EntityProjection> EventEntities { get; init; } = [];
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

/// <summary>Replayable child output supplied to a parent as frozen JSON data.</summary>
public sealed record ChildMechanicResult(
    string MechanicId,
    int Version,
    long Seed,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    MechanicOutput Output,
    IReadOnlyList<string> Log,
    int ElapsedMilliseconds);

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

    /// <summary>Guard-only decision: exactly <c>allow</c> or <c>deny</c>.</summary>
    public string Decision { get; init; } = string.Empty;

    /// <summary>Required stable refusal code when a guard denies.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Required human-readable refusal reason when a guard denies.</summary>
    public string Reason { get; init; } = string.Empty;
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
