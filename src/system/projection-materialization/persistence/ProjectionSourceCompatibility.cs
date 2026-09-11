using DantesRoleplay.Ecs;

namespace DantesRoleplay.Projections;

/// <summary>Display may follow an attached version of the same registered component identity.</summary>
internal sealed class ProjectionSourceCompatibility(IApplicationComponentTypeRegistry? types)
{
    private readonly Dictionary<EcsComponentReference, ComponentTypeVersion> registered = [];

    public void Prepare(IEnumerable<(EcsComponentReference Declared, EcsComponentReference Actual)> sources)
    {
        var references = sources.Where(value => value.Declared != value.Actual)
            .SelectMany(value => new[] { value.Declared, value.Actual })
            .Distinct().Where(value => !registered.ContainsKey(value)).ToArray();
        if (references.Length == 0 || types is not IApplicationComponentTypeIdentityReader batch) return;
        var identities = batch.ReadIdentities(references).ToDictionary(value => (value.QualifiedId, value.Version));
        foreach (var reference in references)
        {
            if (!identities.TryGetValue((reference.QualifiedTypeId, reference.TypeVersion), out var identity)
                || identity.SchemaHash != reference.SchemaHash)
                throw new InvalidOperationException("A display source does not match its registered component authority.");
            registered.Add(reference, identity);
        }
    }

    public bool Matches(EcsComponentReference declared, EcsComponentReference actual, bool display)
    {
        if (declared == actual) return true;
        if (!display || declared.QualifiedTypeId != actual.QualifiedTypeId) return false;
        if (types is null)
            throw new InvalidOperationException("Current-component display requires the registered type reader.");
        var source = Get(actual);
        var anchor = Get(declared);
        if (source.Owner != anchor.Owner)
            throw new InvalidOperationException("A display source does not match its registered component authority.");
        return true;
    }

    private ComponentTypeVersion Get(EcsComponentReference reference)
    {
        if (registered.TryGetValue(reference, out var value)) return value;
        var type = types!.Get(reference.QualifiedTypeId, reference.TypeVersion);
        if (type is null || type.SchemaHash != reference.SchemaHash)
            throw new InvalidOperationException("A display source does not match its registered component authority.");
        value = new(type.Owner, type.QualifiedId, type.Version, type.SchemaHash);
        registered.Add(reference, value);
        return value;
    }
}
