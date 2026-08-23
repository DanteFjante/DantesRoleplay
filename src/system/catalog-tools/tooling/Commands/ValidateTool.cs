using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Runs the catalog's complete file-to-fresh-database gate without touching the live database.
/// </summary>
public sealed class ValidateTool : ITool
{
    public string Name => "validate";

    public string Summary => "Validate catalog files through a disposable fresh database.";

    public bool RequiresDatabase => false;

    public string Usage => """
        roleplay validate <directory>

        Copies the catalog to a temporary directory, migrates a fresh temporary SQLite database,
        imports through the production importer, runs the same write-side checks used by MCP dry
        runs, and verifies that the imported database round-trips cleanly. The source catalog and
        live database are never changed.

        This is the normal inner-loop gate for file-authored contracts, mechanics, schemas and
        fixtures. Focused behavioral tests are still required for changed mechanics; use
        `roleplay import` only when synchronizing a reviewed feature into a persistent database.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var target = context.Arguments.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(target))
        {
            context.Error.WriteLine("validate needs a directory: roleplay validate <directory>");
            return 2;
        }

        var result = await CatalogValidator.ValidateAsync(target, cancellationToken);

        foreach (var issue in result.Issues
                     .OrderBy(issue => issue.Warning)
                     .ThenBy(issue => issue.Kind, StringComparer.Ordinal)
                     .ThenBy(issue => issue.Id, StringComparer.Ordinal)
                     .ThenBy(issue => issue.Check, StringComparer.Ordinal))
        {
            context.Out.WriteLine(
                $"{(issue.Warning ? "WARNING" : "ERROR")} {issue.Kind} {issue.Id} "
                + $"[{issue.Check}]: {issue.Detail}");
        }

        context.Out.WriteLine();
        context.Out.WriteLine(
            $"Validated {result.Records} records: {result.Mechanics} mechanics, "
            + $"{result.Procedures} procedures, {result.Components} components, "
            + $"{result.EventTypes} event types, {result.Subscriptions} subscriptions, "
            + $"{result.Entities} entities.");
        context.Out.WriteLine(
            result.IsValid
                ? $"Catalog is valid ({result.Warnings} warning(s)). No live data was touched."
                : $"Catalog validation failed: {result.Errors} error(s), {result.Warnings} warning(s). "
                  + "No live data was touched.");

        return result.IsValid ? 0 : 1;
    }
}
