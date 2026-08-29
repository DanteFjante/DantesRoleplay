namespace DnD2024CSVersion.Character.Classes;

/// <summary>
/// Everything a class grants when a character reaches one class level.
/// </summary>
public sealed class ClassLevel
{
    public int Level { get; init; }

    public IReadOnlyList<string> FeatureIds { get; init; } = [];

    public IReadOnlyList<ClassChoice> Choices { get; init; } = [];

    /// <summary>
    /// Class-specific table values, such as Rage uses, Focus Points,
    /// Sneak Attack dice, prepared spells, or a Bardic Die.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProgressionValues { get; init; } =
        new Dictionary<string, string>();
}
