using DantesRoleplay.Quest;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

public sealed class QuestTools
{
    public async Task<ToolEnvelope> CreateAsync(
        IQuestCreator creator,
        QuestCreateRequest request,
        string intent,
        IReadOnlyList<string>? proceduresUsed,
        CancellationToken cancellationToken)
    {
        var result = await creator.CreateAsync(request, intent, proceduresUsed, cancellationToken);
        return result.Created
            ? ToolEnvelope.Success(result, result.OperationId, $"query(kind: \"entities\", id: \"{result.QuestId}\")")
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, VerbSurface.CommitCall("quest"), result.OperationId);
    }

    public async Task<ToolEnvelope> TransitionAsync(
        IQuestLifecycleRunner runner,
        QuestLifecycleRequest request,
        string intent,
        IReadOnlyList<string>? proceduresUsed,
        CancellationToken cancellationToken)
    {
        var result = await runner.TransitionAsync(request, intent, proceduresUsed, cancellationToken);
        return result.Succeeded
            ? ToolEnvelope.Success(result, result.OperationId, $"query(kind: \"entities\", id: \"{result.QuestId}\")")
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, VerbSurface.CommitCall("quest"), result.OperationId);
    }

    public async Task<ToolEnvelope> TransitionObjectiveAsync(
        IQuestLifecycleRunner runner,
        QuestObjectiveTransitionRequest request,
        string intent,
        IReadOnlyList<string>? proceduresUsed,
        CancellationToken cancellationToken)
    {
        var result = await runner.TransitionObjectiveAsync(request, intent, proceduresUsed, cancellationToken);
        return result.Succeeded
            ? ToolEnvelope.Success(result, result.OperationId, $"query(kind: \"entities\", id: \"{result.QuestId}\")")
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, VerbSurface.CommitCall("quest"), result.OperationId);
    }

    public Task<ToolEnvelope> SummaryAsync(
        IQuestSummaryReader reader,
        IOperationLog log,
        string questId,
        CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", "", questId, ["procedure.quest.inspect"], async () =>
        {
            var result = await reader.GetAsync(questId, cancellationToken);
            return result is null
                ? ToolOutcome.Fail("QUEST_SUMMARY_UNAVAILABLE", "questId does not name a readable active quest.", "query(kind: \"entities\", id: \"...\")", "Quest summary was unavailable.")
                : ToolOutcome.OkAbout(questId, result, "Returned trusted-host quest summary.", $"query(kind: \"quest-summary\", id: \"{questId}\")");
        }, consumesReadEvidence: false);
}
