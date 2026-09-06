using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Projections;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Live;

public enum WebChangeKind { Invalidate, ObjectChange, Cursor, PageRevision, KeepAlive }

public sealed record WebChange(
    WebChangeKind Kind, string Reason, long DatabaseVersion, string? PageId = null,
    int? PageRevision = null, long? Cursor = null, string? ApplicationId = null,
    string? StateSpaceId = null, string? Scope = null, string? ObjectQualifiedId = null,
    int? ObjectVersion = null);

public sealed record WebChangeSubscription(
    string ApplicationId, string StateSpaceId, string Perspective, long? AfterCursor = null)
{
    public void Validate()
    {
        Bounded(ApplicationId, 63, nameof(ApplicationId));
        Bounded(StateSpaceId, 200, nameof(StateSpaceId));
        if (Perspective is not ("player" or "dm"))
            throw new ArgumentException("The change perspective is invalid.", nameof(Perspective));
        if (AfterCursor is < 0) throw new ArgumentOutOfRangeException(nameof(AfterCursor));
    }

    private static void Bounded(string value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim()
            || value.Any(char.IsControl))
            throw new ArgumentException("The change scope is invalid.", name);
    }
}

/// <summary>Host boundary that binds a requested stream scope to its ambient authorized seat.</summary>
public interface IWebChangeScopeAuthorizer
{
    Task<bool> AuthorizeAsync(
        WebChangeSubscription subscription,
        CancellationToken cancellationToken = default);
}

internal sealed class UnavailableWebChangeScopeAuthorizer : IWebChangeScopeAuthorizer
{
    public Task<bool> AuthorizeAsync(WebChangeSubscription subscription,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
}

public sealed class SqliteWebChangeFeed(WebContentDbContext db)
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(15);

    // Compatibility surface for system pages and callers without an application scope.
    public IAsyncEnumerable<WebChange> WatchAsync(
        string? pageId = null, TimeSpan? pollInterval = null, TimeSpan? keepAliveInterval = null,
        CancellationToken cancellationToken = default) =>
        WatchCoreAsync(pageId, null, pollInterval, keepAliveInterval, cancellationToken);

    public IAsyncEnumerable<WebChange> WatchAsync(
        WebChangeSubscription subscription, string? pageId = null, TimeSpan? pollInterval = null,
        TimeSpan? keepAliveInterval = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        subscription.Validate();
        return WatchCoreAsync(pageId, subscription, pollInterval, keepAliveInterval, cancellationToken);
    }

