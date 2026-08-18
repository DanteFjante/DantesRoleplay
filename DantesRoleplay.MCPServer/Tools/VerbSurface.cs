using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Effects;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The single description of the three-verb surface: which kinds exist, which parameters each
/// query kind reads, and the exact payload each commit kind expects.
///
/// It exists because the surface was briefly described in four places at once — the query
/// dispatcher's switch, the commit dispatcher's switch, orient's capability block, and the
/// capabilities catalog — with nothing tying them together. That is the exact failure that
/// crippled TravelRoleplay (ARCHITECTURE.md §1): a manual advertising an operation the code does
/// not have. Orient and both dispatchers now read their kind lists from here, and a guard test
/// asserts these lists against the dispatch switches in both directions, so an advertised kind
/// nothing handles fails the build rather than a session.
///
/// Public because <c>GuardTests</c> reads it directly rather than by regex over source.
/// </summary>
public static class VerbSurface
{
    /// <summary>
    /// Every kind <c>query</c> accepts, ordered the way a session should meet them. Flat on
    /// purpose: a bounded list costs ~10–20 tokens an entry, whereas a sub-level costs a round
    /// trip and a navigation decision every single time (VERB_MIGRATION.md, the layering rule).
    /// </summary>
    public static IReadOnlyList<QueryKindSpec> QueryKinds { get; } =
    [
        new(
            "capabilities",
            "This catalog: every query kind with the parameters it reads, and every commit kind "
            + "with its exact payload shape.",
            [],
            []),
        new(
            "procedures",
            "Procedure summaries. With id, one contract in full; add version for an older revision.",
            ["id", "version", "query", "category", "includeInactive", "limit"],
            ["procedure.system.inspect"]),
        new(
            "world",
            "Which component definitions exist, with a sample of entities.",
            ["sample"],
            ["procedure.world.model"]),
        new(
            "entities",
            "Entities in full — by id, by ids, by name substring, or by component definition.",
            ["id", "ids", "nameQuery", "withDefinitionId", "limit"],
            ["procedure.system.inspect"]),
        new(
            "mechanics",
            "Mechanic summaries. With id, one game rule in full including its JavaScript source.",
            ["id", "version", "query", "category", "scope", "includeInactive", "limit"],
            ["procedure.mechanic.find"]),
        new(
            "history",
            "Recent operation audit records, newest first, including failures.",
            ["limit", "failuresOnly", "tool", "subject"],
            ["procedure.system.inspect"])
    ];

    /// <summary>
    /// Every kind <c>commit</c> accepts. <see cref="CommitKindSpec.Example"/> is a complete,
    /// copyable payload rather than a description of one: D4 requires the shape to travel inside
    /// the failure itself, so a session that guessed wrong is corrected in the same round trip.
    /// </summary>
    public static IReadOnlyList<CommitKindSpec> CommitKinds { get; } =
    [
        new(
            "procedure",
            "Write or revise a procedure contract. Never overwrites — a change appends a version.",
            "{id, category, name, description, instructions, governs?, constraints?, status?, changeNote?}",
            """
            {"id":"procedure.<category>.<name>","category":"...","name":"...","description":"...","instructions":"1. ...","governs":"...","constraints":"- ...","changeNote":"..."}
            """,
            SupportsDryRun: true,
            ["procedure.contract.create"]),
        new(
            "component",
            "Declare a component definition, so entities may carry it. Committing an existing id "
            + "updates that definition in place — the one write in this system that is not append-only.",
            "{id, name, description, schema?}",
            """
            {"id":"...","name":"...","description":"...","schema":"{}"}
            """,
            SupportsDryRun: false,
            ["procedure.world.model", "procedure.world.change"]),
        new(
            "effects",
            "Change world state directly. The whole list is validated, then applied in one "
            + "transaction — there is no partial write.",
            "{effects: [{type, entityId?, definitionId?, toEntityId?, kind?, slot?, name?, data?}, ...]} "
            + "— see Vocabulary for the nine types and the fields each needs; \"data\" is a JSON "
            + "object encoded as a string, e.g. \"{\\\"strength\\\":12}\". A later effect may "
            + "rely on an earlier one, so create an entity and populate it in a single list.",
            """
            {"effects":[{"type":"entity.create","entityId":"...","name":"..."},{"type":"component.set","entityId":"...","definitionId":"...","data":"{}"}]}
            """,
            SupportsDryRun: true,
            ["procedure.world.change"]),
        new(
            "mechanic",
            "Write or revise a game rule as JavaScript. Never overwrites — a change appends a version.",
            "{id, category, name, description?, matches?, requirements?, source, scope?, status?, changeNote?}",
            """
            {"id":"...","category":"...","name":"...","description":"...","matches":"words a player would say","requirements":"{}","source":"function run(ctx) { return { narration: '...', effects: [] }; }","status":"active","changeNote":"..."}
            """,
            SupportsDryRun: true,
            ["procedure.mechanic.write"]),
        new(
            "action",
            "Resolve what someone is trying to do. The best-ranked active rule matching the "
            + "intent is selected, run in the sandbox, and what it proposes is applied in one "
            + "transaction. You cannot name the rule — read it first with "
            + "query(kind: \"mechanics\") so you know which roles it declares. Input, when supplied, "
            + "must be JSON-object text.",
            "{intent, roleEntityIds?, input?, scope?, seed?}",
            """
            {"intent":"what the player is trying to do","roleEntityIds":{"<role>":"<entity-id>"},"input":"{}"}
            """,
            SupportsDryRun: false,
            ["procedure.action.run"])
    ];

