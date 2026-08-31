using DantesRoleplay.Information;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Protocol adapters for generic, non-game information.</summary>
public sealed class InformationHandler
{
    public Task<ToolEnvelope> AnswerAsync(IInformationAnswerCoordinator coordinator, IOperationLog log, InformationAnswerRequest request, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", request.Question, request.ScopeId, ["procedure.information.answer"], async () =>
        {
            var answer = await coordinator.AnswerAsync(request, cancellationToken);
            return answer.Status == "denied"
                ? ToolOutcome.Fail(answer.ErrorCode, answer.ErrorMessage, "query(kind: \"information-answer\", scopeId: \"local.default\", question: \"...\")", "Information scope access was denied.")
                : ToolOutcome.OkAbout(request.ScopeId, answer, "Returned a bounded answer from the authorized information scope.", "query(kind: \"information-answer\", scopeId: \"...\", question: \"...\")");
        }, consumesReadEvidence: false);

    public Task<ToolEnvelope> WriteSourceAsync(IInformationStore store, IOperationLog log, InformationSourceWriteRequest request, string intent, string[]? proceduresUsed, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", intent, request.Id, proceduresUsed, async () => Outcome(await store.WriteSourceAsync(request, cancellationToken), request.Id, "information-source"));

    public Task<ToolEnvelope> WriteRecordAsync(IInformationStore store, IOperationLog log, InformationRecordWriteRequest request, string intent, string[]? proceduresUsed, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", intent, request.Id, proceduresUsed, async () => Outcome(await store.WriteRecordAsync(request, cancellationToken), request.Id, "information-record"));

    public Task<ToolEnvelope> WriteActionContractAsync(IInformationStore store, IOperationLog log, InformationActionContractWriteRequest request, string intent, string[]? proceduresUsed, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", intent, request.Id, proceduresUsed, async () => Outcome(await store.WriteActionContractAsync(request, cancellationToken), request.Id, "information-action-contract"));

    public Task<ToolEnvelope> ListActionsAsync(IInformationActionCoordinator coordinator, IOperationLog log, string scopeSelector, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", scopeSelector, scopeSelector, ["procedure.information.action"], async () =>
        {
            var contracts = await coordinator.ListAsync(scopeSelector, cancellationToken);
            return contracts.Count == 0
                ? ToolOutcome.OkAbout(scopeSelector, contracts, "No authorized action contracts were available in this namespace.", "commit(kind: \"information-action-contract\", payload: \"...\")")
                : ToolOutcome.OkAbout(scopeSelector, contracts, "Returned explicit action contracts in the authorized namespace.", "commit(kind: \"information-action\", payload: \"...\")");
        }, consumesReadEvidence: false);

    public Task<ToolEnvelope> ExecuteActionAsync(IInformationActionCoordinator coordinator, IOperationLog log, InformationActionExecutionRequest request, string intent, string[]? proceduresUsed, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", intent, request.ContractId, proceduresUsed, async () =>
        {
            var result = await coordinator.ExecuteAsync(request, cancellationToken);
            return result.ErrorCode.Length > 0
                ? ToolOutcome.Fail(result.ErrorCode, result.ErrorMessage, McpVerbCatalog.CommitCall("information-action"), "Rejected information action execution.")
                : ToolOutcome.OkAbout(request.ContractId, result.Result, "Executed the declared information action contract.", "query(kind: \"information-actions\", scopeId: \"...\")");
        });

    private static ToolOutcome Outcome(InformationSourceWriteResult result, string id, string kind) => result.ErrorCode.Length > 0
        ? ToolOutcome.Fail(result.ErrorCode, result.ErrorMessage, McpVerbCatalog.CommitCall(kind), $"Rejected {kind} write.")
        : ToolOutcome.OkAbout(id, result.Source, $"Information source {result.Status}.", "query(kind: \"information-answer\", scopeId: \"...\", question: \"...\")");
    private static ToolOutcome Outcome(InformationRecordWriteResult result, string id, string kind) => result.ErrorCode.Length > 0
        ? ToolOutcome.Fail(result.ErrorCode, result.ErrorMessage, McpVerbCatalog.CommitCall(kind), $"Rejected {kind} write.")
        : ToolOutcome.OkAbout(id, result.Record, $"Information record {result.Status}.", "query(kind: \"information-answer\", scopeId: \"...\", question: \"...\")");
    private static ToolOutcome Outcome(InformationActionContractWriteResult result, string id, string kind) => result.ErrorCode.Length > 0
        ? ToolOutcome.Fail(result.ErrorCode, result.ErrorMessage, McpVerbCatalog.CommitCall(kind), $"Rejected {kind} write.")
        : ToolOutcome.OkAbout(id, result.Contract, $"Information action contract {result.Status}.", "query(kind: \"information-actions\", scopeId: \"...\")");
}
