using DantesRoleplay.SystemFeedback;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Local, reversible retention staging for durable system-feedback evidence.</summary>
public sealed class SystemFeedbackRetentionService(DantesRoleplayDbContext db) : ISystemFeedbackRetentionService
{
    private const int MaximumLimit = 1000;
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<SystemFeedbackRetentionFindResult> FindEligibleAsync(
        SystemFeedbackRetentionQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateQuery(query, out var asOf, out var problem))
            return new SystemFeedbackRetentionFindResult([], problem);

        var positiveThreshold = asOf.AddDays(-90);
        var nonPositiveThreshold = asOf.AddDays(-180);
        var rows = await _db.SystemFeedbackReports.AsNoTracking()
            .Where(report => report.State == SystemFeedbackState.Resolved || report.State == SystemFeedbackState.Dismissed)
            .Where(report => report.HoldState == SystemFeedbackHoldState.None)
            .Where(report => query.IncludeArchived || report.ArchivedAt == null)
            .Where(report => query.Category == null || report.Category == query.Category)
            .Where(report => query.State == null || report.State == query.State)
            .Select(report => new
            {
                report.Id,
                report.Category,
                report.Impact,
                report.State,
                report.ArchivedAt,
                report.HoldState,
                report.RetentionRevision,
                report.Summary,
                ClosingAt = report.Dispositions
                    .Where(disposition => disposition.ToState == report.State)
                    .OrderByDescending(disposition => disposition.CreatedAt)
                    .Select(disposition => (DateTime?)disposition.CreatedAt)
                    .FirstOrDefault()
            })
            .Where(report => report.ClosingAt != null)
            .Where(report => report.Category == SystemFeedbackCategory.Positive
                ? report.ClosingAt <= positiveThreshold
                : report.ClosingAt <= nonPositiveThreshold)
            .OrderBy(report => report.Category == SystemFeedbackCategory.Positive
                ? report.ClosingAt!.Value.AddDays(90)
                : report.ClosingAt!.Value.AddDays(180))
            .ThenBy(report => report.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        var candidates = rows.Select(report =>
        {
            var closingAt = report.ClosingAt!.Value;
            return new SystemFeedbackRetentionCandidateView(
                report.Id,
                Name(report.Category),
                Name(report.Impact),
                Name(report.State),
                closingAt,
                closingAt.AddDays(report.Category == SystemFeedbackCategory.Positive ? 90 : 180),
                report.ArchivedAt,
                Name(report.HoldState),
                report.RetentionRevision,
                report.Summary);
        }).ToList();
        return new SystemFeedbackRetentionFindResult(candidates);
    }

