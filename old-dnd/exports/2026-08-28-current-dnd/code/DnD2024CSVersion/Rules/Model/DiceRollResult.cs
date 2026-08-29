namespace DnD2024CSVersion.Rules.Model;

public sealed record DiceRollResult(
    DiceExpression Expression,
    IReadOnlyList<int> Results,
    int Total);
