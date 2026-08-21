using DantesRoleplay.Actions;
using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Executes one freshly verified itinerary leg through its existing action owner.</summary>
public sealed class ItineraryAdvanceTools
{
    public Task<ToolEnvelope> AdvanceAsync(
        IModeAwareItineraryReader itineraries,
        IActionRunner actions,
        IOperationLog log,
        ModeAwareItineraryAdvanceRequest request,
        IReadOnlyList<string> proceduresUsed,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", "Advance one itinerary leg.", "commit:itinerary-advance", proceduresUsed, async () =>
        {
            var plan = await itineraries.ReadAsync(new(request.WorldId, request.TravellerId, request.DestinationLocationId, request.GroundConveyanceId, request.AerialConveyanceId), cancellationToken);
            if (!plan.Ok) return ToolOutcome.Fail(plan.ErrorCode, plan.ErrorMessage, VerbSurface.CommitCall("itinerary-advance"), "No world state changed.");
            var current = plan.Projection!;
            if (current.Status != "ready" || current.ItineraryFingerprint != request.ItineraryFingerprint || request.NextLegIndex < 0 || request.NextLegIndex >= current.Legs.Count)
                return ToolOutcome.Fail("STALE_ITINERARY", "The supplied fingerprint or nextLegIndex does not name the current ready itinerary. Request a fresh itinerary-plan before advancing.", "query(kind: \"itinerary-plan\", worldId: \"...\", travellerId: \"...\", destinationLocationId: \"...\")", "No world state changed.");

            var leg = current.Legs[request.NextLegIndex];
            var run = await actions.RunAsync(new ActionRequest { Intent = Intent(leg.Mode), RoleEntityIds = Roles(request, leg), Input = "{}", ProceduresUsed = proceduresUsed }, cancellationToken);
            if (!run.Ok) return ToolOutcome.Fail(run.Error!.Code, run.Error.Why, run.Error.Fix, "The existing one-leg owner rejected the leg; no state changed.");

            var next = await itineraries.ReadAsync(new(request.WorldId, request.TravellerId, request.DestinationLocationId, request.GroundConveyanceId, request.AerialConveyanceId), cancellationToken);
            if (!next.Ok) return ToolOutcome.Fail(next.ErrorCode, next.ErrorMessage, "query(kind: \"itinerary-plan\", worldId: \"...\", travellerId: \"...\", destinationLocationId: \"...\")", "One leg committed, but the follow-up read failed.");
            return ToolOutcome.Ok(new { executedLeg = leg, actionOperationId = run.OperationId, appliedCount = run.AppliedCount, nextItinerary = next.Projection }, "Executed exactly one itinerary leg through its existing owner.", "Inspect the returned nextItinerary before advancing again.");
        }, consumesReadEvidence: true);

    private static string Intent(string mode) => mode switch { "on-foot" => "take the named gate-to-market route", "ground" => "take the ground conveyance to market", "air" => "take the aerial conveyance to observatory", "portal" => "use the fixed portal", _ => throw new InvalidOperationException("Unknown itinerary mode.") };
    private static IReadOnlyDictionary<string, string> Roles(ModeAwareItineraryAdvanceRequest request, ModeAwareItineraryLeg leg) => leg.Mode switch
    {
        "on-foot" => new Dictionary<string, string> { ["traveller"] = request.TravellerId, ["origin"] = leg.FromLocationId, ["destination"] = leg.ToLocationId, ["route"] = leg.RouteOrPortalId, ["world"] = request.WorldId },
        "ground" => new Dictionary<string, string> { ["driver"] = request.TravellerId, ["conveyance"] = leg.ConveyanceId!, ["origin"] = leg.FromLocationId, ["destination"] = leg.ToLocationId, ["conveyanceRoute"] = leg.RouteOrPortalId, ["world"] = request.WorldId },
        "air" => new Dictionary<string, string> { ["rider"] = request.TravellerId, ["conveyance"] = leg.ConveyanceId!, ["origin"] = leg.FromLocationId, ["destination"] = leg.ToLocationId, ["aerialRoute"] = leg.RouteOrPortalId, ["world"] = request.WorldId },
        "portal" => new Dictionary<string, string> { ["traveller"] = request.TravellerId, ["portal"] = leg.RouteOrPortalId, ["origin"] = leg.FromLocationId, ["destination"] = leg.ToLocationId, ["world"] = request.WorldId },
        _ => throw new InvalidOperationException("Unknown itinerary mode.")
    };
}

public sealed record ModeAwareItineraryAdvanceRequest(string WorldId, string TravellerId, string DestinationLocationId, string ItineraryFingerprint, int NextLegIndex, string? GroundConveyanceId = null, string? AerialConveyanceId = null);
