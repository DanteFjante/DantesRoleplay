using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.CatalogNamespaces;

namespace DantesRoleplay.DataAccess.Catalog;

public sealed record CatalogNamespaceFile(
    string Id,
    string Owner,
    string Description,
    IReadOnlyList<string> AllowedKinds,
    IReadOnlyList<string> Aliases,
    bool Enabled,
    string ReviewStatus,
    string ReviewNote)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";

    public static CatalogNamespaceFile Parse(string json, string source)
    {
        CatalogNamespaceFile? file;
        try { file = JsonSerializer.Deserialize<CatalogNamespaceFile>(json, Json); }
        catch (JsonException exception) { throw new InvalidOperationException($"{source} is not valid namespace JSON: {exception.Message}", exception); }
        if (file is null || !CatalogNamespaceIdentity.IsNamespaceId(file.Id)
            || string.IsNullOrWhiteSpace(file.Owner) || string.IsNullOrWhiteSpace(file.Description)
            || file.AllowedKinds is null || file.AllowedKinds.Count == 0
            || file.AllowedKinds.Any(kind => !CatalogNamespaceKinds.All.Contains(kind))
            || file.Aliases is null
            || !CatalogNamespaceReviewStatuses.All.Contains(file.ReviewStatus)
            || string.IsNullOrWhiteSpace(file.ReviewNote))
            throw new InvalidOperationException($"{source} does not contain valid namespace metadata.");
        return file with
        {
            AllowedKinds = file.AllowedKinds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Aliases = file.Aliases.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public CatalogNamespaceRegistration Registration() => new(
        Id, Owner, Description, AllowedKinds, Aliases, ReviewStatus, ReviewNote);
}
