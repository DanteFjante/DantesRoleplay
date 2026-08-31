using System.Globalization;
using System.Text;
using DantesRoleplay.Assistants;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemConversations;

namespace DantesRoleplay.Web.Hosting;

public sealed class ControlSystemConversationExplorer(ISystemConversationService conversations)
{
    public async Task<SystemConversationPage> ListAsync(
        AuthorizationAuditEvidence authorization,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken = default)
    {
        var count = ParseLimit(limit);
        var (before, beforeId) = DecodeCursor(cursor);
        try
        {
            var rows = await conversations.ListAsync(
                SystemConversationRequestContext.FromAuthorization(authorization),
                before, beforeId, count + 1, cancellationToken);
            var more = rows.Count > count;
            var page = rows.Take(count).ToArray();
            return new(page, more ? EncodeCursor(page[^1]) : null);
        }
        catch (SystemConversationException exception) { throw Map(exception); }
    }

    public async Task<AssistantConversationDocument?> GetAsync(
        AuthorizationAuditEvidence authorization,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await conversations.GetAsync(
                SystemConversationRequestContext.FromAuthorization(authorization),
                conversationId, cancellationToken);
        }
        catch (SystemConversationException exception) { throw Map(exception); }
    }

    public async Task<AssistantConversationDocument> CreateAsync(
        AuthorizationAuditEvidence authorization,
        SystemConversationCreate request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await conversations.CreateAsync(
                SystemConversationRequestContext.FromAuthorization(authorization),
                request, cancellationToken);
        }
        catch (SystemConversationException exception) { throw Map(exception); }
    }

    public async Task<AssistantConversationDocument> SendAsync(
        AuthorizationAuditEvidence authorization,
        string conversationId,
        AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await conversations.SendAsync(
                SystemConversationRequestContext.FromAuthorization(authorization),
                conversationId, request, cancellationToken);
        }
        catch (SystemConversationException exception) { throw Map(exception); }
    }

    private static int ParseLimit(string? value) => string.IsNullOrWhiteSpace(value) ? 25 :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
        parsed is >= 1 and <= 100
            ? parsed
            : throw Error("SYSTEM_CHAT_LIMIT_INVALID", "limit must be an integer from 1 through 100.", 400);

    private static (DateTime? Before, string? Id) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        if (cursor.Length > 1024)
            throw Error("SYSTEM_CHAT_CURSOR_INVALID", "The conversation cursor is invalid.", 400);
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = text.IndexOf('|');
            if (separator < 1 || !long.TryParse(text[..separator], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var ticks))
                throw new FormatException();
            var id = text[(separator + 1)..];
            if (id.Length != 45) throw new FormatException();
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw Error("SYSTEM_CHAT_CURSOR_INVALID", "The conversation cursor is invalid.", 400);
        }
    }

    private static string EncodeCursor(AssistantConversationSummary item) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            item.UpdatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + item.Id))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ControlAssistantException Map(SystemConversationException exception)
    {
        var status = exception.Code switch
        {
            "ASSISTANT_CONVERSATION_UNKNOWN" => 404,
            "ASSISTANT_IDEMPOTENCY_CONFLICT" or "ASSISTANT_REVISION_STALE" or
            "ASSISTANT_TURN_ACTIVE" => 409,
            "SYSTEM_CHAT_CONTEXT_UNAVAILABLE" or "SYSTEM_CHAT_CONTEXT_TOO_LARGE" or
            "SYSTEM_CHAT_UNAVAILABLE" => 503,
            "PRIVATE_OPERATOR_UNAUTHENTICATED" or "PRIVATE_OPERATOR_WRONG_SCOPE" or
            "PRIVATE_OPERATOR_DENIED" => 403,
            _ => 400
        };
        return Error(exception.Code, exception.Message, status);
    }

    private static ControlAssistantException Error(string code, string message, int status) =>
        new(code, message, status);
}
