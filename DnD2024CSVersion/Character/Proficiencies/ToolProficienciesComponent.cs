namespace DnD2024CSVersion.Character.Proficiencies;

public sealed record ToolProficienciesComponent
{
    public IReadOnlySet<string> ToolProficiencyIds { get; init; } = new HashSet<string>();
}
