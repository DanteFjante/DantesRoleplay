using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Writes the live ruleset out as a folder of ordinary files.
///
/// This is the half of catalog portability that did not exist. Importing already half did — the
/// bootstrap seeders read markdown — but only from resources compiled into the assembly, so a rule
/// an LLM authored over MCP could never come back out to be edited, linted, diffed or reviewed.
///
/// Read-only. It writes files and touches nothing in the database.
/// </summary>
public sealed class ExportTool : ITool
{
    public string Name => "export";

    public string Summary => "Write the live ruleset out to a catalog folder.";

    public string Usage => """
        roleplay export <directory> [--database <path>] [--rules-only] [--with-history]

        Writes the latest version of every record, plus a manifest.json recording what was exported
        and each record's fingerprint.

        Layout — a category becomes a directory path, one segment per directory:

          mechanics/<category>/<id>.md     front matter, description, match phrases, requirements
          mechanics/<category>/<id>.js     the JavaScript, as a real file
          procedures/<category>/<id>.md    the contract, as prose
          components/<id>.json             id, name, description
          components/<id>.schema.json      the JSON Schema, verbatim, when there is one
          world/entities/<id>.json         an entity, its components and its container
          world/relationships.json         every relationship, as one set
          manifest.json                    what was exported, and each record's fingerprint

        Options:
          --rules-only     Rules and component definitions only; skip world state entirely.
          --with-history   Also write history/operations.jsonl, one operation per line.

        History is EXPORT ONLY. Nothing imports it, and that is deliberate: an operation id and a
        seed are the claim that a rule ran at a version and produced a roll, and a log that can be
        written from a file is not evidence of anything.

        Existing files are overwritten. Nothing is ever deleted: files already in the catalog that
        this export did not write are listed as orphans and left alone.

        The database is not modified — no rows, no versions, no operation log entry.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var target = context.Arguments.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(target))
        {
            context.Error.WriteLine("export needs a directory: roleplay export <directory>");
            return 2;
        }

        await using var db = context.OpenDatabase();

        var options = new CatalogExportOptions(
            RulesOnly: context.HasFlag("rules-only"),
            WithHistory: context.HasFlag("with-history"));

        var result = await new CatalogExporter(db).ExportAsync(target, options, cancellationToken);

        context.Out.WriteLine(result.Root);
        context.Out.WriteLine();
        context.Out.WriteLine($"  {result.Mechanics,4} mechanic(s)");
        context.Out.WriteLine($"  {result.Procedures,4} procedure contract(s)");
        context.Out.WriteLine($"  {result.ComponentDefinitions,4} component definition(s)");

        if (options.RulesOnly)
        {
            context.Out.WriteLine("       - world state skipped (--rules-only)");
        }
        else
        {
            context.Out.WriteLine($"  {result.Entities,4} entit(y/ies), with components and containment");
        }

        if (options.WithHistory)
        {
            context.Out.WriteLine($"  {result.Operations,4} operation(s) — export only, nothing imports these");
        }

        context.Out.WriteLine();
        context.Out.WriteLine($"{result.Records} record(s) written, manifest included.");

        if (result.Orphans.Count > 0)
        {
            context.Out.WriteLine();
            context.Out.WriteLine(
                $"{result.Orphans.Count} file(s) already in the catalog were not written by this "
                + "export. Nothing was deleted — check whether a record was renamed or removed:");

            foreach (var orphan in result.Orphans)
            {
                context.Out.WriteLine($"  {orphan}");
            }
        }

        return 0;
    }
}
