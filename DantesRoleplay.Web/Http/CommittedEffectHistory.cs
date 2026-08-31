using System.Globalization;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Events;
using DantesRoleplay.Operations;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

/// <summary>
/// Read-only control-center projection over the accepted event ledger. It deliberately owns no
/// persistence and never rebuilds a past world state from the current one.
/// </summary>
public sealed class CommittedEffectHistory(IEventLedger events, IOperationLog operations)
{
    public const int DefaultLimit = 25;
    public const int MaximumLimit = 100;
    public const int MaximumFilterLength = 160;
    public const int MaximumDetailJsonLength = 64 * 1024;

    private readonly IEventLedger _events = events;
    private readonly IOperationLog _operations = operations;

    public async Task<CommittedEffectHistoryPage> ListAsync(
        string? type,
        string? entityId,
        string? rootOperationId,
        string? cursor,
        string? limit,
        CancellationToken cancellationToken = default)
    {
        var normalisedType = Filter(type, "type");
        var normalisedEntity = Filter(entityId, "entityId");
        var normalisedRoot = Filter(rootOperationId, "rootOperationId");
        var requestedLimit = Limit(limit);
        var before = await DecodeCursorAsync(cursor, cancellationToken);
        var page = await _events.ListRecentAsync(
            new EventHistoryQuery(normalisedType, normalisedEntity, normalisedRoot, before, requestedLimit),
            cancellationToken);

        return new CommittedEffectHistoryPage(
            page.Events
                .GroupBy(@event => @event.RootOperationId, StringComparer.Ordinal)
                .Select(group => new CommittedEffectGroup(
                    group.Key,
                    group.First().CorrelationId,
                    group.Select(Summary).ToList()))
                .ToList(),
            page.NextCursor is null ? null : EncodeCursor(page.NextCursor));
    }

