using System.Text.Json;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>W17's effect-free, fixed-graph World child for the later C10 coordinator.</summary>
public sealed class SmallWorldCompositionPlanner(IStagedWorldComposer staged) : ISmallWorldCompositionPlanner
{
    private static readonly string[] Keys = ["world", "region", "location.gate", "location.market", "location.observatory", "faction", "actor.one", "actor.two", "knowledge.fact", "knowledge.rumour", "knowledge.secret", "knowledge.clue.one", "knowledge.clue.two", "knowledge.clue.three"];
    private static readonly HashSet<string> SubjectKinds = new(["state", "event", "identity", "relationship", "location", "capability", "rule", "quantity", "intention", "negative"], StringComparer.Ordinal);
    private static readonly HashSet<string> Sensitivities = new(["open", "discreet", "confidential", "secret"], StringComparer.Ordinal);
    private readonly IStagedWorldComposer _staged = staged;

    public async Task<SmallWorldCompositionResult> ComposeAsync(SmallWorldBlueprint blueprint, string worldNamespace, CancellationToken cancellationToken = default)
    {
        var problems = Validate(blueprint, worldNamespace);
        if (problems.Count > 0) return Invalid(problems);

        var ids = Keys.ToDictionary(key => key, key => $"{worldNamespace}.{key}", StringComparer.Ordinal);
        if (ids.Values.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            return Invalid([Problem("WORLD_ID_CONFLICT", "worldNamespace", "The derived local-key IDs must be unique.")]);

        var names = Names(blueprint);
        var boundary = new StagedWorldBoundary(new(ids["world"], names["world"]), ids.Values.ToHashSet(StringComparer.Ordinal));
        var start = await _staged.StartAsync(boundary, cancellationToken);
        if (!start.Valid) return Invalid(StagedProblems(start.Problems));

        var effects = Effects(blueprint, ids, names);
        var staged = await _staged.AppendAsync(start, effects.Skip(1).ToArray(), cancellationToken);
        if (!staged.Valid) return Invalid(StagedProblems(staged.Problems));

        var map = Keys.Select(key => new SmallWorldIdentity(key, ids[key], names[key])).ToArray();
        var visibility = new[]
        {
            Review("world", "public"), Review("region", "public"), Review("location.gate", "public"), Review("location.market", "public"), Review("location.observatory", "party"),
            Review("faction", "party"), Review("actor.one", "party"), Review("actor.two", "party"), Review("knowledge.fact", "public"), Review("knowledge.rumour", "party"),
            Review("knowledge.secret", "gm"), Review("knowledge.clue.one", "gm"), Review("knowledge.clue.two", "gm"), Review("knowledge.clue.three", "gm")
        };
        return new("valid", ids["world"], map, new(14, 20, 4, 20), visibility, staged.Effects, staged.World, []);
    }

    private static IReadOnlyList<SmallWorldCompositionProblem> Validate(SmallWorldBlueprint? blueprint, string? worldNamespace)
    {
        var problems = new List<SmallWorldCompositionProblem>();
        if (blueprint is null) return [Problem("WORLD_BLUEPRINT_REQUIRED", "blueprint", "A closed World blueprint is required.")];
        if (!Namespace(worldNamespace)) problems.Add(Problem("WORLD_BLUEPRINT_INVALID", "worldNamespace", "worldNamespace must be a trimmed lowercase world.c10.* identifier."));
        Root(blueprint.World, "world", problems);
        Location(blueprint.Region, "region", problems); Location(blueprint.Gate, "gate", problems); Location(blueprint.Market, "market", problems); Location(blueprint.Observatory, "observatory", problems);
        Faction(blueprint.Faction, problems); Motive(blueprint.ActorOne, "actorOne", problems); Motive(blueprint.ActorTwo, "actorTwo", problems);
        Knowledge(blueprint.Fact, "fact", problems); Knowledge(blueprint.Rumour, "rumour", problems); Knowledge(blueprint.Secret, "secret", problems);
        Knowledge(blueprint.ClueOne, "clueOne", problems); Knowledge(blueprint.ClueTwo, "clueTwo", problems); Knowledge(blueprint.ClueThree, "clueThree", problems);
        return problems;
    }

    private static void Root(SmallWorldRoot? value, string path, List<SmallWorldCompositionProblem> problems)
    {
        if (value is null) { problems.Add(Problem("WORLD_BLUEPRINT_REQUIRED", path, "The required authored record is missing.")); return; }
        Text(value.Name, 160, $"{path}.name", problems); Text(value.Summary, 1000, $"{path}.summary", problems);
    }
    private static void Location(SmallWorldLocation? value, string path, List<SmallWorldCompositionProblem> problems)
    {
        if (value is null) { problems.Add(Problem("WORLD_BLUEPRINT_REQUIRED", path, "The required authored record is missing.")); return; }
        Text(value.Name, 160, $"{path}.name", problems); Text(value.Summary, 1000, $"{path}.summary", problems);
    }
    private static void Motive(SmallWorldMotive? value, string path, List<SmallWorldCompositionProblem> problems)
    {
        if (value is null) { problems.Add(Problem("WORLD_BLUEPRINT_REQUIRED", path, "The required authored record is missing.")); return; }
        Text(value.Name, 160, $"{path}.name", problems); Text(value.Summary, 1000, $"{path}.summary", problems);
    }
    private static void Faction(SmallWorldFaction? value, List<SmallWorldCompositionProblem> problems)
    {
        if (value is null) { problems.Add(Problem("WORLD_BLUEPRINT_REQUIRED", "faction", "The required authored record is missing.")); return; }
        Text(value.Name, 160, "faction.name", problems); Text(value.Summary, 1000, "faction.summary", problems); Text(value.AgendaSummary, 1000, "faction.agendaSummary", problems);
        Texts(value.Goals, 1, 5, "faction.goals", problems); Texts(value.Methods, 1, 5, "faction.methods", problems); Texts(value.Assets, 0, 10, "faction.assets", problems);
    }
    private static void Knowledge(SmallWorldKnowledge? value, string path, List<SmallWorldCompositionProblem> problems)
    {
        if (value is null) { problems.Add(Problem("WORLD_BLUEPRINT_REQUIRED", path, "The required authored record is missing.")); return; }
        Text(value.Name, 160, $"{path}.name", problems); Text(value.Summary, 1000, $"{path}.summary", problems); Text(value.Provenance, 500, $"{path}.provenance", problems);
        if (!SubjectKinds.Contains(value.SubjectKind ?? string.Empty)) problems.Add(Problem("WORLD_BLUEPRINT_INVALID", $"{path}.subjectKind", "Knowledge subjectKind is outside the closed classification vocabulary."));
        if (!Sensitivities.Contains(value.Sensitivity ?? string.Empty)) problems.Add(Problem("WORLD_BLUEPRINT_INVALID", $"{path}.sensitivity", "Knowledge sensitivity is outside the closed classification vocabulary."));
    }
    private static void Texts(IReadOnlyList<string>? values, int minimum, int maximum, string path, List<SmallWorldCompositionProblem> problems)
    {
        if (values is null) { problems.Add(Problem("WORLD_BLUEPRINT_REQUIRED", path, "The collection is required.")); return; }
        if (values.Count < minimum || values.Count > maximum || values.Distinct(StringComparer.Ordinal).Count() != values.Count) problems.Add(Problem("WORLD_BLUEPRINT_INVALID", path, $"The collection must contain {minimum}–{maximum} distinct values."));
        for (var index = 0; index < values.Count; index++) Text(values[index], 500, $"{path}[{index}]", problems);
    }
    private static void Text(string? value, int maximum, string path, List<SmallWorldCompositionProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(value)) problems.Add(Problem("WORLD_BLUEPRINT_REQUIRED", path, "A nonempty value is required."));
        else if (value != value.Trim() || value.Length > maximum) problems.Add(Problem("WORLD_BLUEPRINT_INVALID", path, $"Value must be trimmed and no longer than {maximum} characters."));
    }
    private static bool Namespace(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.StartsWith("world.c10.", StringComparison.Ordinal) && value.Length <= 200 && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-') && !value.EndsWith(".", StringComparison.Ordinal) && !value.Contains("..", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, string> Names(SmallWorldBlueprint blueprint) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["world"] = blueprint.World.Name, ["region"] = blueprint.Region.Name, ["location.gate"] = blueprint.Gate.Name, ["location.market"] = blueprint.Market.Name, ["location.observatory"] = blueprint.Observatory.Name,
        ["faction"] = blueprint.Faction.Name, ["actor.one"] = blueprint.ActorOne.Name, ["actor.two"] = blueprint.ActorTwo.Name, ["knowledge.fact"] = blueprint.Fact.Name, ["knowledge.rumour"] = blueprint.Rumour.Name,
        ["knowledge.secret"] = blueprint.Secret.Name, ["knowledge.clue.one"] = blueprint.ClueOne.Name, ["knowledge.clue.two"] = blueprint.ClueTwo.Name, ["knowledge.clue.three"] = blueprint.ClueThree.Name
    };

