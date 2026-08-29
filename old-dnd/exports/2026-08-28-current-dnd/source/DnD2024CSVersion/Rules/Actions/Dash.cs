namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Grants additional movement for the current turn.
/// </summary>
public sealed class Dash : GameAction
{
    public override string Name => "Dash";

    public override string Description =>
        "Gain extra movement for the current turn equal to your Speed after modifiers.";
}
