namespace DnD2024CSVersion.Rules.Model;

public sealed record DiceExpression(
    IReadOnlyList<DiceTerm> Terms,
    int Modifier = 0);
