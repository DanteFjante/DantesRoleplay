using System.Text.Json.Nodes;
using DantesRoleplay.World;

namespace DantesRoleplay.Web.Data;

public sealed record DynamicDataDocument(JsonNode Json, int? Revision = null);

public sealed class DynamicDataReader(IWorldStore world)
{
    public async Task<DynamicDataDocument?> ReadAsync(
        string type,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var entity = await world.GetEntityAsync(entityId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (string.Equals(type, "entity", StringComparison.Ordinal))
        {
            return new DynamicDataDocument(ToJson(entity));
        }

        var component = entity.Components.SingleOrDefault(candidate =>
            string.Equals(candidate.DefinitionId, type, StringComparison.Ordinal));
        if (component is null)
        {
            return null;
        }

        return new DynamicDataDocument(
            JsonNode.Parse(component.Data)
                ?? throw new InvalidOperationException("Stored component data was null JSON."),
            component.Revision);
    }

    private static JsonObject ToJson(EntitySnapshot entity)
    {
        var components = new JsonObject();
        var revisions = new JsonObject();

        foreach (var component in entity.Components)
        {
            components[component.DefinitionId] = JsonNode.Parse(component.Data)
                ?? throw new InvalidOperationException(
                    $"Stored component '{component.DefinitionId}' was null JSON.");
            revisions[component.DefinitionId] = component.Revision;
        }

        var contains = new JsonArray();
        foreach (var contained in entity.Contains ?? [])
        {
            contains.Add(new JsonObject
            {
                ["id"] = contained.ContainedId,
                ["name"] = contained.Name,
                ["slot"] = contained.Slot
            });
        }

        return new JsonObject
        {
            ["id"] = entity.Id,
            ["name"] = entity.Name,
            ["containerId"] = entity.ContainerId,
            ["containerSlot"] = entity.ContainerSlot,
            ["contains"] = contains,
            ["components"] = components,
            ["componentRevisions"] = revisions
        };
    }
}
