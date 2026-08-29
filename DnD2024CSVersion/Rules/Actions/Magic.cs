namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Casts a spell or activates a magical feature or item that requires an action.
/// </summary>
public sealed class Magic : GameAction
{
    public override string Name => "Magic";

    public override string Description =>
        "Cast an action-time spell or activate a feature or magic item that requires the Magic action.";
}
