using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Reports whether a catalog and a database agree, and exits non-zero when they do not.
///
/// The same planner `import` uses, with nothing applied — so CI can assert that the checked-in
/// catalog matches the database without parsing any output. A ruleset that only exists in a
/// database file nobody diffs is one nobody reviews.
/// </summary>
public sealed class VerifyTool : ITool
{
    public string Name => "verify";

    public string Summary => "Report catalog/database drift. Exits non-zero when they differ.";

    public string Usage => """
        roleplay verify <directory> [--database <path>] [--verbose]

        Plans an import and reports it without applying anything. Exit code 0 when every record
        agrees, 1 otherwise — so this is the form to put in CI.

        Identical to `roleplay import --dry-run` except for the exit code: import reports a clean
        plan as success, verify treats any difference at all as a failure.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var target = context.Arguments.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(target))
        {
            context.Error.WriteLine("verify needs a directory: roleplay verify <directory>");
            return 2;
        }

        await using var db = context.OpenDatabase();

        var plan = await new CatalogImporter(
                db,
                new MechanicStore(db),
                new ProcedureStore(db),
                new WorldStore(db),
                new EventTypeStore(db),
                new SubscriptionStore(db))
            .PlanAsync(target, cancellationToken);

        CatalogReport.Write(context.Out, plan, context.HasFlag("verbose"));

        if (plan.IsClean)
        {
            context.Out.WriteLine();
            context.Out.WriteLine("The catalog and the database agree.");
            return 0;
        }

        return 1;
    }
}
