using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DantesRoleplay.Interactions;

public sealed class InteractionContractException : ArgumentException
{
    public InteractionContractException(string code, string message, string? parameter = null)
        : base(message, parameter) => Code = code;

    public string Code { get; }
}

public static class InteractionContractLimits
{
    public const int IntentText = 8_000;
    public const int IdempotencyKey = 128;
    public const int Identifier = 200;
    public const int RoleHints = 32;
    public const int ConversationFacts = 32;
    public const int ProposalSteps = 16;
    public const int DependenciesPerStep = 16;
    public const int ResultBindingsPerStep = 32;
    public const int EvidenceItems = 16;
    public const int SafeEvidenceText = 1_000;
    public const int JsonBytes = 65_536;
    public const int JsonDepth = 32;
}

public static class InteractionCanonicalJson
{
    public static string CanonicalizeObject(string json) => Canonicalize(json, requireObject: true);

    public static string Canonicalize(string json) => Canonicalize(json, requireObject: false);

    public static string Fingerprint(string domain, string canonicalJson)
    {
        InteractionGuard.Bounded(domain, 120, "INVALID_FINGERPRINT_DOMAIN", nameof(domain));
        ArgumentNullException.ThrowIfNull(canonicalJson);
        var value = Encoding.UTF8.GetBytes(domain + "\0" + canonicalJson);
        return Convert.ToHexString(SHA256.HashData(value));
    }

    private static string Canonicalize(string json, bool requireObject)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > InteractionContractLimits.JsonBytes)
            throw new InteractionContractException("JSON_TOO_LARGE", "JSON exceeds the interaction contract limit.", nameof(json));

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = InteractionContractLimits.JsonDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            if (requireObject && document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InteractionContractException("JSON_OBJECT_REQUIRED", "The interaction value must be a JSON object.", nameof(json));

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
                WriteCanonical(writer, document.RootElement);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            throw new InteractionContractException("INVALID_JSON", "The interaction value is not valid bounded JSON.", nameof(json));
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject().ToArray();
                if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new InteractionContractException("DUPLICATE_JSON_PROPERTY", "JSON objects may not contain duplicate properties.");
                foreach (var property in properties.OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                return;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                return;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                return;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                return;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                return;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                return;
            default:
                throw new InteractionContractException("INVALID_JSON", "Unsupported JSON token.");
        }
    }
}

internal static class InteractionGuard
{
    public static string Bounded(string value, int maximum, string code, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new InteractionContractException(code, $"{parameter} is required and may contain at most {maximum} characters.", parameter);
        return value.Trim();
    }

    public static string? OptionalBounded(string? value, int maximum, string code, string parameter) =>
        value is null ? null : Bounded(value, maximum, code, parameter);

    public static string UpperSha256(string value, string parameter)
    {
        if (value is not { Length: 64 } || value.Any(c => !(char.IsAsciiDigit(c) || c is >= 'A' and <= 'F')))
            throw new InteractionContractException("INVALID_SHA256", $"{parameter} must be an uppercase SHA-256 value.", parameter);
        return value;
    }

    public static string IdempotencyKey(string value)
    {
        var result = Bounded(value, InteractionContractLimits.IdempotencyKey, "INVALID_IDEMPOTENCY_KEY", nameof(value));
        if (result.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-')))
            throw new InteractionContractException("INVALID_IDEMPOTENCY_KEY", "The idempotency key contains unsupported characters.", nameof(value));
        return result;
    }

    public static string Identifier(string value, string parameter) =>
        Bounded(value, InteractionContractLimits.Identifier, "INVALID_IDENTIFIER", parameter);

    public static IReadOnlyDictionary<string, string> CopyMap(
        IReadOnlyDictionary<string, string> values,
        int maximum,
        string code)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > maximum) throw new InteractionContractException(code, "The collection exceeds its interaction contract limit.");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            var key = Identifier(item.Key, "key");
            var value = Bounded(item.Value, InteractionContractLimits.SafeEvidenceText, code, "value");
            if (!result.TryAdd(key, value)) throw new InteractionContractException(code, "The collection contains duplicate keys.");
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
    }

    public static IReadOnlyList<string> CopyDistinctList(
        IEnumerable<string> values,
        int maximum,
        string code,
        bool sort = true)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = values.Select(x => Identifier(x, "value")).ToArray();
        if (result.Length > maximum) throw new InteractionContractException(code, "The collection exceeds its interaction contract limit.");
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new InteractionContractException(code, "The collection contains duplicate values.");
        if (sort) Array.Sort(result, StringComparer.Ordinal);
        return Array.AsReadOnly(result);
    }
}
