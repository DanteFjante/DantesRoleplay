namespace DantesRoleplay.World;

/// <summary>One component, flattened for reading.</summary>
public sealed record ComponentView(string DefinitionId, string Data, int Revision);

/// <summary>
/// An entity together with everything attached to it. This is the shape a mechanic receives —
/// ARCHITECTURE.md §3.6 requires that a mechanic gets its data up front and never reads the
/// store mid-execution, so this record is the unit that gets materialised before the sandbox runs.
/// </summary>
public sealed record EntitySnapshot(
    string Id,
    string Name,
    IReadOnlyList<ComponentView> Components,
    string? ContainerId,
    string ContainerSlot);

/// <summary>Cheap listing shape — no component payloads.</summary>
public sealed record EntitySummary(string Id, string Name, IReadOnlyList<string> ComponentIds);

public sealed record ComponentDefinitionView(
    string Id,
    string Name,
    string Description,
    string Schema,
    int UsageCount);

public sealed record RelationshipView(
    string FromEntityId,
    string ToEntityId,
    string Kind,
    string Data);

public sealed record ContainmentView(string ContainedId, string Name, string Slot);
