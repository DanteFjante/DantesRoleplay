namespace DantesRoleplay.Tools.Commands;

public sealed class UpgradeTool : ITool
{
    public string Name => "upgrade";

    public string Summary => "Back up and upgrade an existing runtime database from the catalog.";

    public string Usage => """
        roleplay upgrade <catalog-directory> [--database <path>]

        Validates the filesystem catalog, creates a consistent timestamped database backup, applies
        pending EF migrations, imports the reviewed filesystem catalog as authority, and verifies
        agreement. A failure restores the original database and retains the backup.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var catalog = context.Arguments.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(catalog))
        {
            context.Error.WriteLine("upgrade needs a catalog directory: roleplay upgrade <catalog-directory>");
            return 2;
        }

        var result = await CatalogDatabaseLifecycle.UpgradeAsync(
            catalog,
            context.DatabasePath,
            cancellationToken);

        context.Out.WriteLine($"Backup: {result.BackupPath}");
        context.Out.WriteLine($"Upgraded: {result.DatabasePath}");
        context.Out.WriteLine(
            result.Created == 0 && result.Updated == 0
                ? "Database already matches the catalog. Ready to run."
                : $"Imported {result.Created} new and {result.Updated} updated catalog record(s). Ready to run.");
        return 0;
    }
}
