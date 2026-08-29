namespace DnD2024CSVersion.Rules.Model;

/// <summary>
/// References a reusable record containing legal alternatives.
/// </summary>
public sealed record ChoiceSetReference(
    string ChoiceSetId,
    int Choose);
