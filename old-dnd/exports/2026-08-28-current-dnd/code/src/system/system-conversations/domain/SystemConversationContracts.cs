using DantesRoleplay.Assistants;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.SystemConversations;

public sealed record SystemConversationCreate(string Message, string IdempotencyKey);

public sealed record SystemConversationPage(
    IReadOnlyList<AssistantConversationSummary> Items,
    string? NextCursor);

public sealed record SystemConversationRequestContext(
    TrustedPrincipalContext Principal,
    string Scope,
    string CorrelationId)
{
    public static SystemConversationRequestContext FromAuthorization(AuthorizationAuditEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var principal = evidence.Allowed &&
            TrustedPrincipalContext.IsValidPrincipalId(evidence.PrincipalReference) &&
            Bounded(evidence.AuthenticationMethod, 64)
                ? TrustedPrincipalContext.VerifiedPrincipal(
                    evidence.PrincipalReference, evidence.AuthenticationMethod)
                : TrustedPrincipalContext.Unauthenticated(
                    Bounded(evidence.ReasonCode, 80)
                        ? evidence.ReasonCode
                        : "PRIVATE_OPERATOR_UNAUTHENTICATED");
        return new(
            principal,
            Bounded(evidence.Scope, 80) ? evidence.Scope : "invalid",
            Bounded(evidence.CorrelationId, 128) ? evidence.CorrelationId : "invalid");
    }

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
}

public sealed record SystemConversationContextSnapshot(
    string Profile,
    string Json,
    string Fingerprint,
    IReadOnlyList<string> SourceReferences);

public interface ISystemConversationContextMaterializer
{
    Task<SystemConversationContextSnapshot> MaterializeAsync(
        string query,
        SystemConversationRequestContext context,
        CancellationToken cancellationToken = default);
}

public interface ISystemConversationService
{
    Task<AssistantProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<AssistantConversationDocument> CreateAsync(
        SystemConversationRequestContext context,
        SystemConversationCreate request,
        CancellationToken cancellationToken = default);

    Task<AssistantConversationDocument> SendAsync(
        SystemConversationRequestContext context,
        string conversationId,
        AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default);

    Task<AssistantConversationDocument?> GetAsync(
        SystemConversationRequestContext context,
        string conversationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(
        SystemConversationRequestContext context,
        DateTime? beforeUpdatedAtUtc,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class SystemConversationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
