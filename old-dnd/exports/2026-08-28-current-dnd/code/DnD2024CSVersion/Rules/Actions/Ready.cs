namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Prepares an action or movement to occur in response to a perceivable trigger.
/// </summary>
public sealed class Ready : GameAction
{
    public override string Name => "Ready";

    public override string Description =>
        "Choose a perceivable trigger and prepare an action or movement to take as a Reaction.";
}
