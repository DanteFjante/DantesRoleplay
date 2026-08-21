using DantesRoleplay.Categories;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The catalog-category handler behind query(kind: "categories").
///
/// Categories are derived from the primary category path already stored on procedure contracts
/// and mechanics. This handler deliberately owns no taxonomy or persistence: it selects one
/// catalog's exact-path counts, then lets CategoryPath construct one tree level.
/// </summary>
public sealed class CategoryTools
{
    public async Task<ToolEnvelope> BrowseAsync(
        IProcedureStore procedures,
        IMechanicStore mechanics,
        IOperationLog log,
        string? catalog,
        string? category,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "query", async () =>
        {
            var selected = catalog?.Trim().ToLowerInvariant() ?? string.Empty;

            if (selected is not "procedures" and not "mechanics")
            {
                return ToolOutcome.Fail(
                    "INVALID_CATALOG",
                    $"'{catalog}' is not a browseable catalog. Use procedures or mechanics.",
                    "query(kind: \"categories\", catalog: \"procedures\")",
                    $"Rejected category browse for catalog '{catalog}'.");
            }

            if (category is not null && !CategoryPath.TryValidate(category, out var problem))
            {
                return ToolOutcome.Fail(
                    "INVALID_CATEGORY",
                    problem,
                    "query(kind: \"categories\", catalog: \"procedures\")",
                    $"Rejected malformed category '{category}'.");
            }

            IReadOnlyList<CategoryCount> counts = selected switch
            {
                "procedures" => [.. (await procedures.GetCategoriesAsync(
                    includeInactive: includeInactive,
                    cancellationToken: cancellationToken))
                    .Select(item => new CategoryCount(item.Category, item.Count))],
                "mechanics" => [.. (await mechanics.GetCategoriesAsync(
                    includeInactive: includeInactive,
                    cancellationToken: cancellationToken))
                    .Select(item => new CategoryCount(item.Category, item.Count))],
                _ => throw new InvalidOperationException($"Unhandled category catalog '{selected}'.")
            };

            var branch = CategoryPath.Browse(category, counts);
            var nextSteps = NextSteps(selected, branch);

            return ToolOutcome.Ok(
                new { Catalog = selected, IncludeInactive = includeInactive, Branch = branch },
                $"Browsed {selected} categories at {(branch.Path.Length == 0 ? "the root" : $"'{branch.Path}'")}: "
                + $"{branch.Direct} direct record(s), {branch.Subtree} in this branch.",
                [.. nextSteps]);
        });

    private static IReadOnlyList<string> NextSteps(string catalog, CategoryBranch branch)
    {
        if (branch.Children.Count > 0)
        {
            var child = branch.Children[0].Path;

            return
            [
                BrowseCall(catalog, child),
                RecordCall(catalog, child)
            ];
        }

        if (branch.Subtree > 0)
        {
            return [RecordCall(catalog, branch.Path)];
        }

        return
        [
            $"query(kind: \"categories\", catalog: \"{catalog}\")"
        ];
    }

    private static string BrowseCall(string catalog, string category) =>
        $"query(kind: \"categories\", catalog: \"{catalog}\", category: \"{category}\")";

    private static string RecordCall(string catalog, string category) =>
        $"query(kind: \"{catalog}\", category: \"{category}\")";
}
