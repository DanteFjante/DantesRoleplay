using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Brings a catalog folder back into the database.
///
/// The interesting behaviour is what it refuses to do. Two populations author this system — a
/// developer editing files, an agent writing over MCP — and import exists to merge them without
/// either silently winning. Where it cannot tell which side moved, it stops and says so.
/// </summary>
public sealed class ImportTool : ITool
{
    public string Name => "import";

    public string Summary => "Bring a catalog folder back into the database, refusing to clobber live work.";

    public string Usage => """
        roleplay import <directory> [--database <path>] [--dry-run] [--force-files|--force-db]

        Compares three fingerprints per record — the file's, the database row's, and the manifest's
        record of the last state at which they agreed — and acts on which side moved:

          unchanged            both agree                        skipped
          new in files         in the catalog, not the database  created
          edited in files      only the file moved               written as a new version
          edited in database   only the row moved                LEFT ALONE, reported
          conflict             both moved                        refuses, nothing written
          missing from files   was exported, now absent          reported; import never deletes

        World state — entities, their components, their container, and the relationship set — is
        included when the catalog has it. history/ is ignored: it is export only, and no code path
        in this tool writes an operation from a file.

        The database wins by default when only it moved, because an agent cannot re-create lost
        work from a checkout and a developer can. Run `export` afterwards to capture it.

        A conflict aborts the whole import — no partial application. Resolve it, or name a side:

          --dry-run       Report the plan and write nothing.
          --force-files   The catalog wins, including where the database moved on its own.
          --force-db      The database wins; conflicting files are skipped and left on disk.

        There is no --delete. A rule absent from the catalog is reported, never removed: something
        else may compose it, and that is not a decision to make as a side effect of a sync.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var target = context.Arguments.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(target))
        {
            context.Error.WriteLine("import needs a directory: roleplay import <directory>");
            return 2;
        }

        if (context.HasFlag("force-files") && context.HasFlag("force-db"))
        {
            context.Error.WriteLine("--force-files and --force-db contradict each other. Pick one.");
            return 2;
        }

        var options = new CatalogImportOptions(
            DryRun: context.HasFlag("dry-run"),
            Force: context.HasFlag("force-files") ? CatalogForce.Files
                : context.HasFlag("force-db") ? CatalogForce.Database
                : CatalogForce.None);

        await using var db = context.OpenDatabase();

        // Every store shares this one context, so the importer's transaction actually covers their
        // writes. See the remarks on CatalogImporter.
        var importer = new CatalogImporter(
            db,
            new MechanicStore(db),
            new ProcedureStore(db),
            new WorldStore(db));

        var result = await importer.ApplyAsync(target, options, cancellationToken);

        CatalogReport.Write(context.Out, result.Plan, context.HasFlag("verbose"));

        if (Directory.Exists(Path.Combine(Path.GetFullPath(target), CatalogLayout.HistoryRoot)))
        {
            context.Out.WriteLine();
            context.Out.WriteLine(
                "history/ is present and was not imported. The operation log is export only — an "
                + "operation id and a seed are provenance, and a log writable from a file is not "
                + "evidence of anything.");
        }

        if (result.Aborted)
        {
            context.Out.WriteLine();
            context.Out.WriteLine(
                $"Nothing was written. {result.Plan.Conflicts.Count()} record(s) moved on both "
                + "sides — resolve them, or re-run with --force-files or --force-db.");

            return 1;
        }

        context.Out.WriteLine();

        if (options.DryRun)
        {
            context.Out.WriteLine(
                $"Dry run: would create {result.Created} and update {result.Updated} record(s). "
                + "Nothing was written.");

            return 0;
        }

        context.Out.WriteLine(
            result.Applied == 0
                ? "Nothing to apply; the database already matches the catalog."
                : $"Created {result.Created} and updated {result.Updated} record(s).");

        var needingExport = result.Plan.NeedingExport.Count();

        if (needingExport > 0)
        {
            context.Out.WriteLine(
                $"{needingExport} record(s) were authored live and are newer than the catalog. "
                + "Run `roleplay export` to capture them.");
        }

        return 0;
    }
}
