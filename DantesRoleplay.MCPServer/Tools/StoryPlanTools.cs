using DantesRoleplay.Operations;
using DantesRoleplay.Story;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Thin MCP adapter; planning and execution remain in the story application service.</summary>
public sealed class StoryPlanTools
{
    public Task<ToolEnvelope> StartAsync(IStoryPlanCoordinator coordinator, IOperationLog log, StoryPlanStartRequest request, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "commit", intent, "story-plan", proceduresUsed, async () =>
        {
            var result = await coordinator.StartAsync(request, cancellationToken);
            return Outcome(result);
        });

    public Task<ToolEnvelope> CancelAsync(IStoryPlanCoordinator coordinator, IOperationLog log, StoryPlanCancelRequest request, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "commit", intent, request.StoryPlanId, proceduresUsed, async () =>
        {
            var result = await coordinator.CancelAsync(request, cancellationToken);
            return Outcome(result);
        });

    public Task<ToolEnvelope> GetAsync(IStoryPlanCoordinator coordinator, IOperationLog log, StoryPlanQueryRequest request, CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "query", () => GetOutcomeAsync(coordinator, request, cancellationToken));

    private static async Task<ToolOutcome> GetOutcomeAsync(IStoryPlanCoordinator coordinator, StoryPlanQueryRequest request, CancellationToken cancellationToken) =>
        Outcome(await coordinator.GetAsync(request, cancellationToken));

    private static ToolOutcome Outcome(StoryPlanResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StopCode) && string.IsNullOrEmpty(result.StoryPlanId))
            return ToolOutcome.Fail(result.StopCode, result.StopMessage, "query(kind: \"capabilities\")", "Story-plan request was rejected.");
        var next = StoryPlanStatus.IsTerminal(result.Status)
            ? "query(kind: \"procedures\", id: \"procedure.play.storytelling\")"
            : $"query(kind: \"story-plan\", id: \"{result.StoryPlanId}\", afterRevision: {result.Revision}, waitSeconds: 20)";
        return ToolOutcome.OkAbout(result.StoryPlanId, result, "Returned the current durable story-plan state.", next);
    }
}
