namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Uses an Intelligence check to recall or derive important information.
/// </summary>
public sealed class Study : GameAction
{
    public override string Name => "Study";

    public override string Description =>
        "Make an Intelligence check using Arcana, History, Investigation, Nature, or Religion to recall information.";
}
