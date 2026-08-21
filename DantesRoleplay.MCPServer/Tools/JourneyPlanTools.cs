using DantesRoleplay.Operations;
using DantesRoleplay.World;

namespace DantesRoleplay.MCPServer.Tools;

public sealed class JourneyPlanTools
{
    public Task<ToolEnvelope> GetAsync(IJourneyPlanReader plans, IOperationLog log, JourneyPlanQuery query, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "get_journey_plan", async () =>
        {
            var result = await plans.ReadAsync(query, cancellationToken);
            if (!result.Ok) return ToolOutcome.Fail(result.ErrorCode, result.ErrorMessage, "query(kind: \"journey-plan\", worldId: \"...\", travellerId: \"...\", destinationId: \"...\")", "Journey planning is read-only; no world state changed.");
            var plan = result.Projection!;
            return ToolOutcome.Ok(plan, $"Journey plan is {plan.Status} with {plan.Legs.Count} leg(s) and {plan.TotalDurationMinutes} minute(s).", "Execute only the first ready leg through the existing on-foot route action, then request a fresh journey-plan.");
        });
}
