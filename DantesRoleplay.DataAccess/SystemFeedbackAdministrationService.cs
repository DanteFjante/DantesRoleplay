using DantesRoleplay.SystemFeedback;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Local, developer-only administration of system feedback. This deliberately records no MCP
/// operation: the shell that invokes it is the review trust boundary until reviewer authority is
/// introduced in a later slice.
/// </summary>
public sealed class SystemFeedbackAdministrationService(DantesRoleplayDbContext db) : ISystemFeedbackAdministrationService
{
    private const int MaximumLimit = 1000;
    private const string Redacted = "[redacted from export]";
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<SystemFeedbackAdministrationFindResult> FindAsync(
        SystemFeedbackAdministrationQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateQuery(query, out var ids, out var problem))
            return new SystemFeedbackAdministrationFindResult([], problem);

        var reports = await QueryReports(ids, query, tracking: false)
            .OrderByDescending(report => report.CreatedAt)
            .ThenBy(report => report.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        if (ids.Count > 0 && reports.Count != ids.Count)
            return new SystemFeedbackAdministrationFindResult([], Problem("FEEDBACK_NOT_FOUND", "ids", "One or more feedback reports do not exist.", "Use feedback list to select existing report ids."));

        return new SystemFeedbackAdministrationFindResult(reports.Select(View).ToList());
    }

    public async Task<SystemFeedbackTransitionResult> TransitionAsync(
        SystemFeedbackDispositionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateTransition(request, out var target, out var note, out var problem))
            return new SystemFeedbackTransitionResult(null, problem);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var report = await _db.SystemFeedbackReports
                .SingleOrDefaultAsync(item => item.Id == request.ReportId, cancellationToken);
            if (report is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new SystemFeedbackTransitionResult(null, Problem("FEEDBACK_NOT_FOUND", "reportId", "No feedback report has that id.", "Use feedback list to select an existing report id."));
            }

