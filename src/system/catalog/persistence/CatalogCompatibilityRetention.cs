using System.Globalization;
using System.Text.Json;
using DantesRoleplay.CatalogNamespaces;

namespace DantesRoleplay.DataAccess.Catalog;

public sealed record RetainedCatalogIdentity(string Kind, string Id, string Path);

/// <summary>An exact migration inventory is a closed retention boundary, not an authoring namespace.</summary>
public static class CatalogCompatibilityRetention
{
    public const string FileName = "compatibility-retention.json";

    public static async Task<IReadOnlyList<CatalogValidationIssue>> ValidateAsync(string root, CatalogContents contents,
        CancellationToken cancellationToken = default)
    {
        var path = System.IO.Path.Combine(root, FileName);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 1_048_576) return [Failure("Retention inventory exceeds its byte limit.")];
        var actual = contents.Mechanics.Select(v => new RetainedCatalogIdentity("mechanic", v.Id, CatalogLayout.MechanicMarkdown(v.Category, v.Id)))
            .Concat(contents.Procedures.Select(v => new RetainedCatalogIdentity("procedure", v.Id, CatalogLayout.ProcedureMarkdown(v.Category, v.Id))))
            .Concat(contents.Components.Select(v => new RetainedCatalogIdentity("component-definition", v.Id, CatalogLayout.Component(v.Id))))
            .Concat(contents.Entities.Select(v => new RetainedCatalogIdentity("entity", v.Id, CatalogLayout.Entity(v.Id))))
            .Concat(contents.EventTypes.Select(v => new RetainedCatalogIdentity("event-type", v.Id, CatalogLayout.EventType(v.Id))))
            .Concat(contents.Subscriptions.Select(v => new RetainedCatalogIdentity("subscription", v.Id, CatalogLayout.Subscription(v.Id)))).ToArray();
        return Validate(await File.ReadAllTextAsync(path, cancellationToken), actual);
    }

    public static IReadOnlyList<CatalogValidationIssue> Validate(string json, IReadOnlyList<RetainedCatalogIdentity> actual)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 2 || root.GetProperty("classification").GetString() != "migration-only")
                return [Failure("Unsupported retention inventory contract.")];
            foreach (var field in new[] { "owner", "reason", "retirementCondition", "namespacePolicy", "evidence" })
                if (string.IsNullOrWhiteSpace(root.GetProperty(field).GetString())) return [Failure($"Missing retention {field}.")];
            var scopes = root.GetProperty("namespaces").EnumerateArray().Select(v => v.GetString()!).ToArray();
            var kinds = root.GetProperty("recordKinds").EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal);
            if (kinds.Count == 0 || kinds.Any(v => !CatalogNamespaceKinds.All.Contains(v)))
                return [Failure("Retention record kinds are invalid.")];
            if (scopes.Length is < 1 or > 100 || scopes.Any(v => !CatalogNamespaceIdentity.IsNamespaceId(v))
                || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Length)
                return [Failure("Retention namespaces are invalid.")];
            bool InScope(string id) => scopes.Any(scope => id.StartsWith(scope + ".", StringComparison.Ordinal));
            var retained = root.GetProperty("records").EnumerateArray().Select(v => new RetainedCatalogIdentity(
                v.GetProperty("kind").GetString()!, v.GetProperty("id").GetString()!, v.GetProperty("path").GetString()!)).ToArray();
            if (retained.Length is < 1 or > 10000 || retained.Any(v => string.IsNullOrWhiteSpace(v.Id) || !InScope(v.Id)
                || !kinds.Contains(v.Kind) || string.IsNullOrWhiteSpace(v.Path))
                || retained.Select(v => (v.Kind, v.Id)).Distinct().Count() != retained.Length
                || retained.Select(v => v.Id).Distinct(StringComparer.Ordinal).Count() != retained.Length)
                return [Failure("Retention records are invalid, duplicated, or outside their namespace boundary.")];
            var reviewProblem = ValidateReview(root.GetProperty("review"), retained);
            if (reviewProblem is not null) return [Failure(reviewProblem)];
            var current = actual.Where(v => kinds.Contains(v.Kind) && InScope(v.Id)).ToHashSet();
            var expected = retained.ToHashSet();
            return current.Except(expected).Select(v => Failure($"Unreviewed compatibility identity or path: {v.Kind} {v.Id}."))
                .Concat(expected.Except(current).Select(v => Failure($"Retained identity is missing or moved without retirement review: {v.Kind} {v.Id}."))).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return [Failure("Malformed retention inventory: " + exception.Message)];
        }
    }

    private static string? ValidateReview(JsonElement review, IReadOnlyList<RetainedCatalogIdentity> retained)
    {
        if (!DateOnly.TryParseExact(review.GetProperty("reviewedAt").GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || review.GetProperty("disposition").GetString() != "retain-all")
            return "The retention review date or disposition is invalid.";

        var export = review.GetProperty("liveExport");
        if (export.GetProperty("recordCount").GetInt32() < retained.Count
            || export.GetProperty("operationCount").GetInt32() < 0
            || export.GetProperty("retainedRecordCount").GetInt32() != retained.Count
            || !Sha256(export.GetProperty("databaseSha256").GetString()))
            return "The retention review live-export evidence is invalid.";
        var recovery = review.GetProperty("recovery");
        if (!Sha256(recovery.GetProperty("databaseSha256").GetString())
            || recovery.GetProperty("blobCount").GetInt32() < 0
            || recovery.GetProperty("blobBytes").GetInt64() < 0
            || recovery.GetProperty("blobHashDifferences").GetInt32() != 0)
            return "The retention review recovery evidence is invalid.";

        var groups = review.GetProperty("groups").EnumerateArray().ToArray();
        if (groups.Length is < 1 or > 20) return "The retention review groups are invalid.";
        var names = new HashSet<string>(StringComparer.Ordinal);
        var reviewed = new HashSet<string>(StringComparer.Ordinal);
        var retainedIds = retained.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var name = group.GetProperty("name").GetString();
            var reason = group.GetProperty("reason").GetString();
            var ids = group.GetProperty("recordIds").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name)
                || string.IsNullOrWhiteSpace(reason) || ids.Length == 0
                || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
                || ids.Any(id => !retainedIds.Contains(id)))
                return "The retention review groups are invalid or refer to unretained identities.";
            reviewed.UnionWith(ids);
        }
        return reviewed.SetEquals(retainedIds)
            ? null
            : "Every retained identity must have a reviewed disposition.";
    }

    private static bool Sha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static CatalogValidationIssue Failure(string detail) => new("catalog", FileName, "compatibility-retention", detail, Warning: false);
}
