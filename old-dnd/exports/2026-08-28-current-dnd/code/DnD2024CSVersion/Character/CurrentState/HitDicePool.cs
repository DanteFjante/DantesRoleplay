using DnD2024CSVersion.Rules.Model;

namespace DnD2024CSVersion.Character.CurrentState;

public sealed record HitDicePool(
    Die Die,
    int Maximum,
    int Remaining);
