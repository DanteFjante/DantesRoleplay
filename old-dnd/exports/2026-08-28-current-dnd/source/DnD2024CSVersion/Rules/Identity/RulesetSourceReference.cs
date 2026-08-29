namespace DnD2024CSVersion.Rules.Identity;

/// <summary>
/// Identifies a source from which ruleset content was derived.
/// </summary>
public sealed record RulesetSourceReference
{
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public string? Version { get; init; }
    public string? Locator { get; init; }
    public string? Url { get; init; }
    public string? LicenseId { get; init; }
}
