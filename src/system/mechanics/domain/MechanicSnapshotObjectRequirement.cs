using System.Text.Json.Serialization;

namespace DantesRoleplay.Mechanics;

/// <summary>A singleton or bounded collection of registered objects, never a new storage read grant.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MechanicSnapshotObjectRequirement
{
    public string QualifiedId { get; init; } = "";
    public int Version { get; init; }
    public string ContentFingerprint { get; init; } = "";
    public Dictionary<string, MechanicSnapshotRoleBinding> RoleBindings { get; init; } = [];
    public IReadOnlyList<string> ReferenceComponentIds { get; init; } = [];
    public int MaximumItems { get; init; } = 1;

    internal bool Valid(IReadOnlyDictionary<string, RoleRequirement> roles) =>
        roles is not null && MechanicObjectRoleRequirement.Token(QualifiedId, 200) && Version > 0 &&
        ContentFingerprint is { Length: 64 } && ContentFingerprint.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F') &&
        ReferenceComponentIds is { Count: <= 32 } &&
        RoleBindings is { Count: > 0 and <= 32 } && RoleBindings.All(pair =>
            MechanicObjectRoleRequirement.Token(pair.Key, 200) && pair.Value is not null &&
            pair.Value.Valid(roles, RoleBindings.Keys, ReferenceComponentIds.Count > 0)) &&
        ReferenceComponentIds.Count <= 32 && ReferenceComponentIds.Distinct(StringComparer.Ordinal).Count() == ReferenceComponentIds.Count &&
        ReferenceComponentIds.All(value => MechanicObjectRoleRequirement.Token(value, 200)) &&
        (ReferenceComponentIds.Count == 0 ? MaximumItems == 1 : MaximumItems is >= 1 and <= 10000);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MechanicSnapshotRoleBinding
{
    public string? Role { get; init; }
    public string? InputEntityId { get; init; }
    public bool Reference { get; init; }
    public string? FromRole { get; init; }
    public string? ComponentId { get; init; }
    public string? Field { get; init; }

    internal bool Valid(IReadOnlyDictionary<string, RoleRequirement> roles, IEnumerable<string> bindings, bool collection)
    {
        if ((Role is not null ? 1 : 0) + (InputEntityId is not null ? 1 : 0) + (Reference ? 1 : 0) + (FromRole is not null ? 1 : 0) != 1)
            return false;
        if (FromRole is not null)
            return bindings.Contains(FromRole, StringComparer.Ordinal) &&
                MechanicObjectRoleRequirement.Token(ComponentId, 200) && MechanicObjectRoleRequirement.Token(Field, 200);
        return ComponentId is null && Field is null &&
            (Role is null || roles.ContainsKey(Role)) &&
            (InputEntityId is null || MechanicObjectRoleRequirement.Token(InputEntityId, 100)) && (!Reference || collection);
    }
}