    private async IAsyncEnumerable<WebChange> WatchCoreAsync(
        string? pageId, WebChangeSubscription? subscription, TimeSpan? pollInterval,
        TimeSpan? keepAliveInterval, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (pageId is not null && !WebPageId.IsValid(pageId))
            throw new ArgumentException("Page ID is invalid.", nameof(pageId));
        var poll = ValidateInterval(pollInterval ?? DefaultPollInterval, nameof(pollInterval));
        var keepAlive = ValidateInterval(keepAliveInterval ?? DefaultKeepAliveInterval, nameof(keepAliveInterval));

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var version = await ReadDataVersionAsync(cancellationToken);
            var pageRevision = await ReadPageRevisionAsync(pageId, cancellationToken);
            var durableDelivery = subscription is not null && await ChangeTableExistsAsync(cancellationToken);
            var bounds = durableDelivery ? await ReadCursorBoundsAsync(cancellationToken) : default;
            var cursor = subscription?.AfterCursor ?? bounds.Maximum;
            var lastWrite = Stopwatch.GetTimestamp();

            yield return Scoped(WebChangeKind.Invalidate, "connected", version, pageId, pageRevision,
                subscription, cursor);
            if (durableDelivery && subscription!.AfterCursor is not null)
            {
                var replay = await ReplayAsync(subscription, bounds, cursor, version, pageId, pageRevision,
                    cancellationToken);
                cursor = replay.Cursor;
                foreach (var change in replay.Changes) yield return change;
            }

            using var timer = new PeriodicTimer(poll);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var currentVersion = await ReadDataVersionAsync(cancellationToken);
                if (currentVersion != version)
                {
                    version = currentVersion;
                    var currentPageRevision = await ReadPageRevisionAsync(pageId, cancellationToken);
                    if (!durableDelivery)
                    {
                        yield return new WebChange(WebChangeKind.Invalidate, "database-commit", version,
                            pageId, currentPageRevision);
                    }
                    else
                    {
                        var currentBounds = await ReadCursorBoundsAsync(cancellationToken);
                        var replay = await ReplayAsync(subscription!, currentBounds, cursor, version, pageId,
                            currentPageRevision, cancellationToken);
                        cursor = replay.Cursor;
                        if (replay.Changes.Count == 0)
                            yield return Scoped(WebChangeKind.Invalidate, "untracked-commit", version, pageId,
                                currentPageRevision, subscription, cursor);
                        else
                            foreach (var change in replay.Changes) yield return change;
                    }

                    if (pageId is not null && currentPageRevision != pageRevision)
                    {
                        pageRevision = currentPageRevision;
                        yield return Scoped(WebChangeKind.PageRevision, "page-activated", version, pageId,
                            pageRevision, subscription, cursor);
                    }
                    lastWrite = Stopwatch.GetTimestamp();
                    continue;
                }

                if (Stopwatch.GetElapsedTime(lastWrite) >= keepAlive)
                {
                    yield return Scoped(WebChangeKind.KeepAlive, "keep-alive", version, pageId, pageRevision,
                        subscription, cursor);
                    lastWrite = Stopwatch.GetTimestamp();
                }
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task<ReplayResult> ReplayAsync(
        WebChangeSubscription subscription, CursorBounds bounds, long cursor, long databaseVersion,
        string? pageId, int? pageRevision, CancellationToken cancellationToken)
    {
        if (cursor > bounds.Maximum || (bounds.Minimum > 0 && cursor < bounds.Minimum - 1))
            return new(bounds.Maximum,
                [Scoped(WebChangeKind.Invalidate, "continuity-gap", databaseVersion, pageId, pageRevision,
                    subscription, bounds.Maximum)]);

        var rows = await ReadChangesAfterAsync(cursor, ApplicationObjectChangeContract.MaximumReplayRows + 1,
            cancellationToken);
        if (rows.Count > ApplicationObjectChangeContract.MaximumReplayRows)
            return new(bounds.Maximum,
                [Scoped(WebChangeKind.Invalidate, "continuity-gap", databaseVersion, pageId, pageRevision,
                    subscription, bounds.Maximum)]);
        if (rows.Count == 0) return new(cursor, []);

        var changes = new List<WebChange>();
        foreach (var row in rows)
        {
            cursor = row.Cursor;
            if (row.ApplicationId != subscription.ApplicationId || row.StateSpaceId != subscription.StateSpaceId
                || !row.ReadPerspectives.Contains(subscription.Perspective, StringComparer.Ordinal)) continue;
            changes.Add(Scoped(
                row.Scope == ApplicationObjectChangeContract.ObjectScope
                    ? WebChangeKind.ObjectChange : WebChangeKind.Invalidate,
                row.Reason, databaseVersion, pageId, pageRevision, subscription, row.Cursor,
                row.ObjectQualifiedId, row.ObjectVersion, row.Scope));
        }

        // Advance Last-Event-ID past other audiences without exposing their object identities.
        if (changes.Count == 0 || changes[^1].Cursor != cursor)
            changes.Add(Scoped(WebChangeKind.Cursor, "cursor-advanced", databaseVersion, pageId, pageRevision,
                subscription, cursor));
        return new(cursor, changes.AsReadOnly());
    }

    private static WebChange Scoped(
        WebChangeKind kind, string reason, long databaseVersion, string? pageId, int? pageRevision,
        WebChangeSubscription? subscription, long? cursor, string? objectQualifiedId = null,
        int? objectVersion = null, string? scope = null) =>
        new(kind, reason, databaseVersion, pageId, pageRevision, cursor, subscription?.ApplicationId,
            subscription?.StateSpaceId, scope, objectQualifiedId, objectVersion);

    private async Task<bool> ChangeTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'system_application_object_change');";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private async Task<CursorBounds> ReadCursorBoundsAsync(CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COALESCE(MIN(\"Cursor\"), 0), COALESCE(MAX(\"Cursor\"), 0) FROM system_application_object_change;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(reader.GetInt64(0), reader.GetInt64(1));
    }

    private async Task<IReadOnlyList<DeliveryRow>> ReadChangesAfterAsync(
        long cursor, int limit, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "Cursor", "ApplicationId", "StateSpaceId", "Scope", "ObjectQualifiedId",
                   "ObjectVersion", "ReadPerspectivesJson", "Reason"
            FROM system_application_object_change
            WHERE "Cursor" > $cursor
            ORDER BY "Cursor"
            LIMIT $limit;
            """;
        Add(command, "$cursor", cursor);
        Add(command, "$limit", limit);
        var rows = new List<DeliveryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                JsonSerializer.Deserialize<string[]>(reader.GetString(6)) ?? [], reader.GetString(7)));
        return rows.AsReadOnly();
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task<long> ReadDataVersionAsync(CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA data_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private Task<int?> ReadPageRevisionAsync(string? pageId, CancellationToken cancellationToken) =>
        pageId is null ? Task.FromResult<int?>(null) : db.Pages.AsNoTracking()
            .Where(page => page.Id == pageId).Select(page => (int?)page.ActiveRevision)
            .SingleOrDefaultAsync(cancellationToken);

    private static TimeSpan ValidateInterval(TimeSpan interval, string parameterName)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, "The interval must be positive.");
        return interval;
    }

    private readonly record struct CursorBounds(long Minimum, long Maximum);
    private sealed record ReplayResult(long Cursor, IReadOnlyList<WebChange> Changes);
    private sealed record DeliveryRow(
        long Cursor, string ApplicationId, string StateSpaceId, string Scope,
        string? ObjectQualifiedId, int? ObjectVersion, IReadOnlyList<string> ReadPerspectives,
        string Reason);
}

public static class WebChangeSseFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Format(WebChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Kind == WebChangeKind.KeepAlive) return ": keep-alive\n\n";
        var eventName = change.Kind switch
        {
            WebChangeKind.PageRevision => "page-revision",
            WebChangeKind.ObjectChange => "object-change",
            WebChangeKind.Cursor => "cursor",
            _ => "invalidate"
        };
        var pageUrl = change is { Kind: WebChangeKind.PageRevision, PageRevision: not null }
            ? $"/ui/{change.PageId}/index.html" : null;
        var data = change.Kind == WebChangeKind.ObjectChange
            ? JsonSerializer.Serialize(new ObjectChangeData(
                ApplicationObjectChangeContract.Version, change.Cursor!.Value, change.ApplicationId!,
                change.StateSpaceId!, change.Scope!,
                new ObjectReference(change.ObjectQualifiedId!, change.ObjectVersion!.Value), change.Reason), JsonOptions)
            : JsonSerializer.Serialize(new WebChangeData(change.Reason, change.DatabaseVersion,
                change.PageId, change.PageRevision, pageUrl, change.Cursor, change.ApplicationId,
                change.StateSpaceId, change.Scope), JsonOptions);
        var id = change.Cursor is not null ? $"id: {change.Cursor.Value}\n" : string.Empty;
        return $"{id}event: {eventName}\ndata: {data}\n\n";
    }

    private sealed record ObjectReference(string QualifiedId, int Version);
    private sealed record ObjectChangeData(
        int ContractVersion, long Cursor, string ApplicationId, string StateSpaceId, string Scope,
        ObjectReference Object, string Reason);
    private sealed record WebChangeData(
        string Reason, long DatabaseVersion, string? PageId, int? PageRevision, string? Url,
        long? Cursor, string? ApplicationId, string? StateSpaceId, string? Scope);
}
