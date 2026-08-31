using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Thin public-protocol adapter over one already public, immutable catalog navigator.</summary>
internal sealed class SystemCatalogHandler
{
    public Task<ToolEnvelope> ListAsync(
        IPublicApplicationCatalogProvider catalogs,
        IOperationLog log,
        string? applicationId) => RunAsync(catalogs, log, applicationId, "system.catalogs", (app, navigator) =>
    {
        var collections = navigator.ListCollections(app);
        var next = collections.Count == 0
            ? Capabilities
            : BrowseCall(app.Value, collections[0].Id, "", CatalogNavigationLimits.DefaultPageSize, null);
        return ToolOutcome.Ok(new { ApplicationId = app.Value, Collections = collections },
            $"Returned {collections.Count} public catalog collection(s) for '{app.Value}'.", next);
    });

    public Task<ToolEnvelope> BrowseAsync(
        IPublicApplicationCatalogProvider catalogs,
        IOperationLog log,
        string? applicationId,
        string? collection,
        string? branch,
        int? pageSize,
        string? cursor) => RunAsync(catalogs, log, applicationId, "system.catalog.browse", (app, navigator) =>
    {
        if (string.IsNullOrWhiteSpace(collection)) return Invalid("system.catalog.browse", "collection is required.");
        var size = pageSize ?? CatalogNavigationLimits.DefaultPageSize;
        var value = navigator.Browse(new(app, collection, branch ?? "", size, cursor));
        var next = new List<string>();
        if (value.NextCursor is not null) next.Add(BrowseCall(app.Value, collection, branch ?? "", size, value.NextCursor));
        var child = value.Entries.FirstOrDefault(entry => entry.Kind == CatalogBrowseEntryKind.Node)?.Node;
        if (child is not null) next.Add(BrowseCall(app.Value, collection, child.Path, size, null));
        var record = value.Entries.FirstOrDefault(entry => entry.Kind == CatalogBrowseEntryKind.Record)?.Record;
        if (record is not null) next.Add(RecordCall(app.Value, collection, record.QualifiedId));
        if (next.Count == 0) next.Add(CatalogsCall(app.Value));
        return ToolOutcome.Ok(new { ApplicationId = app.Value, Collection = collection, Result = value },
            $"Browsed public catalog '{collection}' at '{branch ?? ""}'.", [.. next]);
    }, () => BrowseCall(applicationId ?? "application-id", collection ?? "collection", branch ?? "", pageSize ?? CatalogNavigationLimits.DefaultPageSize, null));

    public Task<ToolEnvelope> SearchAsync(
        IPublicApplicationCatalogProvider catalogs,
        IOperationLog log,
        string? applicationId,
        string? query,
        string? collection,
        string? branch,
        string[]? kinds,
        string[]? statuses,
        int? pageSize,
        string? cursor) => RunAsync(catalogs, log, applicationId, "system.catalog.search", (app, navigator) =>
    {
        if (string.IsNullOrWhiteSpace(query)) return Invalid("system.catalog.search", "query is required.");
        var size = pageSize ?? CatalogNavigationLimits.DefaultPageSize;
        var value = navigator.Search(new(app, query, collection, branch ?? "", kinds, statuses, size, cursor));
        var next = new List<string>();
        if (value.NextCursor is not null) next.Add(SearchCall(app.Value, query, collection, branch ?? "", kinds, statuses, size, value.NextCursor));
        if (value.Records.Count > 0) next.Add(RecordCall(app.Value, value.Records[0].Record.Collection, value.Records[0].Record.QualifiedId));
        if (next.Count == 0) next.Add(CatalogsCall(app.Value));
        return ToolOutcome.Ok(new { ApplicationId = app.Value, Result = value },
            $"Searched the public catalog for '{query}'.", [.. next]);
    }, () => SearchCall(applicationId ?? "application-id", query ?? "query", collection, branch ?? "", kinds, statuses,
        pageSize ?? CatalogNavigationLimits.DefaultPageSize, null));

    public Task<ToolEnvelope> RecordAsync(
        IPublicApplicationCatalogProvider catalogs,
        IOperationLog log,
        string? applicationId,
        string? collection,
        string? id) => RunAsync(catalogs, log, applicationId, "system.catalog.record", (app, navigator) =>
    {
        if (string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(id))
            return Invalid("system.catalog.record", "collection and id are required.");
        var value = navigator.Inspect(new(app, collection, id));
        return ToolOutcome.Ok(new { ApplicationId = app.Value, Record = value },
            $"Returned exact public catalog record '{id}'.",
            BrowseCall(app.Value, collection, value.Summary.Path, CatalogNavigationLimits.DefaultPageSize, null));
    }, () => CatalogsCall(applicationId ?? "application-id"));

