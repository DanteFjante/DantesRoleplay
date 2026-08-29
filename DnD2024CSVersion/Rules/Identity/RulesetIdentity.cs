namespace DnD2024CSVersion.Rules.Identity;

/// <summary>
/// Stable identity and display information for a ruleset across all of its versions.
/// </summary>
public sealed record RulesetIdentity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Edition { get; init; }
    public string Description { get; init; } = string.Empty;
}
