namespace DnD2024CSVersion.ECS;

/// <summary>
/// Indicates that a system requested a component an entity does not have.
/// </summary>
public sealed class MissingComponentException : InvalidOperationException
{
    public MissingComponentException(string entityId, Type componentType)
        : base($"Entity '{entityId}' is missing required component '{componentType.Name}'.")
    {
        EntityId = entityId;
        ComponentType = componentType;
    }

    public string EntityId { get; }

    public Type ComponentType { get; }
}
