using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Obtains application event origin from the registered state-space binding, never from a caller
/// or catalog payload. That makes the source an immutable fact of the committed ECS transaction.
/// </summary>
internal static class ApplicationEventSourceContext
{
    public static EventSourceContext Require(
        IStateSpaceRegistry stateSpaces,
        ApplicationEcsEffectBatch batch)
    {
        var stateSpace = stateSpaces.Get(batch.StateSpaceId)
            ?? throw new ApplicationEcsTransactionParticipantException(
                "The application event state space is unknown.");
        return new EventSourceContext(stateSpace.ApplicationRevision.ApplicationId.Value,
            stateSpace.StateSpaceId);
    }
}