    private static Task<ToolEnvelope> RunAsync(
        IPublicApplicationCatalogProvider catalogs,
        IOperationLog log,
        string? applicationId,
        string kind,
        Func<ApplicationIdentifier, ICatalogNavigator, ToolOutcome> body,
        Func<string>? restart = null)
    {
        return ToolRunner.RunAsync(log, "query", () => Task.FromResult(Execute()));

        ToolOutcome Execute()
        {
            if (string.IsNullOrWhiteSpace(applicationId))
                return ToolOutcome.Fail("INVALID_APPLICATION", "applicationId is required.", Capabilities, $"Rejected {kind} without an application.");
            ApplicationIdentifier app;
            try { app = ApplicationIdentifier.Parse(applicationId); }
            catch (ArgumentException)
            {
                return ToolOutcome.Fail("INVALID_APPLICATION", "applicationId must be one valid non-system application segment.",
                    Capabilities, $"Rejected {kind} with an invalid application.");
            }
            if (!catalogs.TryGet(app, out var navigator))
                return ToolOutcome.Fail("PUBLIC_CATALOG_UNAVAILABLE", "No public catalog is published for this application.",
                    Capabilities, $"No public catalog was available for '{app.Value}'.");
            try { return body(app, navigator); }
            catch (ArgumentException exception) when (exception.Message.StartsWith("CURSOR_INVALID", StringComparison.Ordinal))
            {
                return ToolOutcome.Fail("CURSOR_INVALID", "The cursor is malformed or its signature is invalid.",
                    restart?.Invoke() ?? CatalogsCall(app.Value), $"Rejected an invalid cursor for {kind}.");
            }
            catch (InvalidOperationException exception) when (exception.Message.StartsWith("CURSOR_STALE", StringComparison.Ordinal))
            {
                return ToolOutcome.Fail("CURSOR_STALE", "The cursor belongs to a different catalog snapshot or query scope.",
                    restart?.Invoke() ?? CatalogsCall(app.Value), $"Rejected a stale cursor for {kind}.");
            }
            catch (KeyNotFoundException exception)
            {
                var code = exception.Message.StartsWith("CATALOG_RECORD_UNKNOWN", StringComparison.Ordinal)
                    ? "CATALOG_RECORD_UNKNOWN" : "CATALOG_NODE_UNKNOWN";
                return ToolOutcome.Fail(code, "The requested public catalog location does not exist.",
                    restart?.Invoke() ?? CatalogsCall(app.Value), $"Could not resolve a public catalog location for {kind}.");
            }
            catch (ArgumentException exception)
            {
                var code = exception.Message.StartsWith("CATALOG_COLLECTION_UNKNOWN", StringComparison.Ordinal)
                    ? "CATALOG_COLLECTION_UNKNOWN" : "INVALID_PAYLOAD";
                return ToolOutcome.Fail(code, "The public catalog request contains an invalid or out-of-scope field.",
                    restart?.Invoke() ?? CatalogsCall(app.Value), $"Rejected invalid fields for {kind}.");
            }
        }
    }

    private static ToolOutcome Invalid(string kind, string message) => ToolOutcome.Fail(
        "INVALID_PAYLOAD", $"{kind} {message}", Capabilities, $"Rejected malformed {kind} query.");

    private const string Capabilities = "query(kind: \"capabilities\")";
    private static string CatalogsCall(string app) => $"query(kind: \"system.catalogs\", applicationId: {Quote(app)})";
    private static string BrowseCall(string app, string collection, string branch, int size, string? cursor) =>
        $"query(kind: \"system.catalog.browse\", applicationId: {Quote(app)}, collection: {Quote(collection)}, branch: {Quote(branch)}, pageSize: {size}{Cursor(cursor)})";
    private static string SearchCall(string app, string query, string? collection, string branch, string[]? kinds, string[]? statuses, int size, string? cursor) =>
        $"query(kind: \"system.catalog.search\", applicationId: {Quote(app)}, query: {Quote(query)}{Optional("collection", collection)}, branch: {Quote(branch)}{Array("kinds", kinds)}{Array("statuses", statuses)}, pageSize: {size}{Cursor(cursor)})";
    private static string RecordCall(string app, string collection, string id) =>
        $"query(kind: \"system.catalog.record\", applicationId: {Quote(app)}, collection: {Quote(collection)}, id: {Quote(id)})";
    private static string Cursor(string? value) => value is null ? "" : $", cursor: {Quote(value)}";
    private static string Optional(string name, string? value) => value is null ? "" : $", {name}: {Quote(value)}";
    private static string Array(string name, string[]? values) => values is null ? "" : $", {name}: [{string.Join(", ", values.Select(Quote))}]";
    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
