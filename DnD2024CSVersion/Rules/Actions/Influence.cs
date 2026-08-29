namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Tries to persuade, deceive, intimidate, amuse, or otherwise influence a monster.
/// </summary>
public sealed class Influence : GameAction
{
    public override string Name => "Influence";

    public override string Description =>
        "Urge a monster to do something through roleplay or description, with a check when it is hesitant.";
}
