namespace DnD2024CSVersion.Rules.Identity;

/// <summary>
/// Licensing and attribution information for ruleset sources or content.
/// </summary>
public sealed record RulesetLicense
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? SpdxIdentifier { get; init; }
    public string? Url { get; init; }
    public string Attribution { get; init; } = string.Empty;
}
