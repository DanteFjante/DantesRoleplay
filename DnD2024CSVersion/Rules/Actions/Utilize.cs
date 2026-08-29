namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Uses an object whose use requires an action.
/// </summary>
public sealed class Utilize : GameAction
{
    public override string Name => "Utilize";

    public override string Description => "Use an object whose use requires an action.";
}
