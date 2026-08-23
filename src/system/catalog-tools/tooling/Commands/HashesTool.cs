using DantesRoleplay.DataAccess;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Reports whether every stored revision carries a current content fingerprint.
///
/// This is the exit gate of Slice 0 made runnable. Catalog import decides which side of a
/// divergence is newer by comparing fingerprints, so a database with missing or stale ones cannot
/// be imported into safely — and nothing about that failure is visible from the outside, which is
/// why it gets a command rather than a paragraph in a document.
///
/// Exits non-zero when anything is missing or stale, so CI can assert the invariant without
/// reading the output.
/// </summary>
public sealed class HashesTool : ITool
{
    public string Name => "hashes";

    public string Summary => "Report revisions whose content fingerprint is missing or stale.";

    public string Usage => """
        roleplay hashes [--database <path>] [--verbose]

        Recomputes the content fingerprint of every mechanic and procedure revision and compares it
        with what is stored.

          missing   the row was never fingerprinted — written over MCP before the store computed one
          stale     the row was fingerprinted by an older formula and is not comparable with today's

        Exit code 0 when every revision is current, 1 otherwise.

        Options:
          --verbose   List every affected revision rather than the first ten of each kind.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        await using var db = context.OpenDatabase();

        var rows = await new ContentHashBackfill(db).AuditAsync(cancellationToken);

        if (rows.Count == 0)
        {
            context.Out.WriteLine($"No revisions in {context.DatabasePath}.");
            return 0;
        }

        var missing = rows.Where(r => r.IsMissing).ToList();
        var stale = rows.Where(r => !r.IsCurrent && !r.IsMissing).ToList();

        context.Out.WriteLine(context.DatabasePath);
        context.Out.WriteLine();
        Report(context, ContentHashKind.Mechanic, rows, missing, stale);
        Report(context, ContentHashKind.Procedure, rows, missing, stale);

        if (missing.Count == 0 && stale.Count == 0)
        {
            context.Out.WriteLine("Every revision carries a current fingerprint.");
            return 0;
        }

        var verbose = context.HasFlag("verbose");
        List(context, "missing", missing, verbose);
        List(context, "stale", stale, verbose);

        context.Out.WriteLine();
        context.Out.WriteLine(
            $"{missing.Count + stale.Count} revision(s) need correcting. "
            + "Run `roleplay backfill-hashes` — or just start the server, which does it too.");

        return 1;
    }

    private static void Report(
        ToolContext context,
        ContentHashKind kind,
        IReadOnlyList<ContentHashRow> rows,
        IReadOnlyList<ContentHashRow> missing,
        IReadOnlyList<ContentHashRow> stale)
    {
        var total = rows.Count(r => r.Kind == kind);
        var label = kind == ContentHashKind.Mechanic ? "mechanic" : "procedure";

        context.Out.WriteLine(
            $"  {label,-10} {total,4} revision(s)  "
            + $"{missing.Count(r => r.Kind == kind),4} missing  "
            + $"{stale.Count(r => r.Kind == kind),4} stale");
    }

    private static void List(
        ToolContext context,
        string label,
        IReadOnlyList<ContentHashRow> rows,
        bool verbose)
    {
        if (rows.Count == 0)
        {
            return;
        }

        context.Out.WriteLine();
        context.Out.WriteLine($"{label}:");

        var shown = verbose ? rows : rows.Take(10).ToList();

        foreach (var row in shown.OrderBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Version))
        {
            context.Out.WriteLine($"  {row.Id} v{row.Version}");
        }

        if (!verbose && rows.Count > shown.Count)
        {
            context.Out.WriteLine($"  ... and {rows.Count - shown.Count} more (--verbose to list them).");
        }
    }
}
