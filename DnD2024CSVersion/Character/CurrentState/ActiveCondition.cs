namespace DnD2024CSVersion.Character.CurrentState;

public sealed record ActiveCondition(
    string ConditionId,
    string? SourceId,
    int? RemainingRounds,
    string? Notes);
