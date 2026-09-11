using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Interactions;

internal sealed class ApplicationQueryRoleBindingResolver(IBoundedJsonSchemaValidator schemas)
    : IApplicationQueryRoleBindingResolver
{
    public IReadOnlyDictionary<string, string> Resolve(
        ApplicationQueryContract contract,
        string inputJson,
        ApplicationQueryRoleBindingContext context)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(context);
        if (contract.RoleBindings is null)
            throw Failure("READ_MODEL_ROLES_INVALID", "The query has no explicit role-binding contract.");
        if (!Token(context.RouteEntityId) || context.AuthorizedRoleEntityIds is null
            || context.AuthorizedRoleEntityIds.Count > 32 || context.AuthorizedRoleEntityIds.Any(value =>
                !Key(value.Key) || !Token(value.Value)))
            throw Failure("READ_MODEL_ROLES_INVALID", "The trusted role-binding context is invalid.");

        var input = ApplicationReadModelInput.Normalize(inputJson);
        if (contract.InputSchemaJson is null)
        {
            if (input != "{}") throw Failure("READ_MODEL_INPUT_INVALID", "The request is invalid.");
        }
        else
        {
            var schema = schemas.Compile(contract.InputSchemaJson);
            if (!schema.IsAccepted || schemas.Validate(schema.ProfileId,
                    schema.NormalizedSchema, input).Status != SchemaValueStatus.Valid)
                throw Failure("READ_MODEL_INPUT_INVALID", "The request is invalid.");
        }

        using var document = JsonDocument.Parse(input);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in contract.Roles.Keys)
        {
            if (!contract.RoleBindings.TryGetValue(role, out var declaration))
                throw Failure("READ_MODEL_ROLES_INVALID", "The query does not bind every declared role.");
            var value = declaration.Source switch
            {
                "route-entity" => context.RouteEntityId,
                "input" => SelectString(document.RootElement, declaration.Pointer!),
                "authorized-context" when context.AuthorizedRoleEntityIds.TryGetValue(
                    declaration.Key!, out var authorized) => authorized,
                _ => null
            };
            if (!Token(value))
                throw Failure("READ_MODEL_ROLES_UNAVAILABLE", "A declared query role is unavailable.");
            result.Add(role, value!);
        }
        return new ReadOnlyDictionary<string, string>(result);
    }

    private static string? SelectString(JsonElement root, string pointer)
    {
        var current = root;
        foreach (var token in pointer.Split('/').Skip(1)
                     .Select(value => value.Replace("~1", "/").Replace("~0", "~")))
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(token, out current)) return null;
                continue;
            }
            if (current.ValueKind == JsonValueKind.Array && TryParseArrayIndex(token, out var index)
                && index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
                continue;
            }
            return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    internal static bool TryParseArrayIndex(string token, out int index)
    {
        index = -1;
        return (token == "0" || token.Length > 0 && token[0] is >= '1' and <= '9'
                && token.All(char.IsAsciiDigit))
            && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private static bool Key(string? value) => value is { Length: > 0 and <= 200 }
        && value.Split('.').All(segment => segment is { Length: > 0 and <= 63 }
            && char.IsAsciiLetterLower(segment[0])
            && segment.All(character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character) || character == '-'));
    private static bool Token(string? value) => value is { Length: > 0 and <= 200 }
        && value == value.Trim() && !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace);
    private static ApplicationReadModelException Failure(string code, string message) => new(code, message);
}
