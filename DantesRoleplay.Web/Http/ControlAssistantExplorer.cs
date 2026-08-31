using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Assistants;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

public sealed class ControlAssistantException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class ControlAssistantExplorer(IAssistantConversationService conversations)
{
    private const int MaximumBodyBytes = 16 * 1024;
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public Task<AssistantProviderStatus> StatusAsync(CancellationToken cancellationToken = default) =>
        conversations.GetLocalStatusAsync(cancellationToken);

    public async Task<AssistantConversationPage> ListAsync(
        string operatorId, string? provider, string? cursor, string? limit,
        CancellationToken cancellationToken = default)
    {
        var providerName = string.IsNullOrWhiteSpace(provider) ? "local" : provider.Trim();
        var count = ParseLimit(limit);
        var (before, beforeId) = DecodeCursor(cursor);
        try
        {
            var rows = await conversations.ListAsync(
                operatorId, providerName, before, beforeId, count + 1, cancellationToken);
            var more = rows.Count > count;
            var page = rows.Take(count).ToArray();
            return new(page, more ? EncodeCursor(page[^1]) : null);
        }
        catch (AssistantConversationException exception) { throw Map(exception); }
    }

    public async Task<AssistantConversationDocument?> GetAsync(
        string operatorId, string conversationId, CancellationToken cancellationToken = default)
    {
        try { return await conversations.GetAsync(operatorId, conversationId, cancellationToken); }
        catch (AssistantConversationException exception) { throw Map(exception); }
    }

    public async Task<AssistantConversationDocument> CreateAsync(
        string operatorId, AssistantConversationCreate request, CancellationToken cancellationToken = default)
    {
        try { return await conversations.CreateAsync(operatorId, request, cancellationToken); }
        catch (AssistantConversationException exception) { throw Map(exception); }
    }

    public async Task<AssistantConversationDocument> SendAsync(
        string operatorId, string conversationId, AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default)
    {
        try { return await conversations.SendAsync(operatorId, conversationId, request, cancellationToken); }
        catch (AssistantConversationException exception) { throw Map(exception); }
    }

    public static async Task<T> ReadBodyAsync<T>(HttpRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ContentLength > MaximumBodyBytes) throw Error(
            "ASSISTANT_BODY_TOO_LARGE", "The assistant request exceeds 16 KiB.", StatusCodes.Status413PayloadTooLarge);
        await using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumBodyBytes) throw Error(
                "ASSISTANT_BODY_TOO_LARGE", "The assistant request exceeds 16 KiB.", StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0) throw Error("ASSISTANT_BODY_INVALID", "A JSON request body is required.", 400);
        try { return JsonSerializer.Deserialize<T>(buffer.ToArray(), RequestJson) ?? throw new JsonException(); }
        catch (JsonException) { throw Error("ASSISTANT_BODY_INVALID", "The assistant request body is invalid.", 400); }
    }

    private static int ParseLimit(string? value) => string.IsNullOrWhiteSpace(value) ? 25 :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 100
            ? parsed : throw Error("ASSISTANT_LIMIT_INVALID", "limit must be an integer from 1 through 100.", 400);

    private static (DateTime? Before, string? Id) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = text.IndexOf('|');
            if (separator < 1 || !long.TryParse(text[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                throw new FormatException();
            var id = text[(separator + 1)..];
            if (id.Length != 45) throw new FormatException();
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        { throw Error("ASSISTANT_CURSOR_INVALID", "The conversation cursor is invalid.", 400); }
    }

    private static string EncodeCursor(AssistantConversationSummary item) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            item.UpdatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + item.Id))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ControlAssistantException Map(AssistantConversationException exception)
    {
        var status = exception.Code switch
        {
            "ASSISTANT_CONVERSATION_UNKNOWN" => 404,
            "ASSISTANT_IDEMPOTENCY_CONFLICT" or "ASSISTANT_REVISION_STALE" or "ASSISTANT_TURN_ACTIVE" => 409,
            "ASSISTANT_SERVICE_UNAVAILABLE" => 503,
            _ => 400
        };
        return Error(exception.Code, exception.Message, status);
    }
    private static ControlAssistantException Error(string code, string message, int status) => new(code, message, status);
}
