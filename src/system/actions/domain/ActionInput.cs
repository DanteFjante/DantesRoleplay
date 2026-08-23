using System.Text.Json;

namespace DantesRoleplay.Actions;

/// <summary>
/// Defines the boundary between an action caller's JSON text and a mechanic's <c>ctx.input</c>.
/// An omitted <see cref="ActionRequest.Input"/> already defaults to <c>{}</c>; an explicitly
/// supplied value must be a JSON object and is otherwise preserved unchanged for replay.
/// </summary>
public static class ActionInput
{
    public static bool TryValidateObject(string? input, out string? problem)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            problem = "Action input must be a non-empty JSON object. Use {} when the action has no arguments.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(input);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problem = $"Action input must be a JSON object, not {document.RootElement.ValueKind.ToString().ToLowerInvariant()}.";
                return false;
            }
        }
        catch (JsonException)
        {
            problem = "Action input must be a valid JSON object.";
            return false;
        }

        problem = null;
        return true;
    }
}
