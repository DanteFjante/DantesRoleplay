namespace DnD2024CSVersion.ECS;

/// <summary>
/// Stores the independent components that currently exist for one game entity.
/// </summary>
public sealed class Entity
{
    private readonly Dictionary<Type, object> _components = [];

    public Entity(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }

    public IReadOnlyCollection<Type> ComponentTypes => _components.Keys;

    /// <summary>
    /// Adds a component or replaces the component of the same type.
    /// </summary>
    public void Set<T>(T component) where T : class
    {
        ArgumentNullException.ThrowIfNull(component);
        _components[typeof(T)] = component;
    }

    public bool Has<T>() where T : class => _components.ContainsKey(typeof(T));

    /// <summary>
    /// Reads an optional component without treating its absence as an error.
    /// </summary>
    public bool TryGet<T>(out T? component) where T : class
    {
        if (_components.TryGetValue(typeof(T), out var value))
        {
            component = (T)value;
            return true;
        }

        component = null;
        return false;
    }

    public T? GetOrDefault<T>() where T : class =>
        TryGet<T>(out var component) ? component : null;

    /// <summary>
    /// Reads a required component and reports precisely what the entity lacks.
    /// </summary>
    public T Require<T>() where T : class
    {
        if (TryGet<T>(out var component))
        {
            return component!;
        }

        throw new MissingComponentException(Id, typeof(T));
    }

    public bool Remove<T>() where T : class => _components.Remove(typeof(T));
}
