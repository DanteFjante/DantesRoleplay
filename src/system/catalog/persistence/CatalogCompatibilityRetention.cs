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
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("classification").GetString() != "migration-only")
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
                || retained.Select(v => (v.Kind, v.Id)).Distinct().Count() != retained.Length)
                return [Failure("Retention records are invalid, duplicated, or outside their namespace boundary.")];
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

    private static CatalogValidationIssue Failure(string detail) => new("catalog", FileName, "compatibility-retention", detail, Warning: false);
}
