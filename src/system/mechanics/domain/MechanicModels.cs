using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;

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
    /// Optional authored JSON Schema for the mechanic's input object. Discovery surfaces expose
    /// this contract before execution; JavaScript still performs semantic checks that depend on
    /// projected state.
    /// </summary>
    public JsonElement? InputSchema { get; init; }

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
    /// Component identities that effects from this mechanic may write even when the affected
    /// entity is created by the same effect bundle and therefore cannot be a projected role.
    /// Existing entities still require a required or optional role snapshot for stale-write safety.
    /// </summary>
    public IReadOnlyList<string> EffectComponentIds { get; init; } = [];

    /// <summary>
    /// Declares whether this mechanic advances authoritative time. The declaration describes
    /// where the catalog obtains its duration; the generic kernel never calculates game time.
    /// </summary>
    public ElapsedTimeRequirement? ElapsedTime { get; init; }

    /// <summary>
    /// Everything the mechanic may read, flattened. Used by the resolver to build one query, and
    /// by the supervision view to answer "what can this rule see?" without reading its source.
    /// </summary>
    public IReadOnlyList<string> AllComponentIds() =>
        Roles.Values
            .SelectMany(r => r.Components.Concat(r.OptionalComponents ?? [])
                .Concat(r.ContentComponentIds ?? [])
                .Concat((r.ComponentReferences ?? []).SelectMany(reference =>
                    new[] { reference.SourceComponentId }.Concat(reference.TargetComponentIds)
                        .Concat(reference.OptionalTargetComponentIds ?? [])))
                .Concat((r.RelationshipComponents ?? []).SelectMany(reference =>
                    reference.TargetComponentIds.Concat(reference.OptionalTargetComponentIds ?? []))))
            .Distinct(StringComparer.Ordinal)
            .ToList();

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

        if (ElapsedTime is not null)
        {
            var mode = ElapsedTime.Mode?.Trim() ?? string.Empty;
            if (mode is not ("zero" or "fixed" or "derived" or "supplied"))
                problems.Add("Elapsed time mode must be zero, fixed, derived, or supplied.");
            if (mode == "fixed" && ElapsedTime.Minutes is not > 0)
                problems.Add("Fixed elapsed time requires positive minutes.");
            if (mode != "fixed" && ElapsedTime.Minutes is not null)
                problems.Add("Only fixed elapsed time may declare minutes.");
            if (mode == "supplied" && string.IsNullOrWhiteSpace(ElapsedTime.InputProperty))
                problems.Add("Supplied elapsed time requires an inputProperty.");
            if (mode != "supplied" && !string.IsNullOrWhiteSpace(ElapsedTime.InputProperty))
                problems.Add("Only supplied elapsed time may declare an inputProperty.");
            if (mode == "derived" && string.IsNullOrWhiteSpace(ElapsedTime.Source))
                problems.Add("Derived elapsed time requires a bounded source description.");
            if (mode != "derived" && !string.IsNullOrWhiteSpace(ElapsedTime.Source))
                problems.Add("Only derived elapsed time may declare a source description.");
            if ((ElapsedTime.InputProperty?.Length ?? 0) > 100 || (ElapsedTime.Source?.Length ?? 0) > 400)
                problems.Add("Elapsed time declaration values exceed their bounds.");
        }

        foreach (var (key, child) in Children)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace))
                problems.Add("A child result key must be non-empty and contain no whitespace.");

            if (child is null || string.IsNullOrWhiteSpace(child.MechanicId))
            {
                problems.Add($"Child '{key}' must name a mechanicId.");
                continue;
            }

            var hasVersion = child.MechanicVersion != 0;
            var hasFingerprint = !string.IsNullOrWhiteSpace(child.ContentFingerprint);
            if (hasVersion != hasFingerprint || child.MechanicVersion < 0
                || (hasFingerprint && !IsUpperSha256(child.ContentFingerprint)))
                problems.Add($"Child '{key}' must supply both a positive mechanicVersion and an uppercase SHA-256 contentFingerprint, or neither.");

            if (!string.IsNullOrWhiteSpace(child.ForEachContentsOf) &&
                !Roles.ContainsKey(child.ForEachContentsOf))
            {
                problems.Add(
                    $"Child '{key}' iterates contents of undeclared parent role '{child.ForEachContentsOf}'.");
            }

            if (!string.IsNullOrWhiteSpace(child.ForEachInputProperty))
            {
                if (child.ForEachInputProperty.Trim() != child.ForEachInputProperty)
                    problems.Add($"Child '{key}' has an untrimmed forEachInputProperty.");
                if (!string.IsNullOrWhiteSpace(child.ForEachContentsOf) || child.InheritInput
                    || !string.Equals(child.Input, "{}", StringComparison.Ordinal)
                    || !string.IsNullOrWhiteSpace(child.InputFromParentProperty)
                    || child.InputForEachItem || child.InputFromChildData is not null)
                    problems.Add($"Child '{key}' cannot combine forEachInputProperty with another input or foreach source.");
                if (!string.IsNullOrWhiteSpace(child.InputFromEachItemProperty)
                    && child.InputFromEachItemProperty.Trim() != child.InputFromEachItemProperty)
                    problems.Add($"Child '{key}' has an untrimmed inputFromEachItemProperty.");
            }
            else if (!string.IsNullOrWhiteSpace(child.InputFromEachItemProperty))
                problems.Add($"Child '{key}' uses inputFromEachItemProperty without forEachInputProperty.");

            if (child.After.Count > 64 || child.After.Any(string.IsNullOrWhiteSpace)
                || child.After.Distinct(StringComparer.Ordinal).Count() != child.After.Count)
                problems.Add($"Child '{key}' has invalid after dependencies.");

            foreach (var (childRole, source) in child.RoleBindings)
            {
                if (string.IsNullOrWhiteSpace(childRole) || string.IsNullOrWhiteSpace(source))
                {
                    problems.Add($"Child '{key}' has an empty role binding.");
                    continue;
                }

                if (source != "$item" && !source.StartsWith("$input.", StringComparison.Ordinal)
                    && !Roles.ContainsKey(source))
                    problems.Add($"Child '{key}' binds '{childRole}' from undeclared parent role '{source}'.");

                if (source == "$item" && string.IsNullOrWhiteSpace(child.ForEachContentsOf))
                    problems.Add($"Child '{key}' uses '$item' but does not declare forEachContentsOf.");

                if (source.StartsWith("$input.", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(child.ForEachInputProperty))
                    problems.Add($"Child '{key}' uses an input role binding without forEachInputProperty.");
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

            if (child.InputFromChildData is not null)
            {
                var sourceKey = child.InputFromChildData.ResultKey;
                if (string.IsNullOrWhiteSpace(sourceKey) || sourceKey.Trim() != sourceKey || sourceKey.Any(char.IsWhiteSpace))
                    problems.Add($"Child '{key}' has an invalid inputFromChildData resultKey.");

                if (child.InheritInput || !string.Equals(child.Input, "{}", StringComparison.Ordinal) ||
                    !string.IsNullOrWhiteSpace(child.InputFromParentProperty) || child.InputForEachItem ||
                    !string.IsNullOrWhiteSpace(child.ForEachContentsOf))
                {
                    problems.Add($"Child '{key}' cannot combine inputFromChildData with another input source or foreach declaration.");
                }
            }
        }

        foreach (var (key, child) in Children)
        {
            var sourceKey = child?.InputFromChildData?.ResultKey;
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                foreach (var dependency in child?.After ?? [])
                {
                    if (string.Equals(key, dependency, StringComparison.Ordinal))
                        problems.Add($"Child '{key}' cannot run after itself.");
                    else if (!Children.ContainsKey(dependency))
                        problems.Add($"Child '{key}' runs after unknown child '{dependency}'.");
                }
                continue;
            }

            if (string.Equals(key, sourceKey, StringComparison.Ordinal))
            {
                problems.Add($"Child '{key}' cannot read its own data result.");
                continue;
            }

            if (!Children.TryGetValue(sourceKey, out var producer) || producer is null)
            {
                problems.Add($"Child '{key}' reads data from unknown child '{sourceKey}'.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(producer.ForEachContentsOf))
                problems.Add($"Child '{key}' cannot read data from foreach child '{sourceKey}'.");

            foreach (var dependency in child?.After ?? [])
            {
                if (string.Equals(key, dependency, StringComparison.Ordinal))
                    problems.Add($"Child '{key}' cannot run after itself.");
                else if (!Children.ContainsKey(dependency))
                    problems.Add($"Child '{key}' runs after unknown child '{dependency}'.");
            }
        }

        if (HasChildCycle())
            problems.Add("Child input and after dependencies must form an acyclic sibling graph.");

        return problems;
    }

    private bool HasChildCycle()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string key)
        {
            if (!visited.Add(key))
                return active.Contains(key);

            active.Add(key);
            var child = Children[key];
            var dependencies = (child?.After ?? []).Concat(
                string.IsNullOrWhiteSpace(child?.InputFromChildData?.ResultKey)
                    ? [] : [child!.InputFromChildData!.ResultKey]);
            var hasCycle = dependencies.Any(dependency => Children.ContainsKey(dependency) && Visit(dependency));
            active.Remove(key);
            return hasCycle;
        }

        return Children.Keys.Any(Visit);
    }

    /// <summary>Checks the declared, generic containment projection boundary.</summary>
    public IReadOnlyList<string> ProjectionProblems()
    {
        var problems = new List<string>();
        if (InputSchema is JsonElement inputSchema
            && (inputSchema.ValueKind != JsonValueKind.Object
                || inputSchema.GetRawText().Length > 64 * 1024))
            problems.Add("inputSchema must be one bounded JSON Schema object.");
        if (EffectComponentIds.Count > ProjectionLimits.MaxContentComponentIds ||
            EffectComponentIds.Any(string.IsNullOrWhiteSpace) ||
            EffectComponentIds.Distinct(StringComparer.Ordinal).Count() != EffectComponentIds.Count)
            problems.Add("effectComponentIds must be a bounded distinct list of component identities.");
        foreach (var (role, requirement) in Roles)
        {
            var optionalComponents = requirement.OptionalComponents ?? [];
            if (requirement.Components.Concat(optionalComponents).Distinct(StringComparer.Ordinal).Count() !=
                requirement.Components.Count + optionalComponents.Count)
                problems.Add($"Role '{role}' required and optional components must be distinct.");
            var descendantComponents = requirement.ContentComponentIds ?? [];
            var references = requirement.ComponentReferences ?? [];
            if (references.Count > ProjectionLimits.MaxContentComponentIds)
                problems.Add($"Role '{role}' may declare at most {ProjectionLimits.MaxContentComponentIds} componentReferences.");
            foreach (var reference in references)
            {
                var optionalTargetComponentIds = reference?.OptionalTargetComponentIds ?? [];
                if (reference is null || string.IsNullOrWhiteSpace(reference.SourceComponentId) ||
                    !ComponentReferencePath.IsValid(reference.Field) ||
                    reference.TargetComponentIds.Count + optionalTargetComponentIds.Count >
                        ProjectionLimits.MaxContentComponentIds ||
                    reference.TargetComponentIds.Any(string.IsNullOrWhiteSpace) ||
                    optionalTargetComponentIds.Any(string.IsNullOrWhiteSpace) ||
                    reference.TargetComponentIds.Concat(optionalTargetComponentIds)
                        .Distinct(StringComparer.Ordinal).Count() !=
                    reference.TargetComponentIds.Count + optionalTargetComponentIds.Count)
                {
                    problems.Add($"Role '{role}' has an invalid component reference declaration.");
                    continue;
                }

                if (!requirement.Components.Contains(reference.SourceComponentId, StringComparer.Ordinal) &&
                    !optionalComponents.Contains(reference.SourceComponentId, StringComparer.Ordinal) &&
                    !descendantComponents.Contains(reference.SourceComponentId, StringComparer.Ordinal))
                    problems.Add($"Role '{role}' component reference source '{reference.SourceComponentId}' is not declared on the role or its contents.");
            }
            var relevantRoles = requirement.ContentsRelevantToRoles ?? [];
            if (relevantRoles.Count > 0)
            {
                if (!requirement.IncludeContents)
                    problems.Add($"Role '{role}' sets contentsRelevantToRoles without includeContents.");
                if (relevantRoles.Count > ProjectionLimits.MaxContentsRelevantToRoles)
                    problems.Add($"Role '{role}' may declare at most {ProjectionLimits.MaxContentsRelevantToRoles} contentsRelevantToRoles.");
                if (relevantRoles.Distinct(StringComparer.Ordinal).Count() != relevantRoles.Count)
                    problems.Add($"Role '{role}' contentsRelevantToRoles must be distinct.");
                foreach (var relevant in relevantRoles)
                {
                    if (string.IsNullOrWhiteSpace(relevant) || !Roles.ContainsKey(relevant))
                        problems.Add($"Role '{role}' contentsRelevantToRoles names undeclared role '{relevant}'.");
                    else if (StringComparer.Ordinal.Equals(relevant, role))
                        problems.Add($"Role '{role}' cannot declare itself in contentsRelevantToRoles.");
                }
            }
            var relationshipComponents = requirement.RelationshipComponents ?? [];
            if (relationshipComponents.Count > ProjectionLimits.MaxRelationshipComponentDeclarations)
                problems.Add($"Role '{role}' may declare at most {ProjectionLimits.MaxRelationshipComponentDeclarations} relationshipComponents.");
            if (relationshipComponents.Count > 0 && !requirement.IncludeRelationships)
                problems.Add($"Role '{role}' sets relationshipComponents without includeRelationships.");
            var relationshipKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var related in relationshipComponents)
            {
                var optionalTargetComponentIds = related?.OptionalTargetComponentIds ?? [];
                if (related is null || string.IsNullOrWhiteSpace(related.Kind) || related.Kind.Trim() != related.Kind ||
                    related.Direction is not ("outgoing" or "incoming" or "either") ||
                    related.TargetComponentIds.Count == 0 ||
                    related.TargetComponentIds.Count + optionalTargetComponentIds.Count > ProjectionLimits.MaxContentComponentIds ||
                    related.TargetComponentIds.Any(string.IsNullOrWhiteSpace) ||
                    optionalTargetComponentIds.Any(string.IsNullOrWhiteSpace) ||
                    related.TargetComponentIds.Concat(optionalTargetComponentIds)
                        .Distinct(StringComparer.Ordinal).Count() !=
                        related.TargetComponentIds.Count + optionalTargetComponentIds.Count)
                {
                    problems.Add($"Role '{role}' has an invalid relationship component declaration.");
                    continue;
                }
                if (!relationshipKeys.Add(related.Kind + "\n" + related.Direction))
                    problems.Add($"Role '{role}' relationship component declarations must have unique kind/direction pairs.");
            }
            if (!requirement.IncludeContents)
            {
                if (requirement.ContentsDepth is not null)
                    problems.Add($"Role '{role}' sets contentsDepth without includeContents.");
                if (requirement.ContentComponentIds is not null)
                    problems.Add($"Role '{role}' sets contentComponentIds without includeContents.");
                continue;
            }

            var depth = requirement.ContentsDepth ?? 1;
            if (depth is < 1 or > ProjectionLimits.MaxContentsDepth)
                problems.Add($"Role '{role}' contentsDepth must be between 1 and {ProjectionLimits.MaxContentsDepth}.");
            if (descendantComponents.Count > ProjectionLimits.MaxContentComponentIds)
                problems.Add($"Role '{role}' may declare at most {ProjectionLimits.MaxContentComponentIds} contentComponentIds.");
            if (descendantComponents.Any(string.IsNullOrWhiteSpace) || descendantComponents.Distinct(StringComparer.Ordinal).Count() != descendantComponents.Count)
                problems.Add($"Role '{role}' contentComponentIds must be distinct and non-empty.");
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

    private static bool IsUpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
}

