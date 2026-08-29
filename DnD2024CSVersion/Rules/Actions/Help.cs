namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Assists an ally with an ability check or attack roll.
/// </summary>
public sealed class Help : GameAction
{
    public override string Name => "Help";

    public override string Description =>
        "Assist an ally's eligible ability check or distract a nearby enemy to assist an ally's attack.";
}