    public async Task<SystemFeedbackRetentionTransitionResult> TransitionAsync(
        SystemFeedbackRetentionActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateRequest(request, out var action, out var note, out var reference, out var asOf, out var problem))
            return new SystemFeedbackRetentionTransitionResult(null, problem);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var report = await _db.SystemFeedbackReports
                .Include(item => item.Dispositions)
                .Include(item => item.RetentionActions)
                .SingleOrDefaultAsync(item => item.Id == request.ReportId, cancellationToken);
            if (report is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new SystemFeedbackRetentionTransitionResult(null, Problem("FEEDBACK_NOT_FOUND", "reportId", "No feedback report has that id.", "Use feedback list to select an existing report id."));
            }
            if (report.RetentionRevision != request.ExpectedRetentionRevision)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(report);
            }

            var archive = report.ArchivedAt is not null;
            var hold = report.HoldState;
            if (!CanApply(report, action, asOf, out var failure))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new SystemFeedbackRetentionTransitionResult(null, failure!, report.RetentionRevision, archive, Name(hold));
            }

            var toArchived = action switch
            {
                SystemFeedbackRetentionActionKind.Archive => true,
                SystemFeedbackRetentionActionKind.Restore => false,
                _ => archive
            };
            var toHold = action switch
            {
                SystemFeedbackRetentionActionKind.PlaceHold => SystemFeedbackHoldState.Held,
                SystemFeedbackRetentionActionKind.ReleaseHold => SystemFeedbackHoldState.None,
                _ => hold
            };
            var now = DateTime.UtcNow;
            var nextRevision = checked(report.RetentionRevision + 1);
            _db.SystemFeedbackRetentionActions.Add(new SystemFeedbackRetentionAction
            {
                Id = "feedback-retention." + Guid.NewGuid().ToString("n"),
                ReportId = report.Id,
                Revision = nextRevision,
                Action = action,
                FromArchived = archive,
                ToArchived = toArchived,
                FromHoldState = hold,
                ToHoldState = toHold,
                Reference = reference,
                Note = note,
                EffectiveAsOf = action == SystemFeedbackRetentionActionKind.Archive ? asOf : null,
                CreatedAt = now
            });
            report.ArchivedAt = action switch
            {
                SystemFeedbackRetentionActionKind.Archive => now,
                SystemFeedbackRetentionActionKind.Restore => null,
                _ => report.ArchivedAt
            };
            report.HoldState = toHold;
            report.RetentionRevision = nextRevision;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SystemFeedbackRetentionTransitionResult(View(report));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await CurrentOrMissingAsync(request.ReportId!, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsRevisionUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await CurrentOrMissingAsync(request.ReportId!, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return new SystemFeedbackRetentionTransitionResult(null, Problem("FEEDBACK_RETENTION_FAILED", "$", "The feedback retention action could not be recorded.", "Refresh the report and try again."));
        }
    }

    private static bool CanApply(SystemFeedbackReport report, SystemFeedbackRetentionActionKind action, DateTime? asOf, out SystemFeedbackProblem? problem)
    {
        problem = null;
        var archived = report.ArchivedAt is not null;
        switch (action)
        {
            case SystemFeedbackRetentionActionKind.Archive:
                var candidate = Candidate(report);
                if (archived || report.HoldState != SystemFeedbackHoldState.None || candidate is null || asOf is null || candidate.EligibleAt > asOf)
                    return Fail(out problem, "FEEDBACK_RETENTION_INELIGIBLE", "action", "That report is not eligible for archival at the supplied time.", "Use feedback retention eligible with the same as-of time.");
                return true;
            case SystemFeedbackRetentionActionKind.Restore when archived:
                return true;
            case SystemFeedbackRetentionActionKind.PlaceHold when report.HoldState == SystemFeedbackHoldState.None:
                return true;
            case SystemFeedbackRetentionActionKind.ReleaseHold when report.HoldState == SystemFeedbackHoldState.Held:
                return true;
            default:
                return Fail(out problem, "INVALID_FEEDBACK_RETENTION_TRANSITION", "action", "That retention transition is not allowed from the current projection.", "Refresh the report and choose an allowed retention action.");
        }
    }

    private async Task<SystemFeedbackRetentionTransitionResult> CurrentOrMissingAsync(string reportId, CancellationToken cancellationToken)
    {
        var report = await _db.SystemFeedbackReports.AsNoTracking().Include(item => item.RetentionActions)
            .SingleOrDefaultAsync(item => item.Id == reportId, cancellationToken);
        return report is null
            ? new SystemFeedbackRetentionTransitionResult(null, Problem("FEEDBACK_NOT_FOUND", "reportId", "No feedback report has that id.", "Use feedback list to select an existing report id."))
            : Conflict(report);
    }

    private static SystemFeedbackRetentionTransitionResult Conflict(SystemFeedbackReport report) =>
        new(null, Problem("FEEDBACK_RETENTION_CONFLICT", "expectedRetentionRevision", "The report retention projection changed before this action was recorded.", "Refresh the report and retry with its current retention revision."), report.RetentionRevision, report.ArchivedAt is not null, Name(report.HoldState));

    private static SystemFeedbackRetentionCandidateView? Candidate(SystemFeedbackReport report)
    {
        if (report.State is not (SystemFeedbackState.Resolved or SystemFeedbackState.Dismissed)) return null;
        var closing = report.Dispositions.Where(item => item.ToState == report.State).OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        if (closing is null) return null;
        var eligible = closing.CreatedAt.AddDays(report.Category == SystemFeedbackCategory.Positive ? 90 : 180);
        return new SystemFeedbackRetentionCandidateView(report.Id, Name(report.Category), Name(report.Impact), Name(report.State), closing.CreatedAt, eligible, report.ArchivedAt, Name(report.HoldState), report.RetentionRevision, report.Summary);
    }

    private static SystemFeedbackRetentionView View(SystemFeedbackReport report) => new(
        report.RetentionRevision,
        report.ArchivedAt,
        Name(report.HoldState),
        report.RetentionActions.OrderBy(item => item.Revision).Select(item => new SystemFeedbackRetentionActionView(
            item.Id, item.Revision, Name(item.Action), item.FromArchived, item.ToArchived,
            Name(item.FromHoldState), Name(item.ToHoldState), item.Reference, item.Note,
            item.EffectiveAsOf, item.CreatedAt)).ToList());

    private static bool TryValidateQuery(SystemFeedbackRetentionQuery query, out DateTime asOf, out SystemFeedbackProblem? problem)
    {
        asOf = default;
        problem = null;
        if (query.AsOfUtc is null || query.AsOfUtc.Value.Kind != DateTimeKind.Utc)
            return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "asOfUtc", "The as-of time is required and must be UTC.", "Use an ISO-8601 UTC timestamp ending in Z.");
        asOf = query.AsOfUtc.Value;
        if (asOf > DateTime.UtcNow.AddMinutes(5))
            return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "asOfUtc", "The as-of time cannot be more than five minutes in the future.", "Use the current UTC time or an earlier time.");
        if (query.Limit is < 1 or > MaximumLimit)
            return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "limit", "The limit must be between 1 and 1000.", "Correct the limit and retry.");
        if (query.Category is not null && !Enum.IsDefined(query.Category.Value) || query.State is not null && query.State is not (SystemFeedbackState.Resolved or SystemFeedbackState.Dismissed))
            return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "filter", "Retention eligibility accepts only valid category and resolved/dismissed state filters.", "Correct the filter and retry.");
        return true;
    }

    private static bool TryValidateRequest(SystemFeedbackRetentionActionRequest request, out SystemFeedbackRetentionActionKind action, out string note, out string? reference, out DateTime? asOf, out SystemFeedbackProblem? problem)
    {
        action = default;
        note = string.Empty;
        reference = null;
        asOf = request.AsOfUtc;
        problem = null;
        if (!IsFeedbackId(request.ReportId)) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "reportId", "The feedback id must use the canonical feedback.<32 lowercase hex> form.", "Correct the report id and retry.");
        if (request.ExpectedRetentionRevision < 0) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "expectedRetentionRevision", "The expected retention revision cannot be negative.", "Correct the revision and retry.");
        if (!TryAction(request.Action, out action)) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "action", "The action must be archive, restore, place-hold, or release-hold.", "Correct the action and retry.");
        if (!TryText(request.Note, 1, 500, out note)) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "note", "The note must be 1 to 500 trimmed single-line printable characters.", "Correct the note and retry.");
        if (action == SystemFeedbackRetentionActionKind.Archive)
        {
            if (asOf is null || asOf.Value.Kind != DateTimeKind.Utc || asOf.Value > DateTime.UtcNow.AddMinutes(5)) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "asOfUtc", "Archive requires a UTC as-of time no more than five minutes in the future.", "Use an ISO-8601 UTC timestamp ending in Z.");
            if (request.Reference is not null) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "reference", "Archive does not accept a hold reference.", "Remove the reference and retry.");
        }
        else if (action is SystemFeedbackRetentionActionKind.PlaceHold or SystemFeedbackRetentionActionKind.ReleaseHold)
        {
            if (asOf is not null) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "asOfUtc", "Only archive accepts an as-of time.", "Remove the as-of time and retry.");
            if (!TryText(request.Reference, 1, 100, out var validReference)) return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "reference", "Hold actions require a 1 to 100 character trimmed single-line reference.", "Correct the reference and retry.");
            reference = validReference;
        }
        else if (asOf is not null || request.Reference is not null)
            return Fail(out problem, "INVALID_FEEDBACK_RETENTION_QUERY", "action", "Restore accepts neither an as-of time nor a hold reference.", "Remove those options and retry.");
        return true;
    }

    private static bool TryAction(string? value, out SystemFeedbackRetentionActionKind action) => value switch
    {
        "archive" => Set(SystemFeedbackRetentionActionKind.Archive, out action),
        "restore" => Set(SystemFeedbackRetentionActionKind.Restore, out action),
        "place-hold" => Set(SystemFeedbackRetentionActionKind.PlaceHold, out action),
        "release-hold" => Set(SystemFeedbackRetentionActionKind.ReleaseHold, out action),
        _ => Set(default, out action, false)
    };

    private static bool Set(SystemFeedbackRetentionActionKind value, out SystemFeedbackRetentionActionKind action, bool success = true) { action = value; return success; }
    private static bool TryText(string? value, int minimum, int maximum, out string normalized) { normalized = value ?? string.Empty; return normalized.Length >= minimum && normalized.Length <= maximum && normalized == normalized.Trim() && normalized.All(character => character >= ' ' && character != '\u007f'); }
    private static bool IsFeedbackId(string? value) => value is { Length: 41 } && value.StartsWith("feedback.", StringComparison.Ordinal) && value[9..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Fail(out SystemFeedbackProblem? problem, string code, string path, string message, string fix) { problem = Problem(code, path, message, fix); return false; }
    private static SystemFeedbackProblem Problem(string code, string path, string message, string fix) => new(code, path, message, fix);
    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static bool IsRevisionUniqueViolation(DbUpdateException exception) => exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 };
}