    public async Task<CommittedEffectHistoryDetail?> GetAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > MaximumFilterLength)
        {
            throw new CommittedEffectHistoryException(
                "INVALID_EVENT_ID",
                "Event IDs must be between 1 and 160 characters.");
        }

        var detail = await _events.GetAsync(eventId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        EnsureDetailBound(detail.PayloadJson, "EVENT_PAYLOAD_TOO_LARGE", "event payload");
        var operation = string.IsNullOrWhiteSpace(detail.RootOperationId)
            ? null
            : await _operations.GetAsync(detail.RootOperationId, cancellationToken);
        if (operation is not null)
        {
            EnsureDetailBound(operation.GuardEvidenceJson, "GUARD_EVIDENCE_TOO_LARGE", "guard evidence");
        }

        return new CommittedEffectHistoryDetail(
            new CommittedEffectDetail(
                detail.Id,
                detail.TypeId,
                detail.TypeVersion,
                detail.Scope,
                detail.PayloadJson,
                detail.Timestamp,
                detail.CorrelationId,
                detail.CausationId,
                detail.Depth,
                detail.Sequence,
                detail.RootOperationId,
                detail.EntityIds,
                detail.ProducerExecutionId),
            operation is null ? null : OperationContext(operation));
    }

    private async Task<EventHistoryCursor?> DecodeCursorAsync(
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        if (cursor.Length > 1024)
        {
            throw new CommittedEffectHistoryException("INVALID_CURSOR", "The effect-history cursor is invalid.");
        }

        EventCursorToken token;
        try
        {
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            token = JsonSerializer.Deserialize<EventCursorToken>(Convert.FromBase64String(encoded))
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new CommittedEffectHistoryException("INVALID_CURSOR", "The effect-history cursor is invalid.");
        }

        if (token.TimestampTicks <= 0 || token.Sequence < 0 || string.IsNullOrWhiteSpace(token.Id) ||
            token.Id.Length > MaximumFilterLength)
        {
            throw new CommittedEffectHistoryException("INVALID_CURSOR", "The effect-history cursor is invalid.");
        }

        EventDetail? source;
        try
        {
            source = await _events.GetAsync(token.Id, cancellationToken);
        }
        catch (ArgumentException)
        {
            source = null;
        }
        if (source is null || source.Timestamp.Ticks != token.TimestampTicks || source.Sequence != token.Sequence)
        {
            throw new CommittedEffectHistoryException("STALE_CURSOR", "The effect-history cursor no longer identifies an accepted event. Restart the list.");
        }

        return new EventHistoryCursor(source.Timestamp, source.Sequence, source.Id);
    }

    private static string EncodeCursor(EventHistoryCursor cursor)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new EventCursorToken(cursor.Timestamp.Ticks, cursor.Sequence, cursor.Id));
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static CommittedEffectSummary Summary(EventSummary @event) => new(
        @event.Id,
        @event.TypeId,
        @event.TypeVersion,
        @event.Scope,
        @event.Timestamp,
        @event.CorrelationId,
        @event.CausationId,
        @event.Depth,
        @event.Sequence,
        @event.RootOperationId,
        @event.EntityIds);

    private static CommittedOperationContext OperationContext(Operation operation) => new(
        operation.Id,
        operation.Timestamp,
        operation.Tool,
        operation.Subject,
        operation.Intent,
        operation.ProceduresCited,
        operation.ProceduresRead,
        operation.ConsumedReadEvidence,
        operation.Summary,
        operation.Success,
        operation.Error,
        operation.MechanicId,
        operation.MechanicVersion,
        operation.Seed,
        operation.GuardEvidenceJson);

    private static string? Filter(string? value, string name)
    {
        if (value is null) return null;
        var normalised = value.Trim();
        if (normalised.Length == 0 || normalised.Length > MaximumFilterLength)
        {
            throw new CommittedEffectHistoryException(
                "INVALID_FILTER",
                $"{name} must be between 1 and {MaximumFilterLength} characters.");
        }
        return normalised;
    }

    private static int Limit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultLimit;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ||
            limit < 1 || limit > MaximumLimit)
        {
            throw new CommittedEffectHistoryException(
                "INVALID_LIMIT",
                $"limit must be an integer from 1 through {MaximumLimit}.");
        }
        return limit;
    }

    private static void EnsureDetailBound(string value, string code, string label)
    {
        if (Encoding.UTF8.GetByteCount(value) > MaximumDetailJsonLength)
        {
            throw new CommittedEffectHistoryException(
                code,
                $"The selected {label} exceeds the {MaximumDetailJsonLength} byte control-center limit.",
                StatusCodes.Status413PayloadTooLarge);
        }
    }

    private sealed record EventCursorToken(long TimestampTicks, int Sequence, string Id);
}

public sealed class CommittedEffectHistoryException(
    string code,
    string message,
    int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record CommittedEffectHistoryPage(
    IReadOnlyList<CommittedEffectGroup> Groups,
    string? NextCursor);

public sealed record CommittedEffectGroup(
    string RootOperationId,
    string CorrelationId,
    IReadOnlyList<CommittedEffectSummary> Events);

public sealed record CommittedEffectSummary(
    string Id,
    string TypeId,
    int TypeVersion,
    string Scope,
    DateTime Timestamp,
    string CorrelationId,
    string CausationId,
    int Depth,
    int Sequence,
    string RootOperationId,
    IReadOnlyList<string> EntityIds);

public sealed record CommittedEffectHistoryDetail(
    CommittedEffectDetail Event,
    CommittedOperationContext? Operation);

public sealed record CommittedEffectDetail(
    string Id,
    string TypeId,
    int TypeVersion,
    string Scope,
    string PayloadJson,
    DateTime Timestamp,
    string CorrelationId,
    string CausationId,
    int Depth,
    int Sequence,
    string RootOperationId,
    IReadOnlyList<string> EntityIds,
    string ProducerExecutionId);

public sealed record CommittedOperationContext(
    string Id,
    DateTime Timestamp,
    string Tool,
    string Subject,
    string Intent,
    string ProceduresCited,
    string ProceduresRead,
    bool ConsumedReadEvidence,
    string Summary,
    bool Success,
    string Error,
    string MechanicId,
    int? MechanicVersion,
    long? Seed,
    string GuardEvidenceJson);
