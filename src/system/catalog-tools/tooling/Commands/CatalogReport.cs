using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Prints an import plan.
///
/// Shared by `import` and `verify` so the two never describe the same situation differently — the
/// dry run a person reads before applying has to be the thing that gets applied.
/// </summary>
internal static class CatalogReport
{
    /// <summary>Ordered so the rows that need a decision are read first.</summary>
    private static readonly CatalogChange[] Order =
    [
        CatalogChange.Conflict,
        CatalogChange.DatabaseEdited,
        CatalogChange.FileEdited,
        CatalogChange.NewInFiles,
        CatalogChange.NewInDatabase,
        CatalogChange.MissingFromFiles,
        CatalogChange.GoneFromBoth,
        CatalogChange.Unchanged
    ];

    public static void Write(TextWriter output, CatalogImportPlan plan, bool verbose)
    {
        output.WriteLine(plan.Root);

        if (!plan.HasManifest)
        {
            output.WriteLine();
            output.WriteLine(
                "No manifest.json in this catalog, so there is no record of when the two sides last "
                + "agreed. Every difference is reported as a conflict — export once to establish a "
                + "baseline.");
        }

        output.WriteLine();

        foreach (var change in Order)
        {
            var entries = plan.Entries.Where(e => e.Change == change).ToList();

            if (entries.Count == 0)
            {
                continue;
            }

            output.WriteLine($"  {Label(change),-20} {entries.Count,4}");

            // Unchanged is the bulk of a healthy catalog and listing it buries everything else.
            if (change == CatalogChange.Unchanged && !verbose)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                output.WriteLine($"       {entry.Id}");
            }

            output.WriteLine($"       — {entries[0].Detail}");
        }
    }

    private static string Label(CatalogChange change) => change switch
    {
        CatalogChange.Unchanged => "unchanged",
        CatalogChange.FileEdited => "edited in files",
        CatalogChange.DatabaseEdited => "edited in database",
        CatalogChange.Conflict => "CONFLICT",
        CatalogChange.NewInFiles => "new in files",
        CatalogChange.NewInDatabase => "new in database",
        CatalogChange.MissingFromFiles => "missing from files",
        CatalogChange.GoneFromBoth => "gone from both",
        _ => change.ToString()
    };
}