public sealed record ElapsedTimeRequirement
{
    public string Mode { get; init; } = string.Empty;
    public long? Minutes { get; init; }
    public string InputProperty { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
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
/// <param name="IncludeRelationships">
/// Materialise the stable relationship records touching this entity. As with contents, this is
/// opt-in so a mechanic declaration remains an honest account of the world data it can inspect.
/// The resolver supplies relationship records only, never the other endpoint's projection.
/// </param>
/// <param name="ContentsDepth">
/// When contents are requested, the number of containment levels to materialise. Omitted is the
/// compatible direct-child view; the generic projection boundary permits one through four.
/// </param>
/// <param name="ContentComponentIds">
/// The separately declared component allow-list for contained nodes. It never changes what the
/// root role can see, and an omitted list leaves contained nodes as identity/name/slot only.
/// </param>
public sealed record RoleRequirement(
    IReadOnlyList<string> Components,
    bool Optional = false,
    string Description = "",
    bool IncludeContents = false,
    bool IncludeRelationships = false,
    int? ContentsDepth = null,
    IReadOnlyList<string>? ContentComponentIds = null,
    IReadOnlyList<ComponentReferenceRequirement>? ComponentReferences = null,
    IReadOnlyList<RelationshipComponentRequirement>? RelationshipComponents = null,
    IReadOnlyList<string>? OptionalComponents = null,
    IReadOnlyList<string>? ContentsRelevantToRoles = null);

/// <summary>A declared entity-id field inside a declared component, and the target components it may reveal.</summary>
public sealed record ComponentReferenceRequirement(
    string SourceComponentId,
    string Field,
    IReadOnlyList<string> TargetComponentIds,
    IReadOnlyList<string>? OptionalTargetComponentIds = null);

/// <summary>A bounded declaration of components visible on matching relationship endpoints.</summary>
public sealed record RelationshipComponentRequirement(
    string Kind,
    string Direction,
    IReadOnlyList<string> TargetComponentIds,
    IReadOnlyList<string>? OptionalTargetComponentIds = null);

/// <summary>Generic containment-projection limits; they carry no game meaning.</summary>
public static class ProjectionLimits
{
    public const int MaxContentsDepth = 4;
    public const int MaxContainedNodes = 100;
    public const int MaxContentsRelevantToRoles = 12;
    public const int MaxContentComponentIds = 12;
    public const int MaxRelationshipComponentDeclarations = 12;
    public const int MaxRelatedNodes = 100;
    public const int MaxReferencedEntities = 512;
}

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

    /// <summary>
    /// Optional exact catalog pin. Application composition rejects a child whose active record no
    /// longer has this version and fingerprint. The pair is optional only for compatibility with
    /// legacy database-authored compositions; new catalog composites should always supply both.
    /// </summary>
    public int MechanicVersion { get; init; }

    public string ContentFingerprint { get; init; } = string.Empty;

    public Dictionary<string, string> RoleBindings { get; init; } = [];

    public string ForEachContentsOf { get; init; } = string.Empty;

    /// <summary>
    /// Invoke the child once for every object in this bounded parent-input array. Each array item
    /// becomes the child's complete input object. Caller data never becomes an entity role.
    /// </summary>
    public string ForEachInputProperty { get; init; } = string.Empty;

    /// <summary>Optional object property selected from each input-array item as the child input.</summary>
    public string InputFromEachItemProperty { get; init; } = string.Empty;

    /// <summary>Sibling result keys that must finish before this declaration begins.</summary>
    public IReadOnlyList<string> After { get; init; } = [];

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

    /// <summary>
    /// The one closed dependent-input form. The consumer receives a deep JSON copy of the named
    /// sibling child's single object-valued output data after that sibling has completed.
    /// </summary>
    public ChildDataInputRequirement? InputFromChildData { get; init; }
}

/// <summary>Names the sole sibling result whose complete object data becomes a child input.</summary>
public sealed record ChildDataInputRequirement
{
    public string ResultKey { get; init; } = string.Empty;
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
    /// <summary>
    /// The immutable state-space identity for application-backed execution. Catalog mechanics use
    /// it only when a component must carry a complete reference to an already-projected edge or
    /// entity. Legacy world mechanics leave it empty; it is context, never caller input.
    /// </summary>
    public string StateSpaceId { get; init; } = string.Empty;

