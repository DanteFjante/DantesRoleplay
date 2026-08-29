namespace DnD2024CSVersion.Character.Advancement;

public sealed record CharacterChoicesComponent
{
    public IReadOnlyList<CharacterChoice> Choices { get; init; } = [];
}
