using System.Globalization;
using DantesRoleplay.Operations;
using DantesRoleplay.SystemFeedback;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Thin protocol adapter for the feedback store; the service owns submission transactions.</summary>
public sealed class SystemFeedbackHandler
{
    public async Task<ToolEnvelope> SubmitAsync(ISystemFeedbackService feedback, SystemFeedbackSubmitRequest request, string intent, string[]? proceduresUsed, CancellationToken cancellationToken)
    {
        var result = await feedback.SubmitAsync(request, intent, proceduresUsed ?? [], cancellationToken);
        return result.Ok
            ? ToolEnvelope.Success(new { Report = result.Report, result.Duplicate }, result.OperationId, "query(kind: \"feedback\", id: \"" + result.Report!.Id + "\")")
            : ToolEnvelope.Failure(result.Problem!.Code, result.Problem.Message, result.Problem.Fix, result.OperationId);
    }

    public async Task<ToolEnvelope> FindAsync(ISystemFeedbackService feedback, IOperationLog log, string? id, string? category, string? impact, string? state, string? from, string? to, int? limit, CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "query", async () =>
        {
            if (!TryEnum(category, out SystemFeedbackCategory? parsedCategory) || !TryEnum(impact, out SystemFeedbackImpact? parsedImpact) || !TryEnum(state, out SystemFeedbackState? parsedState))
                return ToolOutcome.Fail("INVALID_FEEDBACK_QUERY", "Feedback category, impact, and state must use their documented lowercase values.", "query(kind: \"feedback\")", "Rejected feedback query filters.");
            if (!TryInstant(from, out var fromUtc) || !TryInstant(to, out var toUtc) || fromUtc is not null && toUtc is not null && fromUtc >= toUtc)
                return ToolOutcome.Fail("INVALID_FEEDBACK_QUERY", "Feedback time filters must be ISO-8601 UTC and form a non-empty [from, to) window.", "query(kind: \"feedback\", from: \"2026-08-21T00:00:00Z\")", "Rejected feedback time filters.");
            if (limit is <= 0 or > 100)
                return ToolOutcome.Fail("INVALID_FEEDBACK_QUERY", "Feedback limit must be between 1 and 100.", "query(kind: \"feedback\", limit: 50)", "Rejected feedback limit.");
            var result = await feedback.FindAsync(id, parsedCategory, parsedImpact, parsedState, fromUtc, toUtc, limit ?? 50, cancellationToken);
            if (!result.Ok) return ToolOutcome.Fail(result.Problem!.Code, result.Problem.Message, result.Problem.Fix, "Feedback report was not found.");
            return ToolOutcome.Ok(new { Reports = result.Reports }, result.Reports.Count == 0 ? "No feedback reports matched." : $"Found {result.Reports.Count} feedback report(s).", "query(kind: \"feedback\")");
        });

    private static bool TryInstant(string? value, out DateTime? instant)
    {
        instant = null;
        if (value is null) return true;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) || !value.EndsWith('Z')) return false;
        instant = parsed.UtcDateTime; return true;
    }
    private static bool TryEnum<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null; if (value is null) return true;
        if (!Enum.TryParse<T>(value, true, out var candidate) || value != candidate.ToString().ToLowerInvariant()) return false;
        parsed = candidate; return true;
    }
}
