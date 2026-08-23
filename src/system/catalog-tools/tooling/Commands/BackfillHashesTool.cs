using DantesRoleplay.DataAccess;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Corrects every missing or stale content fingerprint in a database file.
///
/// The server does this at startup too, so this command is for the case the startup path cannot
/// reach: a database file you have in hand and do not want to launch a server against — a copy
/// taken for inspection, a backup, or a live file whose server is running a stale binary.
///
/// It rewrites one derived column and never touches authored content, so it appends no version and
/// loses no history.
/// </summary>
public sealed class BackfillHashesTool : ITool
{
    public string Name => "backfill-hashes";

    public string Summary => "Recompute missing or stale content fingerprints in place.";

    public string Usage => """
        roleplay backfill-hashes [--database <path>] [--dry-run]

        Recomputes the content fingerprint of every mechanic and procedure revision and writes back
        the ones that are missing or stale. Idempotent: a second run corrects nothing.

        Authored content is not touched, no version is appended, and no history is lost — the
        fingerprint is derived from the content, not part of it.

        Options:
          --dry-run   Report what would change and write nothing.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        await using var db = context.OpenDatabase();
        var backfill = new ContentHashBackfill(db);

        if (context.HasFlag("dry-run"))
        {
            var audit = await backfill.AuditAsync(cancellationToken);
            var stale = audit.Where(r => !r.IsCurrent).ToList();

            context.Out.WriteLine(
                stale.Count == 0
                    ? "Nothing to correct."
                    : $"Would correct {stale.Count} revision(s):");

            foreach (var row in stale.OrderBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Version))
            {
                context.Out.WriteLine($"  {row.Id} v{row.Version}  {(row.IsMissing ? "missing" : "stale")}");
            }

            return 0;
        }

        var result = await backfill.RunAsync(cancellationToken);

        context.Out.WriteLine(
            result.Total == 0
                ? "Nothing to correct."
                : $"Corrected {result.MechanicVersions} mechanic and {result.ProcedureVersions} "
                  + "procedure revision(s).");

        return 0;
    }
}
