using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Web.Live;

public enum WebChangeKind
{
    Invalidate,
    PageRevision,
    KeepAlive
}

public sealed record WebChange(
    WebChangeKind Kind,
    string Reason,
    long DatabaseVersion,
    string? PageId = null,
    int? PageRevision = null);

public sealed class SqliteWebChangeFeed(WebContentDbContext db)
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(15);

    public async IAsyncEnumerable<WebChange> WatchAsync(
        string? pageId = null,
        TimeSpan? pollInterval = null,
        TimeSpan? keepAliveInterval = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (pageId is not null && !WebPageId.IsValid(pageId))
        {
            throw new ArgumentException("Page ID is invalid.", nameof(pageId));
        }

        var poll = ValidateInterval(
            pollInterval ?? DefaultPollInterval,
            nameof(pollInterval));
        var keepAlive = ValidateInterval(
            keepAliveInterval ?? DefaultKeepAliveInterval,
            nameof(keepAliveInterval));

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var version = await ReadDataVersionAsync(cancellationToken);
            var pageRevision = await ReadPageRevisionAsync(pageId, cancellationToken);
            var lastWrite = Stopwatch.GetTimestamp();

            yield return new WebChange(
                WebChangeKind.Invalidate,
                "connected",
                version,
                pageId,
                pageRevision);

            using var timer = new PeriodicTimer(poll);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var currentVersion = await ReadDataVersionAsync(cancellationToken);
                if (currentVersion != version)
                {
                    version = currentVersion;
                    var currentPageRevision = await ReadPageRevisionAsync(pageId, cancellationToken);

                    yield return new WebChange(
                        WebChangeKind.Invalidate,
                        "database-commit",
                        version,
                        pageId,
                        currentPageRevision);

                    if (pageId is not null && currentPageRevision != pageRevision)
                    {
                        pageRevision = currentPageRevision;
                        yield return new WebChange(
                            WebChangeKind.PageRevision,
                            "page-activated",
                            version,
                            pageId,
                            pageRevision);
                    }

                    lastWrite = Stopwatch.GetTimestamp();
                    continue;
                }

                if (Stopwatch.GetElapsedTime(lastWrite) >= keepAlive)
                {
                    yield return new WebChange(
                        WebChangeKind.KeepAlive,
                        "keep-alive",
                        version,
                        pageId,
                        pageRevision);
                    lastWrite = Stopwatch.GetTimestamp();
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<long> ReadDataVersionAsync(CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA data_version;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private Task<int?> ReadPageRevisionAsync(
        string? pageId,
        CancellationToken cancellationToken) =>
        pageId is null
            ? Task.FromResult<int?>(null)
            : db.Pages
                .AsNoTracking()
                .Where(page => page.Id == pageId)
                .Select(page => (int?)page.ActiveRevision)
                .SingleOrDefaultAsync(cancellationToken);

    private static TimeSpan ValidateInterval(TimeSpan interval, string parameterName)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The interval must be positive.");
        }

        return interval;
    }
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
        if (change.Kind == WebChangeKind.KeepAlive)
        {
            return ": keep-alive\n\n";
        }

        var eventName = change.Kind == WebChangeKind.PageRevision
            ? "page-revision"
            : "invalidate";
        var pageUrl = change is { Kind: WebChangeKind.PageRevision, PageRevision: not null }
            ? $"/ui/{change.PageId}/index.html"
            : null;
        var data = JsonSerializer.Serialize(
            new WebChangeData(
                change.Reason,
                change.DatabaseVersion,
                change.PageId,
                change.PageRevision,
                pageUrl),
            JsonOptions);

        return $"event: {eventName}\ndata: {data}\n\n";
    }

    private sealed record WebChangeData(
        string Reason,
        long DatabaseVersion,
        string? PageId,
        int? PageRevision,
        string? Url);
}
