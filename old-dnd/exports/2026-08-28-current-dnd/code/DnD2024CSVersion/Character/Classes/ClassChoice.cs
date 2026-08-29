namespace DnD2024CSVersion.Character.Classes;

/// <summary>
/// A choice introduced by a class feature or level.
/// </summary>
public sealed class ClassChoice
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int NumberOfSelections { get; init; } = 1;

    public IReadOnlyList<string> OptionIds { get; init; } = [];
}
