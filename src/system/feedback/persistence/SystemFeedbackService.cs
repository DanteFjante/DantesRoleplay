using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Operations;
using DantesRoleplay.SystemFeedback;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Owns the feedback boundary: validation, idempotency, durable append-only storage and its
/// audit record. It intentionally does not touch world, event, or notification tables.
/// </summary>
public sealed class SystemFeedbackService(DantesRoleplayDbContext db, IOperationLog log) : ISystemFeedbackService
{
    private const int MaxLimit = 100;
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IOperationLog _log = log;

    public async Task<SystemFeedbackSubmitResult> SubmitAsync(SystemFeedbackSubmitRequest request, string intent, IReadOnlyList<string> proceduresUsed, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(request, out var input, out var problem))
            return await FailureAsync(problem!, intent, proceduresUsed, cancellationToken);

        var existing = await ByTokenAsync(input!.RequestToken, cancellationToken);
        if (existing is not null)
            return await ExistingAsync(existing, input.Fingerprint, intent, proceduresUsed, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            existing = await ByTokenAsync(input.RequestToken, cancellationToken);
            if (existing is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await ExistingAsync(existing, input.Fingerprint, intent, proceduresUsed, cancellationToken);
            }

            var references = await ResolveReferencesAsync(input, cancellationToken);
            if (references.Problem is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await FailureAsync(references.Problem, intent, proceduresUsed, cancellationToken);
            }

            var reportId = "feedback." + Guid.NewGuid().ToString("n");
            var operationId = Operation.NewId();
            var report = new SystemFeedbackReport
            {
                Id = reportId,
                RequestToken = input.RequestToken,
                PayloadFingerprint = input.Fingerprint,
                Category = input.Category,
                Impact = input.Impact,
                Summary = input.Summary,
                Observed = input.Observed,
                Expected = input.Expected,
                CreatedAt = DateTime.UtcNow,
                SubmissionOperationId = operationId,
                Steps = input.Steps.Select((text, ordinal) => new SystemFeedbackStep { ReportId = reportId, Ordinal = ordinal, Text = text }).ToList(),
                OperationReferences = input.OperationIds.Select((id, ordinal) => new SystemFeedbackOperationReference { ReportId = reportId, OperationId = id, Ordinal = ordinal }).ToList(),
                ProcedureReferences = references.Procedures.Select((procedure, ordinal) => new SystemFeedbackProcedureReference { ReportId = reportId, ProcedureId = procedure.Id, ProcedureVersion = procedure.Version, Ordinal = ordinal }).ToList()
            };
            _db.SystemFeedbackReports.Add(report);

            var operation = await _log.RecordAsync(
                "commit",
                $"Recorded {Name(input.Category)} feedback '{reportId}' with {Name(input.Impact)} impact.",
                success: true,
                intent: intent,
                subject: reportId,
                proceduresCited: proceduresUsed,
                consumesReadEvidence: true,
                cancellationToken: cancellationToken,
                id: operationId);

            await transaction.CommitAsync(cancellationToken);
            return new SystemFeedbackSubmitResult(View(report), operation.Id, false);
        }
        catch (DbUpdateException ex) when (IsTokenUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            existing = await ByTokenAsync(input.RequestToken, cancellationToken);
            if (existing is not null)
                return await ExistingAsync(existing, input.Fingerprint, intent, proceduresUsed, cancellationToken);
            throw;
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
            return await FailureAsync(new SystemFeedbackProblem("FEEDBACK_SUBMISSION_FAILED", "$", "The feedback report could not be recorded.", "Try the same request token again."), intent, proceduresUsed, cancellationToken);
        }
    }

    public async Task<SystemFeedbackFindResult> FindAsync(string? id = null, SystemFeedbackCategory? category = null, SystemFeedbackImpact? impact = null, SystemFeedbackState? state = null, DateTime? from = null, DateTime? to = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        var query = _db.SystemFeedbackReports.AsNoTracking()
            .Include(r => r.Steps).Include(r => r.OperationReferences).Include(r => r.ProcedureReferences).AsQueryable();
        if (!string.IsNullOrWhiteSpace(id)) query = query.Where(r => r.Id == id);
        if (category is not null) query = query.Where(r => r.Category == category);
        if (impact is not null) query = query.Where(r => r.Impact == impact);
        if (state is not null) query = query.Where(r => r.State == state);
        if (from is not null) query = query.Where(r => r.CreatedAt >= from);
        if (to is not null) query = query.Where(r => r.CreatedAt < to);
        var reports = await query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Take(Math.Clamp(limit, 1, MaxLimit)).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(id) && reports.Count == 0)
            return new SystemFeedbackFindResult([], new SystemFeedbackProblem("FEEDBACK_NOT_FOUND", "id", "No feedback report has that id.", "query(kind: \"feedback\")"));
        return new SystemFeedbackFindResult(reports.Select(View).ToList());
    }

    private async Task<SystemFeedbackSubmitResult> ExistingAsync(SystemFeedbackReport report, string fingerprint, string intent, IReadOnlyList<string> proceduresUsed, CancellationToken ct)
    {
        if (!string.Equals(report.PayloadFingerprint, fingerprint, StringComparison.Ordinal))
            return await FailureAsync(new SystemFeedbackProblem("FEEDBACK_REQUEST_CONFLICT", "requestToken", "That request token was already used with a different report.", "Use a new requestToken for a new report."), intent, proceduresUsed, ct);
        var operation = await _log.RecordAsync("commit", $"Recorded duplicate feedback submission for '{report.Id}'.", true, intent, report.Id, proceduresUsed, consumesReadEvidence: true, cancellationToken: ct);
        return new SystemFeedbackSubmitResult(View(report), operation.Id, true);
    }

    private async Task<SystemFeedbackSubmitResult> FailureAsync(SystemFeedbackProblem problem, string intent, IReadOnlyList<string> proceduresUsed, CancellationToken ct)
    {
        var operation = await _log.RecordAsync("commit", "Rejected feedback submission.", false, intent, string.Empty, proceduresUsed, error: problem.Code, consumesReadEvidence: true, cancellationToken: ct);
        return new SystemFeedbackSubmitResult(null, operation.Id, false, problem);
    }

    private async Task<SystemFeedbackReport?> ByTokenAsync(string token, CancellationToken ct) => await _db.SystemFeedbackReports
        .Include(r => r.Steps).Include(r => r.OperationReferences).Include(r => r.ProcedureReferences)
        .AsNoTracking().FirstOrDefaultAsync(r => r.RequestToken == token, ct);

    private async Task<(IReadOnlyList<(string Id, int Version)> Procedures, SystemFeedbackProblem? Problem)> ResolveReferencesAsync(Validated input, CancellationToken ct)
    {
        if (input.OperationIds.Count > 0)
        {
            var found = await _db.Operations.AsNoTracking().Where(o => input.OperationIds.Contains(o.Id)).Select(o => o.Id).ToListAsync(ct);
            if (found.Count != input.OperationIds.Count)
                return ([], new SystemFeedbackProblem("FEEDBACK_REFERENCE_NOT_FOUND", "relatedOperationIds", "One or more referenced operations do not exist.", "Use ids returned by query(kind: \"history\")."));
        }
        var procedures = await _db.ProcedureContracts.AsNoTracking().Where(p => input.ProcedureIds.Contains(p.Id)).Select(p => new { p.Id, p.CurrentVersion }).ToListAsync(ct);
        if (procedures.Count != input.ProcedureIds.Count)
            return ([], new SystemFeedbackProblem("FEEDBACK_REFERENCE_NOT_FOUND", "relatedProcedureIds", "One or more referenced procedures do not exist.", "Use ids returned by query(kind: \"procedures\")."));
        return (input.ProcedureIds.Select(id => { var procedure = procedures.Single(p => p.Id == id); return (procedure.Id, procedure.CurrentVersion); }).ToList(), null);
    }

    private static bool TryValidate(SystemFeedbackSubmitRequest request, out Validated? input, out SystemFeedbackProblem? problem)
    {
        input = null; problem = null;
        if (!Token(request.RequestToken)) { problem = Invalid("requestToken", "Request token must be feedback-request. followed by 32 lowercase hexadecimal characters."); return false; }
        if (!ParseCategory(request.Category, out var category)) { problem = Invalid("category", "Category must be defect, friction, documentation, suggestion, or positive."); return false; }
        if (!ParseImpact(request.Impact, out var impact)) { problem = Invalid("impact", "Impact must be blocked, degraded, minor, or none."); return false; }
        if (!Text(request.Summary, 1, 200, false)) { problem = Invalid("summary", "Summary must be 1–200 trimmed characters on one line."); return false; }
        if (!Text(request.Observed, 1, 2000, true)) { problem = Invalid("observed", "Observed must be 1–2000 trimmed characters without carriage returns or control characters."); return false; }
        if (request.Expected is not null && !Text(request.Expected, 1, 1000, true)) { problem = Invalid("expected", "Expected must be 1–1000 trimmed characters without carriage returns or control characters."); return false; }
        var steps = request.ReproductionSteps ?? [];
        if (steps.Count > 8 || steps.Any(step => !Text(step, 1, 400, false))) { problem = Invalid("reproductionSteps", "Reproduction steps contain at most eight trimmed one-line strings of 1–400 characters."); return false; }
        var operations = request.RelatedOperationIds ?? [];
        if (operations.Count > 8 || operations.Any(id => !OperationId(id)) || operations.Distinct(StringComparer.Ordinal).Count() != operations.Count) { problem = Invalid("relatedOperationIds", "Related operation ids must be at most eight distinct canonical operation ids."); return false; }
        var procedures = request.RelatedProcedureIds ?? [];
        if (procedures.Count > 8 || procedures.Any(id => !ProcedureId(id)) || procedures.Distinct(StringComparer.Ordinal).Count() != procedures.Count) { problem = Invalid("relatedProcedureIds", "Related procedure ids must be at most eight distinct canonical procedure ids."); return false; }
        var orderedOperations = operations.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var orderedProcedures = procedures.OrderBy(x => x, StringComparer.Ordinal).ToList();
        input = new Validated(request.RequestToken!, category, impact, request.Summary!, request.Observed!, request.Expected, steps.ToList(), orderedOperations, orderedProcedures, Fingerprint(category, impact, request.Summary!, request.Observed!, request.Expected, steps, orderedOperations, orderedProcedures));
        return true;
    }

    private static SystemFeedbackProblem Invalid(string path, string message) => new("INVALID_FEEDBACK", path, message, "Correct the named field and submit again with the same requestToken.");

    private static bool Token(string? value) => value is not null && value.Length == 49 && value.StartsWith("feedback-request.", StringComparison.Ordinal) && value[17..].All(Hex);
    private static bool OperationId(string? value) => value is not null && value.Length == 32 && value.All(Hex);
    private static bool ProcedureId(string? value) => value is not null && value.Length is >= 1 and <= 200 && value.All(c => char.IsLower(c) || char.IsDigit(c) || c is '.' or '-' or '_');
    private static bool Hex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';
    private static bool Text(string? value, int min, int max, bool allowLf) => value is not null && value.Length >= min && value.Length <= max && value == value.Trim() && !value.Any(c => c == '\r' || c < ' ' && (!allowLf || c != '\n'));
    private static bool ParseCategory(string? value, out SystemFeedbackCategory category) => Enum.TryParse(value, true, out category) && value == Name(category);
    private static bool ParseImpact(string? value, out SystemFeedbackImpact impact) => Enum.TryParse(value, true, out impact) && value == Name(impact);
    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static string Fingerprint(SystemFeedbackCategory category, SystemFeedbackImpact impact, string summary, string observed, string? expected, IReadOnlyList<string> steps, IReadOnlyList<string> operations, IReadOnlyList<string> procedures)
    {
        using var sha = SHA256.Create();
        var json = JsonSerializer.Serialize(new { category = Name(category), impact = Name(impact), summary, observed, expected, reproductionSteps = steps, relatedOperationIds = operations, relatedProcedureIds = procedures });
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
    private static bool IsTokenUniqueViolation(DbUpdateException exception) => exception.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode == 19 && sqlite.Message.Contains("system_feedback_report.RequestToken", StringComparison.Ordinal);
    private static SystemFeedbackView View(SystemFeedbackReport report) => new(report.Id, Name(report.Category), Name(report.Impact), Name(report.State), report.Summary, report.Observed, report.Expected, report.CreatedAt, report.SubmissionOperationId, report.Steps.OrderBy(s => s.Ordinal).Select(s => s.Text).ToList(), report.OperationReferences.OrderBy(r => r.Ordinal).Select(r => r.OperationId).ToList(), report.ProcedureReferences.OrderBy(r => r.Ordinal).Select(r => new SystemFeedbackProcedureView(r.ProcedureId, r.ProcedureVersion)).ToList());
    private sealed record Validated(string RequestToken, SystemFeedbackCategory Category, SystemFeedbackImpact Impact, string Summary, string Observed, string? Expected, IReadOnlyList<string> Steps, IReadOnlyList<string> OperationIds, IReadOnlyList<string> ProcedureIds, string Fingerprint);
}
