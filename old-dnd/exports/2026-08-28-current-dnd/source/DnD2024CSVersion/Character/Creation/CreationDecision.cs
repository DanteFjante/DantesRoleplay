namespace DnD2024CSVersion.Character.Creation;

/// <summary>
/// Records one input used to create the character without interpreting the choice in C#.
/// </summary>
public sealed record CreationDecision
{
    public required string StepId { get; init; }
    public required string ChoiceId { get; init; }
    public string? SourceId { get; init; }
    public IReadOnlyList<string> SelectedOptionIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>();
}