    /// <summary>
    /// What each query parameter means. Named once here rather than per kind, because the same
    /// parameter means the same thing everywhere it is read — which is itself worth stating.
    /// </summary>
    public static IReadOnlyDictionary<string, string> QueryParameters { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "One full record instead of a list.",
            ["ids"] = "Several entities in full, in one call.",
            ["version"] = "An older revision. Only meaningful together with id.",
            ["query"] = "Search words, matched against ids, names, descriptions and match phrases.",
            ["nameQuery"] = "Entity name substring.",
            ["withDefinitionId"] = "Only entities carrying this component definition.",
            ["category"] = "Filter to one category. A filter, never a level — omitting it costs nothing.",
            ["scope"] = "Ruleset to prefer. Shared mechanics stay eligible either way.",
            ["includeInactive"] = "Include deprecated and archived records. Default false.",
            ["limit"] = "Maximum records returned. Defaults: 200 procedures, 50 mechanics and "
                + "entities, 20 history.",
            ["sample"] = "How many example entities the world summary carries. Default 10.",
            ["failuresOnly"] = "Only operations that failed.",
            ["tool"] = "Only operations recorded against this tool name.",
            ["subject"] = "Only operations that touched this subject — usually an id."
        };

    /// <summary>
    /// Every structural change there is, with the fields each one needs.
    ///
    /// This used to reach clients inside the old `apply_effects` tool description. Unregistering
    /// that class took it off the surface entirely, and for a while the only way to learn that
    /// `containment.move` or `relationship.create` existed was to send a wrong type and read the
    /// rejection. A capability nobody can find is not one the system has.
    ///
    /// The keys are asserted against <see cref="EffectType.All"/> by a guard test, so a tenth verb
    /// cannot be added without documenting it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EffectVocabulary { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EffectType.EntityCreate] = "entityId, name — entityId is yours to choose and permanent.",
            [EffectType.EntityDelete] = "entityId — the id stays taken afterwards.",
            [EffectType.ComponentAdd] = "entityId, definitionId, data — fails if already present.",
            [EffectType.ComponentSet] = "entityId, definitionId, data — replaces the data wholesale.",
            [EffectType.ComponentMerge] = "entityId, definitionId, data — patches top-level keys only.",
            [EffectType.ComponentRemove] = "entityId, definitionId.",
            [EffectType.ContainmentMove] =
                "entityId, toEntityId, slot — omit toEntityId to take it out of its container. A "
                + "thing is inside at most one other thing.",
            [EffectType.RelationshipCreate] = "entityId, toEntityId, kind, data.",
            [EffectType.RelationshipRemove] = "entityId, toEntityId, kind."
        };

    public static IReadOnlyList<string> QueryKindNames { get; } =
        [.. QueryKinds.Select(k => k.Name)];

    public static IReadOnlyList<string> CommitKindNames { get; } =
        [.. CommitKinds.Select(k => k.Name)];

    public static bool IsQueryKind(string kind) =>
        QueryKinds.Any(k => string.Equals(k.Name, kind, StringComparison.Ordinal));

    public static CommitKindSpec? Commit(string kind) =>
        CommitKinds.FirstOrDefault(k => string.Equals(k.Name, kind, StringComparison.Ordinal));

    /// <summary>
    /// Relaxed escaping because these strings are read, not rendered. The default encoder turns
    /// every quote inside the payload into <c>\u0022</c>, which is valid JSON and unreadable — and
    /// a shape nobody can read is not the shape travelling with the error that D4 asks for.
    /// Nothing here reaches HTML, so the injection risk the strict encoder guards against does
    /// not apply.
    /// </summary>
    private static readonly JsonSerializerOptions Readable = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// The payload example ready to paste into a literal call — JSON encoded as the string
    /// argument <c>commit</c> actually takes, escaping and all. Every <c>fix</c> that names a
    /// commit uses this, so no error ever suggests a call that would not parse.
    /// </summary>
    public static string PayloadArgument(string kind) =>
        JsonSerializer.Serialize(Commit(kind)?.Example ?? "{}", Readable);

    /// <summary>A complete, valid commit call for this kind, for use as a <c>fix</c>.</summary>
    public static string CommitCall(string kind, bool dryRun = false) =>
        Call(kind, PayloadArgument(kind), dryRun);

    /// <summary>
    /// The same call with a known id already filled in — for the common case where the system
    /// knows exactly which record the next call is about, and the only thing left for the caller
    /// to supply is the content.
    /// </summary>
    public static string CommitCall(string kind, string id, bool dryRun = false)
    {
        var spec = Commit(kind);

        if (spec is null || JsonNode.Parse(spec.Example) is not JsonObject example)
        {
            return CommitCall(kind, dryRun);
        }

        example["id"] = id;

        return Call(kind, JsonSerializer.Serialize(example.ToJsonString(Readable), Readable), dryRun);
    }

    private static string Call(string kind, string payloadArgument, bool dryRun) =>
        dryRun && (Commit(kind)?.SupportsDryRun ?? false)
            ? $"commit(kind: \"{kind}\", payload: {payloadArgument}, dryRun: true)"
            : $"commit(kind: \"{kind}\", payload: {payloadArgument})";

    /// <summary>What orient shows: the two kind lists, one line each.</summary>
    public static object Announcement() =>
        new
        {
            Orient = "orient — you are here. Cheap, read-only, safe to call again whenever you lose track.",
            Query = QueryKinds.ToDictionary(k => $"query(kind: \"{k.Name}\")", k => k.Returns),
            Commit = CommitKinds.ToDictionary(k => $"commit(kind: \"{k.Name}\")", k => k.Summary)
        };

    /// <summary>What <c>query(kind: "capabilities")</c> returns: the same truth, in full.</summary>
    public static object Catalog() =>
        new
        {
            Query = QueryKinds.ToDictionary(
                k => k.Name,
                k => (object)new
                {
                    k.Returns,
                    Reads = k.Reads,
                    Contracts = k.Contracts,
                    Call = k.Reads.Count == 0
                        ? $"query(kind: \"{k.Name}\")"
                        : $"query(kind: \"{k.Name}\", {k.Reads[0]}: ...)"
                }),
            Commit = CommitKinds.ToDictionary(
                k => k.Name,
                k => (object)new
                {
                    k.Summary,
                    k.Payload,
                    k.Example,
                    k.SupportsDryRun,
                    Contracts = k.Contracts,
                    Call = CommitCall(k.Name, k.SupportsDryRun),
                    Vocabulary = k.Name == "effects" ? EffectVocabulary : null
                }),
            QueryParameters,
            HowToRead =
                "Every commit takes payload as a JSON object encoded in a string. Example is that "
                + "object, ready to fill in. Irrelevant query parameters are ignored rather than "
                + "rejected, so exploring costs nothing.",
            NothingElseExists =
                "These are all the kinds there are. If an operation is not here, this system "
                + "cannot do it — say so rather than inventing a kind or a parameter."
        };
}

/// <param name="Name">The literal value of the <c>kind</c> argument.</param>
/// <param name="Returns">What comes back, in one sentence.</param>
/// <param name="Reads">The parameters this kind actually reads. Others are ignored, not rejected.</param>
/// <param name="Contracts">Procedure ids worth reading before relying on this.</param>
public sealed record QueryKindSpec(
    string Name,
    string Returns,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Contracts);

/// <param name="Name">The literal value of the <c>kind</c> argument.</param>
/// <param name="Summary">What this changes, in one sentence.</param>
/// <param name="Payload">The payload shape, written the way the migration table writes it.</param>
/// <param name="Example">A complete payload, ready to fill in and send.</param>
/// <param name="SupportsDryRun">Whether <c>dryRun: true</c> is honoured. Where it is, use it first.</param>
/// <param name="Contracts">Procedure ids that govern this commit. Read them before committing.</param>
public sealed record CommitKindSpec(
    string Name,
    string Summary,
    string Payload,
    string Example,
    bool SupportsDryRun,
    IReadOnlyList<string> Contracts);
