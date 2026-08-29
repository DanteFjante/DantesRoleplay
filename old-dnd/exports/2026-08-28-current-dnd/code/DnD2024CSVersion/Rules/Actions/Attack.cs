namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Makes a weapon attack or an Unarmed Strike.
/// </summary>
public sealed class Attack : GameAction
{
    public override string Name => "Attack";

    public override string Description => "Make a weapon attack or an Unarmed Strike.";
}
