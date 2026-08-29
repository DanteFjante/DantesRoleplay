namespace DnD2024CSVersion.Character.Identity;

public sealed record IdentityComponent
{
    public required string Name { get; init; }
    public string? PlayerName { get; init; }
    public string? AlignmentId { get; init; }
    public string Biography { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;
}
