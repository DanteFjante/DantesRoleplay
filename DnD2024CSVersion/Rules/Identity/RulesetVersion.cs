namespace DnD2024CSVersion.Rules.Identity;

/// <summary>
/// Identifies one immutable release of a ruleset.
/// </summary>
public sealed record RulesetVersion
{
    public required string Version { get; init; }
    public required string Status { get; init; }
    public DateOnly? ReleasedOn { get; init; }
    public string? SupersedesVersion { get; init; }
}
