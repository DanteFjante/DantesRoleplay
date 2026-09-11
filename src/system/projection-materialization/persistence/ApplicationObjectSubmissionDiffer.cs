using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DantesRoleplay.Projections;

/// <summary>
/// Prepares a partial or complete submitted application object for the existing declared-change
/// writer. This helper is deliberately not wired to a transport yet: it defines and tests the
/// comparison boundary without creating a second ECS mutation path.
/// </summary>
internal static class ApplicationObjectSubmissionDiffer
{
    private const int MaximumSubmittedObjectBytes = 65_536;
    private const int MaximumCurrentObjectBytes = 1_048_576;

    internal static string CreateChanges(
        string currentJson,
        string submittedJson,
        IReadOnlyList<GeneratedApplicationObjectWriteMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var current = ParseObject(currentJson, MaximumCurrentObjectBytes);
        var submitted = ParseObject(submittedJson, MaximumSubmittedObjectBytes);
        var operations = mappings
            .Where(value => value.Operation is "set" or "clear")
            .GroupBy(value => value.ObjectPointer, StringComparer.Ordinal)
            .ToDictionary(
                value => value.Key,
                value => value.Select(mapping => mapping.Operation)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var changes = new JsonObject();
        Visit(current, true, submitted, string.Empty, operations, changes);
        return Canonical(changes);
    }

    private static void Visit(
        JsonNode? current,
        bool currentExists,
        JsonNode? submitted,
        string pointer,
        IReadOnlyDictionary<string, HashSet<string>> operations,
        JsonObject changes)
    {
        if (pointer.Length > 0 && operations.TryGetValue(pointer, out var allowed))
        {
            if (currentExists && JsonNode.DeepEquals(current, submitted))
                return;
            var operation = submitted is null ? "clear" : "set";
            if (!allowed.Contains(operation))
                throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                    "The submitted value requires an undeclared object field operation.");
            // A declared clear of an already absent value is intentionally idempotent. Existence
            // remains significant for read-only fields, where adding null must be rejected.
            if (!currentExists && submitted is null) return;
            Set(changes, pointer, submitted?.DeepClone());
            return;
        }

        if (currentExists && JsonNode.DeepEquals(current, submitted)) return;
        if (submitted is not JsonObject submittedObject)
            throw Failure("OBJECT_WRITE_READ_ONLY_CHANGED",
                "The submitted object changes a read-only field.");
        // An object submission is patch-shaped until it reaches an exact writable pointer. An
        // empty object therefore omits all child fields; it never deletes the current subtree.
        if (submittedObject.Count == 0) return;
        if (currentExists && current is not null && current is not JsonObject)
            throw Failure("OBJECT_WRITE_READ_ONLY_CHANGED",
                "The submitted object changes a read-only field.");
        var currentObject = current as JsonObject;

        foreach (var property in submittedObject)
        {
            JsonNode? currentValue = null;
            var propertyExists = currentObject is not null
                && currentObject.TryGetPropertyValue(property.Key, out currentValue);
            Visit(currentValue, propertyExists, property.Value, pointer + "/" + Escape(property.Key),
                operations, changes);
        }
    }

    private static JsonObject ParseObject(string json, int maximumBytes)
    {
        try
        {
            if (json is null || Encoding.UTF8.GetByteCount(json) > maximumBytes)
                throw new JsonException();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            RejectDuplicateProperties(document.RootElement);
            return JsonNode.Parse(document.RootElement.GetRawText())?.AsObject()
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                "The submitted object contains invalid bounded JSON.", exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static void Set(JsonObject root, string pointer, JsonNode? value)
    {
        var tokens = Tokens(pointer).ToArray();
        if (tokens.Length == 0)
            throw Failure("OBJECT_WRITE_REQUEST_INVALID", "An object field cannot replace the root.");
        var current = root;
        foreach (var token in tokens[..^1])
        {
            current[token] ??= new JsonObject();
            current = current[token] as JsonObject
                ?? throw Failure("OBJECT_WRITE_REQUEST_INVALID",
                    "Declared writable object paths overlap incompatibly.");
        }
        current[tokens[^1]] = value;
    }

    private static string Canonical(JsonObject value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? value)
    {
        if (value is JsonObject objectValue)
        {
            writer.WriteStartObject();
            foreach (var property in objectValue.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Key);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value is JsonArray arrayValue)
        {
            writer.WriteStartArray();
            foreach (var item in arrayValue) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else if (value is null) writer.WriteNullValue();
        else value.WriteTo(writer);
    }

    private static IEnumerable<string> Tokens(string pointer) => pointer.Split('/').Skip(1)
        .Select(value => value.Replace("~1", "/").Replace("~0", "~"));
    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
    private static ApplicationObjectWriteException Failure(
        string code, string message, Exception? inner = null) => new(code, message, inner);
}