    private static IReadOnlyList<Effect> Effects(SmallWorldBlueprint b, IReadOnlyDictionary<string, string> ids, IReadOnlyDictionary<string, string> names)
    {
        var effects = Keys.Select(key => Create(ids[key], names[key])).ToList();
        effects.Add(Component(ids["world"], "game.core.world.root", new { status = "active", summary = b.World.Summary, visibility = "public" }));
        effects.Add(Location(ids["region"], "region", b.Region.Summary, "public")); effects.Add(Location(ids["location.gate"], "settlement", b.Gate.Summary, "public")); effects.Add(Location(ids["location.market"], "site", b.Market.Summary, "public")); effects.Add(Location(ids["location.observatory"], "interior", b.Observatory.Summary, "party"));
        effects.Add(Component(ids["faction"], "game.core.world.faction", new { status = "active", summary = b.Faction.Summary, visibility = "party", goals = b.Faction.Goals, methods = b.Faction.Methods, assets = b.Faction.Assets, agenda = new { state = "ready", summary = b.Faction.AgendaSummary } }));
        effects.Add(Component(ids["actor.one"], "game.core.world.motive", new { status = "active", summary = b.ActorOne.Summary, visibility = "party" })); effects.Add(Component(ids["actor.two"], "game.core.world.motive", new { status = "active", summary = b.ActorTwo.Summary, visibility = "party" }));
        Knowledge(effects, ids["knowledge.fact"], "game.core.world.fact", b.Fact, "active", "public"); Knowledge(effects, ids["knowledge.rumour"], "game.core.world.rumour", b.Rumour, "unconfirmed", "party"); Knowledge(effects, ids["knowledge.secret"], "game.core.world.secret", b.Secret, "active", "gm"); Knowledge(effects, ids["knowledge.clue.one"], "game.core.world.clue", b.ClueOne, "unrevealed", "gm"); Knowledge(effects, ids["knowledge.clue.two"], "game.core.world.clue", b.ClueTwo, "unrevealed", "gm"); Knowledge(effects, ids["knowledge.clue.three"], "game.core.world.clue", b.ClueThree, "unrevealed", "gm");
        effects.Add(Move(ids["region"], ids["world"], "region")); effects.Add(Move(ids["location.gate"], ids["region"], "location")); effects.Add(Move(ids["location.market"], ids["region"], "location")); effects.Add(Move(ids["location.observatory"], ids["region"], "location"));
        effects.Add(Link(ids["location.gate"], ids["location.market"], "game.core.world.location.connected-to")); effects.Add(Link(ids["location.market"], ids["location.observatory"], "game.core.world.location.connected-to"));
        effects.Add(Link(ids["faction"], ids["actor.one"], "game.core.world.faction.member")); effects.Add(Link(ids["faction"], ids["actor.two"], "game.core.world.faction.member")); effects.Add(Link(ids["faction"], ids["location.market"], "game.core.world.faction.controls"));
        Links(effects, ids, "knowledge.fact", "location.market"); Links(effects, ids, "knowledge.rumour", "location.observatory"); Links(effects, ids, "knowledge.secret", "actor.two"); Links(effects, ids, "knowledge.clue.one", "location.market", "knowledge.fact"); Links(effects, ids, "knowledge.clue.two", "location.observatory", "knowledge.secret"); Links(effects, ids, "knowledge.clue.three", "actor.two", "knowledge.secret");
        return effects;
    }
    private static void Knowledge(List<Effect> effects, string id, string definition, SmallWorldKnowledge value, string status, string visibility) { effects.Add(Component(id, definition, new { status, summary = value.Summary, provenance = value.Provenance, visibility })); effects.Add(Component(id, "game.core.world.knowledge.classification", new { subjectKind = value.SubjectKind, sensitivity = value.Sensitivity })); }
    private static void Links(List<Effect> effects, IReadOnlyDictionary<string, string> ids, string source, string about, string? supports = null) { effects.Add(Link(ids[source], ids["world"], "game.core.world.knowledge.in-world")); effects.Add(Link(ids[source], ids[about], "game.core.world.knowledge.about")); if (supports is not null) effects.Add(Link(ids[source], ids[supports], "game.core.world.clue.supports")); }
    private static Effect Create(string id, string name) => new() { Type = EffectType.EntityCreate, EntityId = id, Name = name };
    private static Effect Component(string id, string definition, object data) => new() { Type = EffectType.ComponentAdd, EntityId = id, DefinitionId = definition, Data = JsonSerializer.Serialize(data) };
    private static Effect Location(string id, string kind, string summary, string visibility) => Component(id, "game.core.world.location", new { kind, status = "active", summary, visibility });
    private static Effect Move(string id, string target, string slot) => new() { Type = EffectType.ContainmentMove, EntityId = id, ToEntityId = target, Slot = slot };
    private static Effect Link(string id, string target, string kind) => new() { Type = EffectType.RelationshipCreate, EntityId = id, ToEntityId = target, Kind = kind, Data = "{}" };
    private static SmallWorldVisibilityReview Review(string key, string visibility) => new(key, visibility, visibility == "gm" ? "gm" : "party");
    private static SmallWorldCompositionProblem Problem(string code, string path, string reason) => new(code, path, reason);
    private static IReadOnlyList<SmallWorldCompositionProblem> StagedProblems(IReadOnlyList<StagedWorldProblem> problems) => problems.Select(problem => Problem(problem.Reason.Contains("already", StringComparison.OrdinalIgnoreCase) || problem.Reason.Contains("taken", StringComparison.OrdinalIgnoreCase) ? "WORLD_ID_CONFLICT" : "WORLD_EFFECTS_INVALID", problem.Path, problem.Reason)).ToArray();
    private static SmallWorldCompositionResult Invalid(IReadOnlyList<SmallWorldCompositionProblem> problems) => new("invalid", null, [], null, [], [], null, problems);
}
