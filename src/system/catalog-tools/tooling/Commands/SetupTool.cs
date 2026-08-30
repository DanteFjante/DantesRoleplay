namespace DantesRoleplay.Tools.Commands;

public sealed class SetupTool : ITool
{
    public string Name => "setup";

    public string Summary => "Create, migrate, and populate a new runtime database from the catalog.";

    public bool RequiresDatabase => false;

    public string Usage => """
        roleplay setup <catalog-directory> [--database <path>]

        Creates a new SQLite database, applies every EF migration, validates and imports the entire
        filesystem catalog, and verifies that the result agrees with the files. Refuses to overwrite
        an existing database; use `roleplay upgrade` for that case.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var catalog = context.Arguments.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(catalog))
        {
            context.Error.WriteLine("setup needs a catalog directory: roleplay setup <catalog-directory>");
            return 2;
        }

        var database = DatabaseLocator.ResolveTarget(context.Option("database"));
        var result = await CatalogDatabaseLifecycle.SetupAsync(catalog, database, cancellationToken);

        context.Out.WriteLine($"Created and migrated {result.DatabasePath}");
        context.Out.WriteLine(
            $"Imported {result.Created} new and {result.Updated} updated catalog record(s). Ready to run.");
        return 0;
    }
}
