using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;

namespace DantesRoleplay.Web.Hosting;

internal sealed class UnavailableCodexConversationService : ICodexConversationService
{
    public Task<CodexBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new CodexBridgeStatus(
            false, "codex", "", "", "", "read-only", false,
            "CODEX_SERVICE_UNAVAILABLE", "The Codex bridge is not registered in this host."));

    public async IAsyncEnumerable<CodexConversationEvent> CreateAsync(
        string operatorId, AssistantConversationCreate request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw Unavailable();
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public async IAsyncEnumerable<CodexConversationEvent> SendAsync(
        string operatorId, string conversationId, AssistantConversationTurnCreate request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw Unavailable();
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public Task<CodexCancelResult> CancelAsync(
        string operatorId, string conversationId, string turnId,
        CancellationToken cancellationToken = default) => Task.FromException<CodexCancelResult>(Unavailable());

    public Task<CodexApprovalResult> ApproveAsync(
        string operatorId, string conversationId, string turnId, string approvalId,
        CodexApprovalDecisionInput request, CancellationToken cancellationToken = default) =>
        Task.FromException<CodexApprovalResult>(Unavailable());

    private static CodexBridgeException Unavailable() => new(
        "CODEX_SERVICE_UNAVAILABLE", "The Codex bridge is not registered in this host.");
}
