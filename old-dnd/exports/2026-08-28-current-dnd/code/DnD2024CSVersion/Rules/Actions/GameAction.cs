namespace DnD2024CSVersion.Rules.Actions;

/// <summary>
/// Base type for an action a creature can take during play.
/// </summary>
public abstract class GameAction
{
    /// <summary>
    /// Gets the action's rulebook name.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets a short description of what the action does.
    /// </summary>
    public abstract string Description { get; }
}
