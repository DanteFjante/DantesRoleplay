using DantesRoleplay.Assistants;
using DantesRoleplay.SystemConversations;

namespace DantesRoleplay.Web.Hosting;

internal sealed class UnavailableSystemConversationService : ISystemConversationService
{
    public Task<AssistantProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AssistantProviderStatus(
            false, "ollama", "", "", "", "SYSTEM_CHAT_UNAVAILABLE",
            "The system conversation service is not registered in this host."));

    public Task<AssistantConversationDocument> CreateAsync(
        SystemConversationRequestContext context, SystemConversationCreate request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<AssistantConversationDocument>(Unavailable());

    public Task<AssistantConversationDocument> SendAsync(
        SystemConversationRequestContext context, string conversationId,
        AssistantConversationTurnCreate request, CancellationToken cancellationToken = default) =>
        Task.FromException<AssistantConversationDocument>(Unavailable());

    public Task<AssistantConversationDocument?> GetAsync(
        SystemConversationRequestContext context, string conversationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AssistantConversationDocument?>(null);

    public Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(
        SystemConversationRequestContext context, DateTime? beforeUpdatedAtUtc, string? beforeId,
        int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssistantConversationSummary>>([]);

    private static SystemConversationException Unavailable() => new(
        "SYSTEM_CHAT_UNAVAILABLE", "The system conversation service is not registered in this host.");
}
