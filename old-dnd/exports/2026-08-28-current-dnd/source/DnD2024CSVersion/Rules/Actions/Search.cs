namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Uses a Wisdom check to discern something that is not obvious.
/// </summary>
public sealed class Search : GameAction
{
    public override string Name => "Search";

    public override string Description =>
        "Make a Wisdom check using Insight, Medicine, Perception, or Survival to discern something not obvious.";
}
