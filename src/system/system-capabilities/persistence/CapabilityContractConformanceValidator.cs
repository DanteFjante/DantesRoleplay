using System.Text.Json;
using DantesRoleplay.Capabilities;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>Runs one bounded compiler over every transport-neutral capability descriptor.</summary>
public static class CapabilityContractConformanceValidator
{
    public static IReadOnlyList<string> FindProblems(
        CapabilityContractDescriptor descriptor,
        IBoundedJsonSchemaValidator schemas)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(schemas);
        var problems = new List<string>();
        var input = schemas.Compile(descriptor.Input.SchemaJson);
        var output = schemas.Compile(descriptor.Output.SchemaJson);
        if (!input.IsAccepted)
            problems.Add(input.Diagnostics.FirstOrDefault()?.Message ?? "The input schema is invalid.");
        if (!output.IsAccepted)
            problems.Add(output.Diagnostics.FirstOrDefault()?.Message ?? "The output schema is invalid.");
        if (!ClosedObject(descriptor.Input.SchemaJson, allowBoundedCompatibility: true))
            problems.Add("The input schema must reject unknown top-level properties or declare a bounded compatibility object.");
        if (!ClosedObject(descriptor.Output.SchemaJson, allowBoundedCompatibility: false))
            problems.Add("The output schema must reject unknown top-level properties.");
        if (!input.IsAccepted || !output.IsAccepted) return problems;

        foreach (var example in descriptor.Examples)
        {
            var actualValid = schemas.Validate(input.ProfileId, input.NormalizedSchema, example.InputJson).Status
                == SchemaValueStatus.Valid;
            if (actualValid != example.ExpectedValid)
                problems.Add($"Example '{example.Name}' does not have its declared input validity.");
            if (example.OutputJson is not null && schemas.Validate(output.ProfileId,
                    output.NormalizedSchema, example.OutputJson).Status != SchemaValueStatus.Valid)
                problems.Add($"Example '{example.Name}' does not satisfy the output schema.");
        }
        return problems;
    }

    private static bool ClosedObject(string schemaJson, bool allowBoundedCompatibility)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return ClosedObject(document.RootElement, allowBoundedCompatibility);
    }

    private static bool ClosedObject(JsonElement schema, bool allowBoundedCompatibility)
    {
        if (schema.ValueKind != JsonValueKind.Object) return false;
        if (schema.TryGetProperty("additionalProperties", out var additional))
        {
            if (additional.ValueKind == JsonValueKind.False) return true;
            if (allowBoundedCompatibility && additional.ValueKind == JsonValueKind.Object
                && schema.TryGetProperty("maxProperties", out _)) return true;
        }
        if (allowBoundedCompatibility && schema.TryGetProperty("maxProperties", out var maximum)
            && maximum.ValueKind == JsonValueKind.Number && maximum.TryGetInt32(out var limit)
            && limit is >= 0 and <= 64) return true;
        if (!schema.TryGetProperty("anyOf", out var alternatives)
            || alternatives.ValueKind != JsonValueKind.Array || alternatives.GetArrayLength() == 0) return false;
        return alternatives.EnumerateArray().All(value => ClosedObject(value, allowBoundedCompatibility));
    }
}
