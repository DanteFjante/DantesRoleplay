using DantesRoleplay.Assistants;

namespace DantesRoleplay.Web.Hosting;

internal sealed class UnavailableAssistantConversationService : IAssistantConversationService
{
    public Task<AssistantProviderStatus> GetLocalStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AssistantProviderStatus(
            false, "ollama", "", "", "", "ASSISTANT_SERVICE_UNAVAILABLE",
            "The assistant conversation service is not registered in this host."));

    public Task<AssistantConversationDocument> CreateAsync(
        string operatorId, AssistantConversationCreate request, CancellationToken cancellationToken = default) =>
        Task.FromException<AssistantConversationDocument>(Unavailable());

    public Task<AssistantConversationDocument> SendAsync(
        string operatorId, string conversationId, AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AssistantConversationDocument>(Unavailable());

    public Task<AssistantConversationDocument?> GetAsync(
        string operatorId, string conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<AssistantConversationDocument?>(null);

    public Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(
        string operatorId, string provider, DateTime? beforeUpdatedAtUtc, string? beforeId, int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssistantConversationSummary>>([]);

    public Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    private static AssistantConversationException Unavailable() => new(
        "ASSISTANT_SERVICE_UNAVAILABLE", "The assistant conversation service is not registered in this host.");
}
