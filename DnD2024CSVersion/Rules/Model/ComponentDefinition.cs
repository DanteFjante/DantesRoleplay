namespace DnD2024CSVersion.Rules.Model;

/// <summary>
/// Describes one component that may be attached to an entity.
/// </summary>
public sealed record ComponentDefinition(
    string Id,
    string Name,
    string Category,
    string Description,
    string Model,
    ComponentAuthority Authority);
