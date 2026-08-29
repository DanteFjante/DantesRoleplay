namespace DnD2024CSVersion.Character.Advancement;

public sealed record CharacterChoice(
    string SourceId,
    string ChoiceId,
    IReadOnlyList<string> SelectedOptionIds);