            if (report.TriageRevision != request.ExpectedRevision)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(report);
            }

            if (!CanTransition(report.State, target))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new SystemFeedbackTransitionResult(null, Problem("INVALID_FEEDBACK_TRANSITION", "targetState", "That state transition is not allowed.", "Refresh the report and choose an allowed target state."), Name(report.State), report.TriageRevision);
            }

            var nextRevision = checked(report.TriageRevision + 1);
            var now = DateTime.UtcNow;
            _db.SystemFeedbackDispositions.Add(new SystemFeedbackDisposition
            {
                Id = "feedback-disposition." + Guid.NewGuid().ToString("n"),
                ReportId = report.Id,
                Revision = nextRevision,
                FromState = report.State,
                ToState = target,
                Note = note,
                CreatedAt = now
            });
            report.State = target;
            report.TriageRevision = nextRevision;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await _db.Entry(report).Collection(item => item.Steps).LoadAsync(cancellationToken);
            await _db.Entry(report).Collection(item => item.OperationReferences).LoadAsync(cancellationToken);
            await _db.Entry(report).Collection(item => item.ProcedureReferences).LoadAsync(cancellationToken);
            await _db.Entry(report).Collection(item => item.Dispositions).LoadAsync(cancellationToken);
            await _db.Entry(report).Collection(item => item.RetentionActions).LoadAsync(cancellationToken);
            return new SystemFeedbackTransitionResult(View(report));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await CurrentOrMissingAsync(request.ReportId!, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDispositionUniqueViolation(exception))
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
            return new SystemFeedbackTransitionResult(null, Problem("FEEDBACK_TRIAGE_FAILED", "$", "The feedback disposition could not be recorded.", "Refresh the report and try again."));
        }
    }

    public async Task<SystemFeedbackExportResult> BuildExportAsync(
        SystemFeedbackAdministrationQuery query,
        IReadOnlySet<string> redactedReportIds,
        CancellationToken cancellationToken = default)
    {
        var found = await FindAsync(query, cancellationToken);
        if (!found.Ok)
            return new SystemFeedbackExportResult(null, found.Problem);

        var selected = found.Reports.Select(report => report.Report.Id).ToHashSet(StringComparer.Ordinal);
        if (!redactedReportIds.IsSubsetOf(selected))
            return new SystemFeedbackExportResult(null, Problem("INVALID_FEEDBACK_ADMIN_QUERY", "redactIds", "Every redacted report id must be selected for export.", "Choose ids returned by the same export query."));

        var ordered = found.Reports
            .OrderBy(report => report.Report.CreatedAt)
            .ThenBy(report => report.Report.Id, StringComparer.Ordinal)
            .Select(report => Export(report, redactedReportIds.Contains(report.Report.Id)))
            .ToList();
        var asOf = ordered
            .SelectMany(report => report.Dispositions.Select(disposition => disposition.CreatedAt).Append(report.CreatedAt))
            .DefaultIfEmpty()
            .Max();
        return new SystemFeedbackExportResult(new SystemFeedbackExportDocument(asOf == default ? null : asOf, query, ordered));
    }

    private IQueryable<SystemFeedbackReport> QueryReports(IReadOnlyCollection<string> ids, SystemFeedbackAdministrationQuery query, bool tracking)
    {
        IQueryable<SystemFeedbackReport> reports = tracking
            ? _db.SystemFeedbackReports
            : _db.SystemFeedbackReports.AsNoTracking();
        reports = reports.Include(report => report.Steps)
            .Include(report => report.OperationReferences)
            .Include(report => report.ProcedureReferences)
            .Include(report => report.Dispositions)
            .Include(report => report.RetentionActions);
        if (ids.Count > 0) reports = reports.Where(report => ids.Contains(report.Id));
        if (query.Category is not null) reports = reports.Where(report => report.Category == query.Category);
        if (query.Impact is not null) reports = reports.Where(report => report.Impact == query.Impact);
        if (query.State is not null) reports = reports.Where(report => report.State == query.State);
        if (query.From is not null) reports = reports.Where(report => report.CreatedAt >= query.From);
        if (query.To is not null) reports = reports.Where(report => report.CreatedAt < query.To);
        if (!query.IncludeArchived) reports = reports.Where(report => report.ArchivedAt == null);
        return reports;
    }

    private async Task<SystemFeedbackTransitionResult> CurrentOrMissingAsync(string reportId, CancellationToken cancellationToken)
    {
        var report = await _db.SystemFeedbackReports.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reportId, cancellationToken);
        return report is null
            ? new SystemFeedbackTransitionResult(null, Problem("FEEDBACK_NOT_FOUND", "reportId", "No feedback report has that id.", "Use feedback list to select an existing report id."))
            : Conflict(report);
    }

    private static SystemFeedbackTransitionResult Conflict(SystemFeedbackReport report) =>
        new(null, Problem("FEEDBACK_TRIAGE_CONFLICT", "expectedRevision", "The feedback report changed before this transition was recorded.", "Refresh the report and retry with its current revision."), Name(report.State), report.TriageRevision);

    private static SystemFeedbackAdministrationView View(SystemFeedbackReport report) => new(
        new SystemFeedbackView(
            report.Id, Name(report.Category), Name(report.Impact), Name(report.State), report.Summary,
            report.Observed, report.Expected, report.CreatedAt, report.SubmissionOperationId,
            report.Steps.OrderBy(item => item.Ordinal).Select(item => item.Text).ToList(),
            report.OperationReferences.OrderBy(item => item.Ordinal).Select(item => item.OperationId).ToList(),
            report.ProcedureReferences.OrderBy(item => item.Ordinal).Select(item => new SystemFeedbackProcedureView(item.ProcedureId, item.ProcedureVersion)).ToList()),
        report.TriageRevision,
        report.Dispositions.OrderBy(item => item.Revision).Select(item => new SystemFeedbackDispositionView(item.Id, item.Revision, Name(item.FromState), Name(item.ToState), item.Note, item.CreatedAt)).ToList(),
        new SystemFeedbackRetentionView(
            report.RetentionRevision,
            report.ArchivedAt,
            Name(report.HoldState),
            report.RetentionActions.OrderBy(item => item.Revision).Select(item => new SystemFeedbackRetentionActionView(
                item.Id, item.Revision, Name(item.Action), item.FromArchived, item.ToArchived,
                Name(item.FromHoldState), Name(item.ToHoldState), item.Reference, item.Note,
                item.EffectiveAsOf, item.CreatedAt)).ToList()));

    private static SystemFeedbackExportReport Export(SystemFeedbackAdministrationView view, bool redact)
    {
        var report = view.Report;
        return new SystemFeedbackExportReport(
            report.Id, report.CreatedAt, report.Category, report.Impact, report.State, view.TriageRevision,
            redact, redact ? Redacted : report.Summary, redact ? Redacted : report.Observed,
            redact && report.Expected is not null ? Redacted : report.Expected,
            redact && report.ReproductionSteps.Count > 0 ? [Redacted] : report.ReproductionSteps,
            report.RelatedOperationIds, report.RelatedProcedures, report.SubmissionOperationId,
            view.Dispositions.Select(disposition => redact ? disposition with { Note = Redacted } : disposition).ToList());
    }

    private static bool TryValidateQuery(SystemFeedbackAdministrationQuery query, out IReadOnlyCollection<string> ids, out SystemFeedbackProblem? problem)
    {
        ids = (query.Ids ?? []).Distinct(StringComparer.Ordinal).ToArray();
        problem = null;
        if (query.Limit is < 1 or > MaximumLimit)
            return Fail(out problem, "INVALID_FEEDBACK_ADMIN_QUERY", "limit", "The limit must be between 1 and 1000.", "Correct the command filters and retry.");
        if (query.From is not null && query.From.Value.Kind != DateTimeKind.Utc || query.To is not null && query.To.Value.Kind != DateTimeKind.Utc)
            return Fail(out problem, "INVALID_FEEDBACK_ADMIN_QUERY", "time", "Time filters must be UTC.", "Correct the command filters and retry.");
        if (query.From is not null && query.To is not null && query.From >= query.To)
            return Fail(out problem, "INVALID_FEEDBACK_ADMIN_QUERY", "time", "The from time must be earlier than the to time.", "Correct the command filters and retry.");
        if (query.Category is not null && !Enum.IsDefined(query.Category.Value) || query.Impact is not null && !Enum.IsDefined(query.Impact.Value) || query.State is not null && !Enum.IsDefined(query.State.Value))
            return Fail(out problem, "INVALID_FEEDBACK_ADMIN_QUERY", "filter", "One or more feedback filters are invalid.", "Correct the command filters and retry.");
        if (ids.Any(id => !IsFeedbackId(id)))
            return Fail(out problem, "INVALID_FEEDBACK_ADMIN_QUERY", "ids", "Every feedback id must use the canonical feedback.<32 lowercase hex> form.", "Correct the command filters and retry.");
        return true;
    }

    private static bool TryValidateTransition(SystemFeedbackDispositionRequest request, out SystemFeedbackState target, out string note, out SystemFeedbackProblem? problem)
    {
        target = default;
        note = string.Empty;
        problem = null;
        if (!IsFeedbackId(request.ReportId)) return Fail(out problem, "INVALID_FEEDBACK_TRANSITION", "reportId", "The feedback id must use the canonical feedback.<32 lowercase hex> form.", "Correct the disposition and retry.");
        if (request.ExpectedRevision < 0) return Fail(out problem, "INVALID_FEEDBACK_TRANSITION", "expectedRevision", "The expected revision cannot be negative.", "Correct the disposition and retry.");
        if (!TryState(request.TargetState, out target)) return Fail(out problem, "INVALID_FEEDBACK_TRANSITION", "targetState", "The target state must be open, acknowledged, resolved, or dismissed.", "Correct the disposition and retry.");
        if (!TryText(request.Note, 1, 500, out note)) return Fail(out problem, "INVALID_FEEDBACK_TRANSITION", "note", "The disposition note must be 1 to 500 trimmed single-line printable characters.", "Correct the disposition and retry.");
        return true;
    }

    private static bool CanTransition(SystemFeedbackState from, SystemFeedbackState to) => from switch
    {
        SystemFeedbackState.Open => to is SystemFeedbackState.Acknowledged or SystemFeedbackState.Resolved or SystemFeedbackState.Dismissed,
        SystemFeedbackState.Acknowledged => to is SystemFeedbackState.Open or SystemFeedbackState.Resolved or SystemFeedbackState.Dismissed,
        SystemFeedbackState.Resolved or SystemFeedbackState.Dismissed => to == SystemFeedbackState.Open,
        _ => false
    };

    private static bool TryState(string? value, out SystemFeedbackState state) => value switch
    {
        "open" => Set(SystemFeedbackState.Open, out state),
        "acknowledged" => Set(SystemFeedbackState.Acknowledged, out state),
        "resolved" => Set(SystemFeedbackState.Resolved, out state),
        "dismissed" => Set(SystemFeedbackState.Dismissed, out state),
        _ => Set(default, out state, false)
    };

    private static bool Set(SystemFeedbackState value, out SystemFeedbackState state, bool success = true) { state = value; return success; }

    private static bool TryText(string? value, int minimum, int maximum, out string normalized)
    {
        normalized = value ?? string.Empty;
        return normalized.Length >= minimum && normalized.Length <= maximum && normalized == normalized.Trim() && normalized.All(character => character >= ' ' && character != '\u007f');
    }

    private static bool IsFeedbackId(string? value) => value is { Length: 41 } && value.StartsWith("feedback.", StringComparison.Ordinal) && value[9..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Fail(out SystemFeedbackProblem? problem, string code, string path, string message, string fix) { problem = Problem(code, path, message, fix); return false; }
    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static SystemFeedbackProblem Problem(string code, string path, string message, string fix) => new(code, path, message, fix);
    private static bool IsDispositionUniqueViolation(DbUpdateException exception) => exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 };
}
