namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Prevents the creature's movement from provoking Opportunity Attacks this turn.
/// </summary>
public sealed class Disengage : GameAction
{
    public override string Name => "Disengage";

    public override string Description =>
        "Your movement does not provoke Opportunity Attacks for the rest of the current turn.";
}
