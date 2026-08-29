namespace DnD2024CSVersion.Rules.Model;

/// <summary>
/// Display information for a reusable die kind.
/// </summary>
public sealed record DieDefinition(
    string Id,
    string Name,
    int Sides);