    /// <summary>
    /// Host-owned identity of this exact mechanic invocation. It is absent for read-only
    /// evaluation that has no authorized execution operation and is never sourced from input.
    /// </summary>
    public MechanicExecutionContext? Execution { get; init; }

    /// <summary>Role name to the entity filling it. A missing optional role is simply absent.</summary>
    public Dictionary<string, EntityProjection> Roles { get; init; } = [];

    /// <summary>Host-only component revisions observed while materialising the immutable projection.</summary>
    [JsonIgnore]
    public Dictionary<string, Dictionary<string, int?>> ComponentRevisions { get; init; } = [];

    /// <summary>Host-only snapshots of each direct containment list observed across the projected graph.</summary>
    [JsonIgnore]
    public Dictionary<string, IReadOnlyList<ContainmentRevision>> ContainmentRevisions { get; init; } = [];

    /// <summary>Declared component-reference targets, keyed by their stable entity ids.</summary>
    public Dictionary<string, ReferencedEntityProjection> References { get; init; } = [];

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
    IReadOnlyList<ContainedProjection>? Contains = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RelationshipProjection>? Relationships = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RelatedEntityProjection>? Related = null);

public sealed record ContainedProjection(
    string Id,
    string Name,
    string Slot,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? Components = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ContainedProjection>? Contains = null);

public sealed record ReferencedEntityProjection(
    string Id,
    IReadOnlyDictionary<string, string> Components,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);

/// <summary>
/// One relationship touching an explicitly opted-in role. The raw object JSON is preserved so the
/// mechanic sees the same authored data the relationship store holds, without gaining access to
/// either endpoint's components or other world state.
/// </summary>
public sealed record RelationshipProjection(
    string FromEntityId,
    string ToEntityId,
    string Kind,
    string Data);

/// <summary>A declared opposite-endpoint projection for one relationship touching a role.</summary>
public sealed record RelatedEntityProjection(
    string Id,
    string Name,
    string FromEntityId,
    string ToEntityId,
    string Kind,
    string Data,
    IReadOnlyDictionary<string, string> Components);

public sealed record ContainmentRevision(string EntityId, string Slot, int Revision);

/// <summary>Immutable host-owned identity projected into one mechanic invocation.</summary>
public sealed record MechanicExecutionContext(
    string RootOperationId,
    string OperationId,
    string? ParentOperationId,
    int InvocationOrdinal);

/// <summary>Replayable child output supplied to a parent as frozen JSON data.</summary>
public sealed record ChildMechanicResult(
    string MechanicId,
    int Version,
    long Seed,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    MechanicOutput Output,
    IReadOnlyList<string> Log,
    int ElapsedMilliseconds,
    MechanicExecutionContext? Execution = null);

// ---- what comes back ------------------------------------------------------------------

/// <summary>
/// What a mechanic returns. Proposed changes and something to say — never a write (§3.3).
/// </summary>
public sealed record MechanicOutput
{
    /// <summary>Proposed changes, validated and applied by the effect applier, never by the mechanic.</summary>
    public IReadOnlyList<Effect> Effects { get; init; } = [];

    /// <summary>
    /// Events the rule declares happened, beyond whatever its effects imply. Validated against
    /// their registered types at emission and, once accepted, visible to the rest of the chain.
    /// </summary>
    public IReadOnlyList<DeclaredEvent> Events { get; init; } = [];

    /// <summary>
    /// Notices a rule wants a person told about. Created only if the whole chain commits, and read
    /// only when somebody asks — nothing here delivers anything.
    /// </summary>
    public IReadOnlyList<DeclaredNotification> Notifications { get; init; } = [];

    /// <summary>Text describing what happened, for the LLM to relay to the player.</summary>
    public string Narration { get; init; } = string.Empty;

    /// <summary>Anything else the mechanic wants to hand back, as JSON. Not interpreted.</summary>
    public string Data { get; init; } = "{}";

    /// <summary>
    /// Whether the mechanic explicitly returned <see cref="Data"/>. The text defaults to an
    /// object for compatibility, but dependent composition must distinguish an omitted result from
    /// an explicitly produced object.
    /// </summary>
    public bool HasData { get; init; }

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
