using DantesRoleplay.Applications;

namespace DantesRoleplay.Ecs;

public sealed record ComponentTypeVersion(ApplicationIdentifier Owner, string QualifiedId, int Version, string SchemaHash);

public sealed record ComponentTypeDefinition(ApplicationIdentifier Owner, string QualifiedId, string SchemaJson);

public sealed record RegisteredComponentTypeVersion(
    ApplicationIdentifier Owner,
    string QualifiedId,
    int Version,
    string ProfileId,
    string SchemaJson,
    string SchemaHash,
    DateTime CreatedAtUtc);

public sealed record ComponentTypeDiscoveryPage(
    IReadOnlyList<RegisteredComponentTypeVersion> ComponentTypes,
    string? NextQualifiedId);

/// <summary>The caller supplies neither version nor fingerprint; the registry derives both.</summary>
public interface IApplicationComponentTypeRegistry
{
    RegisteredComponentTypeVersion Define(ComponentTypeDefinition definition);
    RegisteredComponentTypeVersion? Get(string qualifiedId, int version);
    RegisteredComponentTypeVersion? GetLatest(string qualifiedId);
    RegisteredComponentTypeVersion? GetBySchemaHash(string qualifiedId, string profileId, string schemaHash);
    ComponentTypeDiscoveryPage ListLatestPage(
        ApplicationIdentifier owner,
        string? afterQualifiedId,
        int limit);
}

public static class ComponentTypeIdentifier
{
    public static void Validate(ApplicationIdentifier owner, string qualifiedId)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrWhiteSpace(qualifiedId) || qualifiedId.Length > 200)
            throw new ArgumentException("A component type ID is required and may not exceed 200 characters.", nameof(qualifiedId));
        var segments = qualifiedId.Split('.');
        if (segments.Length < 2 || segments[0] != owner.Value || segments.Skip(1).Any(segment =>
                segment.Length is < 1 or > 63
                || !char.IsAsciiLetterLower(segment[0])
                || segment.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))))
            throw new ArgumentException("A component type ID must use its owner prefix and lowercase ASCII segments.", nameof(qualifiedId));
    }
}

public interface IComponentTypeRegistry
{
    ComponentTypeVersion Define(ComponentTypeVersion type);
    ComponentTypeVersion? Get(string qualifiedId, int version);
}

public sealed class InMemoryComponentTypeRegistry : IComponentTypeRegistry
{
    private readonly Dictionary<(string Id, int Version), ComponentTypeVersion> _types = [];

    public ComponentTypeVersion Define(ComponentTypeVersion type)
    {
        try { ComponentTypeIdentifier.Validate(type.Owner, type.QualifiedId); }
        catch (ArgumentException exception) { throw new ArgumentException("A component type must use its owner's qualified ID, positive version, and SHA-256 schema hash.", nameof(type), exception); }
        if (type.Version < 1 || type.SchemaHash is not { Length: 64 } || !type.SchemaHash.All(Uri.IsHexDigit))
            throw new ArgumentException("A component type must use its owner's qualified ID, positive version, and SHA-256 schema hash.", nameof(type));
        var key = (type.QualifiedId, type.Version);
        if (_types.TryGetValue(key, out var existing))
        {
            if (existing != type) throw new InvalidOperationException("A component type version is immutable.");
            return existing;
        }
        if (type.Version > 1 && !_types.ContainsKey((type.QualifiedId, type.Version - 1)))
            throw new InvalidOperationException("Component type versions must be appended without gaps.");
        _types.Add(key, type);
        return type;
    }

    public ComponentTypeVersion? Get(string qualifiedId, int version) =>
        _types.TryGetValue((qualifiedId, version), out var value) ? value : null;

}
