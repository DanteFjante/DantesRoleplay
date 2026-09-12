using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;

namespace DantesRoleplay.Interactions;

/// <summary>Exact authored-content comparison only; it does not establish behavioral equivalence.</summary>
internal static class CandidateReuseContentComparison
{
    private const int MaximumContentBytes = 64 * 1024;

    internal static bool TryFindExactDuplicate(ApplicationIdentifier applicationId,
        CatalogRecordDefinition left, CatalogRecordDefinition right)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(applicationId);
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            if (left.QualifiedId == right.QualifiedId || left.Kind != right.Kind
                || left.Kind is not ("procedure" or "mechanic" or "query")
                || !IsActive(left.Status) || !IsActive(right.Status)
                || !Owns(applicationId, left.QualifiedId) || !Owns(applicationId, right.QualifiedId)) return false;
            return ComparableContent(applicationId, left, out var leftContent)
                && ComparableContent(applicationId, right, out var rightContent)
                && string.Equals(leftContent, rightContent, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool ComparableContent(ApplicationIdentifier applicationId, CatalogRecordDefinition record,
        out string normalized)
    {
        normalized = "";
        if (record.ContentJson is null || record.ContentFingerprint is null
            || Encoding.UTF8.GetByteCount(record.ContentJson) > MaximumContentBytes
            || !string.Equals(Hash(record.ContentJson), record.ContentFingerprint, StringComparison.OrdinalIgnoreCase)) return false;
        var canonical = InteractionCanonicalJson.CanonicalizeObject(record.ContentJson);
        using var document = JsonDocument.Parse(canonical);
        if (!document.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || !MatchesRecordId(applicationId, record.QualifiedId, id.GetString())) return false;
        var objectNode = JsonNode.Parse(canonical)?.AsObject();
        if (objectNode is null || !objectNode.Remove("id")) return false;
        normalized = InteractionCanonicalJson.CanonicalizeObject(objectNode.ToJsonString());
        return true;
    }

    private static bool Owns(ApplicationIdentifier applicationId, string qualifiedId) =>
        qualifiedId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal);
    private static bool MatchesRecordId(ApplicationIdentifier applicationId, string qualifiedId, string? value) =>
        value == qualifiedId || value == qualifiedId[(applicationId.Value.Length + 1)..];
    private static bool IsActive(string value) => string.Equals(value, "active", StringComparison.OrdinalIgnoreCase);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
