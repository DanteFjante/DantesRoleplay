using System.Globalization;
using System.Text;
using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SystemFeedback;

namespace DantesRoleplay.Tools.Commands;

/// <summary>Local triage and deliberately redacted export for runtime system feedback.</summary>
public sealed class FeedbackTool : ITool
{
    private static readonly HashSet<string> QueryOptions = new(StringComparer.OrdinalIgnoreCase)
    { "database", "ids", "state", "category", "impact", "from", "to", "limit" };

    public string Name => "feedback";
    public string Summary => "Review, triage, retain, or export runtime system-feedback reports.";
    public string Usage => """
        roleplay feedback list [--state <state>] [--category <category>] [--impact <impact>] [--from <utc>] [--to <utc>] [--include-archived] [--limit <1..1000>] [--database <path>]
        roleplay feedback show <feedback-id> [--database <path>]
        roleplay feedback triage <feedback-id> --to <open|acknowledged|resolved|dismissed> --expected-revision <n> --note <text> [--database <path>]
        roleplay feedback export <file> --format <json|markdown> [--ids <comma-separated ids>] [--redact-ids <comma-separated ids>] [--state <state>] [--category <category>] [--impact <impact>] [--from <utc>] [--to <utc>] [--limit <1..1000>] [--overwrite] [--database <path>]
        roleplay feedback retention eligible --as-of <utc> [--category <category>] [--state <resolved|dismissed>] [--include-archived] [--limit <1..1000>] [--database <path>]
        roleplay feedback retention <archive|restore|place-hold|release-hold> <feedback-id> --expected-retention-revision <n> --note <text> [--as-of <utc>] [--reference <text>] [--database <path>]

        list is newest-first and prints metadata only. show includes the report and immutable local
        disposition history. triage requires the revision shown by list or show, preventing a stale
        review from overwriting a newer one. Allowed transitions are open -> acknowledged/resolved/
        dismissed, acknowledged -> open/resolved/dismissed, and resolved/dismissed -> open.

        export is a read-only, deterministic JSON or Markdown artifact. It never contains request
        tokens, payload fingerprints, operation payloads, database paths, or hidden world data.
        --redact-ids replaces report prose, steps, and disposition notes in the artifact only; it
        does not change the database. Existing files require --overwrite.

        retention is local and reversible. Only closed reports age into eligibility; a hold prevents
        archival. It has no purge, delete, bulk mutation, or remote operation.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var command = context.Arguments.FirstOrDefault()?.ToLowerInvariant();
        return command switch
        {
            "list" => await ListAsync(context, cancellationToken),
            "show" => await ShowAsync(context, cancellationToken),
            "triage" => await TriageAsync(context, cancellationToken),
            "export" => await ExportAsync(context, cancellationToken),
            "retention" => await RetentionAsync(context, cancellationToken),
            _ => Invalid(context, "feedback needs list, show, triage, export, or retention.")
        };
    }

    private static async Task<int> ListAsync(ToolContext context, CancellationToken cancellationToken)
    {
        SystemFeedbackAdministrationQuery? query = null;
        string? error = null;
        var permitted = new HashSet<string>(QueryOptions, StringComparer.OrdinalIgnoreCase) { "include-archived" };
        if (!Only(context, permitted) || context.Arguments.Count != 1 || !TryQuery(context, out query, out error, context.HasFlag("include-archived"))) return Invalid(context, error ?? "Invalid feedback list arguments.");
        await using var db = context.OpenDatabase();
        var result = await new SystemFeedbackAdministrationService(db).FindAsync(query!, cancellationToken);
        if (!result.Ok) return Problem(context, result.Problem!, 2);
        foreach (var item in result.Reports)
        {
            var report = item.Report;
            context.Out.WriteLine($"{report.Id}\t{Utc(report.CreatedAt)}\t{report.Category}\t{report.Impact}\t{report.State}\trevision {item.TriageRevision}\t{report.Summary}");
        }
        return 0;
    }

    private static async Task<int> ShowAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (!Only(context, Options("database")) || context.Arguments.Count != 2) return Invalid(context, "Usage: roleplay feedback show <feedback-id> [--database <path>]");
        await using var db = context.OpenDatabase();
        var result = await new SystemFeedbackAdministrationService(db).FindAsync(new SystemFeedbackAdministrationQuery([context.Arguments[1]], Limit: 1), cancellationToken);
        if (!result.Ok) return Problem(context, result.Problem!, 2);
        WriteReport(context.Out, result.Reports.Single());
        return 0;
    }

    private static async Task<int> TriageAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (!Only(context, Options("database", "to", "expected-revision", "note")) || context.Arguments.Count != 2 || !int.TryParse(context.Option("expected-revision"), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || context.Option("to") is null || context.Option("note") is null)
            return Invalid(context, "Usage: roleplay feedback triage <feedback-id> --to <state> --expected-revision <n> --note <text> [--database <path>]");
        await using var db = context.OpenDatabase();
        var result = await new SystemFeedbackAdministrationService(db).TransitionAsync(new SystemFeedbackDispositionRequest(context.Arguments[1], context.Option("to"), revision, context.Option("note")), cancellationToken);
        if (!result.Ok) return Problem(context, result.Problem!, result.Problem!.Code == "FEEDBACK_TRIAGE_CONFLICT" ? 3 : 2, result.CurrentState, result.CurrentRevision);
        WriteReport(context.Out, result.Report!);
        return 0;
    }

    private static async Task<int> ExportAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var permitted = new HashSet<string>(QueryOptions, StringComparer.OrdinalIgnoreCase) { "format", "redact-ids", "overwrite" };
        SystemFeedbackAdministrationQuery? query = null;
        IReadOnlySet<string>? redacted = null;
        string? error = null;
        if (!Only(context, permitted) || context.Arguments.Count != 2 || !TryQuery(context, out query, out error) || !TryIds(context.Option("redact-ids"), out redacted, out error)) return Invalid(context, error ?? "Invalid feedback export arguments.");
        var format = context.Option("format")?.ToLowerInvariant();
        if (format is not ("json" or "markdown")) return Invalid(context, "Export needs --format json or --format markdown.");
        if (!TryTarget(context.Arguments[1], context.HasFlag("overwrite"), out var target, out error)) return ExportFailure(context, "FEEDBACK_EXPORT_EXISTS", error!);
        await using var db = context.OpenDatabase();
        var result = await new SystemFeedbackAdministrationService(db).BuildExportAsync(query!, redacted!, cancellationToken);
        if (!result.Ok) return Problem(context, result.Problem!, 2);
        var content = format == "json" ? Json(result.Document!, Path.GetFileName(context.DatabasePath)) : Markdown(result.Document!, Path.GetFileName(context.DatabasePath));
        try { await WriteAtomicallyAsync(target!, content, context.HasFlag("overwrite"), cancellationToken); context.Out.WriteLine(target); return 0; }
        catch { return ExportFailure(context, "FEEDBACK_EXPORT_FAILED", "The export file could not be written; the existing destination was left unchanged."); }
    }

    private static async Task<int> RetentionAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var command = context.Arguments.Skip(1).FirstOrDefault()?.ToLowerInvariant();
        if (command == "eligible") return await EligibleAsync(context, cancellationToken);
        if (command is not ("archive" or "restore" or "place-hold" or "release-hold"))
            return Invalid(context, "Retention needs eligible, archive, restore, place-hold, or release-hold.");
        if (context.Arguments.Count != 3 || !Only(context, Options("database", "as-of", "reference", "expected-retention-revision", "note")) ||
            !int.TryParse(context.Option("expected-retention-revision"), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || context.Option("note") is null ||
            !TryUtc(context.Option("as-of"), out var asOf))
            return Invalid(context, "Usage: roleplay feedback retention <action> <feedback-id> --expected-retention-revision <n> --note <text> [--as-of <utc>] [--reference <text>] [--database <path>]");
        await using var db = context.OpenDatabase();
        var result = await new SystemFeedbackRetentionService(db).TransitionAsync(
            new SystemFeedbackRetentionActionRequest(context.Arguments[2], command, revision, context.Option("reference"), context.Option("note"), asOf), cancellationToken);
        if (!result.Ok) return RetentionProblem(context, result, result.Problem!.Code == "FEEDBACK_RETENTION_CONFLICT" ? 3 : 2);
        WriteRetention(context.Out, result.Retention!);
        return 0;
    }

    private static async Task<int> EligibleAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var permitted = Options("database", "as-of", "category", "state", "include-archived", "limit");
        SystemFeedbackRetentionQuery? query = null;
        string? error = null;
        if (!Only(context, permitted) || context.Arguments.Count != 2 || !TryRetentionQuery(context, out query, out error))
            return Invalid(context, error ?? "Usage: roleplay feedback retention eligible --as-of <utc> [options]");
        await using var db = context.OpenDatabase();
        var result = await new SystemFeedbackRetentionService(db).FindEligibleAsync(query!, cancellationToken);
        if (!result.Ok) return Problem(context, result.Problem!, 2);
        foreach (var report in result.Reports)
            context.Out.WriteLine($"{report.ReportId}\teligible {Utc(report.EligibleAt)}\t{report.Category}\t{report.Impact}\t{report.State}\tarchived {Boolean(report.ArchivedAt is not null)}\thold {report.HoldState}\trevision {report.RetentionRevision}\t{report.Summary}");
        return 0;
    }

    private static bool TryRetentionQuery(ToolContext context, out SystemFeedbackRetentionQuery? query, out string? error)
    {
        query = null;
        error = null;
        if (!TryUtc(context.Option("as-of"), out var asOf) || asOf is null) { error = "--as-of must be an ISO-8601 UTC timestamp ending in Z."; return false; }
        if (!TryEnum(context.Option("category"), Category, out SystemFeedbackCategory? category) || !TryEnum(context.Option("state"), State, out SystemFeedbackState? state)) { error = "Use lowercase category and resolved/dismissed state values."; return false; }
        var limit = 100;
        if (context.Option("limit") is { } raw && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 1000)) { error = "--limit must be an integer from 1 through 1000."; return false; }
        query = new SystemFeedbackRetentionQuery(asOf, category, state, context.HasFlag("include-archived"), limit);
        return true;
    }

    private static bool TryQuery(ToolContext context, out SystemFeedbackAdministrationQuery? query, out string? error, bool includeArchived = true)
    {
        query = null; error = null;
        if (!TryIds(context.Option("ids"), out var ids, out error)) return false;
        if (!TryEnum(context.Option("category"), Category, out SystemFeedbackCategory? category) || !TryEnum(context.Option("impact"), Impact, out SystemFeedbackImpact? impact) || !TryEnum(context.Option("state"), State, out SystemFeedbackState? state)) { error = "Use lowercase category, impact, and state values from feedback list."; return false; }
        if (!TryUtc(context.Option("from"), out var from) || !TryUtc(context.Option("to"), out var to)) { error = "Time filters must be ISO-8601 UTC timestamps ending in Z."; return false; }
        var limit = 100;
        if (context.Option("limit") is { } raw && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 1000)) { error = "--limit must be an integer from 1 through 1000."; return false; }
        query = new SystemFeedbackAdministrationQuery(ids, category, impact, state, from, to, limit, includeArchived);
        return true;
    }

    private static bool TryIds(string? raw, out IReadOnlySet<string>? ids, out string? error)
    {
        var collected = new HashSet<string>(StringComparer.Ordinal);
        ids = collected; error = null;
        if (raw is null) return true;
        var values = raw.Split(',', StringSplitOptions.None | StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(string.IsNullOrEmpty)) { error = "Feedback ids must be a comma-separated list with no empty entries."; return false; }
        foreach (var value in values) collected.Add(value);
        return true;
    }

    private static bool TryEnum<T>(string? value, Func<string, T?> parse, out T? parsed) where T : struct { parsed = null; if (value is null) return true; parsed = parse(value); return parsed is not null; }
    private static SystemFeedbackCategory? Category(string value) => value switch { "defect" => SystemFeedbackCategory.Defect, "friction" => SystemFeedbackCategory.Friction, "documentation" => SystemFeedbackCategory.Documentation, "suggestion" => SystemFeedbackCategory.Suggestion, "positive" => SystemFeedbackCategory.Positive, _ => null };
    private static SystemFeedbackImpact? Impact(string value) => value switch { "blocked" => SystemFeedbackImpact.Blocked, "degraded" => SystemFeedbackImpact.Degraded, "minor" => SystemFeedbackImpact.Minor, "none" => SystemFeedbackImpact.None, _ => null };
    private static SystemFeedbackState? State(string value) => value switch { "open" => SystemFeedbackState.Open, "acknowledged" => SystemFeedbackState.Acknowledged, "resolved" => SystemFeedbackState.Resolved, "dismissed" => SystemFeedbackState.Dismissed, _ => null };
    private static bool TryUtc(string? value, out DateTime? utc) { utc = null; if (value is null) return true; if (!value.EndsWith('Z') || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return false; utc = parsed.UtcDateTime; return true; }
    private static bool Only(ToolContext context, IReadOnlySet<string> permitted) => context.Options.Keys.All(permitted.Contains);
    private static IReadOnlySet<string> Options(params string[] values) => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    private static int Invalid(ToolContext context, string message) { context.Error.WriteLine(message); return 2; }
    private static int Problem(ToolContext context, SystemFeedbackProblem problem, int code, string? currentState = null, int? currentRevision = null) { context.Error.WriteLine($"{problem.Code}: {problem.Message}"); if (currentState is not null) context.Error.WriteLine($"Current state: {currentState}; revision: {currentRevision}."); return code; }
    private static int RetentionProblem(ToolContext context, SystemFeedbackRetentionTransitionResult result, int code)
    {
        context.Error.WriteLine($"{result.Problem!.Code}: {result.Problem.Message}");
        if (result.CurrentRevision is not null) context.Error.WriteLine($"Current retention revision: {result.CurrentRevision}; archived: {Boolean(result.CurrentArchived == true)}; hold: {result.CurrentHoldState}.");
        return code;
    }
    private static int ExportFailure(ToolContext context, string code, string message) { context.Error.WriteLine($"{code}: {message}"); return 1; }

    private static void WriteReport(TextWriter writer, SystemFeedbackAdministrationView view)
    {
        var report = view.Report;
        writer.WriteLine($"{report.Id}  {report.Category}/{report.Impact}  {report.State}  revision {view.TriageRevision}"); writer.WriteLine($"Created: {Utc(report.CreatedAt)}"); writer.WriteLine($"Submission operation: {report.SubmissionOperationId}"); writer.WriteLine($"Summary: {report.Summary}"); writer.WriteLine($"Observed: {report.Observed}");
        if (report.Expected is not null) writer.WriteLine($"Expected: {report.Expected}"); if (report.ReproductionSteps.Count > 0) writer.WriteLine("Steps: " + string.Join(" | ", report.ReproductionSteps)); if (report.RelatedOperationIds.Count > 0) writer.WriteLine("Related operations: " + string.Join(", ", report.RelatedOperationIds)); if (report.RelatedProcedures.Count > 0) writer.WriteLine("Related procedures: " + string.Join(", ", report.RelatedProcedures.Select(item => $"{item.Id}@{item.Version}")));
        foreach (var item in view.Dispositions) writer.WriteLine($"Disposition {item.Revision}: {item.FromState} -> {item.ToState} at {Utc(item.CreatedAt)} — {item.Note}");
        WriteRetention(writer, view.Retention);
    }

    private static void WriteRetention(TextWriter writer, SystemFeedbackRetentionView retention)
    {
        writer.WriteLine($"Retention: archived {Boolean(retention.ArchivedAt is not null)}; hold {retention.HoldState}; revision {retention.RetentionRevision}{(retention.ArchivedAt is { } archived ? $"; archived at {Utc(archived)}" : string.Empty)}");
        foreach (var action in retention.Actions)
            writer.WriteLine($"Retention action {action.Revision}: {action.Action} at {Utc(action.CreatedAt)} — {action.Note}{(action.Reference is null ? string.Empty : $" ({action.Reference})")}");
    }

    private static bool TryTarget(string raw, bool overwrite, out string? target, out string? error)
    {
        target = null; error = null;
        try
        {
            target = Path.GetFullPath(raw); var directory = Path.GetDirectoryName(target);
            if (directory is null || !Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { error = "The export directory must already exist and must not be a link."; return false; }
            if (File.Exists(target)) { if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) { error = "The export destination must not be a link."; return false; } if (!overwrite) { error = "The export destination already exists; add --overwrite to replace that exact file."; return false; } }
            return true;
        }
        catch { error = "The export destination is not a valid file path."; return false; }
    }

    private static async Task WriteAtomicallyAsync(string target, string content, bool overwrite, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():n}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            if (File.Exists(target)) { if (!overwrite) throw new IOException("The destination was created while exporting."); File.Replace(temporary, target, null, ignoreMetadataErrors: true); } else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Json(SystemFeedbackExportDocument document, string sourceDatabase)
    {
        using var stream = new MemoryStream(); using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject(); json.WriteString("schemaVersion", "dantes-system-feedback-export-v1"); json.WriteString("sourceDatabase", sourceDatabase); if (document.SourceAsOfUtc is { } asOf) json.WriteString("sourceAsOfUtc", Utc(asOf)); else json.WriteNull("sourceAsOfUtc"); json.WritePropertyName("filters"); WriteFilters(json, document.Filters); json.WriteNumber("count", document.Reports.Count); json.WritePropertyName("reports"); json.WriteStartArray(); foreach (var report in document.Reports) WriteJsonReport(json, report); json.WriteEndArray(); json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteFilters(Utf8JsonWriter json, SystemFeedbackAdministrationQuery query)
    {
        json.WriteStartObject(); json.WritePropertyName("ids"); json.WriteStartArray(); foreach (var id in (query.Ids ?? Array.Empty<string>()).OrderBy(id => id, StringComparer.Ordinal)) json.WriteStringValue(id); json.WriteEndArray(); WriteOptional(json, "category", query.Category is null ? null : StateName(query.Category.Value)); WriteOptional(json, "impact", query.Impact is null ? null : StateName(query.Impact.Value)); WriteOptional(json, "state", query.State is null ? null : StateName(query.State.Value)); WriteOptional(json, "from", query.From is null ? null : Utc(query.From.Value)); WriteOptional(json, "to", query.To is null ? null : Utc(query.To.Value)); json.WriteNumber("limit", query.Limit); json.WriteEndObject();
    }
    private static void WriteJsonReport(Utf8JsonWriter json, SystemFeedbackExportReport report)
    {
        json.WriteStartObject(); json.WriteString("id", report.Id); json.WriteString("createdAt", Utc(report.CreatedAt)); json.WriteString("category", report.Category); json.WriteString("impact", report.Impact); json.WriteString("state", report.State); json.WriteNumber("triageRevision", report.TriageRevision); json.WriteBoolean("redacted", report.Redacted); json.WriteString("summary", report.Summary); json.WriteString("observed", report.Observed); WriteOptional(json, "expected", report.Expected); json.WritePropertyName("reproductionSteps"); WriteStrings(json, report.ReproductionSteps); json.WritePropertyName("relatedOperationIds"); WriteStrings(json, report.RelatedOperationIds); json.WritePropertyName("relatedProcedures"); json.WriteStartArray(); foreach (var procedure in report.RelatedProcedures) { json.WriteStartObject(); json.WriteString("id", procedure.Id); json.WriteNumber("version", procedure.Version); json.WriteEndObject(); } json.WriteEndArray(); json.WriteString("submissionOperationId", report.SubmissionOperationId); json.WritePropertyName("dispositions"); json.WriteStartArray(); foreach (var disposition in report.Dispositions) { json.WriteStartObject(); json.WriteString("id", disposition.Id); json.WriteNumber("revision", disposition.Revision); json.WriteString("fromState", disposition.FromState); json.WriteString("toState", disposition.ToState); json.WriteString("note", disposition.Note); json.WriteString("createdAt", Utc(disposition.CreatedAt)); json.WriteEndObject(); } json.WriteEndArray(); json.WriteEndObject();
    }
    private static void WriteOptional(Utf8JsonWriter json, string name, string? value) { if (value is null) json.WriteNull(name); else json.WriteString(name, value); }
    private static void WriteStrings(Utf8JsonWriter json, IReadOnlyList<string> values) { json.WriteStartArray(); foreach (var value in values) json.WriteStringValue(value); json.WriteEndArray(); }

    private static string Markdown(SystemFeedbackExportDocument document, string sourceDatabase)
    {
        var text = new StringBuilder(); text.AppendLine("# Dantes system feedback export"); text.AppendLine(); text.AppendLine("- Schema: `dantes-system-feedback-export-v1`"); text.AppendLine($"- Source database: `{Escape(sourceDatabase)}`"); text.AppendLine($"- Source as of UTC: `{(document.SourceAsOfUtc is { } asOf ? Utc(asOf) : "null")}`"); text.AppendLine($"- Reports: {document.Reports.Count}");
        foreach (var report in document.Reports)
        {
            text.AppendLine(); text.AppendLine($"## {Escape(report.Id)}"); text.AppendLine(); text.AppendLine($"- Created: `{Utc(report.CreatedAt)}`"); text.AppendLine($"- Category: `{report.Category}`"); text.AppendLine($"- Impact: `{report.Impact}`"); text.AppendLine($"- State: `{report.State}` (revision {report.TriageRevision})"); text.AppendLine($"- Redacted: `{report.Redacted.ToString().ToLowerInvariant()}`"); Block(text, "Summary", report.Summary); Block(text, "Observed", report.Observed); if (report.Expected is not null) Block(text, "Expected", report.Expected);
            if (report.ReproductionSteps.Count > 0) { text.AppendLine("### Reproduction steps"); foreach (var step in report.ReproductionSteps) text.AppendLine($"- {Escape(step)}"); }
            if (report.RelatedOperationIds.Count > 0) text.AppendLine($"Related operations: `{string.Join("`, `", report.RelatedOperationIds)}`"); if (report.RelatedProcedures.Count > 0) text.AppendLine($"Related procedures: `{string.Join("`, `", report.RelatedProcedures.Select(procedure => $"{procedure.Id}@{procedure.Version}"))}`"); text.AppendLine($"Submission operation: `{report.SubmissionOperationId}`"); if (report.Dispositions.Count > 0) { text.AppendLine("### Dispositions"); foreach (var disposition in report.Dispositions) text.AppendLine($"- Revision {disposition.Revision}: `{disposition.FromState}` → `{disposition.ToState}` at `{Utc(disposition.CreatedAt)}` — {Escape(disposition.Note)}"); }
        }
        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
    private static void Block(StringBuilder text, string heading, string value) { text.AppendLine($"### {heading}"); text.AppendLine("```"); text.AppendLine(value.Replace("```", "``\\`", StringComparison.Ordinal)); text.AppendLine("```"); }
    private static string Escape(string value) => value.Replace("`", "\\`", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    private static string Utc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Boolean(bool value) => value ? "true" : "false";
    private static string StateName<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
}
