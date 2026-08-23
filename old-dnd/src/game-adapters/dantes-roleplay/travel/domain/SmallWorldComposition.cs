using DantesRoleplay.Effects;

namespace DantesRoleplay.World;

/// <summary>Closed authored content for one C10 world root. Lifecycle and visibility are derived.</summary>
public sealed record SmallWorldRoot(string Name, string Summary);
public sealed record SmallWorldLocation(string Name, string Summary);
public sealed record SmallWorldMotive(string Name, string Summary);
public sealed record SmallWorldFaction(string Name, string Summary, IReadOnlyList<string> Goals, IReadOnlyList<string> Methods, IReadOnlyList<string> Assets, string AgendaSummary);
public sealed record SmallWorldKnowledge(string Name, string Summary, string Provenance, string SubjectKind, string Sensitivity);

/// <summary>
/// The complete closed C10 World contribution. Local keys, state, visibility, links, and IDs are
/// all derived by the World owner rather than supplied by a caller.
/// </summary>
public sealed record SmallWorldBlueprint(
    SmallWorldRoot World,
    SmallWorldLocation Region,
    SmallWorldLocation Gate,
    SmallWorldLocation Market,
    SmallWorldLocation Observatory,
    SmallWorldFaction Faction,
    SmallWorldMotive ActorOne,
    SmallWorldMotive ActorTwo,
    SmallWorldKnowledge Fact,
    SmallWorldKnowledge Rumour,
    SmallWorldKnowledge Secret,
    SmallWorldKnowledge ClueOne,
    SmallWorldKnowledge ClueTwo,
    SmallWorldKnowledge ClueThree);

public sealed record SmallWorldCompositionProblem(string Code, string Path, string Reason);
public sealed record SmallWorldIdentity(string LocalKey, string EntityId, string Name);
public sealed record SmallWorldVisibilityReview(string LocalKey, string Visibility, string Audience);
public sealed record SmallWorldCreationCounts(int Entities, int Components, int Containment, int Relationships);

/// <summary>Read-only World child result for the later C10 coordinator and Campaign adapter.</summary>
public sealed record SmallWorldCompositionResult(
    string Status,
    string? WorldRootId,
    IReadOnlyList<SmallWorldIdentity> LocalKeyMap,
    SmallWorldCreationCounts? Counts,
    IReadOnlyList<SmallWorldVisibilityReview> VisibilityReview,
    IReadOnlyList<Effect> Effects,
    IWorldStore? World,
    IReadOnlyList<SmallWorldCompositionProblem> Problems)
{
    public bool Valid => Status == "valid" && WorldRootId is not null && World is not null;
}

public interface ISmallWorldCompositionPlanner
{
    Task<SmallWorldCompositionResult> ComposeAsync(
        SmallWorldBlueprint blueprint,
        string worldNamespace,
        CancellationToken cancellationToken = default);
}
