namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Attempts to become hidden while sufficiently concealed and outside enemy sight.
/// </summary>
public sealed class Hide : GameAction
{
    public override string Name => "Hide";

    public override string Description =>
        "Attempt a DC 15 Dexterity (Stealth) check while sufficiently concealed and outside enemy sight.";
}
