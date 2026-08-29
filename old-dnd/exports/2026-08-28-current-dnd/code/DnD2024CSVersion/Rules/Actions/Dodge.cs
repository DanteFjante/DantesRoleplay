namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Concentrates on avoiding attacks until the start of the creature's next turn.
/// </summary>
public sealed class Dodge : GameAction
{
    public override string Name => "Dodge";

    public override string Description =>
        "Visible attackers have Disadvantage against you, and you have Advantage on Dexterity saving throws.";
}
