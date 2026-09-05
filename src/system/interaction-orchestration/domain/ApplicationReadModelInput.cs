using System.Text;
using System.Text.Json;

namespace DantesRoleplay.Interactions;

/// <summary>Transport-independent bounds; trusted identities never come from query input.</summary>
public static class ApplicationReadModelInput
{
    public static string Normalize(string json)
    {
        try
        {
            if (json is null || Encoding.UTF8.GetByteCount(json) > 1024) throw new JsonException();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            CheckDuplicates(document.RootElement);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new ApplicationReadModelException("READ_MODEL_INPUT_INVALID", "The request is invalid.");
        }
    }

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) CheckDuplicates(value);
    }
}
