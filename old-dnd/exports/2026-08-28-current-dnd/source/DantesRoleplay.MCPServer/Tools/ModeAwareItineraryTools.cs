using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.MCPServer.Tools;

public sealed class ModeAwareItineraryTools
{
    public Task<ToolEnvelope> GetAsync(IModeAwareItineraryReader itineraries, IOperationLog log, ModeAwareItineraryQuery query, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "get_mode_aware_itinerary", async () =>
        {
            var result = await itineraries.ReadAsync(query, cancellationToken);
            if (!result.Ok) return ToolOutcome.Fail(result.ErrorCode, result.ErrorMessage, "query(kind: \"itinerary-plan\", worldId: \"...\", travellerId: \"...\", destinationLocationId: \"...\")", "Itinerary planning is read-only; no world state changed.");
            var plan = result.Projection!;
            return ToolOutcome.Ok(plan, $"Itinerary is {plan.Status} with {plan.Legs.Count} leg(s).", "Execute only the first ready leg through its named mode owner, then request a fresh itinerary-plan.");
        });
}
