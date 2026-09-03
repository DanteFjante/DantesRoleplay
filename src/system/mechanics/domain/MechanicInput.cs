using System.Text.Json;

namespace DantesRoleplay.Mechanics;

/// <summary>
/// Defines the boundary between an execution caller's JSON text and a mechanic's <c>ctx.input</c>.
/// Input must be a JSON object and is otherwise preserved unchanged for replay.
/// </summary>
public static class MechanicInput
{
    public static bool TryValidateObject(string? input, out string? problem)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            problem = "Mechanic input must be a non-empty JSON object. Use {} when it has no arguments.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(input);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problem = $"Mechanic input must be a JSON object, not {document.RootElement.ValueKind.ToString().ToLowerInvariant()}.";
                return false;
            }
        }
        catch (JsonException)
        {
            problem = "Mechanic input must be a valid JSON object.";
            return false;
        }

        problem = null;
        return true;
    }
}
