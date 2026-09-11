using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Data;

namespace DantesRoleplay.Web.Pages;

public sealed record CompositionPagePreviewResult(
    string Generation,
    string? Html,
    IReadOnlyList<WebCompositionError> Errors,
    IReadOnlyDictionary<string, InteractionInvocationResult> QueryResults);

/// <summary>
/// Combines a parsed immutable content generation with per-request authorized reads. This is a
/// preview/render consumer, not a publication or activation service. The host resolves exact
/// query contracts before entry; binding names alone are never authority to perform a read.
/// </summary>
public sealed class CompositionPagePreview(CompositionQueryMaterializer queries)
{
    public async Task<CompositionPagePreviewResult> RenderAsync(
        WebCompositionDocument document,
        InteractionInvocationHost host,
        IReadOnlyDictionary<string, ApplicationReadModelInvocationRequest> selectedQueries,
        CancellationToken cancellationToken = default,
        string? assetBasePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedQueries);
        if (!document.QueryBindings.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(selectedQueries.Keys))
            return new(document.Generation, null,
                [new("$.queries", "COMPOSITION_QUERY_SELECTION_MISMATCH", "The host must select exactly the declared query bindings.")],
                new Dictionary<string, InteractionInvocationResult>());
        var materialized = await queries.ReadAsync(host, selectedQueries, cancellationToken);
        var failures = materialized.Results.Where(pair => pair.Value.Tag != InteractionInvocationResultTag.Completed)
            .Select(pair => new WebCompositionError("$.queries." + pair.Key, pair.Value.Code, pair.Value.SafeMessage)).ToArray();
        if (failures.Length != 0) return new(document.Generation, null, failures, materialized.Results);
        cancellationToken.ThrowIfCancellationRequested();
        var rendered = new WebCompositionRenderer().Render(document, materialized.Values, assetBasePath);
        return new(document.Generation, rendered.Html, rendered.Errors, materialized.Results);
    }
}
