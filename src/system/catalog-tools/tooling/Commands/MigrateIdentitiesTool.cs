using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tools.Commands;

public sealed class MigrateIdentitiesTool : ITool
{
    public string Name => "migrate-identities";
    public string Summary => "Rename reviewed catalog identities atomically without touching live data.";
    public bool RequiresDatabase => false;
    public string Usage => """
        roleplay migrate-identities <catalog> <plan.json> [--apply] [--references-only]

        Stages all identity renames in a sibling catalog copy, registers the reviewed namespaces
        declared by the plan, rewrites exact references, imports and exports through a fresh
        disposable database, and requires warning-free catalog validation. Without --apply it is a
        dry run. With --apply the validated staging copy is committed with a temporary rollback
        copy, which is removed afterwards.

        This tool never opens the live game database.

        --references-only completes interrupted migrations after the corrected records exist. It
        rewrites stale exact references but refuses while any source identity still exists.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Count < 2)
        {
            context.Error.WriteLine("migrate-identities needs a catalog directory and plan file.");
            return 2;
        }
        var plan = await CatalogIdentityMigrationPlan.ReadAsync(context.Arguments[1], cancellationToken);
        var referencesOnly = context.HasFlag("references-only");
        if (referencesOnly) plan = plan with { Namespaces = [] };
        var result = await CatalogIdentityLifecycleMigrator.MigrateAsync(
            context.Arguments[0], plan, context.HasFlag("apply"), referencesOnly, cancellationToken);
        context.Out.WriteLine(referencesOnly
            ? $"{(result.Applied ? "Applied" : "Validated")} exact identity-reference repairs in "
              + $"{result.RewrittenFiles} file(s). No live data was touched."
            : $"{(result.Applied ? "Applied" : "Validated")} {result.RenamedRecords} identity rename(s), "
              + $"registered {result.RegisteredNamespaces} namespace(s), and rewrote "
              + $"{result.RewrittenFiles} file(s). No live data was touched.");
        if (!result.Applied) context.Out.WriteLine("Dry run only; run again with --apply to replace the catalog.");
        return 0;
    }
}
