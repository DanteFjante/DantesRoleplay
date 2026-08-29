namespace DnD2024CSVersion.Character.CurrentState;

public sealed record ConditionsComponent
{
    public IReadOnlyList<ActiveCondition> Conditions { get; init; } = [];
    public int ExhaustionLevel { get; set; }
}
