using DantesRoleplay.Mechanics;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Builds what a guard or reaction sees as <c>ctx.eventEntities</c>.
///
/// One definition, for the same reason <see cref="Events.EventEnvelope"/> is one definition: a
/// guard and a reaction must see the same shape, or an author has to learn the middleware surface
/// twice and will get the second one wrong.
///
/// Only the components the mechanic DECLARED are projected. That is not an optimisation — it is the
/// same rule that governs roles. A mechanic states what it needs, the host materialises exactly
/// that, and nothing else crosses the sandbox boundary. A middleware that silently received every
/// component of every affected entity would be able to depend on data it never declared, and the
/// day someone removed that component nothing would say why the rule broke.
/// </summary>
internal static class AffectedEntities
{
    public static async Task<Dictionary<string, EntityProjection>> ProjectAsync(
        IWorldStore world,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> declaredComponents,
        CancellationToken cancellationToken)
    {
        var projected = new Dictionary<string, EntityProjection>(StringComparer.Ordinal);

        if (entityIds.Count == 0)
        {
            return projected;
        }

        // A deleted entity simply comes back absent, which is the documented behaviour: there is
        // nothing left to project, and what it was is already frozen in the event payload.
        var snapshots = await world.GetEntitiesAsync(entityIds, declaredComponents, cancellationToken);

        foreach (var snapshot in snapshots)
        {
            projected[snapshot.Id] = new EntityProjection(
                snapshot.Id,
                snapshot.Name,
                snapshot.Components.ToDictionary(c => c.DefinitionId, c => c.Data, StringComparer.Ordinal),
                snapshot.ContainerId,
                snapshot.ContainerSlot);
        }

        return projected;
    }
}
