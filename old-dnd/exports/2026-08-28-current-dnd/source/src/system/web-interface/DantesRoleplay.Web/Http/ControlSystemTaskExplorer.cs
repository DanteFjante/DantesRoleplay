using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemTasks;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

public sealed class ControlSystemTaskExplorer(ISystemTaskService tasks)
{
    private const int MaximumBodyBytes = 640 * 1024;
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<SystemTaskPage> ListAsync(AuthorizationAuditEvidence authorization,
        string conversationId, string? cursor, string? limit, CancellationToken cancellationToken)
    {
        var count = ParseLimit(limit);
        var (before, beforeId) = DecodeCursor(cursor);
        try
        {
            var rows = await tasks.ListAsync(SystemTaskRequestContext.FromAuthorization(authorization),
                conversationId, before, beforeId, count + 1, cancellationToken);
            var more = rows.Count > count;
            var page = rows.Take(count).ToArray();
            return new(page, more ? EncodeCursor(page[^1]) : null);
        }
        catch (SystemTaskException exception) { throw Map(exception); }
    }

    public async Task<SystemTaskDocument?> GetAsync(AuthorizationAuditEvidence authorization,
        string taskId, CancellationToken cancellationToken)
    {
        try { return await tasks.GetAsync(SystemTaskRequestContext.FromAuthorization(authorization), taskId, cancellationToken); }
        catch (SystemTaskException exception) { throw Map(exception); }
    }

    public async Task<SystemTaskDocument> PrepareAsync(AuthorizationAuditEvidence authorization,
        string conversationId, SystemTaskPrepareRequest request, CancellationToken cancellationToken)
    {
        try { return await tasks.PrepareAsync(SystemTaskRequestContext.FromAuthorization(authorization), conversationId, request, cancellationToken); }
        catch (SystemTaskException exception) { throw Map(exception); }
    }

    public async Task<SystemTaskConfirmationDocument> ConfirmAsync(AuthorizationAuditEvidence authorization,
        string taskId, SystemTaskConfirmationRequest request, CancellationToken cancellationToken)
    {
        try { return await tasks.ConfirmAsync(SystemTaskRequestContext.FromAuthorization(authorization), taskId, request, cancellationToken); }
        catch (SystemTaskException exception) { throw Map(exception); }
    }

    public async Task<SystemTaskExecutionDocument> ExecuteAsync(AuthorizationAuditEvidence authorization,
        string taskId, SystemTaskExecutionRequest request, CancellationToken cancellationToken)
    {
        try { return await tasks.ExecuteAsync(SystemTaskRequestContext.FromAuthorization(authorization), taskId, request, cancellationToken); }
        catch (SystemTaskException exception) { throw Map(exception); }
    }

    public static async Task<T> ReadBodyAsync<T>(HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ContentLength > MaximumBodyBytes)
            throw Error("SYSTEM_TASK_BODY_TOO_LARGE", "The system task request exceeds 640 KiB.", 413);
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumBodyBytes)
                throw Error("SYSTEM_TASK_BODY_TOO_LARGE", "The system task request exceeds 640 KiB.", 413);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0)
            throw Error("SYSTEM_TASK_BODY_INVALID", "A JSON system task request is required.", 400);
        try { return JsonSerializer.Deserialize<T>(buffer.ToArray(), RequestJson) ?? throw new JsonException(); }
        catch (JsonException)
        { throw Error("SYSTEM_TASK_BODY_INVALID", "The system task request body is invalid.", 400); }
    }

    private static int ParseLimit(string? value) => string.IsNullOrWhiteSpace(value) ? 25 :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 100
            ? parsed : throw Error("SYSTEM_TASK_LIMIT_INVALID", "limit must be from 1 through 100.", 400);

    private static (DateTime? Before, string? Id) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        if (cursor.Length > 1024) throw Error("SYSTEM_TASK_CURSOR_INVALID", "The task cursor is invalid.", 400);
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = text.IndexOf('|');
            if (separator < 1 || !long.TryParse(text[..separator], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var ticks)) throw new FormatException();
            var id = text[(separator + 1)..];
            if (id.Length != 44) throw new FormatException();
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        { throw Error("SYSTEM_TASK_CURSOR_INVALID", "The task cursor is invalid.", 400); }
    }

    private static string EncodeCursor(SystemTaskSummary item) => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            item.CreatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + item.Id))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ControlAssistantException Map(SystemTaskException exception)
    {
        var status = exception.Code switch
        {
            "SYSTEM_TASK_UNKNOWN" or "SYSTEM_TASK_CONFIRMATION_UNKNOWN" or
            "ASSISTANT_CONVERSATION_UNKNOWN" => 404,
            "SYSTEM_TASK_IDEMPOTENCY_CONFLICT" or "SYSTEM_TASK_PLAN_STALE" or
            "SYSTEM_TASK_CONFIRMATION_EXPIRED" or "SYSTEM_TASK_CONFIRMATION_USED" or
            "SYSTEM_TASK_EXECUTION_ACTIVE" or "SYSTEM_TASK_ALREADY_EXECUTED" => 409,
            "SYSTEM_TASK_UNAVAILABLE" or "SYSTEM_TASK_MODEL_UNAVAILABLE" or
            "SYSTEM_TASK_CAPABILITIES_UNAVAILABLE" => 503,
            "PRIVATE_OPERATOR_UNAUTHENTICATED" or "PRIVATE_OPERATOR_WRONG_SCOPE" or
            "PRIVATE_OPERATOR_DENIED" => 403,
            _ => 400
        };
        return Error(exception.Code, exception.Message, status);
    }

    private static ControlAssistantException Error(string code, string message, int status) => new(code, message, status);
}
