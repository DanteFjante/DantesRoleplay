using DantesRoleplay.Assistants;
using DantesRoleplay.Authorization;
using System.Security.Cryptography;
using System.Text;

namespace DantesRoleplay.SystemConversations;

public sealed record SystemConversationCreate(string Message, string IdempotencyKey);

/**
 * The durable identity recipe for a read-only system turn. Keeping it here
 * lets the persistence compatibility check recognize only historic system
 * requests, without treating an omitted context as permission to replay a
 * separately captured request.
 */
public static class SystemConversationRequestIdentity
{
    public const string Provider = "local";

    public static string Hash(string normalizedMessage) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            AssistantConversationScopes.System + "\0" + Provider + "\0" + normalizedMessage)));

    public static bool IsMaterializedReference(string? value) => value is not null &&
        (value.StartsWith("capability:", StringComparison.Ordinal) ||
         value.StartsWith("procedure:", StringComparison.Ordinal) ||
         value.StartsWith("application:", StringComparison.Ordinal));
}

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

    Task<AssistantTurnRecovery?> RecoverAsync(
        SystemConversationRequestContext context, string idempotencyKey,
        CancellationToken cancellationToken = default) => Task.FromResult<AssistantTurnRecovery?>(null);
}

public sealed class SystemConversationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
